using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

using var cancellation_token_source = new CancellationTokenSource();

Console.CancelKeyPress += OnCancelKeyPress;

try
{
    if (args.Any(a => a is "--upgrade" or "--update"))
    {
        await Updater.UpdateAsync();
        return 0;
    }

    if (args.Any(a => a == "-v"))
    {
        Console.WriteLine(Updater.CurrentVersion);
        return 0;
    }

    var main_options = MainOptions.Parse(args);
    if (main_options.ErrorMessage is { } parse_error)
    {
        Console.WriteLine(parse_error);
        return 2;
    }

    var host = main_options.Host ?? "ya.ru";

    var resolve_address_result = await Ex.ResolveAddressAsync(host, cancellation_token_source.Token).ConfigureAwait(false);
    if (resolve_address_result.Address is not { } ip)
    {
        Console.WriteLine(resolve_address_result.ErrorMessage ?? $"Unknown host ip \"{host}\"");
        return 1;
    }

    if (main_options.Json)
    {
        var hops = await Ex.TraceAsJsonAsync(
                ip,
                main_options.MaxTtl,
                main_options.ProbesPerHop,
                main_options.TimeoutMs,
                !main_options.NoDns,
                cancellation_token_source.Token)
            .ConfigureAwait(false);

        Console.WriteLine(JsonSerializer.Serialize(hops, ProgramJsonSerializationContext.Default.ListTraceHop));
        return 0;
    }

    if (!Console.IsOutputRedirected)
        Console.Clear();

    Ex.WriteLine($"Trace route to {(ip.ToString() == host ? ip : $"{host} [{ip}]")}");

    if (!Console.IsOutputRedirected)
        Console.Title = $"Trace {(ip.ToString() == host ? ip : $"{host} [{ip}]")}";

    Ex.WriteLine("════╤═════════╤═════════════════╤════════════════════════════════════════");
    Ex.WriteLine("ttl │ ping    │ ip─address      │ host name");
    Ex.WriteLine("────┼─────────┼─────────────────┼────────────────────────────────────────");

    var monitors = new List<PingMonitor>();
    for (var ttl = 1; ttl <= main_options.MaxTtl; ttl++)
    {
        cancellation_token_source.Token.ThrowIfCancellationRequested();

        if (await ip.PingAsync(ttl, main_options.ProbesPerHop, main_options.TimeoutMs, cancellation_token_source.Token).ConfigureAwait(false) is { Address: var response_ip })
        {
            monitors.Add(new(Console.CursorTop, response_ip, !main_options.NoDns, cancellation_token_source.Token));

            Ex.WriteLine($"{ttl,3} │ ---- ms │ {response_ip,-15} │ ");

            if (response_ip.Equals(ip))
                break;
        }
        else
            Ex.WriteLine($"{ttl,3} │         │                 │ no response");
    }

    try
    {
        var monitor_tasks = monitors.Select(m => m.CompleteTask).ToArray();
        await Task.WhenAll(monitor_tasks).ConfigureAwait(false);
    }
    catch
    {
        var faulted_count = monitors.Count(m => m.CompleteTask.IsFaulted);
        var canceled_count = monitors.Count(m => m.CompleteTask.IsCanceled);

        if (faulted_count > 0)
            Ex.WriteLine($"Warning: {faulted_count} monitor task(s) failed");

        if (canceled_count > 0)
            Ex.WriteLine($"Warning: {canceled_count} monitor task(s) canceled");
    }

    Ex.WriteLine("════╧═════════╧═════════════════╧════════════════════════════════════════");
    Ex.WriteLine("End.");

    return 0;
}
catch (OperationCanceledException)
{
    Ex.WriteLine("Canceled by user.");
    return 130;
}
finally
{
    Console.CancelKeyPress -= OnCancelKeyPress;
}

void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs event_args)
{
    event_args.Cancel = true;
    cancellation_token_source.Cancel();
}

