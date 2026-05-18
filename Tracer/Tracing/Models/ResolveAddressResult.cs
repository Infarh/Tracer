using System.Net;

namespace Tracer.Tracing.Models;

/// <summary>Результат разрешения адреса назначения</summary>
internal readonly record struct ResolveAddressResult(IPAddress? Address, string? ErrorMessage);
