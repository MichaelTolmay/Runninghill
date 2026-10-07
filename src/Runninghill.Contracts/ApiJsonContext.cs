using System.Text.Json.Serialization;

namespace Runninghill.Contracts;

// Generate the JSON reader/writer during compilation. Native AOT cannot rely on discovering
// unknown model types at runtime, and generated metadata avoids that extra runtime work.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatusResponse))]
public partial class ApiJsonContext : JsonSerializerContext;
