# Databases with Entity Framework Core 10

The service supports **PostgreSQL, Microsoft SQL Server (MSSQL), SQLite and MySQL**. Select one database per service deployment. Switching providers selects a different database; it does not copy an existing collection between engines. Clients continue using the same REST/gRPC API.

All application database operations use EF Core: word CRUD, filtering, pagination, sentence snapshots, duplicate protection, readiness and schema migrations. Provider-specific connection settings, mappings and generated migrations live in `Runninghill.Database`. `Runninghill.Application` has no EF dependency.

## Configuration

Set `Database:Provider` and `ConnectionStrings:Runninghill` in service configuration, or use their environment variable forms `Database__Provider` and `ConnectionStrings__Runninghill`. PostgreSQL remains the default for existing installations. Invalid providers and missing database names fail startup without exposing passwords. There is no automatic fallback or in-memory database.

| Provider | Accepted names | Example connection string |
| --- | --- | --- |
| PostgreSQL | `Postgres`, `PostgreSQL` | `Host=localhost;Database=runninghill;Username=runninghill;Password=YOUR_PASSWORD;Timeout=5` |
| SQL Server | `MSSQL`, `SqlServer` | `Server=localhost;Database=runninghill;User ID=runninghill;Password=YOUR_PASSWORD;Encrypt=True` |
| SQLite | `SQLite` | `Data Source=/absolute/writable/path/runninghill.db;Default Timeout=5` |
| MySQL | `MySQL` | `Server=localhost;Database=runninghill;User=runninghill;Password=YOUR_PASSWORD;SslMode=VerifyFull` |

Use deployment secrets for credentials. SQLite's directory must exist and be writable by the service account. Use a persistent local disk; SQLite permits only one writer at a time and is best suited to a single service instance. SQL Server and MySQL certificates must match the configured host and be trusted in production. The development Compose overrides use less strict certificate validation for their isolated database containers; use a least-privilege runtime account and trusted certificates in production.

The providers are `Microsoft.EntityFrameworkCore.SqlServer` and `.Sqlite` 10.0.12, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, and Oracle's `MySql.EntityFrameworkCore` 10.0.9. They all run on EF Core **10.0.12**. Oracle's package supports EF Core 10 on .NET 10 ([package metadata](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.9)).

## Apply migrations before starting the API

Use the application's migration command, rather than `EnsureCreated` or direct `dotnet ef database update`. It also performs the bounded Unicode-key backfill needed by older databases. It never starts HTTP listeners and needs only database settings, not authentication settings.

```sh
# Set Database__Provider and ConnectionStrings__Runninghill in your environment first.
python3 scripts/migrate.py --local
# Or use the published executable with those same settings:
./Runninghill.Service --migrate-database
```

Back up an existing database, stop writers and run **one migration process**. Use a deployment account with schema permissions, then run the API with a more restricted account. Readiness requires schema version 3. Normal API startup never changes the schema. Migrations may be rerun; the backfill resumes in batches of 500 rows. MySQL DDL is not transactional, so a failed schema change may need operator repair before rerunning; do not assume all database engines roll back DDL the same way.

Existing Runninghill PostgreSQL **version 2** databases are adopted into EF migration history without recreating their tables. IDs, words, request UUIDs and sentence snapshots remain intact. A normalized spelling key is added and populated before its unique index is enforced. If old rows become duplicates under the shared Unicode casing rules, migration fails instead of deleting or merging them; resolve the conflicting spellings/types before proceeding. Other pre-existing schemas are not automatically imported. Cross-engine data transfer is a separate operation.

The SQL under `tests/integration/fixtures` is frozen historical test input only. Production setup no longer executes SQL files or uses `psql`.

## Release publishing with ReadyToRun

The service defaults to a **self-contained ReadyToRun** publish in Release. `PublishAot` and `PublishTrimmed` are disabled for this host because EF and its providers need runtime query compilation and metadata. Client AOT settings are unchanged. Debug remains managed and debuggable.

```sh
python3 scripts/build.py publish --target service --rid linux-x64
# Equivalent direct SDK command; no publish-mode overrides are necessary:
dotnet publish src/Runninghill.Service -c Release -r linux-x64 \
  -o artifacts/Release/service/linux-x64
```

ReadyToRun precompiles managed code, includes the .NET runtime in this publish, and still permits JIT compilation for EF queries and runtime optimizations. Its main benefit is startup performance; it does not precompile LINQ into SQL or guarantee faster database operations ([ReadyToRun documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)). The Dockerfile uses the same project settings and a `runtime-deps` base because the runtime is already bundled. EF's NativeAOT support remains experimental ([official limitations](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries)).

## Docker

For the existing PostgreSQL stack:

```sh
python3 scripts/dev-setup.py
# Include the same host override files in each Compose command if you use them.
docker --context default compose build
docker --context default compose up -d --wait database
python3 scripts/migrate.py
docker --context default compose up -d --wait
```

