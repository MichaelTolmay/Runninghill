using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runninghill.Diagnostics;
using Runninghill.Application;

namespace Runninghill.Database;

/// <summary>All collection reads and writes go through EF; providers generate the SQL.</summary>
public sealed class EfWordRepository(CollectionContextFactory factory, ILogger<EfWordRepository> logger) : IWordRepository
{
    /// <summary>
    /// Runs filtered, ordered and bounded word lookup in the database, returning only the requested
    /// page without EF change tracking.
    /// </summary>
    public async Task<WordEntry[]> ListAsync(long after, string search, string[] types, int take, CancellationToken cancellation)
    {
        using var operation = new OperationLog(logger, "Database.ListAsync", cancellation: cancellation);
        await using var db = await factory.CreateAsync(cancellation);
        // Ask for rows after the last displayed ID, rather than skipping an ever-growing
        // number of rows. Filtering, sorting and Take all run inside the database.
        // AsNoTracking skips edit bookkeeping for rows we are only going to display.
        var query = db.Words.AsNoTracking().Where(w => w.Id > after);
        if (search.Length > 0)
        {
            var key = CollectionDbContext.SearchKey(search);
            // StartsWith treats % and _ as literal characters; EF escapes SQL LIKE patterns.
            query = query.Where(w => w.NormalizedWord.StartsWith(key));
        }
        if (types.Length > 0)
            query = query.Where(w => types.Contains(w.Type));
        var result = await query.OrderBy(w => w.Id).Take(take)
            .Select(w => new WordEntry(w.Id, w.Word, w.Type)).ToArrayAsync(cancellation);
        OperationLog.Record(logger, "WordsListed", null, result.Length);
        return operation.Complete(result);
    }

    /// <summary>
    /// Reads one word by ID without tracking it, returning null if the row is absent.
    /// </summary>
    public async Task<WordEntry?> GetAsync(long id, CancellationToken cancellation)
    {
        using var operation = new OperationLog(logger, "Database.GetAsync", cancellation: cancellation);
        await using var db = await factory.CreateAsync(cancellation);
        var word = await db.Words.AsNoTracking().Where(w => w.Id == id)
            .Select(w => new WordEntry(w.Id, w.Word, w.Type)).SingleOrDefaultAsync(cancellation);
        OperationLog.Record(logger, "WordRead", id, word is null ? 0 : 1);
        operation.Complete(word is null ? "NotFound" : "Completed");
        return word;
    }

