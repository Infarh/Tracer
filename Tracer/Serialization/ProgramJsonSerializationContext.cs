using System.Text.Json.Serialization;
using Tracer.Tracing.Models;

namespace Tracer.Serialization;

/// <summary>Контекст сериализации JSON для результата трассировки</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<TraceHop>))]
internal partial class ProgramJsonSerializationContext : JsonSerializerContext
{
}
