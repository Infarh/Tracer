namespace Tracer.Tracing.Models;

/// <summary>Данные одного хопа трассировки</summary>
internal readonly record struct TraceHop(int Ttl, string? IpAddress, double? PingMs, string? HostName, bool Responded, bool IsDestination);
