# Asterloom 文件存储指南

[简体中文](File-Storage.zh-CN.md) | [English](File-Storage.md) | [模块索引](README.zh-CN.md)

本文说明当前 Asterloom C# 实现中的文件存储模型、Web 管理入口、应用接入方式、传输协议、
权限与生命周期。这里的 File Storage 是受 Asterloom 管理的 S3 兼容对象存储能力，不是
PostgreSQL 文件字段，也不是把 MinIO/S3 管理员账号直接交给业务应用。

## 1. 先建立正确的资源模型

```text
Tenant
  └─ Asterloom logical Bucket
       ├─ 配额、单对象大小、Content-Type 与访问策略
       └─ Object metadata（PostgreSQL）
            └─ Object bytes（S3 / MinIO physical bucket）
```

- Asterloom `Bucket` 是租户内的逻辑资源和策略边界，不要求在 S3 中为每个逻辑 Bucket
  创建同名物理 Bucket。
- 当前 S3 Transport 默认使用一个物理 Bucket `asterloom-objects`，并通过内部 Physical Key
  隔离租户、逻辑 Bucket 和 Object。
- PostgreSQL 保存 Bucket、Object、Upload Session、状态、版本、SHA-256 和 Custom Metadata；
  实际字节保存在 S3 兼容对象存储中。
- Bucket 属于 `Tenant`。Object 可选关联 `ApplicationId` 与 `EnvironmentId`，用于表达归属；
  如果填写 Environment，就必须同时填写对应 Application。
- `ObjectKey` 是 Bucket 内唯一的业务键，例如 `avatars/user-42.png`，不是本机文件路径。

File Storage 适合用户附件、导出文件、图片、模型、应用生成物等大块不可查询字节。需要按字段
查询、事务关联或频繁局部更新的数据应放 PostgreSQL；密码、Token 和私钥应放 Secret Manager。

## 2. Web 中在哪里创建

Storage 侧边栏当前默认进入：

```text
/storage/objects
```

这个页面主要管理 Object，因此第一次使用时容易误以为 Web 只能查看文件。Storage 工作区顶部
还有两个页签：

| 页面 | 路由 | 能力 |
| --- | --- | --- |
| Buckets | `/storage/buckets` | List/Get/Create/Update/Archive/Restore Bucket |
| Objects | `/storage/objects` | List/Get/Upload/Download/Metadata/Copy/Delete Object |

创建 Bucket 的操作顺序：

1. 在侧边栏打开 Storage。
2. 在页面顶部切换到 **Buckets**。
3. 在 **Create bucket** 卡片填写 Key、Display Name、Quota、Maximum Object、Allowed Content Types
   和 Access Policy。
4. 创建后回到 **Objects**，选择该 Bucket，即可从 Web 手工上传和管理对象。

因此，Web 已经覆盖当前全部 Storage Admin API；创建入口只是没有作为独立侧边栏菜单显示。

## 3. 谁负责创建 Bucket，谁负责上传文件

推荐按“控制面”和“数据面”分工：

| 角色 | 推荐职责 |
| --- | --- |
| 平台管理员 / 运维 | 通过 Web 预创建 Bucket，设置配额、对象大小、Content-Type、访问策略和权限 |
| 应用后台 / 受信客户端 | 使用预配置的 `BucketId` 和 Storage SDK/API 上传、下载业务文件 |
| Web 管理后台 | 故障排查、人工上传下载、查看 Metadata、复制和删除对象 |

普通业务应用通常不需要动态创建 Bucket。可以把 `BucketId` 作为应用配置注入，应用只获得
`storage.object.upload`、`storage.object.download` 等最小权限。

如果一个业务确实需要“每个项目动态建 Bucket”，可以授予服务账号 `storage.bucket.create` 并
调用 gRPC/JSON Transcoding API；但当前 `AsterloomStorageClient` 专注于对象上传/下载，没有
封装 Bucket 管理方法，Bucket 管理应使用生成的 API Client 或直接调用 Storage Admin API。

## 4. Bucket 配置含义

| 字段 | 含义 |
| --- | --- |
| Key | 租户内唯一的稳定标识，只允许小写字母、数字、`.`、`_`、`-` |
| Display Name / Description | 管理界面显示信息 |
| Quota | Bucket 已使用字节与上传预留字节的总上限 |
| Maximum Object | 单个对象允许的最大字节数，不能超过 Quota |
| Allowed Content Types | 空列表或 `*/*` 表示全部；支持 `image/*` 等类型通配 |
| Access Policy | `Private` 或 `AuthenticatedRead` |

当前版本中，`AccessPolicy` 会被保存和返回，但下载 RPC 仍统一经过 Authorization
Interceptor 并要求 `storage.object.download`。`AuthenticatedRead` 目前不会生成无需权限的公共
URL，也不会绕过 Casbin 授权；不要把它理解为“互联网上任何已登录用户都可访问”。后续若扩展
终端用户读取策略，可以在不改变 Bucket 数据模型的前提下实现该语义。

