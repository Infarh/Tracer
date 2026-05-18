using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

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

var host = args.FirstOrDefault() ?? "ya.ru";

//const string ip_str = "212.164.140.129";

var resolve_address_result = await Ex.ResolveAddressAsync(host);
if (resolve_address_result.Address is not { } ip)
{
    Console.WriteLine(resolve_address_result.ErrorMessage ?? $"Unknown host ip \"{host}\"");
    return 1;
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
for (var ttl = 1; ttl < 100; ttl++)
    if (await ip.PingAsync(ttl) is { Address: var response_ip })
    {
        monitors.Add(new(Console.CursorTop, response_ip));

        Ex.WriteLine($"{ttl,3} │ ---- ms │ {response_ip,-15} │ ");

        if (response_ip.Equals(ip))
            break;
    }
    else
        Ex.WriteLine($"{ttl,3} │         │                 │ no response");


try
{
    var monitor_tasks = monitors.Select(m => m.CompleteTask).ToArray();
    await Task.WhenAll(monitor_tasks);
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

internal class PingMonitor
{
    private readonly TaskCompletionSource _CompletionSource = new();
    private readonly int _Line;
    private readonly IPAddress _Address;

    public PingMonitor(int Line, IPAddress Address)
    {
        _Line = Line;
        _Address = Address;
        Start();
    }

    public Task CompleteTask => _CompletionSource.Task;

    private void Start()
    {
        var get_name_task = GetNameAsync();
        var get_ping_task = GetPingAsync();

        var total_task = Task.WhenAll(get_name_task, get_ping_task);
        total_task.ContinueWith(GetResult);
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
            var ip_host_entry = await Dns.GetHostEntryAsync(_Address).ConfigureAwait(false);
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
            pings[i] = GetSinglePingAsync();
            await Task.Delay(10).ConfigureAwait(false);
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
                var response = await ping.SendPingAsync(_Address, 1000).ConfigureAwait(false);
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
    public static async Task<ResolveAddressResult> ResolveAddressAsync(string address)
    {
        if (IPAddress.TryParse(address, out var ip))
            return new(ip, null);

        try
        {
            var entry = await Dns.GetHostEntryAsync(address);

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

    public static async Task<PingReply?> PingAsync(this IPAddress ip, int ttl, int count = 5)
    {
        for (var i = 0; i < count; i++)
            try
            {
                using var ping = new Ping();
                var response = await ping.SendPingAsync(
                    ip,
                    TimeSpan.FromSeconds(2),
                    options: new(ttl, true)).ConfigureAwait(false);

                if (response is { Status: IPStatus.Success or IPStatus.TtlExpired })
                    return response;
            }
            catch (PingException)
            {
                // ignore
            }

        return null;
    }
}