internal readonly record struct MainOptions(string? Host, int MaxTtl, int TimeoutMs, int ProbesPerHop, bool NoDns, bool Json, string? ErrorMessage)
{
    public static MainOptions Parse(string[] args)
    {
        string? host = null;
        var max_ttl = 99;
        var timeout_ms = 2000;
        var probes_per_hop = 5;
        var no_dns = false;
        var json = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-v" or "--update" or "--upgrade")
                continue;

            if (arg.Equals("--no-dns", StringComparison.OrdinalIgnoreCase))
            {
                no_dns = true;
                continue;
            }

            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (TryReadIntOption(args, ref i, "--max-ttl", out var max_ttl_value, out var max_ttl_error))
            {
                if (max_ttl_value is < 1 or > 255)
                    return new(null, 0, 0, 0, false, false, "Option --max-ttl must be in range [1..255]");

                max_ttl = max_ttl_value;
                continue;
            }

            if (max_ttl_error is not null)
                return new(null, 0, 0, 0, false, false, max_ttl_error);

            if (TryReadIntOption(args, ref i, "--timeout-ms", out var timeout_ms_value, out var timeout_ms_error))
            {
                if (timeout_ms_value is < 100 or > 60000)
                    return new(null, 0, 0, 0, false, false, "Option --timeout-ms must be in range [100..60000]");

                timeout_ms = timeout_ms_value;
                continue;
            }

            if (timeout_ms_error is not null)
                return new(null, 0, 0, 0, false, false, timeout_ms_error);

            if (TryReadIntOption(args, ref i, "--probes-per-hop", out var probes_per_hop_value, out var probes_per_hop_error))
            {
                if (probes_per_hop_value is < 1 or > 20)
                    return new(null, 0, 0, 0, false, false, "Option --probes-per-hop must be in range [1..20]");

                probes_per_hop = probes_per_hop_value;
                continue;
            }

            if (probes_per_hop_error is not null)
                return new(null, 0, 0, 0, false, false, probes_per_hop_error);

            if (arg.StartsWith("-", StringComparison.Ordinal))
                return new(null, 0, 0, 0, false, false, $"Unknown option: {arg}");

            if (host is not null)
                return new(null, 0, 0, 0, false, false, "Only one host value is allowed");

            host = arg;
        }

        return new(host, max_ttl, timeout_ms, probes_per_hop, no_dns, json, null);
    }

    private static bool TryReadIntOption(string[] args, ref int index, string option_name, out int value, out string? error)
    {
        value = 0;
        error = null;

        var arg = args[index];
        if (arg.Equals(option_name, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Length)
            {
                error = $"Option {option_name} requires value";
                return false;
            }

            index++;
            var raw_value = args[index];
            if (!int.TryParse(raw_value, out value))
            {
                error = $"Option {option_name} expects integer value";
                return false;
            }

            return true;
        }

        var option_prefix = option_name + "=";
        if (arg.StartsWith(option_prefix, StringComparison.OrdinalIgnoreCase))
        {
            var raw_value = arg[option_prefix.Length..];
            if (!int.TryParse(raw_value, out value))
            {
                error = $"Option {option_name} expects integer value";
                return false;
            }

            return true;
        }

        return false;
    }
}

internal class PingMonitor
{
    private readonly TaskCompletionSource _CompletionSource = new();
    private readonly int _Line;
    private readonly IPAddress _Address;
    private readonly bool _ResolveDns;
    private readonly CancellationToken _CancellationToken;

    public PingMonitor(int Line, IPAddress Address, bool ResolveDns, CancellationToken CancellationToken)
    {
        _Line = Line;
        _Address = Address;
        _ResolveDns = ResolveDns;
        _CancellationToken = CancellationToken;
        Start();
    }

    public Task CompleteTask => _CompletionSource.Task;

