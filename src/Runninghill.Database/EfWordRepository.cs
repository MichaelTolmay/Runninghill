using Microsoft.EntityFrameworkCore;
using Runninghill.Application;

namespace Runninghill.Database;

/// <summary>All collection reads and writes go through EF; providers generate the SQL.</summary>
public sealed class EfWordRepository(CollectionContextFactory factory) : IWordRepository
{
    public async Task<WordEntry[]> ListAsync(long after, string search, string[] types, int take, CancellationToken cancellation)
    {
        await using var db = await factory.CreateAsync(cancellation);
        var query = db.Words.AsNoTracking().Where(w => w.Id > after);
        if (search.Length > 0)
        {
            var key = CollectionDbContext.SearchKey(search);
            // StartsWith treats % and _ as literal characters; EF escapes SQL LIKE patterns.
            query = query.Where(w => w.NormalizedWord.StartsWith(key));
        }
        if (types.Length > 0) query = query.Where(w => types.Contains(w.Type));
        return await query.OrderBy(w => w.Id).Take(take)
            .Select(w => new WordEntry(w.Id, w.Word, w.Type)).ToArrayAsync(cancellation);
    }

    public async Task<WordEntry?> GetAsync(long id, CancellationToken cancellation)
    {
        await using var db = await factory.CreateAsync(cancellation);
        return await db.Words.AsNoTracking().Where(w => w.Id == id)
            .Select(w => new WordEntry(w.Id, w.Word, w.Type)).SingleOrDefaultAsync(cancellation);
    }

    public async Task<WordEntry> CreateAsync(string word, string type, CancellationToken cancellation)
    {
        await using var db = await factory.CreateAsync(cancellation);
        var row = new WordRow { Word = word, Type = type, NormalizedWord = CollectionDbContext.SearchKey(word) };
        db.Words.Add(row);
        try { await db.SaveChangesAsync(cancellation); }
        catch (DbUpdateException exception) when (DatabaseFailures.IsUniqueViolation(exception)) { throw DuplicateWord(); }
        return new(row.Id, row.Word, row.Type);
    }

    public async Task<WordEntry?> UpdateAsync(long id, string word, string type, CancellationToken cancellation)
    {
        await using var db = await factory.CreateAsync(cancellation);
        // Attach the complete replacement by key: one UPDATE, with no preliminary SELECT.
        // EF checks affected rows, so a missing/deleted word still produces a friendly 404.
        var row = new WordRow { Id = id, Word = word, Type = type, NormalizedWord = CollectionDbContext.SearchKey(word) };
        db.Entry(row).State = EntityState.Modified;
        try { await db.SaveChangesAsync(cancellation); }
        catch (DbUpdateConcurrencyException) { return null; }
        catch (DbUpdateException exception) when (DatabaseFailures.IsUniqueViolation(exception)) { throw DuplicateWord(); }
        return new(row.Id, row.Word, row.Type);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellation)
    {
        await using var db = await factory.CreateAsync(cancellation);
        // A key-only tracked row avoids downloading the word just to delete it.
        db.Words.Remove(new WordRow { Id = id });
        try { return await db.SaveChangesAsync(cancellation) > 0; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    public async Task<SentenceEntry?> SaveSentenceAsync(long[] wordIds, Guid requestId, CancellationToken cancellation)
    {
        await using var db = await factory.CreateAsync(cancellation);
        var existing = await db.Sentences.AsNoTracking().SingleOrDefaultAsync(s => s.RequestId == requestId, cancellation);
        if (existing is not null) return RetryResult(existing, wordIds);
        var distinctIds = wordIds.Distinct().ToArray();
        // One bounded query obtains a consistent spelling snapshot. Keep repetitions and
        // chosen order in memory; never rely on the order a database returns rows in.
        var words = await db.Words.AsNoTracking().Where(w => distinctIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Word }).ToDictionaryAsync(w => w.Id, w => w.Word, cancellation);
        if (words.Count != distinctIds.Length) return null;
        // Use microsecond precision so retries return identical timestamps on every engine.
        var sentence = new SentenceRow { RequestId = requestId, WordIds = wordIds.ToArray(),
            Text = string.Join(' ', wordIds.Select(id => words[id])), CreatedAt = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc) };
        db.Sentences.Add(sentence);
        try { await db.SaveChangesAsync(cancellation); }
        catch (DbUpdateException exception) when (DatabaseFailures.IsUniqueViolation(exception))
        {
            // Another request may have saved this UUID while we were reading words.
            // Read the winner using a fresh context, not the failed insert's tracked row.
            await using var retry = await factory.CreateAsync(cancellation);
            var winner = await retry.Sentences.AsNoTracking().SingleAsync(s => s.RequestId == requestId, cancellation);
            return RetryResult(winner, wordIds);
        }
        return Result(sentence);
    }

    public async Task<SentenceEntry[]> ListSentencesAsync(long after, int take, CancellationToken cancellation)
    {
        await using var db = await factory.CreateAsync(cancellation);
        var query = db.Sentences.AsNoTracking();
        if (after != 0) query = query.Where(s => s.Id < after);
        var rows = await query.OrderByDescending(s => s.Id).Take(take)
            .Select(s => new { s.Id, s.Text, s.CreatedAt }).ToArrayAsync(cancellation);
        // Some engines return UTC timestamps with Kind=Unspecified. We always store UTC.
        return rows.Select(s => new SentenceEntry(s.Id, s.Text, DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc))).ToArray();
    }
    private static SentenceEntry Result(SentenceRow row) => new(row.Id, row.Text, DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc));
    private static SentenceEntry RetryResult(SentenceRow row, long[] ids)
    {
        if (!row.WordIds.AsSpan().SequenceEqual(ids))
            throw new CollectionException(409, "This sentence request was already used. Start a new sentence before saving different words.");
        return Result(row);
    }
    private static CollectionException DuplicateWord() => new(409, "That word already exists with this type. Choose another word or type.");
}
