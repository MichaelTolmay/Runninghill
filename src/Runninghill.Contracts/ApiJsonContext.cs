using System.Text.Json.Serialization;

namespace Runninghill.Contracts;

// Generate the JSON reader/writer during compilation. Native AOT cannot rely on discovering
// unknown model types at runtime, and generated metadata avoids that extra runtime work.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(WordResponse))]
[JsonSerializable(typeof(SaveWordRequest))]
[JsonSerializable(typeof(WordPage))]
[JsonSerializable(typeof(SaveSentenceRequest))]
[JsonSerializable(typeof(SentenceResponse))]
[JsonSerializable(typeof(SentencePage))]
[JsonSerializable(typeof(ApiProblem))]
public partial class ApiJsonContext : JsonSerializerContext;
