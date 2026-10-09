using System.Text.Json.Serialization;

namespace Runninghill.Contracts;

// Generate the JSON reader/writer during compilation. Native AOT cannot rely on discovering
// unknown model types at runtime, and generated metadata avoids that extra runtime work.
/// <summary>
/// Requests JSON readers and writers at compile time for the public messages, avoiding runtime
/// discovery of their shapes.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(WordResponse))]
[JsonSerializable(typeof(SaveWordRequest))]
[JsonSerializable(typeof(WordPage))]
[JsonSerializable(typeof(SaveSentenceRequest))]
[JsonSerializable(typeof(SentenceResponse))]
[JsonSerializable(typeof(SentencePage))]
[JsonSerializable(typeof(ApiProblem))]
[JsonSerializable(typeof(LogPage))]
public partial class ApiJsonContext : JsonSerializerContext;
