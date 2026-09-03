# Momiya Bilibili × Asterloom 最小业务 Demo

这里只有两个可执行项目：

- `MomiyaBilibiliServer`：验证用户 Access Token，使用 Confidential Client 做业务授权和发信，并把 UP 主订阅存入自己的 PostgreSQL Schema。
- `MomiyaBilibiliClient`：PKCE 登录后完成一次订阅同步，同时使用 Targeting/Feature、Config、Release、Analytics、Telemetry 和 Storage。

同一个 `Subscribe` 业务实现同时暴露原生 gRPC 和 HTTP/JSON Transcoding；客户端用 gRPC 写入，再用 HTTP/JSON 读回并备份，因此没有重复 Controller。

## Asterloom 管理台准备（按顺序执行）

先启动 Asterloom，并确定一个 `Tenant → Application → Environment`。下面所有带作用域选择器的页面都选择这一组资源；把三个 UUID 分别填入 Client 和 Server 的 `Asterloom:TenantId`、`ApplicationId`、`EnvironmentId`。

### 1. API Scope

创建 API Scope：

- Scope：`momiya.bilibili.api`
- Resource/Audience：`momiya-bilibili-api`
- 绑定当前 Application

Server 的 `Asterloom:Audience` 必须填写 `momiya-bilibili-api`；Client 请求的是 Scope `momiya.bilibili.api`。Scope 是客户端请求令牌时使用的名称，Audience 是 Server 验证 Access Token 时使用的资源标识，二者不要填反。

### 2. 用户登录客户端

在 Identity 中创建 Public Native Client：

- Client ID：`momiya-bilibili-client`
- Application：选择当前 Application
- Authorization Code：开启
- Refresh Token：开启
- PKCE：开启并要求 `S256`
- Client Credentials：关闭
- Client Secret：不创建、不配置
- Redirect URI：`http://localhost/`
- Post Logout Redirect URI：`http://localhost/signout-callback-oidc`
- Scopes：`openid profile email roles offline_access asterloom.api momiya.bilibili.api`
- Membership Auto Join：开启
- User Registration：关闭；账号由管理员或现有注册流程创建

这里的 `http://localhost/` 是桌面应用临时回环回调地址，不是业务 Server 地址。Identity SDK 登录时会在本机选择回环监听端口，并按已登记的 localhost 回调完成 PKCE。

### 3. Server Confidential Client

再创建一个绑定同一 Application 的 Confidential Client：

- Client ID：建议 `momiya-bilibili-server`
- Client Credentials：开启
- Authorization Code、Refresh Token：关闭
- Redirect URI、Post Logout Redirect URI：留空
- Membership Auto Join、User Registration：关闭
- Scope：至少允许 `asterloom.api`

创建后立即复制只显示一次的 Secret。Client ID 填入 Server 的 `Asterloom:ServiceClientId`，Secret 只通过 Server 环境变量 `Asterloom__ServiceClientSecret` 注入；不要放进桌面 Client。

### 4. 业务权限与基础用户授权

创建业务 Permission `bilibili.subscription.write`。然后为下面六个 Permission 分别创建一条 Policy：

- `bilibili.subscription.write`
- `feature.flag.evaluate`
- `config.snapshot.read`
- `release.update.check`
- `storage.object.upload`
- `storage.object.download`

每条 Policy 都填写：Effect `Allow`、Subject Type `Any actor`、Subject `*`、Scope 为当前 Application；如需选择 Environment，也选当前 Environment。这样所有已经登录并因 Membership Auto Join 加入 Application 的用户自动获得这些基础能力，不需要为每个 User ID 手工绑定角色。

### 5. Targeting、Feature 与 Dynamic Config

在当前 Application/Environment 创建一个 Segment，例如 Key `momiya-cn-users`，规则为 `region == CN`。然后创建并发布：

- Boolean Feature Flag `bilibili.live-notifications`：默认值可设 `false`，Segment 命中值设 `true`。
- Integer Dynamic Config `bilibili.polling.interval-seconds`：默认值可设 `60`，Segment 命中值可设 `30`。

两项都要完成 Validate/Publish，只有 Draft 没有发布时客户端读不到。后面创建 `stable` Release Draft 时也选择这个 Segment；Client 已固定传入 `region=CN`，因此会命中。

### 6. Storage Bucket

进入 `Storage → Buckets`（`/storage/buckets`），选择目标 Tenant，在 **Create bucket** 填：

| 字段 | 值 |
| --- | --- |
| Bucket key | `momiya-bilibili-subscriptions` |
| Display name | `Momiya Bilibili subscriptions` |
| Description | `Per-user subscription JSON backups` |
| Quota (MiB) | `100` |
| Maximum object (MiB) | `10` |
| Allowed content types | `application/json` |
| Access policy | `Private` |

