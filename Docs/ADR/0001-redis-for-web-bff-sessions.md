# ADR 0001：使用 Redis 保存 Web BFF 会话

- 状态：Accepted
- 日期：2026-08-30

## 背景

Web Console 必须采用 OIDC Authorization Code + PKCE，同时禁止把 Access Token、
Refresh Token 或可解密为 Token 的会话材料发给浏览器。浏览器只应持有随机、不透明、
HttpOnly 的会话 Cookie。Next.js BFF 也不得直接访问 Asterloom PostgreSQL。

进程内 Map 无法跨实例共享，重启即丢失，并且不支持可靠的 Refresh Token 并发控制；
把 Token 加密后放入 Cookie 仍然不满足“不透明服务端会话”的约束。

## 决策

生产环境使用独立 Redis 保存 BFF 会话和短期 OIDC Login Transaction：

- Cookie 只保存 256-bit 随机会话 ID；Redis Key 使用该 ID 的 SHA-256 摘要。
- Redis 中的 Token 会话载荷再使用独立 256-bit AES-GCM Key 加密。
- Session、Login Transaction 和刷新锁均设置有限 TTL。
- Refresh Token 采用 Redis 分布式锁串行刷新，以配合 Passport 的零重用宽限。
- Redis 使用认证且只暴露在部署内部网络，不发布宿主机端口。
- `memory` Store 只允许本地开发和测试；生产配置检测到它时启动失败。

Redis 仅属于 Web 会话基础设施，不保存 Asterloom 领域数据，也不允许 BFF 绕过 API
访问 PostgreSQL。

## 后果

- Web 可以水平扩展并在实例重启后保留会话。
- Redis 暂时不可用时，BFF 返回明确的 503，而不是无认证转发请求。
- 部署新增一个需要监控、备份策略和凭据轮换的依赖。
- 后续 Identity Session 管理 API 需要通过受控的内部契约索引或撤销这些 BFF
  Session；不得让管理 UI 直接连接 Redis。
