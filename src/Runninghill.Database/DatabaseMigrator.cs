using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runninghill.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Runninghill.Database;

/// <summary>Run explicitly during deployment, before starting service replicas.</summary>
public sealed class DatabaseMigrator(CollectionContextFactory factory, DatabaseSettings settings, ILogger<DatabaseMigrator> logger)
{
    public const int SchemaVersion = 3;

    /// <summary>
    /// Applies deployment migrations, fills missing search keys in bounded batches and enforces the
    /// final schema. It can resume an interrupted backfill.
    /// </summary>
    public async Task MigrateAsync(CancellationToken cancellation = default)
    {
        using var operation = new OperationLog(logger, "Database.Migrate", cancellation: cancellation);
        await using var db = await factory.CreateAsync(cancellation);
        db.Database.SetCommandTimeout(120); // Deployment DDL can take longer than an API request.
        if (settings.Provider == DatabaseProvider.PostgreSql)
            await AdoptLegacyPostgresAsync(db, cancellation);
        var migrations = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellation)).ToHashSet();
        var searchMigration = migrations.Single(m => m.EndsWith("_AddSearchKey", StringComparison.Ordinal));
        var readyMigration = migrations.Single(m => m.EndsWith("_RequireSearchKey", StringComparison.Ordinal));
        if (!applied.Contains(readyMigration))
        {
            await db.GetService<IMigrator>().MigrateAsync(searchMigration, cancellation);
            // Backfill in bounded batches using exactly the same Unicode folding as new writes.
            // This can resume after interruption; the last migration enforces NOT NULL + uniqueness.
            var backfillOptions = new DbContextOptionsBuilder<SearchKeyBackfillContext>();
            DatabaseRegistration.Configure(backfillOptions, settings, settings.Provider);
            await using var backfill = new SearchKeyBackfillContext(backfillOptions.Options);
            while (true)
            {
                var rows = await backfill.Set<WordRow>().Where(w => w.NormalizedWord == null).OrderBy(w => w.Id).Take(500).ToArrayAsync(cancellation);
                if (rows.Length == 0) break;
                foreach (var word in rows) word.NormalizedWord = CollectionDbContext.SearchKey(word.Word);
                await backfill.SaveChangesAsync(cancellation);
                // Forget the finished batch so a large upgrade does not keep every row in RAM.
                backfill.ChangeTracker.Clear();
                logger.LogInformation("Search-key backfill batch completed. Rows: {Count}", rows.Length);
            }
        }
        await db.Database.MigrateAsync(cancellation);
        operation.Complete();
    }

    /// <summary>
    /// Recognizes the existing version-2 PostgreSQL schema and records its EF baseline without
    /// replacing collection tables or IDs.
    /// </summary>
    private async Task AdoptLegacyPostgresAsync(CollectionDbContext db, CancellationToken cancellation)
    {
        if ((await db.Database.GetAppliedMigrationsAsync(cancellation)).Any()) return;
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellation) || !await creator.HasTablesAsync(cancellation)) return;
        // The previous release created version 2 via SQL files. Its tables and IDs stay in place.
        // Refuse unrelated databases instead of assuming every existing table belongs to this app.
        var version = await db.Schema.AsNoTracking().Where(s => s.Id == 1).Select(s => s.Version).SingleAsync(cancellation);
        if (version != 2) throw new InvalidOperationException("Only the existing Runninghill version 2 PostgreSQL schema can be adopted. Back up the database and check its schema before migrating.");
        // Also verify the legacy collection tables before recording the initial migration.
        _ = await db.Words.OrderBy(w => w.Id).Select(w => w.Id).Take(1).ToArrayAsync(cancellation);
        _ = await db.Sentences.OrderBy(s => s.Id).Select(s => s.Id).Take(1).ToArrayAsync(cancellation);
        var options = new DbContextOptionsBuilder<LegacyHistoryContext>().UseNpgsql(settings.ConnectionString).Options;
        await using var baseline = new LegacyHistoryContext(options);
        await using var transaction = await baseline.Database.BeginTransactionAsync(cancellation);
        await baseline.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellation);
        baseline.Add(new LegacyHistoryRow { MigrationId = db.Database.GetMigrations().First(), ProductVersion = "10.0.12" });
        await baseline.SaveChangesAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        OperationLog.Event(logger, "LegacyPostgresAdopted");
    }
}

// Only used once when adopting a database made before EF migrations existed.
/// <summary>
/// Represents the initial EF migration-history entry when adopting a database from the earlier
/// release.
/// </summary>
internal sealed class LegacyHistoryRow
{
    public string MigrationId { get; set; } = "";
    public string ProductVersion { get; set; } = "";
}

/// <summary>
/// Creates only the EF migration-history table during adoption of a legacy PostgreSQL database.
/// </summary>
internal sealed class LegacyHistoryContext(DbContextOptions<LegacyHistoryContext> options) : DbContext(options)
{
    /// <summary>
    /// Maps the baseline migration ID and EF product version to the standard history table.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder model)
    {
        var row = model.Entity<LegacyHistoryRow>();
        row.ToTable("__EFMigrationsHistory");
        row.HasKey(r => r.MigrationId);
        row.Property(r => r.MigrationId).HasMaxLength(150);
        row.Property(r => r.ProductVersion).HasMaxLength(32).IsRequired();
    }
}

// During the upgrade this one column is still nullable. The normal application model
// requires it, so use a small transitional model rather than weakening production rules.
/// <summary>
/// Temporarily permits missing search keys while a deployment upgrade fills existing word rows.
/// </summary>
internal sealed class SearchKeyBackfillContext(DbContextOptions<SearchKeyBackfillContext> options) : DbContext(options)
{
    /// <summary>
    /// Maps the existing word table with a nullable search-key column for the migration backfill only.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder model)
    {
        var row = model.Entity<WordRow>();
        row.ToTable("words", Database.IsNpgsql() ? "runninghill" : null);
        row.HasKey(w => w.Id);
        row.Property(w => w.Id).HasColumnName("id");
        row.Property(w => w.Word).HasColumnName("word");
        row.Property(w => w.Type).HasColumnName("type");
        row.Property(w => w.NormalizedWord).HasColumnName("normalized_word").IsRequired(false);
    }
}
