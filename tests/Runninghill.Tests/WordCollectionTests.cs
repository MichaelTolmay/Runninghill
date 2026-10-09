using Runninghill.Application;
using Runninghill.Contracts;
using Xunit;

namespace Runninghill.Tests;

/// <summary>
/// Checks collection validation and storage requests without requiring a database.
/// </summary>
public sealed class WordCollectionTests
{
    /// <summary>
    /// Verifies that invalid spelling or type is rejected before the repository is called.
    /// </summary>
    [Theory]
    [InlineData(null, "Noun")]
    [InlineData("", "Noun")]
    [InlineData("two words", "Noun")]
    [InlineData("abc123", "Noun")]
    [InlineData("hello\u001b", "Noun")]
    [InlineData("---", "Noun")]
    [InlineData("hello", "noun")]
    [InlineData("hello", null)]
    public async Task InvalidWordsNeverReachStorage(string? word, string? type)
    {
        var repository = new RecordingRepository();
        var error = await Assert.ThrowsAsync<CollectionException>(() => new WordCollection(repository).CreateAsync(word, type, default));
        Assert.Equal(400, error.StatusCode);
        Assert.False(repository.WasCalled);
    }

    /// <summary>
    /// Checks all public word types and verifies that whitespace and equivalent accented spellings are
    /// normalized.
    /// </summary>
    [Fact]
    public async Task EveryPublicTypeIsAcceptedAndTextIsNormalized()
    {
        var repository = new RecordingRepository();
        foreach (var type in WordTypes.All)
        {
            var result = await new WordCollection(repository).CreateAsync("  cafe\u0301  ", type, default);
            Assert.Equal("café", result.Word);
            Assert.Equal(type, result.Type);
        }
    }

    /// <summary>
    /// Verifies support for accented, non-Latin and supplementary Unicode letters, plus allowed
    /// punctuation.
    /// </summary>
    [Theory]
    [InlineData("café")]
    [InlineData("can't")]
    [InlineData("well-being")]
    [InlineData("你好")]
    [InlineData("𐐀𐐁")]
    public async Task LettersAcrossLanguagesAreAccepted(string word)
    {
        var saved = await new WordCollection(new RecordingRepository()).CreateAsync(word, "Noun", default);
        Assert.Equal(word, saved.Word);
    }

    /// <summary>
    /// Verifies that an oversized word is rejected before storage is called.
    /// </summary>
    [Fact]
    public async Task LongWordIsRejectedBeforeStorage()
    {
        var repository = new RecordingRepository();
        await Assert.ThrowsAsync<CollectionException>(() => new WordCollection(repository).CreateAsync(new string('a', 81), "Noun", default));
        Assert.False(repository.WasCalled);
    }

    /// <summary>
    /// Verifies that empty sentences and drafts over 50 words are rejected before storage.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task SentenceSizeIsBounded(int count)
    {
        var repository = new RecordingRepository();
        await Assert.ThrowsAsync<CollectionException>(() => new WordCollection(repository).SaveSentenceAsync(Enumerable.Repeat(1L, count).ToArray(), Guid.NewGuid(), default));
        Assert.False(repository.WasCalled);
    }

    /// <summary>
    /// Checks that ordered IDs, repetitions, request ID and cancellation are passed unchanged to
    /// storage.
    /// </summary>
    [Fact]
    public async Task SentencePreservesOrderRepeatedWordsAndCancellation()
    {
        var repository = new RecordingRepository();
        using var cancellation = new CancellationTokenSource();
        long[] ids = [4, 2, 4];
        var key = Guid.NewGuid();
        await new WordCollection(repository).SaveSentenceAsync(ids, key, cancellation.Token);
        Assert.Equal(ids, repository.WordIds);
        Assert.Equal(key, repository.RequestId);
        Assert.Equal(cancellation.Token, repository.Cancellation);
    }

    /// <summary>
    /// Verifies that a missing selected word returns a conflict with guidance to refresh.
    /// </summary>
    [Fact]
    public async Task MissingSentenceWordHasActionableConflict()
    {
        var repository = new RecordingRepository { Missing = true };
        var error = await Assert.ThrowsAsync<CollectionException>(() => new WordCollection(repository).SaveSentenceAsync([1], Guid.NewGuid(), default));
        Assert.Equal(409, error.StatusCode);
        Assert.Contains("Refresh", error.Message);
    }

    /// <summary>
    /// Checks filter cleanup, the 51-row look-ahead limit and rejection of invalid bookmarks or types.
    /// </summary>
    [Fact]
    public async Task ListIsBoundedAndChecksTypes()
    {
        var repository = new RecordingRepository();
        var collection = new WordCollection(repository);
        await collection.ListAsync(8, " ab ", "Noun,Verb", default);
        Assert.Equal(51, repository.Take);
        Assert.Equal("ab", repository.Search);
        Assert.Equal(new[] { "Noun", "Verb" }, repository.Types);
        await Assert.ThrowsAsync<CollectionException>(() => collection.ListAsync(-1, null, null, default));
        await Assert.ThrowsAsync<CollectionException>(() => collection.ListAsync(0, null, "Other", default));
    }