The Linux host-network fallback remains available through `deploy/compose.host-network.yaml`; use `scripts/migrate.py --host-network` for that stack. Existing volumes must be kept.

Alternative development database overrides are `deploy/compose.mysql.yaml`, `deploy/compose.mssql.yaml`, and `deploy/compose.sqlite.yaml`. For example:

```sh
# Set MYSQL_PASSWORD and MYSQL_ROOT_PASSWORD in .env first.
docker --context default compose -f compose.yaml -f deploy/compose.mysql.yaml build
docker --context default compose -f compose.yaml -f deploy/compose.mysql.yaml up -d --wait database
docker --context default compose -f compose.yaml -f deploy/compose.mysql.yaml run --rm --no-deps service --migrate-database
docker --context default compose -f compose.yaml -f deploy/compose.mysql.yaml up -d --wait
```

For SQL Server, substitute `deploy/compose.mssql.yaml` and set `MSSQL_SA_PASSWORD`. That override uses SQL Server Developer edition and accepts its license for development/testing; use an x64 Docker host. Production licensing/configuration must be selected separately.

For SQLite, use `deploy/compose.sqlite.yaml`, omit the `up ... database` step, and run migration followed by `up -d --wait service web`. SQLite uses two running containers because it needs no database server. Its file is stored in the named `sqlite-data` volume. The debug helper still provisions a separate PostgreSQL database by default; configure/run the service directly to debug a different provider.

## Performance and consistency

Reads project only needed columns, use no tracking, and apply cursor filters and page limits in the database. Updates and deletes issue one write without a preceding read. Pooled EF contexts are borrowed for each operation; they are never shared between requests. Provider connections are pooled too. API database commands retain a five-second timeout and cancellation; deployment migrations use longer DDL timeouts.

The spelling key is NFC-normalized and invariant-uppercase in .NET, with binary database comparison. A unique `(normalized_word, type)` index protects concurrent writes and distinguishes accented words on every engine. Prefix search operates on that stored key rather than applying a function to every row. A `(type, id)` index supports type filtering. Paging uses IDs and never requires a full collection count.

Sentence creation reads at most 50 distinct words in one query, then builds an ordered immutable snapshot, including repeated words. A unique request UUID prevents concurrent retries creating duplicates. PostgreSQL keeps its existing `bigint[]` snapshot IDs; the other engines store a bounded serialized ID list. These IDs are not foreign keys because saved sentences must survive word deletion. Timestamps use UTC microsecond precision so immediate replies and retries agree across engines. Transient writes are not automatically retried; clients reuse their request UUID if a sentence save is uncertain.

## Tests and future migrations

```sh
# Includes real SQLite storage; no Docker needed.
python3 scripts/build.py test -c Debug
# Disposable PostgreSQL 17, MySQL 8.4, SQL Server 2022, plus SQLite:
python3 tests/integration/providers.py
# On this Linux host, whose kernel cannot create bridge interfaces:
python3 tests/integration/providers.py --host-network
```

The container test runner uses random credentials and dedicated `runninghill_test_*` databases. It removes only its own containers and volumes. It tests CRUD, accents, duplicates, pagination, concurrent sentence retries and the old PostgreSQL upgrade. It never targets the normal application database.

Each provider has its own context type and generated migrations, while entity mappings and runtime queries are shared. When changing the model, generate and review migrations for all four contexts:

```sh
dotnet tool restore
dotnet ef migrations add YourChange --project src/Runninghill.Database --context PostgresContext --output-dir Migrations/Postgres
# Repeat with SqlServerContext/Migrations/SqlServer, SqliteContext/Migrations/Sqlite,
# and MySqlContext/Migrations/MySql.
```

Keep legacy migration files unchanged once deployed. Extend `DatabaseMigrator` deliberately if a future change requires another data backfill.

## LINQ and query performance

The repository uses LINQ-to-Entities. EF translates filtering, ordering, projection and page limits into parameterized SQL, which the database executes. Only bounded results are materialized; sentence ordering and timestamp conversion then happen in memory. EF already caches query compilation by query shape.

For measured high-frequency reads, `EF.CompileAsyncQuery` can reduce EF's expression-processing/cache-lookup overhead. Those delegates still originate from LINQ and must be kept separate for each provider model. They do not fix a slow SQL plan, missing index, excessive result set or database/network latency. Inspect generated SQL, execution plans and representative load measurements before introducing them. See [EF compiled queries](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries) and [efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying).

Raw SQL through EF, or a lower-overhead mapper such as Dapper, can be useful for a measured bottleneck. Neither is automatically faster for the same SQL and neither is used by this repository. Handwritten queries also require maintaining equivalent behavior across four database dialects. The current path retains EF Core 10 and the existing no-tracking reads, projections, indexes, pooling, bounded pages and single-write updates/deletes.
