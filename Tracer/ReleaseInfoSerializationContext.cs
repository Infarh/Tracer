using System.Text.Json.Serialization;

namespace Tracer;

[JsonSerializable(typeof(ReleaseInfo))]
public partial class ReleaseInfoSerializationContext : JsonSerializerContext;