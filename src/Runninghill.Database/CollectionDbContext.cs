using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Runninghill.Database;

// Database rows are private storage details. API models stay independent of EF tracking.
/// <summary>
/// Stores a word and its normalized search key as an EF database row.
/// </summary>
public sealed class WordRow
{
    public long Id { get; set; }
    public string Word { get; set; } = "";
    public string Type { get; set; } = "";
    public string NormalizedWord { get; set; } = "";
}

/// <summary>
/// Stores an immutable sentence snapshot, its ordered source IDs and the request ID used for safe
/// retries.
/// </summary>
public sealed class SentenceRow
{
    public long Id { get; set; }
    public Guid RequestId { get; set; }
    public long[] WordIds { get; set; } = [];
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Stores the schema version that readiness checks compare with the application requirement.
/// </summary>
public sealed class SchemaRow
{
    public int Id { get; set; }
    public int Version { get; set; }
}

/// <summary>
/// Defines the shared word, sentence and schema mappings used by all supported database engines.
/// </summary>
public abstract class CollectionDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<WordRow> Words => Set<WordRow>();
    public DbSet<SentenceRow> Sentences => Set<SentenceRow>();
    public DbSet<SchemaRow> Schema => Set<SchemaRow>();

    // One spelling key for all engines, including SQLite which otherwise folds only ASCII.
    /// <summary>
    /// Creates a normalized uppercase key so word lookup and duplicate checks use the same spelling
    /// rules across database engines.
    /// </summary>
    public static string SearchKey(string word) => word.Normalize().ToUpperInvariant();

    /// <summary>
    /// Maps collection tables, required fields and indexes, and selects provider-specific collations
    /// and word-ID storage.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder model)
    {
        var postgres = Database.IsNpgsql();
        var schema = postgres ? "runninghill" : null;
        var words = model.Entity<WordRow>();
        words.ToTable("words", schema);
        words.HasKey(w => w.Id);
        words.Property(w => w.Id).HasColumnName("id");
        words.Property(w => w.Word).HasColumnName("word").HasMaxLength(80).IsRequired();
        words.Property(w => w.Type).HasColumnName("type").HasMaxLength(20).IsRequired();
        var searchKey = words.Property(w => w.NormalizedWord).HasColumnName("normalized_word").HasMaxLength(160).IsRequired();
        // Binary comparison keeps accent-sensitive equality consistent across providers.
        if (Database.ProviderName == "MySql.EntityFrameworkCore")
            MySql.EntityFrameworkCore.Extensions.MySQLPropertyBuilderExtensions.ForMySQLHasCollation(searchKey, "utf8mb4_bin");
        else searchKey.UseCollation(postgres ? "C" : Database.IsSqlServer() ? "Latin1_General_100_BIN2" : "BINARY");
        words.HasIndex(w => new { w.NormalizedWord, w.Type }).IsUnique().HasDatabaseName("words_normalized_type");
        words.HasIndex(w => new { w.Type, w.Id }).HasDatabaseName("words_type_id");

        var sentences = model.Entity<SentenceRow>();
        sentences.ToTable("sentences", schema);
        sentences.HasKey(s => s.Id);
        sentences.Property(s => s.Id).HasColumnName("id");
        sentences.Property(s => s.RequestId).HasColumnName("request_id");
        sentences.HasIndex(s => s.RequestId).IsUnique().HasDatabaseName("sentences_request_id");
        sentences.Property(s => s.Text).HasColumnName("text").HasMaxLength(4050).IsRequired();
        sentences.Property(s => s.CreatedAt).HasColumnName("created_at");
        var ids = sentences.Property(s => s.WordIds).HasColumnName("word_ids").IsRequired();
        if (!postgres)
        {
            // PostgreSQL already stores bigint[]. Other engines store this bounded snapshot
            // as comma-separated IDs. It is never searched, joined, or exposed as SQL.
            ids.HasConversion(v => EncodeIds(v), v => DecodeIds(v)).HasMaxLength(1050);
            // Arrays are mutable. EF needs to compare their contents and keep a copy;
            // comparing array references would miss an ID changed inside an existing array.
            ids.Metadata.SetValueComparer(new ValueComparer<long[]>(
                (a, b) => a!.SequenceEqual(b!),
                a => a.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)), a => a.ToArray()));
        }
        var marker = model.Entity<SchemaRow>();
        marker.ToTable("schema_info", schema);
        marker.HasKey(s => s.Id);
        marker.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        marker.Property(s => s.Version).HasColumnName("version");
        marker.HasData(new SchemaRow { Id = 1, Version = 3 });
    }

    /// <summary>
    /// Encodes ordered word IDs as invariant comma-separated text for engines without PostgreSQL array
    /// storage.
    /// </summary>
    private static string EncodeIds(long[] ids) => string.Join(',', ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Restores ordered word IDs from the invariant comma-separated value stored by non-PostgreSQL
    /// engines.
    /// </summary>
    private static long[] DecodeIds(string ids) => ids.Split(',').Select(id => long.Parse(id, CultureInfo.InvariantCulture)).ToArray();
}

// Separate context types give each engine its own generated migrations and model cache.
// All runtime queries and entity mappings are shared above.
/// <summary>
/// Identifies the PostgreSQL model and migration set while reusing the shared collection mappings.
/// </summary>
public sealed class PostgresContext(DbContextOptions<PostgresContext> options) : CollectionDbContext(options);

/// <summary>
/// Identifies the SQL Server model and migration set while reusing the shared collection mappings.
/// </summary>
public sealed class SqlServerContext(DbContextOptions<SqlServerContext> options) : CollectionDbContext(options);

/// <summary>
/// Identifies the SQLite model and migration set while reusing the shared collection mappings.
/// </summary>
public sealed class SqliteContext(DbContextOptions<SqliteContext> options) : CollectionDbContext(options);

/// <summary>
/// Identifies the MySQL model and migration set while reusing the shared collection mappings.
/// </summary>
public sealed class MySqlContext(DbContextOptions<MySqlContext> options) : CollectionDbContext(options);
