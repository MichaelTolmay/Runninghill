using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Runninghill.Application;
using Runninghill.Database;
using Xunit;

namespace Runninghill.Tests;

/// <summary>
/// Checks the storage contract against SQLite and any explicitly configured provider test
/// databases.
/// </summary>
public sealed class DatabaseProviderTests
{
    // SQLite always runs. CI/local container checks add the other providers explicitly.
    /// <summary>
    /// Always supplies SQLite and adds other engines only when their test connection settings are
    /// present.
    /// </summary>
    public static IEnumerable<object[]> Providers()
    {
        yield return ["SQLite"];
        foreach (var provider in new[] { "Postgres", "MSSQL", "MySQL" })
            if (Environment.GetEnvironmentVariable("RUNNINGHILL_TEST_" + provider.ToUpperInvariant()) is { Length: > 0 } connection)
                yield return [provider];
    }

    /// <summary>
    /// Exercises migrations, Unicode lookup, CRUD and concurrent sentence retries against a real
    /// database, including immutable saved wording.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task RealDatabasePreservesCollectionContract(string provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var repository = fixture.Services.GetRequiredService<IWordRepository>();
        var app = new WordCollection(repository);
        var ct = CancellationToken.None;
        Assert.True(await fixture.Services.GetRequiredService<IDatabaseReadiness>().IsReadyAsync());
        var first = await app.CreateAsync("café", "Noun", ct);
        var second = await app.CreateAsync("flies", "Verb", ct);
        var third = await app.CreateAsync("cafe", "Noun", ct); // Accents are meaningful on every engine.
        Assert.Equal(409, (await Assert.ThrowsAsync<CollectionException>(() => app.CreateAsync("CAFÉ", "Noun", ct))).StatusCode);
        Assert.Equal(first.Id, Assert.Single(await app.ListAsync(0, "CAFÉ", null, ct)).Id);
        Assert.Empty(await app.ListAsync(0, "%", null, ct));
        Assert.Empty(await app.ListAsync(0, "_", null, ct));
        Assert.Equal(second.Id, Assert.Single(await app.ListAsync(0, "", "Verb", ct)).Id);
        Assert.Equal(3, (await app.ListAsync(0, "", null, ct)).Length);
        Assert.Equal(new CollectionCounts(3, 0), await app.CountAsync(ct));
        Assert.Equal(new[] { second.Id, third.Id }, (await app.ListAsync(first.Id, "", null, ct)).Select(w => w.Id));
        Assert.Equal("Adjective", (await app.UpdateAsync(third.Id, "cafe", "Adjective", ct)).Type);
        Assert.Equal(409, (await Assert.ThrowsAsync<CollectionException>(() => app.UpdateAsync(third.Id, "CAFÉ", "Noun", ct))).StatusCode);