未填写 Quota 时服务端默认 10 GiB；未填写 Maximum Object 时默认取 2 GiB 与 Quota 的较小值。
Web 会显式填写这两个值。

## 5. 上传不是一次普通 POST

上传采用三阶段协议。在生产 S3 Transport 中，文件直接传往对象存储，避免大文件经过
Asterloom Server/BFF 中转；内存开发 Transport 的本地 Transfer Endpoint 仍会经过 Server/BFF：

```text
Application
  ├─ 1. Bearer Token → CreateUploadSession(Asterloom API)
  │       └─ 返回短时 Transfer URL、HTTP Method、Required Headers
  ├─ 2. 文件字节 → Transfer URL(S3 / MinIO)
  └─ 3. Bearer Token → CompleteUpload(Asterloom API)
          └─ 服务端检查 Size、Content-Type、SHA-256 后标记 Available
```

### 5.1 Create Upload Session

应用先计算完整文件的字节数和 SHA-256，并提交：

- `BucketId`
- `ObjectKey`
- `FileName`
- `ContentType`
- `SizeBytes`
- 64 位十六进制 `Sha256`
- 可选 `ApplicationId`、`EnvironmentId`、`CustomMetadata`

服务端检查 Bucket 状态、对象键唯一性、Content-Type、单对象上限和剩余配额，并预留空间。
Upload Session 当前有效期为 15 分钟。

### 5.2 Transfer bytes

把文件直接发送到 Ticket 的 `Url`，必须使用 Ticket 返回的 `Method` 和每一个
`RequiredHeader`。这个 URL 可能属于 S3/MinIO，而不是 Asterloom API Origin：

- 不要给它附加 Asterloom Bearer Token。
- 不要假设一定是 `PUT`，以 Ticket 返回值为准。
- 不要修改签名 Header、Content-Type 或请求路径。
- 生产环境应只允许 HTTPS Transfer URL。

### 5.3 Complete Upload

字节传输成功不代表对象已经可用。应用必须调用 Complete；服务端会读取物理对象并核对
Size、Content-Type 和 SHA-256。全部一致后状态才从 `Pending` 变为 `Available`，否则清理无效
字节并把上传标记为 Failed/Expired。

## 6. C# SDK 上传与下载

应用先通过 Passport 获取 Token，并创建统一认证 Transport。`transport.HttpClient` 只用于
Asterloom API；SDK 内部会用不附带 Bearer Token 的 Transfer Client 访问签名 URL。

```csharp
using System.Security.Cryptography;
using Asterloom.Sdk.Storage;

var path = "report.pdf";
var file = new FileInfo(path);

string sha256;
await using (var hashingStream = File.OpenRead(path))
{
    sha256 = Convert.ToHexStringLower(
        await SHA256.HashDataAsync(hashingStream, cancellationToken));
}

using var storage = new AsterloomStorageClient(
    transport.HttpClient,
    new AsterloomStorageClientOptions
    {
        Scope = new AsterloomStorageScope(tenantId),
        // 只允许本机 HTTP 开发环境设置为 true；生产保持 false。
        AllowInsecureTransferUrls = false,
    });

await using var source = File.OpenRead(path);
var stored = await storage.UploadAsync(
    new AsterloomStorageUploadRequest(
        BucketId: documentsBucketId,
        ObjectKey: "reports/monthly-report.pdf",
        FileName: file.Name,
        ContentType: "application/pdf",
        SizeBytes: file.Length,
        Sha256: sha256,
        ApplicationId: applicationId,
        EnvironmentId: environmentId,
        CustomMetadata: new Dictionary<string, string>
        {
            ["document-type"] = "monthly-report",
        }),
    source,
    cancellationToken);

await using var destination = File.Create("downloaded-report.pdf");
await storage.DownloadToAsync(
    stored,
    destination,
    ticketLifetime: TimeSpan.FromMinutes(5),
    cancellationToken: cancellationToken);
```

`UploadAsync` 内部按顺序调用：

1. `CreateUploadSessionAsync`
2. `UploadContentAsync`
3. `CompleteUploadAsync`

`DownloadToAsync` 先向 Asterloom 申请短时 Download Ticket，再直接下载文件，并在写入完成后验证
实际字节数和 SHA-256。Download Ticket 可申请 30 秒到 15 分钟，SDK 默认 5 分钟。

本机使用 HTTP MinIO 时可以临时设置 `AllowInsecureTransferUrls = true`；生产环境不要开启。

## 7. 当前 C# SDK 与 API 的覆盖范围

`AsterloomStorageClient` 当前公开的便捷方法是：

- `CreateUploadSessionAsync`
- `UploadContentAsync`
- `CompleteUploadAsync`
- `UploadAsync`
- `DownloadToAsync`

以下能力已经存在于 gRPC/JSON Transcoding API 和 Web，但尚未封装进上述 Runtime SDK：

