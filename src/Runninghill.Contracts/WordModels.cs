namespace Runninghill.Contracts;

// Public wire shapes are deliberately small. Database details never cross this boundary.
public sealed record WordResponse(long Id, string Word, string Type);
public sealed record SaveWordRequest(string Word, string Type);
public sealed record WordPage(WordResponse[] Items, long? NextAfter);
public sealed record SaveSentenceRequest(long[] WordIds, Guid RequestId);
public sealed record SentenceResponse(long Id, string Text, DateTimeOffset CreatedAt);
public sealed record SentencePage(SentenceResponse[] Items, long? NextAfter);
public sealed record ApiProblem(string? Title, string? Detail, string? RequestId);

// These names are part of the public API. The application still validates every incoming value.
public static class WordTypes
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    { "Noun", "Verb", "Adjective", "Adverb", "Pronoun", "Preposition", "Interjection", "Conjunction", "Determiner" });
}
