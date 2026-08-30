# RPC/HTTP：gRPC、JSON Transcoding 与 OpenAPI

[简体中文](Rpc-Http.zh-CN.md) | [English](Rpc-Http.md) | [模块索引](README.zh-CN.md)

Asterloom 以 Protobuf 作为唯一业务契约，同时提供原生 gRPC 和 HTTP/JSON。浏览器通过 Next.js BFF
调用 JSON Transcoding，.NET 应用优先使用强类型原生 gRPC；业务逻辑只实现一次。

## 1. 传输结构

```text
.NET client ── HTTP/2 gRPC ───────────────┐
                                          ├─ Asterloom.Server gRPC service
Browser ── Next.js BFF ── HTTP/JSON ──────┘      + shared auth/audit/error logic
```

每个自定义 RPC 必须在 `.proto` 中声明 `google.api.http`。JSON Route、OpenAPI 和 Kiota Client 都由同一
契约生成，禁止手写一套行为不同的 REST Controller。

## 2. 服务端基线

```csharp
builder.Services.AddGrpc().AddJsonTranscoding();

app.MapGrpcService<MyService>();
```

Asterloom 的 `AddAsterloomRpc()` 还统一注册：

- gRPC JSON Transcoding 与 gRPC Swagger。
- Bearer Authentication、Authorization Interceptor 与 Audit Interceptor。
- 结构化错误和 Request ID。
- Request decompression。
- 默认 4 MiB gRPC 收发消息上限。

大文件不要塞进 Protobuf；使用 [File Storage](File-Storage.zh-CN.md) 的 Signed Transfer URL。

## 3. C# 统一认证 Transport

```csharp
using Asterloom.Sdk.Rpc;

using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    identity.GetAccessTokenAsync);

var authorization = new AsterloomAuthorizationClient(transport.CallInvoker);
using var config = new AsterloomConfigClient(
    transport.HttpClient,
    configOptions);
```

- `CallInvoker` 给原生 gRPC SDK。
- `HttpClient` 给 JSON/Transfer-aware SDK。
- Bearer Handler 只向同 Scheme+Host+Port 的 Asterloom Origin 添加 Token。
- AWS SigV4 URL 不添加 Bearer，避免双重认证。
- HTTP 明文只允许显式开启的 Loopback 开发地址；生产必须 HTTPS。

反向代理必须正确支持 HTTP/2 gRPC、较长请求和 JSON Transcoding；不要把 gRPC 误代理成普通 HTTP/1.1。

## 4. Proto 变更流程

1. 修改或新增 `Proto/Asterloom/<domain>/v1/*.proto`。
2. 所有业务 RPC 添加 HTTP Mapping。
3. Backend 引用生成的 C# 类型并实现 Service。
4. 运行：

```powershell
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

脚本会构建开发 Server、导出 `Docs/Protocol/openapi/asterloom-v1.json`，并用 Kiota 重新生成
`Frontend/lib/api/generated`。Coverage 工具要求每个 Admin RPC 都有 Permission、UI Route、UI Action
Marker 和 E2E Test。

## 5. 错误契约

领域错误映射到标准 gRPC Status：InvalidArgument、NotFound、AlreadyExists、Aborted、
FailedPrecondition、Unauthenticated、PermissionDenied。Rich Status 包含：

- `google.rpc.ErrorInfo`：稳定 `reason/errorCode` 和 `requestId`。
- `google.rpc.BadRequest`：字段校验错误。
- Trailer：`x-asterloom-error-code`、`x-request-id`。
- HTTP 响应同样返回 `X-Request-ID`，可关联 Audit 和 Telemetry。

未处理异常对调用者只显示通用 `internal_error`，详细异常进入服务日志与受控 Telemetry，不能泄漏堆栈。

## 6. API 兼容性

- Package 和 HTTP Path 使用版本 `v1`。
- 不复用已发布 Field Number，不随意改变字段类型或枚举语义。
- 新字段保持向后兼容并提供明确默认语义。
- 写操作使用 `expectedVersion` 处理并发。
- List 使用 `page_size/page_token/query`，不能依赖数据库内部 Offset。
- 长任务、Streaming 或大 Payload 应单独设计，不应强行塞进现有 Unary RPC。

## 7. 浏览器注意事项

浏览器不直接持有 Access Token，也不直接构造 gRPC Client；它只访问同源 `/api/asterloom/*` BFF。
BFF 读取服务端 Session Token，调用 JSON Transcoding，再把受控结果返回浏览器。完整说明见
[Web Console/BFF](Web-Console-Bff.zh-CN.md)。

## 8. 验证

```powershell
dotnet build Backend/Asterloom.sln
dotnet test Backend/Asterloom.sln
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

同时测试原生 gRPC 和 HTTP/JSON，特别是枚举、Timestamp、64 位整数、错误映射、认证和反向代理。

## 9. 相关实现

- RPC 基线：[RpcServiceCollectionExtensions.cs](../../Backend/Asterloom.Module.Rpc/RpcServiceCollectionExtensions.cs)
- 错误映射：[AsterloomExceptionInterceptor.cs](../../Backend/Asterloom.Module.Rpc/Errors/AsterloomExceptionInterceptor.cs)
- 统一 Transport：[AsterloomAuthenticatedTransport.cs](../../Backend/Asterloom.Sdk.Rpc/AsterloomAuthenticatedTransport.cs)
- 协议同步脚本：[Sync-ProtocolArtifacts.ps1](../../Deploy/Scripts/Sync-ProtocolArtifacts.ps1)
- API 覆盖矩阵：[admin-api-coverage.yaml](../Protocol/admin-api-coverage.yaml)
- 标准端点：[standard-endpoints.md](../Protocol/standard-endpoints.md)
