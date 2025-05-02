using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;

if (args.Any(a => a == "-v"))
{
    Console.WriteLine(typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
    return -1;
}

var host = args.FirstOrDefault() ?? "ya.ru";

//const string ip_str = "212.164.140.129";

if (await Ex.GetAddressAsync(host) is not { } ip)
{
    Console.WriteLine($"Unknown host ip \"{host}\"");
    return 1;
}

Ex.WriteLine($"trace route to {(ip.ToString() == host ? ip : $"{host} [{ip}]")}");

Ex.WriteLine("----+---------+-----------------+----------------------------------------");
Ex.WriteLine("ttl | ping    | ip-address      | host name");
Ex.WriteLine("----+---------+-----------------+----------------------------------------");

var monitors = new List<PingMonitor>();
for (var ttl = 1; ttl < 100; ttl++)
    if (await ip.PingAsync(ttl) is { Address: var response_ip })
    {
        monitors.Add(new(Console.CursorTop, response_ip));

        Ex.WriteLine($"{ttl,3} | ---- ms | {response_ip,-15} | ");

        if (response_ip.Equals(ip))
            break;
    }

Ex.WriteLine("----+---------+-----------------+----------------------------------------");

await Task.WhenAll(monitors.Select(m => m.CompleteTask));

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
        catch (SocketException e) when (e.SocketErrorCode == SocketError.HostNotFound)
        {
            // ignore
        }
    }

    private async Task GetPingAsync()
    {
        const int ping_count = 10;
        var pings = new Task<long>[ping_count];
        for (var i = 0; i < ping_count; i++)
        {
            pings[i] = Task.Run(() =>
            {
                using var ping = new Ping();
                var response = ping.Send(_Address, 1000);
                return response.Status == IPStatus.Success ? response.RoundtripTime : -1;
            });
            await Task.Delay(10).ConfigureAwait(false);
        }

        var results = await Task.WhenAll(pings).ConfigureAwait(false);
        var avg = results.Where(r => r >= 0).DefaultIfEmpty(0).Average();

        if (avg == 0) return;
        Ex.Write(_Line, 6, $"{avg,4:f0}");
    }
}

internal static class Ex
{
    public static async Task<IPAddress?> GetAddressAsync(string address)
    {
        if (IPAddress.TryParse(address, out var ip))
            return ip;

        try
        {
            var entry = await Dns.GetHostEntryAsync(address);

            if (entry.AddressList.Length == 0)
                return null;

            ip = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return ip;
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.HostNotFound)
        {
            return null;
        }
    }

    private static readonly Lock __ConsoleLock = new();

    public static void WriteLine(string message)
    {
        lock (__ConsoleLock)
            Console.WriteLine(message);
    }

    public static void Write(int Line, int Col, string str)
    {
        lock (__ConsoleLock)
        {
            var line = Console.CursorTop;
            var col = Console.CursorLeft;

            Console.SetCursorPosition(Col, Line);

            Console.Write(str);

            Console.SetCursorPosition(col, line);
        }
    }

    public static async Task<PingReply?> PingAsync(this IPAddress ip, int ttl, int count = 5)
    {
        var cancellation = new CancellationTokenSource(2000);

        var tasks = Enumerable.Range(0, count)
            .Select(_ => Task.Run(async () =>
            {
                if (cancellation.Token.IsCancellationRequested)
                    return null;

                try
                {
                    using var ping1 = new Ping();
                    var response = await ping1.SendPingAsync(
                        ip,
                        TimeSpan.FromSeconds(2),
                        options: new(ttl, true), cancellationToken: cancellation.Token);

                    if (response is { Status: IPStatus.Success or IPStatus.TtlExpired })
                        return response;
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }

                await Task.Delay(2000, CancellationToken.None).ConfigureAwait(false);

                return null;
            }, cancellation.Token));

        var result_task = await Task.WhenAny(tasks).ConfigureAwait(false);

        await cancellation.CancelAsync().ConfigureAwait(false);

        var result = await result_task.ConfigureAwait(false);
        return result;
    }
}
