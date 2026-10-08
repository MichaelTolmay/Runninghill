namespace Runninghill.Application;

public sealed record WordEntry(long Id, string Word, string Type);
public sealed record SentenceEntry(long Id, string Text, DateTimeOffset CreatedAt);

// A port: business code asks for storage without knowing SQL or connection details.
public interface IWordRepository
{
    Task<WordEntry[]> ListAsync(long after, string search, string[] types, int take, CancellationToken cancellation);
    Task<WordEntry?> GetAsync(long id, CancellationToken cancellation);
    Task<WordEntry> CreateAsync(string word, string type, CancellationToken cancellation);
    Task<WordEntry?> UpdateAsync(long id, string word, string type, CancellationToken cancellation);
    Task<bool> DeleteAsync(long id, CancellationToken cancellation);
    Task<SentenceEntry?> SaveSentenceAsync(long[] wordIds, Guid requestId, CancellationToken cancellation);
    Task<SentenceEntry[]> ListSentencesAsync(long after, int take, CancellationToken cancellation);
}

// Only these deliberately written messages may be shown to a user.
public sealed class CollectionException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