创建后要配置的是 Bucket 的 UUID，不是 `momiya-bilibili-subscriptions` 这个 Key。当前管理页没有直接显示 UUID；可在浏览器开发者工具的 Network 中查看创建 Bucket 的响应字段 `id`，或在已登录管理台的 Console 执行下面代码，把第一行换成实际 Tenant UUID：

```js
const tenantId = "你的 Tenant UUID";
const result = await fetch(`/api/asterloom/api/v1/tenants/${tenantId}/storage/buckets?pageSize=100&query=momiya-bilibili-subscriptions`).then(async response => {
  if (!response.ok) throw new Error(await response.text());
  return response.json();
});
result.buckets.find(bucket => bucket.key === "momiya-bilibili-subscriptions").id;
```

把结果填入客户端 `Asterloom:StorageBucketId`。运行用户还需通过前述 `Any actor` Policy 获得 `storage.object.upload` 和 `storage.object.download`。成功运行后，`Storage → Objects` 中会出现 `subscriptions/<UUID>.json`；客户端会下载它并逐字节校验。

### 7. Analytics Schema 与 Write Key

进入 `Analytics → Schemas & keys`（`/analytics/schemas`），先选择目标 Tenant、Application、Environment。在 **Create event schema** 填：

| 字段 | 值 |
| --- | --- |
| Event name | `bilibili.subscription.synced` |
| Display name | `Bilibili subscription synced` |
| Retention days | `30` |
| Description | `A user subscription was persisted and backed up` |

JSON Schema 使用：

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": [
    "creatorMid",
    "liveNotifications",
    "pollIntervalSeconds"
  ],
  "properties": {
    "creatorMid": { "type": "string" },
    "liveNotifications": { "type": "boolean" },
    "pollIntervalSeconds": { "type": "integer" }
  }
}
```

`platform`、`version` 属于事件 Context，不要写进 Properties Schema；Actor ID、Session ID 是事件信封字段，也不用写。创建 Schema 后，在同页 **Environment write keys** 中输入 Key name `Momiya Bilibili Client`，点击 **Create write key**，立即复制只显示一次的 Secret，并放入客户端 `Asterloom:AnalyticsWriteKey`（建议用环境变量 `Asterloom__AnalyticsWriteKey`）。这不是 OAuth Client Secret，只能向当前 Application/Environment 写 Analytics，不能读取或管理数据；泄漏时 Rotate。

验证时进入 `Analytics → Explorer`，选择同一作用域并按 Event name `bilibili.subscription.synced` 筛选。应能看到三个 Properties、`platform/version` Context 和当前登录用户的 Actor ID。

### 8. Telemetry Sources 与 OTLP

进入 `Telemetry → Sources & storage`（`/telemetry/sources`），选择同一 Tenant、Application、Environment。分别在 **Register source** 创建：

| 字段 | Client Source | Server Source |
| --- | --- | --- |
| Key | `momiya-bilibili-client` | `momiya-bilibili-server` |
| Display name | `Momiya Bilibili Client` | `Momiya Bilibili Server` |
| Service name | `momiya.bilibili.client` | `momiya.bilibili.server` |
| Description | `Interactive desktop client` | `Business API server` |
| Resource attributes | `{}` | `{}` |

Service name 必须逐字匹配代码里的点分名称；不要填成 Key，也不要填 ActivitySource 名 `Momiya.Bilibili.Client/Server`。Source 只是控制面登记，不会替应用安装 SDK，也不会把下方 Settings 自动推送给正在运行的进程。

在同页 **Sampling and database storage** 将 Sampling ratio 设为 `1`，Diagnostics base URL 留空，并勾选 Traces、Metrics、Logs。两个进程仍需配置实际的 Collector 地址；本 Demo 的两个 `appsettings.json` 已包含这些值。

远端 Collector 只在服务器回环地址 `127.0.0.1:60006` 接收 HTTP OTLP，不暴露公网端口。运行 Demo 前保持下面的 SSH 隧道开启：

```bash
ssh -N -L 4318:127.0.0.1:60006 -p 2222 root@&lt;server-ip&gt;
```

客户端完成同步后会产生 `subscription.sync` Trace、`momiya.bilibili.syncs` Metric 和一条 Log；服务端会产生 `subscription.upsert` Trace、`momiya.bilibili.subscriptions` Metric 和一条 Log。Collector 会将三类信号写入 PostgreSQL，并保留最近 7 天数据。

进入 `Telemetry → Stored signals`（`/telemetry/signals`），选择对应作用域后切换 Traces、Metrics、Logs；可按服务名、Trace ID、关键字和时间范围查询，并点开记录查看属性及原始 OTLP 载荷。`Telemetry → Health & errors` 继续用于 Collector 健康和 Asterloom 技术错误。

### 9. SMTP 与 Server 发信权限

创建 SMTP Account 并记录其 UUID，填入 Server 的 `Asterloom:SmtpAccountId`。单独创建 Server Application Role，只加入 `mail.delivery.send`，然后把该 Role 绑定到第 3 步 Confidential Client 的 Client ID `momiya-bilibili-server`。

这是一次服务身份绑定，不是给每个业务用户绑定。用户只获得订阅等前台权限；真正发信由 Server 使用自己的 Client Credentials 调用 Asterloom。

### 10. Release、RSA 签名与发布

本 Demo 当前程序集版本是 `1.0.0`，因此创建一个 `1.1.0 / osx-arm64 / Full` Release 即可验证更新判断、Manifest 签名、Artifact 签名、SHA-256 和下载。它只下载并校验更新包，不执行真实安装替换。

暂不验证自动更新时，把 Client 的 `Asterloom:Release:Enabled` 设为 `false`；此时不会查询 Channel，也不要求公钥和 Fingerprint。

#### 10.1 创建 stable Channel

进入 `Releases → Channels`（`/channels`），选择同一 Tenant、Application、Environment，在 **Create channel** 填：

| 字段 | 值 |
| --- | --- |
| Channel key | `stable` |
| Display name | `Stable` |
| Description | `Stable releases for Momiya Bilibili` |

#### 10.2 生成 RSA 密钥并登记公钥

在本项目根目录执行：

```bash
mkdir -p .secrets artifacts/publish-1.1.0 artifacts/release-1.1.0