        var request = Guid.NewGuid();
        var ids = new[] { first.Id, second.Id, first.Id };
        var saves = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => app.SaveSentenceAsync(ids, request, ct)));
        Assert.Single(saves.Select(s => s.Id).Distinct());
        Assert.All(saves, s => Assert.Equal("café flies café", s.Text));
        Assert.Equal(409, (await Assert.ThrowsAsync<CollectionException>(() => app.SaveSentenceAsync([second.Id], request, ct))).StatusCode);
        await app.DeleteAsync(first.Id, ct);
        Assert.Equal(saves[0], await app.SaveSentenceAsync(ids, request, ct));
        Assert.Equal(409, (await Assert.ThrowsAsync<CollectionException>(() => app.SaveSentenceAsync(ids, Guid.NewGuid(), ct))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<CollectionException>(() => app.DeleteAsync(first.Id, ct))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<CollectionException>(() => app.UpdateAsync(first.Id, "missing", "Noun", ct))).StatusCode);
        Assert.Equal(saves[0], Assert.Single(await app.ListSentencesAsync(0, ct)));
        Assert.Equal(new CollectionCounts(2, 1), await app.CountAsync(ct));
        Assert.Empty(await app.ListSentencesAsync(saves[0].Id, ct));
        // A second migration run must be a no-op; persisted rows must remain readable.
        await fixture.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync();
        Assert.Equal(second, await app.GetAsync(second.Id, ct));
    }

    /// <summary>
    /// Verifies that a look-ahead page and its next bookmark return every test word without omissions.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task PagingIsBoundedInTheDatabase(string provider)
    {
        await using var fixture = await Fixture.CreateAsync(provider);
        var app = new WordCollection(fixture.Services.GetRequiredService<IWordRepository>());
        var ids = new List<long>();
        for (var i = 0; i < 53; i++)
            ids.Add((await app.CreateAsync("word" + (char)('a' + i / 26) + (char)('a' + i % 26), "Noun", default)).Id);
        var first = await app.ListAsync(0, "word", null, default);
        Assert.Equal(51, first.Length); // One extra row tells the API there is another page.
        Assert.Equal(ids.Skip(50), (await app.ListAsync(first[49].Id, "word", null, default)).Select(w => w.Id));
    }

    /// <summary>
    /// Verifies that adopting the old PostgreSQL schema preserves word IDs, retry receipts and
    /// duplicate-word rules.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task LegacyPostgresUpgradePreservesIdsAndSentenceRetries(string provider)
    {
        if (provider != "Postgres") return; // Only PostgreSQL existed in the previous release.
        await using var fixture = await Fixture.CreateAsync(provider, migrate: false);
        var settings = fixture.Services.GetRequiredService<DatabaseSettings>();
        var options = new DbContextOptionsBuilder<LegacyTestContext>().UseNpgsql(settings.ConnectionString).Options;
        long wordId;
        var requestId = Guid.NewGuid();
        await using (var legacy = new LegacyTestContext(options))
        {
            // Frozen historical DDL is test input, not a production query or upgrade script.
            await legacy.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/postgres-v2.sql")));
            var word = new WordRow { Word = "café", Type = "Noun" };
            legacy.Words.Add(word);
            await legacy.SaveChangesAsync();
            wordId = word.Id;
            legacy.Sentences.Add(new SentenceRow { RequestId = requestId, WordIds = [wordId, wordId],
                Text = "café café", CreatedAt = DateTime.UtcNow });
            await legacy.SaveChangesAsync();
        }
        var migrator = fixture.Services.GetRequiredService<DatabaseMigrator>();
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        var repository = fixture.Services.GetRequiredService<IWordRepository>();
        Assert.Equal(wordId, Assert.Single(await repository.ListAsync(0, "CAFÉ", [], 51, default)).Id);
        Assert.Equal("café café", (await repository.SaveSentenceAsync([wordId, wordId], requestId, default))!.Text);
        Assert.True((await repository.CreateAsync("next", "Noun", default)).Id > wordId);
        Assert.Equal(409, (await Assert.ThrowsAsync<CollectionException>(() => repository.CreateAsync("CAFÉ", "Noun", default))).StatusCode);
    }

    /// <summary>
    /// Represents the earlier PostgreSQL word table before normalized search keys existed.
    /// </summary>
    private sealed class LegacyTestContext(DbContextOptions<LegacyTestContext> options) : CollectionDbContext(options)
    {
        /// <summary>
        /// Reuses collection mappings but omits the search key to match the legacy test schema.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            model.Entity<WordRow>().Ignore(w => w.NormalizedWord);
        }
    }

    /// <summary>
    /// Checks that rejected database settings explain the problem without echoing a password.
    /// </summary>
    [Theory]
    [InlineData("unknown", "Database=private-password")]
    [InlineData("SQLite", "Data Source=:memory:")]
    [InlineData("MSSQL", "password=private-password")]
    [InlineData("MySQL", "password=private-password")]
    [InlineData("Postgres", "password=private-password")]
    public void InvalidSettingsDoNotLeakCredentials(string provider, string connection)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => DatabaseSettings.Parse(provider, connection));
        Assert.DoesNotContain("private-password", exception.Message);
    }

    /// <summary>
    /// Owns an isolated provider test database and its services, cleaning them up after the test.
    /// </summary>
    private sealed class Fixture(ServiceProvider services, string? file) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        /// <summary>
        /// Creates a fresh test database and optionally migrates it. Non-SQLite databases must have the
        /// dedicated test-name prefix before deletion is permitted.
        /// </summary>
        public static async Task<Fixture> CreateAsync(string provider, bool migrate = true)
        {
            var connection = Environment.GetEnvironmentVariable("RUNNINGHILL_TEST_" + provider.ToUpperInvariant()) ?? "";
            string? file = null;
            if (provider == "SQLite") connection = "Data Source=" + (file = Path.Combine(Path.GetTempPath(), "runninghill-test-" + Guid.NewGuid() + ".db"));
            var settings = DatabaseSettings.Parse(provider, connection);
            // Check the parsed database name, not a substring that might appear in a password.
            var databaseName = settings.Provider switch
            {
                DatabaseProvider.PostgreSql => new Npgsql.NpgsqlConnectionStringBuilder(connection).Database,
                DatabaseProvider.SqlServer => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog,
                DatabaseProvider.MySql => new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(connection).Database,
                _ => null
            };
            if (provider != "SQLite" && databaseName?.StartsWith("runninghill_test_", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Provider tests require a dedicated runninghill_test_* database.");
            var services = new ServiceCollection().AddLogging().AddCollectionDatabase(_ => settings).BuildServiceProvider();
            var fixture = new Fixture(services, file);
            try
            {
                await using var db = await services.GetRequiredService<CollectionContextFactory>().CreateAsync();
                await db.Database.EnsureDeletedAsync();
                if (migrate) await services.GetRequiredService<DatabaseMigrator>().MigrateAsync();
                else await db.GetService<IRelationalDatabaseCreator>().CreateAsync();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        /// <summary>
        /// Deletes the isolated test database, disposes its services and removes the temporary SQLite file
        /// when used.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await using (var db = await services.GetRequiredService<CollectionContextFactory>().CreateAsync())
                await db.Database.EnsureDeletedAsync();
            await services.DisposeAsync();
            if (file is not null) File.Delete(file);
        }
    }
}
