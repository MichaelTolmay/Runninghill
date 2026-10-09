namespace Runninghill.Application;

/// <summary>
/// Carries a saved word ID, spelling and type between storage and application code.
/// </summary>
public sealed record WordEntry(long Id, string Word, string Type);

/// <summary>
/// Carries a saved sentence snapshot, its ID and its creation time.
/// </summary>
public sealed record SentenceEntry(long Id, string Text, DateTimeOffset CreatedAt);

// Business code asks this interface to store data. It does not need to know which
// database is selected, how connections work, or how SQL is written.
/// <summary>
/// Defines word and sentence storage operations without tying application rules to a particular
/// database.
/// </summary>
public interface IWordRepository
{
    // The caller asks for one extra row to detect a next page. Results use increasing IDs.
    /// <summary>
    /// Returns at most the requested number of filtered words after the bookmark, in increasing ID
    /// order. The caller may request one extra row to detect another page.
    /// </summary>
    Task<WordEntry[]> ListAsync(long after, string search, string[] types, int take, CancellationToken cancellation);

    /// <summary>
    /// Finds a word by ID, returning null when the word is missing.
    /// </summary>
    Task<WordEntry?> GetAsync(long id, CancellationToken cancellation);

    /// <summary>
    /// Stores a validated word and type and returns the saved word with its assigned ID.
    /// </summary>
    Task<WordEntry> CreateAsync(string word, string type, CancellationToken cancellation);

    /// <summary>
    /// Replaces a saved word with validated values, returning null if the ID no longer exists.
    /// </summary>
    Task<WordEntry?> UpdateAsync(long id, string word, string type, CancellationToken cancellation);

    /// <summary>
    /// Deletes the word with the supplied ID and reports whether it existed. Saved sentence snapshots
    /// remain intact.
    /// </summary>
    Task<bool> DeleteAsync(long id, CancellationToken cancellation);
    // The request ID is a receipt number: retrying it with the same ordered words returns
    // the original saved sentence. A new sentence must get a new request ID.
    /// <summary>
    /// Stores the selected words in order, including repetitions. Reusing a request ID with the same
    /// IDs returns its original snapshot; missing words return null for a new save.
    /// </summary>
    Task<SentenceEntry?> SaveSentenceAsync(long[] wordIds, Guid requestId, CancellationToken cancellation);
    // History runs in the opposite direction: newest ID first, then older IDs after a page.
    /// <summary>
    /// Returns at most the requested number of sentences, newest first. A zero bookmark starts at the
    /// newest sentence; another ID selects older entries.
    /// </summary>
    Task<SentenceEntry[]> ListSentencesAsync(long after, int take, CancellationToken cancellation);
}

// Only these deliberately written messages may be shown to a user.
/// <summary>
/// Carries an expected collection error with a status code and wording deliberately safe for users.
/// </summary>
public sealed class CollectionException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