openssl genpkey \
  -algorithm RSA \
  -pkeyopt rsa_keygen_bits:3072 \
  -out .secrets/momiya-release-private.pem

chmod 600 .secrets/momiya-release-private.pem

openssl pkey \
  -in .secrets/momiya-release-private.pem \
  -pubout \
  -out MomiyaBilibiliClient/release-public-key.pem
```

`.secrets/` 已被 Git 忽略。私钥只能保留在本机受控位置、CI Secret Store 或 HSM，绝不能上传到 Asterloom、提交到 Git 或复制进 Client。`release-public-key.pem` 是 SubjectPublicKeyInfo 格式的公钥，可以分发给 Client。

进入 `Releases → Artifacts & keys`（`/artifacts`）的 **Signing trust store → Register public key**，填写：

| 字段 | 值 |
| --- | --- |
| Key | `momiya-bilibili-release` |
| Display name | `Momiya Bilibili Release` |
| Public key PEM | 完整粘贴 `MomiyaBilibiliClient/release-public-key.pem`，包括 BEGIN/END 行 |

登记后后台会显示 64 位小写 SHA-256 Fingerprint。把它原样填入 Client 的 `Asterloom:Release:SigningKeyFingerprint`；`PublicKeyPemPath` 保持 `release-public-key.pem`。

#### 10.3 构建一个真正的 Velopack Full Artifact

先把与 Asterloom 仓库一致的 Velopack CLI 安装到已忽略的本地 `artifacts/tools`；只需安装一次：

```bash
dotnet tool install --tool-path artifacts/tools vpk --version 1.2.0
```

然后从本项目根目录构建 `1.1.0`：

```bash
dotnet publish MomiyaBilibiliClient/MomiyaBilibiliClient.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:Version=1.1.0 \
  -p:InformationalVersion=1.1.0 \
  -o artifacts/publish-1.1.0

artifacts/tools/vpk pack \
  --packId momiya-bilibili-client \
  --packVersion 1.1.0 \
  --packDir artifacts/publish-1.1.0 \
  --mainExe MomiyaBilibiliClient \
  --channel stable \
  --runtime osx-arm64 \
  --outputDir artifacts/release-1.1.0 \
  --delta None