    /// <summary>
    /// Verifies that an incomplete Unicode character produces helpful validation before storage is
    /// called.
    /// </summary>
    [Fact]
    public async Task BrokenUnicodeSearchHasHelpfulValidationInsteadOfAnOutage()
    {
        var repository = new RecordingRepository();
        var invalidText = new string((char)0xD800, 1); // Half of a UTF-16 character.
        var error = await Assert.ThrowsAsync<CollectionException>(() =>
            new WordCollection(repository).ListAsync(0, invalidText, null, default));
        Assert.Equal(400, error.StatusCode);
        Assert.Contains("Retype", error.Message);
        Assert.False(repository.WasCalled);
    }

    /// <summary>
    /// Checks word and search lengths again after Unicode normalization can expand their
    /// representation.
    /// </summary>
    [Fact]
    public async Task NormalizationCannotExpandTextPastTheStorageLimit()
    {
        var repository = new RecordingRepository();
        var collection = new WordCollection(repository);
        // This accent expands to two combining marks under Unicode normalization.
        var word = "a" + new string('\u0344', 79);
        Assert.True(word.Normalize().Length > 80);
        var error = await Assert.ThrowsAsync<CollectionException>(() => collection.CreateAsync(word, "Noun", default));
        Assert.Equal(400, error.StatusCode);
        await Assert.ThrowsAsync<CollectionException>(() => collection.ListAsync(0, word, null, default));
        Assert.False(repository.WasCalled);
    }

    /// <summary>
    /// Verifies that an excessively long type list is rejected without querying storage.
    /// </summary>
    [Fact]
    public async Task OversizedTypeFilterNeverReachesStorage()
    {
        var repository = new RecordingRepository();
        var types = string.Join(',', Enumerable.Repeat("Noun", 10000));
        var error = await Assert.ThrowsAsync<CollectionException>(() =>
            new WordCollection(repository).ListAsync(0, null, types, default));
        Assert.Equal(400, error.StatusCode);
        Assert.False(repository.WasCalled);
    }
}

/// <summary>
/// Records application requests and returns simple test results without touching a database.
/// </summary>
internal sealed class RecordingRepository : IWordRepository
{
    /// <summary>Supplies known scalar totals for endpoint permission and serialization tests.</summary>
    public Task<CollectionCounts> CountAsync(CancellationToken cancellation) => Task.FromResult(new CollectionCounts(12, 3));

    public bool WasCalled, Missing;
    public long[]? WordIds;
    public Guid RequestId;
    public CancellationToken Cancellation;
    public int Take;
    public string? Search;
    public string[]? Types;

    /// <summary>
    /// Records the requested search, types and row limit, then returns an empty word page.
    /// </summary>
    public Task<WordEntry[]> ListAsync(long after, string search, string[] types, int take, CancellationToken cancellation)
    { WasCalled = true; Take = take; Search = search; Types = types; return Task.FromResult(Array.Empty<WordEntry>()); }

    /// <summary>
    /// Returns no word so tests can exercise missing-record behaviour.
    /// </summary>
    public Task<WordEntry?> GetAsync(long id, CancellationToken cancellation) => Task.FromResult<WordEntry?>(null);

    /// <summary>
    /// Records that storage was called and returns the supplied word with a fixed test ID.
    /// </summary>
    public Task<WordEntry> CreateAsync(string word, string type, CancellationToken cancellation)
    { WasCalled = true; return Task.FromResult(new WordEntry(1, word, type)); }

    /// <summary>
    /// Returns no updated word to simulate a missing record.
    /// </summary>
    public Task<WordEntry?> UpdateAsync(long id, string word, string type, CancellationToken cancellation) => Task.FromResult<WordEntry?>(null);

    /// <summary>
    /// Reports that no word was deleted, simulating a missing record.
    /// </summary>
    public Task<bool> DeleteAsync(long id, CancellationToken cancellation) => Task.FromResult(false);

    /// <summary>
    /// Records ordered IDs, request ID and cancellation, then returns a test sentence or a simulated
    /// missing-word result.
    /// </summary>
    public Task<SentenceEntry?> SaveSentenceAsync(long[] wordIds, Guid requestId, CancellationToken cancellation)
    { WasCalled = true; WordIds = wordIds; RequestId = requestId; Cancellation = cancellation; return Task.FromResult<SentenceEntry?>(Missing ? null : new(1, "test", DateTimeOffset.UtcNow)); }

    /// <summary>
    /// Returns an empty history page without reading a database.
    /// </summary>
    public Task<SentenceEntry[]> ListSentencesAsync(long after, int take, CancellationToken cancellation) => Task.FromResult(Array.Empty<SentenceEntry>());
}