    /// <summary>
    /// Inserts a word and its search key, translating a unique-index conflict into a friendly
    /// duplicate-word error.
    /// </summary>
    public async Task<WordEntry> CreateAsync(string word, string type, CancellationToken cancellation)
    {
        using var operation = new OperationLog(logger, "Database.CreateAsync", cancellation: cancellation);
        await using var db = await factory.CreateAsync(cancellation);
        var row = new WordRow { Word = word, Type = type, NormalizedWord = CollectionDbContext.SearchKey(word) };
        db.Words.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellation);
        }
        catch (DbUpdateException exception) when (DatabaseFailures.IsUniqueViolation(exception))
        {
            // Let the unique index settle simultaneous inserts; a pre-check would race.
            throw DuplicateWord();
        }
        OperationLog.Record(logger, "WordCreated", row.Id, 1);
        return operation.Complete(new WordEntry(row.Id, row.Word, row.Type));
    }

    /// <summary>
    /// Updates a word directly by ID without first reading it. Returns null for a missing row and
    /// explains duplicate spelling/type conflicts.
    /// </summary>
    public async Task<WordEntry?> UpdateAsync(long id, string word, string type, CancellationToken cancellation)
    {
        using var operation = new OperationLog(logger, "Database.UpdateAsync", cancellation: cancellation);
        await using var db = await factory.CreateAsync(cancellation);
        // Attach the complete replacement by key: one UPDATE, with no preliminary SELECT.
        // EF checks affected rows, so a missing/deleted word still produces a friendly 404.
        var row = new WordRow { Id = id, Word = word, Type = type, NormalizedWord = CollectionDbContext.SearchKey(word) };
        db.Entry(row).State = EntityState.Modified;
        try
        {
            await db.SaveChangesAsync(cancellation);
        }
        catch (DbUpdateConcurrencyException)
        {
            operation.Complete("NotFound");
            return null;
        }
        catch (DbUpdateException exception) when (DatabaseFailures.IsUniqueViolation(exception))
        {
            throw DuplicateWord();
        }
        OperationLog.Record(logger, "WordUpdated", row.Id, 1);
        return operation.Complete(new WordEntry(row.Id, row.Word, row.Type));
    }

    /// <summary>
    /// Deletes a word by ID without downloading its contents, returning false when another operation
    /// already removed it.
    /// </summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellation)
    {
        using var operation = new OperationLog(logger, "Database.DeleteAsync", cancellation: cancellation);
        await using var db = await factory.CreateAsync(cancellation);
        // A key-only tracked row avoids downloading the word just to delete it.
        db.Words.Remove(new WordRow { Id = id });
        try
        {
            var deleted = await db.SaveChangesAsync(cancellation) > 0;
            OperationLog.Record(logger, "WordDeleted", id, deleted ? 1 : 0);
            return operation.Complete(deleted);
        }
        catch (DbUpdateConcurrencyException)
        {
            operation.Complete("NotFound");
            return false;
        }
    }

    /// <summary>
    /// Saves a spelling snapshot in the requested order, including repetitions. Repeated request IDs
    /// return the original result, including when simultaneous saves race.
    /// </summary>
    public async Task<SentenceEntry?> SaveSentenceAsync(long[] wordIds, Guid requestId, CancellationToken cancellation)
    {
        using var operation = new OperationLog(logger, "Database.SaveSentenceAsync", cancellation: cancellation);
        await using var db = await factory.CreateAsync(cancellation);
        // Check the receipt first. Retrying must work even if its original words were
        // edited or deleted after the first save: the saved sentence is a snapshot.
        var existing = await db.Sentences.AsNoTracking().SingleOrDefaultAsync(s => s.RequestId == requestId, cancellation);
        if (existing is not null)
        {
            var result = RetryResult(existing, wordIds);
            OperationLog.Record(logger, "SentenceRetry", result.Id, wordIds.Length);
            return operation.Complete(result);
        }
        var distinctIds = wordIds.Distinct().ToArray();
        // One bounded query obtains a consistent spelling snapshot. Keep repetitions and
        // chosen order in memory; never rely on the order a database returns rows in.
        var words = await db.Words.AsNoTracking().Where(w => distinctIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Word }).ToDictionaryAsync(w => w.Id, w => w.Word, cancellation);
        if (words.Count != distinctIds.Length) { operation.Complete("MissingSelection"); return null; }
        // Ten .NET clock ticks make one microsecond. Round down to this shared precision
        // so the first reply and later retries have identical timestamps on every engine.
        var sentence = new SentenceRow
        {
            RequestId = requestId,
            WordIds = wordIds.ToArray(),
            Text = string.Join(' ', wordIds.Select(id => words[id])),
            CreatedAt = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc)
        };
        db.Sentences.Add(sentence);
        try { await db.SaveChangesAsync(cancellation); }
        catch (DbUpdateException exception) when (DatabaseFailures.IsUniqueViolation(exception))
        {
            // Another request may have saved this UUID while we were reading words.
            // Read the winner using a fresh context, not the failed insert's tracked row.
            await using var retry = await factory.CreateAsync(cancellation);
            var winner = await retry.Sentences.AsNoTracking().SingleAsync(s => s.RequestId == requestId, cancellation);
            var result = RetryResult(winner, wordIds);
            OperationLog.Record(logger, "SentenceConcurrentRetry", result.Id, wordIds.Length);
            return operation.Complete(result);
        }
        OperationLog.Record(logger, "SentenceSaved", sentence.Id, wordIds.Length);
        return operation.Complete(Result(sentence));
    }

    /// <summary>
    /// Reads a bounded page of history in decreasing ID order and labels returned creation times as
    /// UTC.
    /// </summary>
    public async Task<SentenceEntry[]> ListSentencesAsync(long after, int take, CancellationToken cancellation)
    {
        using var operation = new OperationLog(logger, "Database.ListSentencesAsync", cancellation: cancellation);
        await using var db = await factory.CreateAsync(cancellation);
        var query = db.Sentences.AsNoTracking();
        if (after != 0) query = query.Where(s => s.Id < after);
        // Build the result directly: no temporary row objects or second result array.
        // EF limits rows in SQL, then labels each returned timestamp as UTC in this final
        // projection. Some providers return Kind=Unspecified even though we stored UTC.
        var result = await query.OrderByDescending(s => s.Id).Take(take)
            .Select(s => new SentenceEntry(s.Id, s.Text, DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc)))
            .ToArrayAsync(cancellation);
        OperationLog.Record(logger, "SentencesListed", null, result.Length);
        return operation.Complete(result);
    }

    /// <summary>
    /// Converts a stored sentence into an application result and restores the UTC meaning of its
    /// timestamp.
    /// </summary>
    private static SentenceEntry Result(SentenceRow row) => new(row.Id, row.Text, DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc));

    /// <summary>
    /// Returns a prior sentence save only when the retry contains the same ordered IDs; otherwise
    /// reports a conflicting use of the request ID.
    /// </summary>
    private static SentenceEntry RetryResult(SentenceRow row, long[] ids)
    {
        if (!row.WordIds.AsSpan().SequenceEqual(ids))
            throw new CollectionException(409, "This sentence request was already used. Start a new sentence before saving different words.");
        return Result(row);
    }

    /// <summary>
    /// Creates the standard conflict message for an existing spelling and word type.
    /// </summary>
    private static CollectionException DuplicateWord() => new(409, "That word already exists with this type. Choose another word or type.");
}
