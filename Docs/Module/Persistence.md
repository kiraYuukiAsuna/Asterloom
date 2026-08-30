# Persistence: PostgreSQL, migrations, and the outbox

[English](Persistence.md) | [简体中文](Persistence.zh-CN.md) | [Module index](README.md)

Persistence is the infrastructure behind Asterloom's domain modules. Production records live in PostgreSQL;
file bytes live in S3-compatible object storage, while Redis only holds Web BFF sessions. These stores are not
interchangeable.

## 1. Providers and configuration

| Provider | Intended use | Behavior |
| --- | --- | --- |
| `PostgreSql` | Production, staging, and durable development | Default; requires `ConnectionStrings:Asterloom` |
| `Memory` | Unit tests and temporary local demos | Lost on process exit; also uses in-memory object storage; not for production |

Environment example:

```text
Persistence__Provider=PostgreSql
ConnectionStrings__Asterloom=Host=postgres;Port=5432;Database=asterloom;Username=asterloom;Password=...
```

PostgreSQL stores share an `NpgsqlDataSource` created by `NpgsqlDataSourceBuilder`. Run the Server with a scoped
database role that has only the required Asterloom permissions, not a PostgreSQL superuser.

## 2. Data boundaries

Module-owned tables and schemas include `platform`, `authorization`, `targeting`, `feature`, `config`, `release`,
`analytics`, `telemetry`, `storage`, and `infrastructure`. Identity owns its separate persistence and migration path.

- Domain modules access data through Store interfaces; business services do not issue cross-module SQL.
- `storage` holds bucket, object, and transfer records; object bytes remain in S3/MinIO.
- `infrastructure` holds migration history, outbox, audit, and other platform records.
- Consumer business data must not be inserted into Asterloom's internal tables. Use an application-owned database
  or schema and associate records through public Tenant/Application/Environment IDs.

There is no generic database CRUD SDK. Persistence is Asterloom infrastructure, not a database connection exposed
to clients. Use [File Storage](File-Storage.md) for arbitrary files; use Npgsql from the application's backend with
an application-owned model for queryable business data.

## 3. Explicit database migrations

`Asterloom.Server` does not mutate the production schema at startup. The required deployment order is:

```text
backup / change review
  -> run Asterloom.Migrations
  -> migration succeeds
  -> start or roll out Asterloom.Server
  -> verify readiness and the reference-app doctor
```

Local command:

```powershell
$env:Persistence__Provider = "PostgreSql"
$env:ConnectionStrings__Asterloom = "Host=localhost;Port=5432;Database=asterloom;Username=asterloom;Password=..."
dotnet run --project Backend/Tools/Asterloom.Migrations
```

The tool runs module migrations, Identity migrations, and Identity bootstrap. Docker Compose runs its one-shot
`migrations` service before starting the Server.

## 4. Migration rules

Each module supplies immutable `(ModuleName, Version, Name, Sql)` values through `IAsterloomModuleMigration`:

- Migrations are ordered by module and version and execute in one PostgreSQL transaction.
- `pg_advisory_xact_lock` prevents two deployment instances from migrating concurrently.
- `infrastructure.schema_migrations` records versions and SHA-256 checksums of SQL.
- Editing already-applied SQL is rejected; add a higher migration version instead.
- Duplicate module/version pairs, non-positive versions, and empty SQL fail before execution.

For production, prefer Expand → Deploy → Migrate Data → Contract: add compatible structures first, deploy code
that supports both versions, then remove obsolete structures after old processes have exited.

## 5. Outbox and consistency

Writes such as Feature and Config publication persist their domain changes and outbox messages in the same database
transaction. The background `OutboxDispatcher` claims pending messages and invokes consumers, retrying failures
according to configuration.

The outbox means an event can eventually be delivered after the business commit; it is not a global transaction
across external systems. Consumers must remain idempotent. Monitor backlog, attempt count, and last error, and define
an operator process for permanently failing messages.

## 6. Backup, recovery, and operations

- Use regular full backups plus WAL/PITR and rehearse restoration.
- Define separate backup policies for PostgreSQL, S3, and Redis; PostgreSQL alone cannot restore file bytes or Web sessions.
- Store timestamps in UTC and convert only for presentation.
- Mutations carry a resource `version`; re-read and merge conflicts instead of overwriting blindly.
- Check PostgreSQL through `/health/ready`, `/health/startup`, or Operations Health, without exposing connection strings.
- If a schema migration fails, do not start the new Server version. Preserve logs, keep a compatible release, and repair the database state.

## 7. Implementation references

- Provider options: [AsterloomPersistenceOptions.cs](../../Backend/Asterloom.Module.Infrastructure/Persistence/AsterloomPersistenceOptions.cs)
- Store registration: [InfrastructureModule.cs](../../Backend/Asterloom.Module.Infrastructure/InfrastructureModule.cs)
- Migrator: [PostgreSqlDatabaseMigrator.cs](../../Backend/Asterloom.Module.Infrastructure/Persistence/PostgreSqlDatabaseMigrator.cs)
- Migration entry point: [Program.cs](../../Backend/Tools/Asterloom.Migrations/Program.cs)
- Migration contract: [IAsterloomModuleMigration.cs](../../Backend/Asterloom.Module/Persistence/IAsterloomModuleMigration.cs)
- Outbox dispatcher: [OutboxDispatcher.cs](../../Backend/Asterloom.Module.Infrastructure/Outbox/OutboxDispatcher.cs)
- Compose: [docker-compose.yml](../../docker-compose.yml)
