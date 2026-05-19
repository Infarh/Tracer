namespace Tracer.Tracing.Models;

/// <summary>Агрегированные метрики отклика одного хопа</summary>
internal readonly record struct HopMetrics(double? MinPingMs, double? MaxPingMs, double? AvgPingMs, double LossPercent, double? JitterMs, int SentCount, int ReceivedCount);
