using System.Text.Json;

using Tracer.Cli;
using Tracer.ConsoleOutput;
using Tracer.Serialization;
using Tracer.Tracing.Services;

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

    var resolve_address_result = await AddressResolver.ResolveAddressAsync(host, cancellation_token_source.Token).ConfigureAwait(false);
    if (resolve_address_result.Address is not { } ip)
    {
        Console.WriteLine(resolve_address_result.ErrorMessage ?? $"Unknown host ip \"{host}\"");
        return 1;
    }

    if (main_options.Json)
    {
        var hops = await TraceService.TraceAsJsonAsync(
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

    ConsoleWriter.WriteLine($"Trace route to {(ip.ToString() == host ? ip : $"{host} [{ip}]")}");

    if (!Console.IsOutputRedirected)
        Console.Title = $"Trace {(ip.ToString() == host ? ip : $"{host} [{ip}]")}";

    ConsoleWriter.WriteLine("════╤═════════╤═════════════════╤════════════════════════════════════════");
    ConsoleWriter.WriteLine("ttl │ ping    │ ip─address      │ host name");
    ConsoleWriter.WriteLine("────┼─────────┼─────────────────┼────────────────────────────────────────");

    var monitors = new List<PingMonitor>();
    for (var ttl = 1; ttl <= main_options.MaxTtl; ttl++)
    {
        cancellation_token_source.Token.ThrowIfCancellationRequested();

        if (await PingService.ProbeHopAsync(ip, ttl, main_options.ProbesPerHop, main_options.TimeoutMs, cancellation_token_source.Token).ConfigureAwait(false) is { Address: var response_ip })
        {
            monitors.Add(new(Console.CursorTop, response_ip, !main_options.NoDns, cancellation_token_source.Token));

            ConsoleWriter.WriteLine($"{ttl,3} │ ---- ms │ {response_ip,-15} │ ");

            if (response_ip.Equals(ip))
                break;
        }
        else
            ConsoleWriter.WriteLine($"{ttl,3} │         │                 │ no response");
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
            ConsoleWriter.WriteLine($"Warning: {faulted_count} monitor task(s) failed");

        if (canceled_count > 0)
            ConsoleWriter.WriteLine($"Warning: {canceled_count} monitor task(s) canceled");
    }

    ConsoleWriter.WriteLine("════╧═════════╧═════════════════╧════════════════════════════════════════");
    ConsoleWriter.WriteLine("End.");

    return 0;
}
catch (OperationCanceledException)
{
    ConsoleWriter.WriteLine("Canceled by user.");
    return 130;
}
finally
{
    Console.CancelKeyPress -= OnCancelKeyPress;
}

void OnCancelKeyPress(object? Sender, ConsoleCancelEventArgs EventArgs)
{
    EventArgs.Cancel = true;
    cancellation_token_source.Cancel();
}
