using System.Net;
using System.Net.NetworkInformation;
using Tracer.Tracing.Models;

namespace Tracer.Tracing.Services;

/// <summary>Сервис ICMP-измерений</summary>
internal static class PingService
{
    /// <summary>Ищет первый успешный ответ хопа для заданного TTL</summary>
    /// <param name="Ip">Целевой IP-адрес</param>
    /// <param name="Ttl">Текущее TTL</param>
    /// <param name="Count">Количество проб</param>
    /// <param name="TimeoutMs">Таймаут одной пробы в миллисекундах</param>
    /// <param name="CancellationToken">Токен отмены</param>
    /// <returns>Ответ ping или null</returns>
    public static async Task<PingReply?> ProbeHopAsync(IPAddress Ip, int Ttl, int Count = 5, int TimeoutMs = 2000, CancellationToken CancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(Ip);

        if (Ttl is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(Ttl));

        if (Count < 1)
            throw new ArgumentOutOfRangeException(nameof(Count));

        if (TimeoutMs < 1)
            throw new ArgumentOutOfRangeException(nameof(TimeoutMs));

        for (var i = 0; i < Count; i++)
        {
            CancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var ping = new Ping();
                var response = await ping.SendPingAsync(
                        Ip,
                        TimeSpan.FromMilliseconds(TimeoutMs),
                        options: new(Ttl, true))
                    .WaitAsync(CancellationToken)
                    .ConfigureAwait(false);

                if (response is { Status: IPStatus.Success or IPStatus.TtlExpired })
                    return response;
            }
            catch (PingException)
            {
                // Игнорируем отдельные ошибки ping-проб
            }
        }

        return null;
    }

    /// <summary>Считает метрики RTT по серии ping</summary>
    /// <param name="Address">Адрес для проверки</param>
    /// <param name="PingCount">Количество ping-запросов</param>
    /// <param name="TimeoutMs">Таймаут одной ping-операции</param>
    /// <param name="CancellationToken">Токен отмены</param>
    /// <returns>Набор метрик хопа</returns>
    public static async Task<HopMetrics> GetHopMetricsAsync(IPAddress Address, int PingCount = 20, int TimeoutMs = 1000, CancellationToken CancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(Address);

        if (PingCount < 1)
            throw new ArgumentOutOfRangeException(nameof(PingCount));

        if (TimeoutMs < 1)
            throw new ArgumentOutOfRangeException(nameof(TimeoutMs));

        var pings = new Task<long>[PingCount];
        for (var i = 0; i < PingCount; i++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            pings[i] = GetSinglePingAsync();
            await Task.Delay(10, CancellationToken).ConfigureAwait(false);
        }

        var results = await Task.WhenAll(pings).ConfigureAwait(false);
        var successful_pings = results.Where(r => r >= 0).Select(r => (double)r).ToArray();
        var received_count = successful_pings.Length;
        var loss_percent = 100.0 * (PingCount - received_count) / PingCount;

        if (received_count == 0)
            return new(null, null, null, loss_percent, null, PingCount, received_count);

        var min_ping = successful_pings.Min();
        var max_ping = successful_pings.Max();
        var avg_ping = successful_pings.Average();
        var jitter = CalculateJitter(successful_pings);

        return new(min_ping, max_ping, avg_ping, loss_percent, jitter, PingCount, received_count);

        async Task<long> GetSinglePingAsync()
        {
            try
            {
                using var ping = new Ping();
                var response = await ping.SendPingAsync(Address, TimeoutMs).WaitAsync(CancellationToken).ConfigureAwait(false);
                return response.Status == IPStatus.Success ? response.RoundtripTime : -1;
            }
            catch (PingException)
            {
                return -1;
            }
        }

        static double? CalculateJitter(double[] samples)
        {
            if (samples.Length < 2)
                return null;

            var deltas = new double[samples.Length - 1];
            for (var i = 1; i < samples.Length; i++)
                deltas[i - 1] = Math.Abs(samples[i] - samples[i - 1]);

            return deltas.Average();
        }
    }
}
