# Operations：API 目录、OpenAPI 与健康检查

[简体中文](Operations.zh-CN.md) | [English](Operations.md) | [模块索引](README.zh-CN.md)

Operations 提供“当前运行实例暴露了哪些 API、依赖是否健康、规范文件是什么”的只读控制面。它用于
运维与集成发现，不代替业务模块、Telemetry Backend 或部署平台的探针。

## 1. Web 管理

| 页面 | 路由 | 能力 |
| --- | --- | --- |
| API Catalog | `/operations/apis` | 搜索 Service/RPC/HTTP Path，按 `admin`/`runtime` 分类，查看请求响应类型 |
| Health | `/operations/health` | 查看聚合状态、检查耗时、Dependency、Description 与 Tag |

API 页面还可下载当前实例生成的 OpenAPI 3 JSON，并显示 SHA-256。这个文件是基于当前 Server 的
Protobuf HTTP Annotation 和 ASP.NET OpenAPI 元数据生成的运行时快照，适合 Kiota/OpenAPI Generator
以及发布时的契约差异检查。

## 2. Operations Admin API

| RPC | JSON Transcoding | Permission |
| --- | --- | --- |
| `ListApis` | `GET /api/v1/operations/apis` | `operations.api.read` |
| `GetOperationsHealth` | `GET /api/v1/operations/health` | `operations.health.read` |
| `GetOpenApiDocument` | `GET /api/v1/operations/openapi` | `operations.openapi.read` |

`ListApis` 从编译后的 Protobuf Descriptor 发现 Asterloom Service，并返回 Service、RPC、类别、HTTP Method、
Path、Request/Response Type 与 Deprecated 标记。`query` 最多 200 字符，`category` 只能为空、`admin`
或 `runtime`。

OpenAPI 响应包含 `contentType`、JSON `content`、小写 SHA-256 和 `generatedAt`。文档在进程内首次生成后
缓存；发布新 Server 后重新获取，不要长期把运行时响应当成未经版本控制的唯一副本。

## 3. Kubernetes/负载均衡探针

Server 还直接公开三条轻量 HTTP Health Endpoint：

| Endpoint | 当前检查 | 用途 |
| --- | --- | --- |
| `/health/live` | `self` | 进程是否存活；失败时可重启 |
| `/health/ready` | `self` + 带 `ready` Tag 的依赖，当前包含 PostgreSQL | 是否接收流量 |
| `/health/startup` | `self` + 带 `startup` Tag 的依赖，当前包含 PostgreSQL | 冷启动是否完成 |

这些探针不要求 Bearer Token，供容器编排和负载均衡器调用，只返回 ASP.NET Health Check 的简化状态。
需要 Dependency 明细时，使用受权限保护的 Operations Health API。

Kubernetes 示例：

```yaml
livenessProbe:
  httpGet: { path: /health/live, port: 8000 }
readinessProbe:
  httpGet: { path: /health/ready, port: 8000 }
startupProbe:
  httpGet: { path: /health/startup, port: 8000 }
  failureThreshold: 30
  periodSeconds: 2
```

不要因为外部 Analytics/Telemetry Backend 暂时不可用就让主 API 失去 liveness；依赖是否阻断流量应按
实际业务关键性选择 Tag。

## 4. 契约生成与覆盖检查

修改 `.proto` 后执行：

```powershell
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

同步脚本更新 OpenAPI 和 Web Kiota Client。覆盖检查要求：

- 每个自定义 RPC 有 `google.api.http` Mapping，可通过 JSON Transcoding 调用。
- 每个 Admin RPC 有 Permission 映射。
- 每个 Admin RPC 有 Web Route、`data-ui-action` 和 E2E 覆盖声明。

开发环境可使用 `/swagger`；生产根路径不会跳转 Swagger，集成方应使用受权限保护的
`GetOpenApiDocument` 或仓库生成产物。

## 5. 故障排查

1. `/health/live` 失败：先看进程、容器退出原因与启动日志。
2. live 正常但 ready/startup 失败：查看 `/operations/health` 的 Dependency，当前重点检查 PostgreSQL。
3. Web 显示 Backend Unavailable：确认 BFF 的 `ASTERLOOM_BACKEND_URL`、Nginx 路由、Server 容器端口 `8000` 和 Compose 宿主机端口 `60001`。
4. API 返回结构化错误：保留响应中的 `X-Request-ID`，在 Audit、Telemetry 和 Server Log 中关联。
5. API Catalog 缺少 RPC：检查 Proto 是否已编译进入 Protocol Assembly，并重新执行协议同步/覆盖检查。
6. OpenAPI Hash 与预期不符：确认命中的 Server 版本，下载两份 JSON 做语义 Diff。

Health Description 不应包含连接串、密码或内部 Token。Operations 权限也应按最小权限授予，尤其是可能
暴露完整契约的 `operations.openapi.read`。

## 6. 相关实现

- Admin Protocol：[operations_admin.proto](../../Proto/Asterloom/operations/v1/operations_admin.proto)
- Types：[operations_types.proto](../../Proto/Asterloom/operations/v1/operations_types.proto)
- 元数据服务：[OperationsMetadataService.cs](../../Backend/Asterloom.Module.Rpc/Operations/OperationsMetadataService.cs)
- Health Endpoint：[Program.cs](../../Backend/Asterloom.Server/Program.cs)
- API 覆盖工具：[Asterloom.ApiCoverage](../../Backend/Tools/Asterloom.ApiCoverage)
- Web：[operations-workspace.tsx](../../Frontend/features/operations/operations-workspace.tsx)
