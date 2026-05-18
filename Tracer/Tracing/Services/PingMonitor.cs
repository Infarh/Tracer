using System.Net;
using Tracer.ConsoleOutput;

namespace Tracer.Tracing.Services;

/// <summary>Фоновый монитор измерений для строки хопа</summary>
internal sealed class PingMonitor
{
    private readonly TaskCompletionSource _CompletionSource = new();
    private readonly int _Line;
    private readonly IPAddress _Address;
    private readonly bool _ResolveDns;
    private readonly CancellationToken _CancellationToken;

    /// <summary>Создает монитор измерений хопа</summary>
    /// <param name="Line">Строка вывода в консоли</param>
    /// <param name="Address">IP-адрес хопа</param>
    /// <param name="ResolveDns">Признак обратного DNS-запроса</param>
    /// <param name="CancellationToken">Токен отмены</param>
    public PingMonitor(int Line, IPAddress Address, bool ResolveDns, CancellationToken CancellationToken)
    {
        ArgumentNullException.ThrowIfNull(Address);

        _Line = Line;
        _Address = Address;
        _ResolveDns = ResolveDns;
        _CancellationToken = CancellationToken;
        Start();
    }

    /// <summary>Задача завершения монитора</summary>
    public Task CompleteTask => _CompletionSource.Task;

    /// <summary>Запускает фоновый сбор DNS и ping</summary>
    private void Start()
    {
        var get_name_task = _ResolveDns ? GetNameAsync() : Task.CompletedTask;
        var get_ping_task = GetPingAsync();

        var total_task = Task.WhenAll(get_name_task, get_ping_task);
        total_task.ContinueWith(GetResult, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Завершает задачу монитора по итоговому статусу</summary>
    /// <param name="Task">Итоговая задача</param>
    private void GetResult(Task Task)
    {
        if (Task.IsFaulted)
            _CompletionSource.TrySetException(Task.Exception!);
        else if (Task.IsCanceled)
            _CompletionSource.TrySetCanceled();
        else
            _CompletionSource.TrySetResult();
    }

    /// <summary>Получает reverse DNS и пишет результат в строку хопа</summary>
    private async Task GetNameAsync()
    {
        var host_name = await AddressResolver.TryGetHostNameAsync(_Address, _CancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(host_name))
            return;

        ConsoleWriter.Write(_Line, 34, host_name);
    }

    /// <summary>Получает усредненный RTT и пишет результат в строку хопа</summary>
    private async Task GetPingAsync()
    {
        var avg = await PingService.GetAveragePingAsync(_Address, CancellationToken: _CancellationToken).ConfigureAwait(false);
        if (avg is null)
            return;

        ConsoleWriter.Write(_Line, 6, $"{avg,4:f0}");
    }
}
