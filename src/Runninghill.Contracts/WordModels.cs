namespace Runninghill.Contracts;

/// <summary>Live collection totals and the time at which the service finished reading them.</summary>
public sealed record CollectionStatistics(long Words, long Sentences, DateTimeOffset CheckedAt);

// Public wire shapes are deliberately small. Database details never cross this boundary.
/// <summary>
/// Contains the saved word ID, spelling and type returned to clients.
/// </summary>
public sealed record WordResponse(long Id, string Word, string Type);

/// <summary>
/// Contains the spelling and type supplied when creating or updating a word.
/// </summary>
public sealed record SaveWordRequest(string Word, string Type);

/// <summary>
/// Contains one word page and the last visible ID to use for the next page, or null when there are
/// no more results.
/// </summary>
public sealed record WordPage(WordResponse[] Items, long? NextAfter);

/// <summary>
/// Contains ordered word IDs, including repetitions, and a request ID that makes retries return the
/// original save.
/// </summary>
public sealed record SaveSentenceRequest(long[] WordIds, Guid RequestId);

/// <summary>
/// Contains a saved sentence snapshot, its ID and its creation time.
/// </summary>
public sealed record SentenceResponse(long Id, string Text, DateTimeOffset CreatedAt);

/// <summary>
/// Contains one history page and the bookmark for older sentences, or null when history is
/// exhausted.
/// </summary>
public sealed record SentencePage(SentenceResponse[] Items, long? NextAfter);

/// <summary>
/// Contains a user-facing error title, explanation and optional support reference.
/// </summary>
public sealed record ApiProblem(string? Title, string? Detail, string? RequestId);

// These names are part of the public API. The application still validates every incoming value.
/// <summary>
/// Lists the nine public word-type names used by client controls; the application still validates
/// submitted values.
/// </summary>
public static class WordTypes
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    { "Noun", "Verb", "Adjective", "Adverb", "Pronoun", "Preposition", "Interjection", "Conjunction", "Determiner" });
}