    private void Start()
    {
        var get_name_task = _ResolveDns ? GetNameAsync() : Task.CompletedTask;
        var get_ping_task = GetPingAsync();

        var total_task = Task.WhenAll(get_name_task, get_ping_task);
        total_task.ContinueWith(GetResult, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void GetResult(Task task)
    {
        if (task.IsFaulted)
            _CompletionSource.TrySetException(task.Exception!);
        else if (task.IsCanceled)
            _CompletionSource.TrySetCanceled();
        else
            _CompletionSource.TrySetResult();
    }

    private async Task GetNameAsync()
    {
        try
        {
            _CancellationToken.ThrowIfCancellationRequested();
            var ip_host_entry = await Dns.GetHostEntryAsync(_Address).WaitAsync(_CancellationToken).ConfigureAwait(false);
            var name = ip_host_entry.HostName;

            Ex.Write(_Line, 34, name);
        }
        catch (SocketException e) when (e is { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData })
        {
            // ignore
        }
    }

    private async Task GetPingAsync()
    {
        const int ping_count = 20;
        var pings = new Task<long>[ping_count];
        for (var i = 0; i < ping_count; i++)
        {
            _CancellationToken.ThrowIfCancellationRequested();
            pings[i] = GetSinglePingAsync();
            await Task.Delay(10, _CancellationToken).ConfigureAwait(false);
        }

        var results = await Task.WhenAll(pings).ConfigureAwait(false);
        var avg = results.Where(r => r >= 0).DefaultIfEmpty(0).Average();

        if (avg == 0) return;
        Ex.Write(_Line, 6, $"{avg,4:f0}");

        async Task<long> GetSinglePingAsync()
        {
            try
            {
                using var ping = new Ping();
                var response = await ping.SendPingAsync(_Address, 1000).WaitAsync(_CancellationToken).ConfigureAwait(false);
                return response.Status == IPStatus.Success ? response.RoundtripTime : -1;
            }
            catch (PingException)
            {
                return -1;
            }
        }
    }
}

internal static class Ex
{
    public static async Task<ResolveAddressResult> ResolveAddressAsync(string address, CancellationToken cancellation_token = default)
    {
        cancellation_token.ThrowIfCancellationRequested();

        if (IPAddress.TryParse(address, out var ip))
            return new(ip, null);

        try
        {
            var entry = await Dns.GetHostEntryAsync(address).WaitAsync(cancellation_token).ConfigureAwait(false);

            if (entry.AddressList.Length == 0)
                return new(null, $"Unknown host ip \"{address}\"");

            ip = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return ip is null
                ? new(null, $"Host \"{address}\" has no IPv4 address")
                : new(ip, null);
        }
        catch (SocketException e) when (e.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
        {
            return new(null, $"Unknown host ip \"{address}\"");
        }
        catch (SocketException e) when (e.SocketErrorCode is SocketError.TryAgain or SocketError.TimedOut)
        {
            return new(null, $"Temporary DNS failure for \"{address}\", try again later");
        }
        catch (SocketException e)
        {
            return new(null, $"DNS resolve error for \"{address}\": {e.SocketErrorCode}");
        }
    }

    public readonly record struct ResolveAddressResult(IPAddress? Address, string? ErrorMessage);

    public readonly record struct TraceHop(int Ttl, string? IpAddress, double? PingMs, string? HostName, bool Responded, bool IsDestination);

    private static readonly Lock __ConsoleLock = new();

    public static void WriteLine(string message)
    {
        lock (__ConsoleLock)
            Console.WriteLine(message);
    }

    public static void Write(int Line, int Col, string str)
    {
        if (Console.IsOutputRedirected)
            return;

        lock (__ConsoleLock)
        {
            var (col, line) = Console.GetCursorPosition();

            Console.SetCursorPosition(Col, Line);

            Console.Write(str);

            Console.SetCursorPosition(col, line);
        }
    }

    public static async Task<PingReply?> PingAsync(this IPAddress ip, int ttl, int count = 5, int timeout_ms = 2000, CancellationToken cancellation_token = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellation_token.ThrowIfCancellationRequested();

            try
            {
                using var ping = new Ping();
                var response = await ping.SendPingAsync(
                    ip,
                    TimeSpan.FromMilliseconds(timeout_ms),
                    options: new(ttl, true)).WaitAsync(cancellation_token).ConfigureAwait(false);

                if (response is { Status: IPStatus.Success or IPStatus.TtlExpired })
                    return response;
            }
            catch (PingException)
            {
                // ignore
            }
        }

        return null;
    }

    public static async Task<List<TraceHop>> TraceAsJsonAsync(IPAddress destination_ip, int max_ttl, int probes_per_hop, int timeout_ms, bool resolve_dns, CancellationToken cancellation_token = default)
    {
        var trace_hops = new List<TraceHop>();
        for (var ttl = 1; ttl <= max_ttl; ttl++)
        {
            cancellation_token.ThrowIfCancellationRequested();

            var hop_response = await destination_ip.PingAsync(ttl, probes_per_hop, timeout_ms, cancellation_token).ConfigureAwait(false);
            if (hop_response is not { Address: { } hop_ip })
            {
                trace_hops.Add(new(ttl, null, null, null, false, false));
                continue;
            }

            var ping_ms = await GetAveragePingAsync(hop_ip, cancellation_token: cancellation_token).ConfigureAwait(false);
            var host_name = resolve_dns ? await TryGetHostNameAsync(hop_ip, cancellation_token).ConfigureAwait(false) : null;
            var is_destination = hop_ip.Equals(destination_ip);

            trace_hops.Add(new(ttl, hop_ip.ToString(), ping_ms, host_name, true, is_destination));
            if (is_destination)
                break;
        }

        return trace_hops;
    }

    public static async Task<string?> TryGetHostNameAsync(IPAddress address, CancellationToken cancellation_token = default)
    {
        try
        {
            cancellation_token.ThrowIfCancellationRequested();
            var ip_host_entry = await Dns.GetHostEntryAsync(address).WaitAsync(cancellation_token).ConfigureAwait(false);
            return ip_host_entry.HostName;
        }
        catch (SocketException e) when (e is { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData })
        {
            return null;
        }
    }

    public static async Task<double?> GetAveragePingAsync(IPAddress address, int ping_count = 20, int timeout_ms = 1000, CancellationToken cancellation_token = default)
    {
        var pings = new Task<long>[ping_count];
        for (var i = 0; i < ping_count; i++)
        {
            cancellation_token.ThrowIfCancellationRequested();
            pings[i] = GetSinglePingAsync();
            await Task.Delay(10, cancellation_token).ConfigureAwait(false);
        }

        var results = await Task.WhenAll(pings).ConfigureAwait(false);
        var avg = results.Where(r => r >= 0).DefaultIfEmpty(0).Average();
        return avg == 0 ? null : avg;

        async Task<long> GetSinglePingAsync()
        {
            try
            {
                using var ping = new Ping();
                var response = await ping.SendPingAsync(address, timeout_ms).WaitAsync(cancellation_token).ConfigureAwait(false);
                return response.Status == IPStatus.Success ? response.RoundtripTime : -1;
            }
            catch (PingException)
            {
                return -1;
            }
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Ex.TraceHop>))]
internal partial class ProgramJsonSerializationContext : JsonSerializerContext
{
}
