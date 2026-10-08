using Npgsql;
using NpgsqlTypes;
using Runninghill.Application;

namespace Runninghill.Database;

/// <summary>Short, parameterized queries borrow connections from the shared pool.</summary>
public sealed class PostgresWordRepository(NpgsqlDataSource source) : IWordRepository
{
    public async Task<WordEntry[]> ListAsync(long after, string search, string[] types, int take, CancellationToken cancellation)
    {
        await using var command = Command("""
            SELECT id, word, type FROM runninghill.words
            WHERE id > $1 AND ($2 = '' OR lower(word) LIKE lower($2) ESCAPE '\')
                AND (cardinality($3) = 0 OR type = ANY($3))
            ORDER BY id LIMIT $4
            """);
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = after });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = search.Length == 0 ? "" : search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%" });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = types });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = take });
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var result = new List<WordEntry>(take);
        while (await reader.ReadAsync(cancellation)) result.Add(ReadWord(reader));
        return result.ToArray();
    }

    public async Task<WordEntry?> GetAsync(long id, CancellationToken cancellation)
    {
        await using var command = Command("SELECT id, word, type FROM runninghill.words WHERE id = $1");
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id });
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? ReadWord(reader) : null;
    }

    public async Task<WordEntry> CreateAsync(string word, string type, CancellationToken cancellation) =>
        (await WriteWordAsync("INSERT INTO runninghill.words (word, type) VALUES ($1, $2) RETURNING id, word, type",
            word, type, null, cancellation))!;

    public Task<WordEntry?> UpdateAsync(long id, string word, string type, CancellationToken cancellation) =>
        WriteWordAsync("UPDATE runninghill.words SET word = $1, type = $2 WHERE id = $3 RETURNING id, word, type",
            word, type, id, cancellation);

    private async Task<WordEntry?> WriteWordAsync(string sql, string word, string type, long? id, CancellationToken cancellation)
    {
        await using var command = Command(sql);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = word });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = type });
        if (id.HasValue) command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id.Value });
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            return await reader.ReadAsync(cancellation) ? ReadWord(reader) : null;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // The unique index also protects against two users adding the same word at once.
            throw new CollectionException(409, "That word already exists with this type. Choose another word or type.");
        }
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellation)
    {
        await using var command = Command("DELETE FROM runninghill.words WHERE id = $1");
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id });
        return await command.ExecuteNonQueryAsync(cancellation) != 0;
    }

    public async Task<SentenceEntry?> SaveSentenceAsync(long[] wordIds, Guid requestId, CancellationToken cancellation)
    {
        // A retry must return the original saved sentence even if its words were since deleted.
        await using (var existing = Command("SELECT id, text, created_at, word_ids FROM runninghill.sentences WHERE request_id = $1"))
        {
            existing.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = requestId });
            await using var reader = await existing.ExecuteReaderAsync(cancellation);
            if (await reader.ReadAsync(cancellation))
            {
                if (!reader.GetFieldValue<long[]>(3).AsSpan().SequenceEqual(wordIds))
                    throw new CollectionException(409, "This sentence request was already used. Start a new sentence before saving different words.");
                return ReadSentence(reader);
            }
        }
        // One SQL statement sees one consistent snapshot. Ordinality preserves the chosen order
        // (including repeated words). HAVING prevents a partial sentence if a word was deleted.
        await using var command = Command("""
            INSERT INTO runninghill.sentences (request_id, word_ids, text)
            SELECT $1, $2, string_agg(w.word, ' ' ORDER BY chosen.position)
            FROM unnest($2::bigint[]) WITH ORDINALITY AS chosen(id, position)
            JOIN runninghill.words w ON w.id = chosen.id
            HAVING count(*) = cardinality($2)
            ON CONFLICT (request_id) DO UPDATE SET request_id = EXCLUDED.request_id
                WHERE runninghill.sentences.word_ids = EXCLUDED.word_ids
            RETURNING id, text, created_at
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = requestId });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bigint, Value = wordIds });
        await using var result = await command.ExecuteReaderAsync(cancellation);
        return await result.ReadAsync(cancellation) ? ReadSentence(result) : null;
    }

    public async Task<SentenceEntry[]> ListSentencesAsync(long after, int take, CancellationToken cancellation)
    {
        await using var command = Command("SELECT id, text, created_at FROM runninghill.sentences WHERE ($1 = 0 OR id < $1) ORDER BY id DESC LIMIT $2");
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = after });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = take });
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var result = new List<SentenceEntry>(take);
        while (await reader.ReadAsync(cancellation)) result.Add(ReadSentence(reader));
        return result.ToArray();
    }

    private NpgsqlCommand Command(string sql)
    {
        var command = source.CreateCommand(sql);
        command.CommandTimeout = 5;
        return command;
    }
    private static WordEntry ReadWord(NpgsqlDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    private static SentenceEntry ReadSentence(NpgsqlDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetDateTime(2));
}