```

需要上传的是 `artifacts/release-1.1.0/momiya-bilibili-client-1.1.0-stable-full.nupkg`，不是 Setup `.pkg` 或 Portable `.zip`。Demo 阶段出现“未做 Apple code signing/notarization”的警告不影响 Asterloom 签名链验证；生产安装包仍必须另外做 Apple 签名与公证。

#### 10.4 对 Artifact SHA-256 文本签名并上传

Asterloom 的签名输入不是 Artifact 原始字节，也不是十六进制解码后的 32 字节 Digest，而是“小写 64 位 SHA-256 文本”的 UTF-8 字节。算法固定为 RSA-PSS-SHA256，Salt 长度 32 字节：

```bash
momiya_artifact='artifacts/release-1.1.0/momiya-bilibili-client-1.1.0-stable-full.nupkg'
momiya_artifact_sha=$(openssl dgst -sha256 "$momiya_artifact" | awk '{print $2}')
momiya_artifact_signature=$(
  printf '%s' "$momiya_artifact_sha" |
    openssl dgst -sha256 \
      -sign .secrets/momiya-release-private.pem \
      -sigopt rsa_padding_mode:pss \
      -sigopt rsa_pss_saltlen:32 |
    openssl base64 -A
)
printf 'SHA-256: %s\nSignature: %s\n' "$momiya_artifact_sha" "$momiya_artifact_signature"
```

回到 `/artifacts`，选择 **Advanced upload**，填写：

| 字段 | 值 |
| --- | --- |
| Artifact file | 上面的 `*-full.nupkg` |
| Release version | `1.1.0` |
| Target runtime | `osx-arm64` |
| Artifact kind | `Full package` |
| Delta from version | 留空 |
| Content type | `application/octet-stream` |
| Signing key | `Momiya Bilibili Release` |
| Detached signature | 上面输出的 Base64 `Signature` |

页面计算出的 SHA-256 必须与命令输出完全一致。点击 **Create upload ticket**，再点击 **Upload and verify**；只有 Artifact 状态变为 `Verified` 才继续。

#### 10.5 创建、验证并签名 Release Manifest

进入 `Releases → Releases`（`/releases`），在 **Create release draft** 填：

| 字段 | 值 |
| --- | --- |
| Channel | `Stable (stable)` |
| Semantic Version | `1.1.0` |
| Display name | `Momiya Bilibili 1.1.0` |
| Minimum client version | `0.0.0` |
| Rollout basis points | `100000`（100%） |
| Target segment | 第 5 步的 `momiya-cn-users` |
| Release notes | `Verify the complete signed release flow` |
| Verified artifacts | 勾选 `1.1.0 / osx-arm64 / full` |
| Mandatory update | 不勾选 |

创建 Draft 后点击 **Validate release**。确认没有 Error，复制页面显示的 Candidate Manifest SHA-256，然后用同一私钥对这个“小写 SHA-256 文本”签名：

```bash
momiya_manifest_sha='把 Candidate Manifest SHA-256 粘贴到这里'
printf '%s' "$momiya_manifest_sha" |
  openssl dgst -sha256 \
    -sign .secrets/momiya-release-private.pem \
    -sigopt rsa_padding_mode:pss \
    -sigopt rsa_pss_saltlen:32 |
  openssl base64 -A
```

把输出粘贴到 **Detached manifest signature**，Manifest signing key 选择 `Momiya Bilibili Release`，点击 **Publish signed release**。注意：Draft 的 Artifact、Segment、Rollout、Notes 等任何字段只要改变，Manifest SHA-256 就会改变，必须重新 Validate 和签名。

最终确认 Client 配置完全一致：

```json
"Release": {
  "Enabled": true,
  "Channel": "stable",
  "PackageId": "momiya-bilibili-client",
  "RuntimeId": "osx-arm64",
  "SigningKeyFingerprint": "后台显示的64位小写指纹",
  "PublicKeyPemPath": "release-public-key.pem"
}
```

Client 以当前版本 `1.0.0` 检查更新时，应命中 `region=CN` Segment，取得 `1.1.0`，验证 Fingerprint、Manifest RSA-PSS 签名、Artifact RSA-PSS 签名和 SHA-256，然后把 `.nupkg` 下载到本地更新目录并输出 `Release verified 1.1.0`。

## 配置与运行

将 Asterloom 管理台创建的 UUID、Key、Secret 填入两个 `appsettings.json` 的对应字段；OTLP/HTTP 配置已预置。

SSH 隧道终端（保持运行）：

```bash
ssh -N -L 4318:127.0.0.1:60006 -p 2222 root@&lt;server-ip&gt;
```

服务端终端：

```bash
dotnet run --project MomiyaBilibiliServer
```

客户端终端：

```bash
dotnet run --project MomiyaBilibiliClient -- 2 哔哩哔哩弹幕网
```

本地 Docker Asterloom 的 HTTP/Passport 与 gRPC 分别是 `60001`、`60002`；生产反向代理若统一为同一 HTTPS Origin，两个 Base URL 填同一个地址即可。

成功时客户端逐行输出 Identity、Feature/Targeting、Config、Authorization、gRPC/DB/Mail、HTTP/JSON、Storage、Release、Analytics/Telemetry 的业务结果；任何一步失败都会返回非零退出码。

## 范围

没有复制 ReferenceApp 的 provision、doctor、管理 API 巡检、测试夹具和 Velopack 安装态回归。Release 在本 demo 中会校验 Manifest、公钥指纹、Artifact 签名和 SHA-256 并下载文件；真正打包桌面程序后，再把它接给 `AsterloomVelopackUpdateSource` 完成替换与重启。
