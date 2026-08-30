# Persistence：PostgreSQL、迁移与 Outbox

[简体中文](Persistence.zh-CN.md) | [English](Persistence.md) | [模块索引](README.zh-CN.md)

Persistence 是 Asterloom 各领域模块的持久化基础设施。生产数据写入 PostgreSQL；文件字节写入
S3 兼容对象存储，Redis 只保存 Web BFF 会话，三者不能互相替代。

## 1. Provider 与配置

| Provider | 用途 | 行为 |
| --- | --- | --- |
| `PostgreSql` | 生产、预发布和需要保留数据的开发环境 | 默认值；要求 `ConnectionStrings:Asterloom` |
| `Memory` | 单元测试、临时本地演示 | 进程退出即丢失；同时使用内存对象存储；生产不可用 |

环境变量示例：

```text
Persistence__Provider=PostgreSql
ConnectionStrings__Asterloom=Host=postgres;Port=5432;Database=asterloom;Username=asterloom;Password=...
```

所有 PostgreSQL Store 共用由 `NpgsqlDataSourceBuilder` 创建的 `NpgsqlDataSource`。应用账号应只拥有
Asterloom 数据库所需权限，不使用 PostgreSQL 超级用户运行 Server。

## 2. 数据边界

Asterloom 使用模块拥有的表与 Schema，例如 `platform`、`authorization`、`targeting`、`feature`、
`config`、`release`、`analytics`、`telemetry`、`storage` 和 `infrastructure`。Identity 由自己的持久化
实现与迁移管理。

- 领域模块只通过 Store 接口访问数据，业务服务不拼接跨模块 SQL。
- `storage` 保存 Bucket、Object 和传输状态；对象字节仍在 S3/MinIO。
- `infrastructure` 保存迁移历史、Outbox 和 Audit 等平台基础记录。
- 调用方自己的业务数据不应写入 Asterloom 内部表；请使用自己的数据库或 Schema，并通过公开 API
  关联 Asterloom 的 Tenant/Application/Environment ID。

当前没有“通用数据库 CRUD SDK”。Persistence 是 Asterloom 自身的基础设施能力，不是把数据库
连接暴露给客户端。如果应用需要存储任意文件，使用 [File Storage](File-Storage.zh-CN.md)；需要保存
自己的可查询业务数据，则由应用后端使用 Npgsql 管理自己的数据模型。

## 3. 显式数据库迁移

`Asterloom.Server` 在生产启动时不会自动修改 Schema。发布顺序必须是：

```text
备份 / 变更评审
  -> 运行 Asterloom.Migrations
  -> 迁移成功
  -> 启动或滚动更新 Asterloom.Server
  -> 检查 readiness 与参考应用 doctor
```

本地执行：

```powershell
$env:Persistence__Provider = "PostgreSql"
$env:ConnectionStrings__Asterloom = "Host=localhost;Port=5432;Database=asterloom;Username=asterloom;Password=..."
dotnet run --project Backend/Tools/Asterloom.Migrations
```

迁移工具依次执行模块迁移、Identity 迁移和 Identity Bootstrap。Docker Compose 中由一次性的
`migrations` 服务先运行，成功后 Server 才启动。

## 4. 迁移规则

每个模块通过 `IAsterloomModuleMigration` 提供不可变的 `(ModuleName, Version, Name, Sql)`：

- 迁移按模块名和版本排序，在同一个 PostgreSQL 事务中执行。
- `pg_advisory_xact_lock` 防止两个部署实例并发迁移。
- `infrastructure.schema_migrations` 保存版本与 SQL 的 SHA-256 校验和。
- 已执行迁移的 SQL 被修改时工具会拒绝继续；修复必须新增更高版本迁移。
- 同一模块与版本重复注册、非正版本或空 SQL 会在执行前失败。

生产变更建议使用 Expand → Deploy → Migrate Data → Contract：先增加兼容结构，再发布读写兼容代码，
最后在确认旧版本退出后删除旧结构。

## 5. Outbox 与一致性

Feature、Config 等需要发布变更通知的写操作，会把领域数据和 Outbox Message 放在同一数据库事务中。
后台 `OutboxDispatcher` 读取待处理消息并交给 Consumer；失败会按配置重试。

Outbox 保证“业务提交后事件最终可投递”，不等于所有外部系统的全局事务。Consumer 仍必须幂等，
监控积压、重试次数和最后错误，并为不可恢复消息准备人工处理流程。

## 6. 备份、恢复与运维

- 对 PostgreSQL 做定期全量备份和 WAL/PITR，并实际演练恢复。
- 数据库、S3 和 Redis 的备份策略分别制定；只备份 PostgreSQL 不能恢复文件字节或 Web 会话。
- 使用 UTC 保存时间，展示时再转换时区。
- 写操作携带资源 `version`，版本冲突应重新读取后合并，不能盲目覆盖。
- 通过 `/health/ready`、`/health/startup` 或 Operations Health 检查 PostgreSQL；不要把连接串输出到日志。
- Schema 迁移失败时不要启动新 Server 版本，先保留日志、恢复兼容版本并处理数据库状态。

## 7. 相关实现

- Provider 配置：[AsterloomPersistenceOptions.cs](../../Backend/Asterloom.Module.Infrastructure/Persistence/AsterloomPersistenceOptions.cs)
- Store 注册：[InfrastructureModule.cs](../../Backend/Asterloom.Module.Infrastructure/InfrastructureModule.cs)
- 迁移器：[PostgreSqlDatabaseMigrator.cs](../../Backend/Asterloom.Module.Infrastructure/Persistence/PostgreSqlDatabaseMigrator.cs)
- 迁移入口：[Program.cs](../../Backend/Tools/Asterloom.Migrations/Program.cs)
- 迁移契约：[IAsterloomModuleMigration.cs](../../Backend/Asterloom.Module/Persistence/IAsterloomModuleMigration.cs)
- Outbox Dispatcher：[OutboxDispatcher.cs](../../Backend/Asterloom.Module.Infrastructure/Outbox/OutboxDispatcher.cs)
- Compose：[docker-compose.yml](../../docker-compose.yml)
