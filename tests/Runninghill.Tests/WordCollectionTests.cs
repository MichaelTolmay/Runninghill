using Runninghill.Application;
using Runninghill.Contracts;
using Xunit;

namespace Runninghill.Tests;

public sealed class WordCollectionTests
{
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

    [Fact]
    public async Task LongWordIsRejectedBeforeStorage()
    {
        var repository = new RecordingRepository();
        await Assert.ThrowsAsync<CollectionException>(() => new WordCollection(repository).CreateAsync(new string('a', 81), "Noun", default));
        Assert.False(repository.WasCalled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task SentenceSizeIsBounded(int count)
    {
        var repository = new RecordingRepository();
        await Assert.ThrowsAsync<CollectionException>(() => new WordCollection(repository).SaveSentenceAsync(Enumerable.Repeat(1L, count).ToArray(), Guid.NewGuid(), default));
        Assert.False(repository.WasCalled);
    }

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

    [Fact]
    public async Task MissingSentenceWordHasActionableConflict()
    {
        var repository = new RecordingRepository { Missing = true };
        var error = await Assert.ThrowsAsync<CollectionException>(() => new WordCollection(repository).SaveSentenceAsync([1], Guid.NewGuid(), default));
        Assert.Equal(409, error.StatusCode);
        Assert.Contains("Refresh", error.Message);
    }

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
}

internal sealed class RecordingRepository : IWordRepository
{
    public bool WasCalled, Missing;
    public long[]? WordIds;
    public Guid RequestId;
    public CancellationToken Cancellation;
    public int Take;
    public string? Search;
    public string[]? Types;
    public Task<WordEntry[]> ListAsync(long after, string search, string[] types, int take, CancellationToken cancellation)
    { WasCalled = true; Take = take; Search = search; Types = types; return Task.FromResult(Array.Empty<WordEntry>()); }
    public Task<WordEntry?> GetAsync(long id, CancellationToken cancellation) => Task.FromResult<WordEntry?>(null);
    public Task<WordEntry> CreateAsync(string word, string type, CancellationToken cancellation)
    { WasCalled = true; return Task.FromResult(new WordEntry(1, word, type)); }
    public Task<WordEntry?> UpdateAsync(long id, string word, string type, CancellationToken cancellation) => Task.FromResult<WordEntry?>(null);
    public Task<bool> DeleteAsync(long id, CancellationToken cancellation) => Task.FromResult(false);
    public Task<SentenceEntry?> SaveSentenceAsync(long[] wordIds, Guid requestId, CancellationToken cancellation)
    { WasCalled = true; WordIds = wordIds; RequestId = requestId; Cancellation = cancellation; return Task.FromResult<SentenceEntry?>(Missing ? null : new(1, "test", DateTimeOffset.UtcNow)); }
    public Task<SentenceEntry[]> ListSentencesAsync(long after, int take, CancellationToken cancellation) => Task.FromResult(Array.Empty<SentenceEntry>());
}