- Bucket List/Get/Create/Update/Archive/Restore
- Object List/Get
- Object Metadata Update
- Object Copy/Delete
- 单独创建 Download URL

这不是后端或 Web 缺失，而是 SDK 的定位目前只覆盖最常用的数据传输路径。应用后台需要其他
操作时，可使用 `storage_admin.proto` 生成客户端或调用对应 HTTP Mapping；不应直接改
PostgreSQL 表或绕过 Asterloom 操作物理 Key。

## 8. 权限

| 操作 | Permission |
| --- | --- |
| 查看 Bucket | `storage.bucket.read` |
| 创建 Bucket | `storage.bucket.create` |
| 修改 Bucket | `storage.bucket.update` |
| 归档 / 恢复 Bucket | `storage.bucket.archive` / `storage.bucket.restore` |
| 查看 Object Metadata | `storage.object.read` |
| 修改 Metadata | `storage.object.metadata.update` |
| 创建及完成上传 | `storage.object.upload` |
| 创建下载 Ticket | `storage.object.download` |
| 复制 Object | `storage.object.copy` |
| 删除 Object | `storage.object.delete` |

Transfer URL 本身使用短时签名授权，不使用上述 Bearer Permission；权限检查发生在创建 Ticket
时。Ticket 泄漏后在有效期内可能被使用，因此不要写入日志、Analytics 或 Telemetry Attribute。

## 9. Object 生命周期与并发

```text
Pending ── Complete + integrity verified ──> Available ── Delete ──> Deleted
   └──────── expired / mismatch ──────────> Failed
```

- Object 创建、Metadata 修改和删除使用版本号进行乐观并发控制。
- 同一 Bucket 中不能存在重复 `ObjectKey`。Deleted Metadata 会保留且唯一约束仍生效，因此当前版本
  即使删除后也不能复用原 Key；更新内容应使用版本化的新 Key。
- Delete 会删除物理字节并保留 Deleted Metadata/审计信息；当前没有 Object Restore API。
- Bucket 只有在没有 Pending/Available Object、没有上传预留且对象计数为零时才能 Archive。
- 归档 Bucket 不接受新上传；需要继续使用时先 Restore。

## 10. 与 Desktop Release 的关系

Desktop Release 的 `.nupkg` 也存放在同一个底层对象存储 Transport 中，但 Release 模块会自动
维护系统逻辑 Bucket `release-artifacts`，并额外处理 Artifact 状态、外部签名、Manifest、Channel、
Rollout 和更新决策。

因此：

- 普通业务文件使用 Storage Web/SDK。
- 桌面更新包必须走 Release 的 Artifact Upload 流程。
- 不要手工把 `.nupkg` 上传到普通 Storage Bucket 后期待它自动成为 Release Artifact。

桌面应用完整发布方法见 [Asterloom 桌面自动更新指南](Desktop-Updates.zh-CN.md)。

## 11. 上线检查表

- [ ] 管理员已为应用创建专用逻辑 Bucket，并记录稳定的 `BucketId`。
- [ ] Quota、Maximum Object 和 Allowed Content Types 已按业务边界设置。
- [ ] 应用身份只拥有必要的 Object Permission，默认没有 Bucket 管理权限。
- [ ] 应用在上传前计算 Size 与 SHA-256，并在 Transfer 后调用 Complete。
- [ ] Transfer URL 不携带 Asterloom Bearer Token，也不进入日志。
- [ ] 生产的 `Storage__PublicEndpoint` 可由客户端访问并使用 HTTPS。
- [ ] S3/MinIO 凭据仅配置在 Asterloom Server，不下发到应用。
- [ ] 已验证大文件超时、Ticket 过期、配额不足、Hash 不匹配和重复 Key 的错误处理。
- [ ] 已定义业务删除、保留期和数据备份策略。

## 12. 相关实现

- C# Storage SDK：[AsterloomStorageClient.cs](../../Backend/Asterloom.Sdk.Storage/AsterloomStorageClient.cs)
- SDK 数据模型：[AsterloomStorageModels.cs](../../Backend/Asterloom.Sdk.Storage/AsterloomStorageModels.cs)
- Storage 管理协议：[storage_admin.proto](../../Proto/Asterloom/storage/v1/storage_admin.proto)
- Storage 类型协议：[storage_types.proto](../../Proto/Asterloom/storage/v1/storage_types.proto)
- 服务端业务规则：[StorageManagementService.cs](../../Backend/Asterloom.Module.Storage/StorageManagementService.cs)
- S3 Transport：[S3ObjectStorageTransport.cs](../../Backend/Asterloom.Module.Infrastructure/Storage/S3ObjectStorageTransport.cs)
- Web Storage 工作区：[storage-workspace.tsx](../../Frontend/features/storage/storage-workspace.tsx)
- 总功能指南：[Feature-Guide.zh-CN.md](../Feature-Guide.zh-CN.md)
