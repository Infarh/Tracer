using System.Net;
using Tracer.Tracing.Models;

namespace Tracer.Tracing.Services;

/// <summary>Сервис построения результата трассировки</summary>
internal static class TraceService
{
    /// <summary>Формирует список хопов трассировки для JSON-вывода</summary>
    /// <param name="DestinationIp">IP-адрес назначения</param>
    /// <param name="MaxTtl">Максимальное TTL</param>
    /// <param name="ProbesPerHop">Количество проб на хоп</param>
    /// <param name="TimeoutMs">Таймаут одной пробы</param>
    /// <param name="ResolveDns">Признак включенного reverse DNS</param>
    /// <param name="CancellationToken">Токен отмены</param>
    /// <returns>Список хопов маршрута</returns>
    public static async Task<List<TraceHop>> TraceAsJsonAsync(IPAddress DestinationIp, int MaxTtl, int ProbesPerHop, int TimeoutMs, bool ResolveDns, CancellationToken CancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(DestinationIp);

        if (MaxTtl is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(MaxTtl));

        var trace_hops = new List<TraceHop>();
        for (var ttl = 1; ttl <= MaxTtl; ttl++)
        {
            CancellationToken.ThrowIfCancellationRequested();

            var hop_response = await PingService.ProbeHopAsync(DestinationIp, ttl, ProbesPerHop, TimeoutMs, CancellationToken).ConfigureAwait(false);
            if (hop_response is not { Address: { } hop_ip })
            {
                trace_hops.Add(new(ttl, null, null, null, null, null, null, null, false, false));
                continue;
            }

            var metrics = await PingService.GetHopMetricsAsync(hop_ip, CancellationToken: CancellationToken).ConfigureAwait(false);
            var host_name = ResolveDns ? await AddressResolver.TryGetHostNameAsync(hop_ip, CancellationToken).ConfigureAwait(false) : null;
            var is_destination = hop_ip.Equals(DestinationIp);

            trace_hops.Add(new(
                ttl,
                hop_ip.ToString(),
                metrics.AvgPingMs,
                metrics.MinPingMs,
                metrics.MaxPingMs,
                metrics.LossPercent,
                metrics.JitterMs,
                host_name,
                true,
                is_destination));

            if (is_destination)
                break;
        }

        return trace_hops;
    }
}
