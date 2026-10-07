using System.Text.Json.Serialization;

namespace Runninghill.Contracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatusResponse))]
public partial class ApiJsonContext : JsonSerializerContext;
