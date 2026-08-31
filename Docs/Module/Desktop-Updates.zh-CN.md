# Asterloom 桌面自动更新指南

[简体中文](Desktop-Updates.zh-CN.md) | [English](Desktop-Updates.md) | [模块索引](README.zh-CN.md)

本文描述当前 Asterloom C# 实现中，桌面应用从首次打包到灰度更新、下载、安装和回滚控制的完整流程。
这里的“自动更新”专指使用 Velopack 安装的桌面程序；服务端、容器和 Web 应用仍应由 CI/CD 与部署系统更新。

## 1. 能力边界

Asterloom 与 Velopack 的职责不同：

```text
CI / 签名系统
  └─ 构建应用并通过 Velopack 生成安装程序与 .nupkg
       └─ 对 Artifact SHA-256 生成外部 RSA-PSS 签名
            └─ 上传 Asterloom，创建并签名 Release Manifest
                 └─ 发布到 stable / beta / canary
                      └─ 客户端携带身份与稳定 Targeting Key 检查更新
                           └─ Asterloom 决定资格并验证 Manifest、Artifact 与下载票据
                                └─ Velopack 下载、替换文件并重启应用
```

| 组件 | 负责 | 不负责 |
| --- | --- | --- |
| Asterloom Release | Channel、Artifact 元数据、签名信任、Manifest、版本比较、Targeting、稳定分桶、下载票据、Pause/Promote/Rollback 控制 | 生成桌面安装程序、替换正在运行的文件、重启进程 |
| Velopack | 首次安装程序、`.nupkg`、更新下载编排、Delta 合成、文件替换、重启与安装 Hook | 用户资格、Asterloom 权限、灰度规则、Asterloom Manifest 签名 |
| CI/HSM/外部签名器 | 保管私钥、签名 Artifact 和 Manifest、可选操作系统代码签名 | 把私钥交给 Asterloom 或桌面客户端 |

首次安装必须分发 Velopack 生成的 Setup/Installer。Asterloom 中的 `.nupkg` 用于已经安装后的版本更新，不能替代首次安装程序。

## 2. 三种容易混淆的“平台”

### 2.1 Platform 资源层级

Asterloom 的 Platform 模块表示业务资源边界，不表示操作系统：

```text
Tenant
  └─ Application
       └─ Environment
            ├─ Feature / Config / Targeting
            ├─ Release Channel / Desktop Release
            └─ Analytics / Telemetry 等作用域资源
```

- `Tenant`：组织或客户边界。
- `Application`：产品或应用，例如 `my-desktop-app`。
- `Environment`：`development`、`staging`、`production` 等部署环境。

同一个桌面产品通常只建立一个 Application，并在一个 Release 中附加多个运行平台的 Artifact。

### 2.2 Release `targetRuntimeId`

`targetRuntimeId` 表示一个 Artifact 可以在哪个操作系统与 CPU 架构上运行。当前后端按小写的 .NET RID
约定保存，并执行**精确字符串匹配**：客户端请求 `win-x64` 时只会选择 `win-x64` Artifact。

推荐值：

| 操作系统 | 架构 | `targetRuntimeId` |
| --- | --- | --- |
| Windows | x64 | `win-x64` |
| Windows | Arm64 | `win-arm64` |
| Windows | x86 | `win-x86` |
| macOS | Intel x64 | `osx-x64` |
| macOS | Apple Silicon | `osx-arm64` |
| Linux | x64 | `linux-x64` |
| Linux | Arm64 | `linux-arm64` |

该字段不是数据库枚举，但只接受 1–100 个小写字母、数字、点和连字符，并按 .NET RID 使用。不要混用
`windows-x64`、`Win64`、`win_x64` 等自定义写法。

客户端应把 RID 当作构建产物的一部分，而不是从操作系统显示名称猜测。单 RID 安装包最稳妥的做法是
由 CI 注入固定值；也可以读取 `RuntimeInformation.RuntimeIdentifier`，但发布前必须断言它与上传
Artifact 的 `targetRuntimeId` 完全一致。

一个 `1.4.0` Release 可以同时关联：

```text
my-app-1.4.0-win-x64-full.nupkg   → win-x64 / Full
my-app-1.4.0-win-arm64-full.nupkg → win-arm64 / Full
my-app-1.4.0-osx-arm64-full.nupkg → osx-arm64 / Full
```

每个 Release Version + Runtime 至少需要一个 Full Artifact；可额外附加从某个精确版本升级的 Delta Artifact。

### 2.3 Targeting Context `platform`

`AsterloomReleaseContext.Create(..., platform: "win-x64")` 中的 `platform` 是 Targeting 属性，用于 Segment
条件和审计解释。它不会自动选择 Artifact，也不会替代 `TargetRuntimeId`：

- Artifact 选择只看 `AsterloomReleaseClientOptions.TargetRuntimeId`。
- Segment 是否命中可以看 Context 中的 `platform`、`region`、`language`、`clientVersion` 或自定义属性。
- 建议两者使用同一个 RID 字符串，避免运维人员误判。

### 2.4 Package ID 与 Channel

- `PackageId` 必须与 Velopack `--packId` 一致，并在应用生命周期内保持不变。
- 建议一个 Asterloom Application 对应一个 Package ID；当前服务端 Manifest 不单独校验 Package ID，禁止在同一
  Application/Channel 中混入其他产品的 `.nupkg`。
- `stable`、`beta`、`canary` 是发布 Channel，不是操作系统平台。
- 用 `--channel stable` 打出的安装程序默认跟随 stable；也可以在 `UpdateOptions.ExplicitChannel` 中显式切换。

## 3. 一次性平台准备

### 3.1 创建作用域

在 Web `/tenants` 中创建或选择 Tenant、Application 和 Environment，并记录三个 UUID。生产与测试环境应分开，
例如：

```text
Tenant: Kirayuuki
Application: My Desktop App
Environment: production
```

### 3.2 配置 Passport 与更新权限

桌面程序推荐使用 Authorization Code + PKCE 的 Public OIDC Client：

- Client ID：例如 `my-desktop-client`。
- Redirect URI：例如 `http://localhost/`。
- Scope：至少包含 `asterloom.api`。
- 不配置、也不内置 Client Secret。

Release 检查接口要求 `release.update.check`。可以：

1. 为明确的用户或服务 Client ID 绑定包含该权限的角色；或者
2. 在目标 Application/Environment 创建 `Any actor` + `Allow` + `release.update.check` Policy，让任意已认证用户检查更新。

当前实现没有匿名 Release Feed。纯桌面应用不得嵌入 Client Credentials Secret。因此现有安全路径是 Passport 登录后检查
更新；如果产品必须在登录前更新，需要另行实现受限的 Bootstrap Update 身份或匿名签名 Manifest Endpoint。

### 3.3 创建外部 RSA 签名密钥

使用至少 2048 位 RSA 密钥。生产私钥应位于 CI Secret Store、HSM 或专用签名服务。Asterloom 只登记
SubjectPublicKeyInfo PEM 公钥：

```text
Web → Releases → Artifacts → Signing trust store → Register public key
```

后台会计算公钥的 SHA-256 Fingerprint。桌面客户端必须内置 `fingerprint → publicKeyPem` 信任映射。

Asterloom Artifact 和 Manifest 使用同一签名约定：

```text
算法：RSA-PSS-SHA256
输入：小写 64 位 SHA-256 十六进制文本的 UTF-8 字节
输出：Base64 detached signature
```

示例：

```csharp
using System.Security.Cryptography;
using System.Text;

static string SignSha256Text(RSA privateKey, string sha256) =>
    Convert.ToBase64String(privateKey.SignData(
        Encoding.UTF8.GetBytes(sha256.ToLowerInvariant()),
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pss));
```

操作系统代码签名和 Asterloom Release 签名是两层独立保护。生产 Windows/macOS 包应同时执行平台代码签名。

### 3.4 创建 Channel

在 Web `/channels` 创建稳定的客户端路由标识：

- `stable`：正式发布。
- `beta`：愿意提前试用的用户。
- `canary`：内部或极小流量验证。

Channel Key 对客户端可见且不可修改。一个 Channel 同一时刻只有一个 Active Release，并记录 Previous Release 供回滚控制。

## 4. 打包规范

当前仓库锁定 Velopack `1.2.0`。CI 中的 `vpk` 应使用相同版本：

```powershell
dotnet tool install --global vpk --version 1.2.0

dotnet publish .\MyDesktopApp.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\publish\win-x64

vpk pack `
  --packId Kirayuuki.MyDesktopApp `
  --packVersion 1.4.0 `
  --packDir .\publish\win-x64 `
  --mainExe MyDesktopApp.exe `
  --channel stable `
  --outputDir .\releases\win-x64
```

规则：

- 使用 SemVer，例如 `1.4.0` 或 `1.4.0-beta.1`；不要使用 `1.4.0.0`。
- `--packId` 在所有版本中保持不变。
- `--packVersion` 必须与 Asterloom Artifact 和 Desktop Release 的 Release Version 完全一致。
- `--channel` 应与客户端要查询的 Asterloom Channel 一致。
- 为每个 RID 单独 `dotnet publish` 和 `vpk pack`。
- 首次发布先只上传 Full `.nupkg`；确认全链路稳定后再引入 Delta。
- Setup/Installer 用于首次安装，不要把它误标为 Velopack Full Artifact。

Velopack 官方打包说明：<https://docs.velopack.io/getting-started/csharp>

### 4.1 生成并发布 Delta 包

Asterloom 负责保存和分发 Delta，但不会根据两个 Full 包自动生成 Delta。只有当 `vpk pack` 的输出目录中
已经存在同一 Package ID、Channel、RID 的上一版本 Release 时，Velopack 才会生成 Delta。因此 CI 必须把
完整 Velopack Release 输出作为流水线制品保存，并在打包新版本前恢复；只保留新的 `dotnet publish` 目录不够。

例如，在输出目录已有 `1.3.0` 的情况下打包 `1.4.0`，通常会得到：

```text
Kirayuuki.MyDesktopApp-1.4.0-full.nupkg
Kirayuuki.MyDesktopApp-1.4.0-delta.nupkg  # 用 1.3.0 合成 1.4.0
```

把两个文件都上传并加入同一个 Asterloom Desktop Release：

| 文件 | Artifact Kind | Release Version | Delta From | Runtime |
| --- | --- | --- | --- | --- |
| `*-1.4.0-full.nupkg` | Full | `1.4.0` | 留空 | `win-x64` |
| `*-1.4.0-delta.nupkg` | Delta | `1.4.0` | `1.3.0` | `win-x64` |

每个 Runtime 在已发布 Release 中都必须保留一个 Verified Full Artifact。Delta 只是流量和速度优化，不能
成为唯一恢复包。每个 RID 都需要独立完成构建和上传。

当前 Asterloom 使用**直接、精确来源 Delta**。`1.3.0` 客户端可获得 `1.3.0 → 1.4.0` Delta；`1.2.0`
客户端默认获得 Full，除非 `1.4.0` Release 中还上传了一个 `Delta From Version = 1.2.0` 的独立 Delta。
当前不会跨多个历史 Release 拼接 `1.2.0 → 1.3.0 → 1.4.0` Delta 链。

Velopack 打包细节：<https://docs.velopack.io/packaging/overview>

## 5. Web 发布流程

### 5.1 上传 Artifact

进入 Web `/artifacts`：

1. 选择 Velopack `.nupkg`。
2. 填写与 `--packVersion` 相同的 Release Version。
3. 填写 `targetRuntimeId`，例如 `win-x64`。
4. 选择 `Full`；Delta 还必须填写精确的 `Delta From Version`。
5. Web 计算文件 SHA-256。
6. 在外部签名器对该 SHA-256 文本签名。
7. 选择已登记公钥并粘贴 Base64 Signature。
8. 创建短时 Upload Ticket，将文件上传到对象存储。
9. Complete Upload；服务端重新检查大小、Content-Type、SHA-256 和 RSA-PSS Signature。

只有状态为 `Verified` 的 Artifact 才能加入 Release。`Rejected` 常见原因：

- 签名的是文件原始字节或二进制 Digest，而不是小写 SHA-256 文本；
- 使用了 RSA PKCS#1 v1.5，而不是 RSA-PSS；
- 选错签名公钥；
- 上传时遗漏 Signed URL 返回的 Required Header；
- 文件、大小或 Content-Type 与创建 Upload Ticket 时声明的不一致。

Release Artifact 会使用 Storage 模块中的 Tenant 系统 Bucket `release-artifacts`，无需手工创建普通 Bucket 或绕过
Release 页面上传。

### 5.2 创建 Release Draft

进入 Web `/releases`，填写：

- Channel。
- Semantic Version。
- Display Name 与 Release Notes。
- 一个或多个相同版本的 Verified Artifact。
- Minimum Version。
- Rollout Basis Points。
- 可选 Target Segment。
- Mandatory 标记。

Rollout Basis Points 总数为 `100000`：

| 值 | 比例 |
| ---: | ---: |
| `1000` | 1% |
| `5000` | 5% |
| `25000` | 25% |
| `100000` | 100% |

同一 Release 可以包含多个 Runtime，但同一 Runtime 不能出现两个 Full，也不能出现重复的 Delta 来源映射。

### 5.3 Validate、签名并 Publish

1. 保存 Draft。
2. 点击 `Validate release`。
3. 修复所有 Error；Warning 需要人工确认。
4. 复制 Candidate Manifest SHA-256。
5. 用外部私钥按相同 RSA-PSS 规则签名。
6. 选择 Manifest Signing Key 并粘贴 Base64 Signature。
7. 点击 `Publish signed release`。

Draft 的任何字段变化都会改变 Manifest，必须重新 Validate 和签名。Publish 后 Manifest 不再可编辑；新变更应创建新版本。

### 5.4 模拟与灰度

页面底部更新模拟器应至少覆盖：

- 当前版本等于目标版本：应返回 `Current`。
- 不同 `targetRuntimeId`：应返回 `NoCompatibleArtifact`。
- Segment 命中与未命中。
- Rollout 内外的固定 Targeting Key。
- 低于 Minimum Version 的客户端。

发布建议：

```text
canary 100% → stable 1% → 5% → 25% → 50% → 100%
```

每次 Promote 之间观察 Crash、启动失败、Telemetry Error、下载失败和业务指标。

## 6. C# 客户端集成

SDK 当前以仓库项目提供并面向 `net10.0`：

```powershell
dotnet add .\MyDesktopApp.csproj reference .\Backend\Asterloom.Sdk.Identity\Asterloom.Sdk.Identity.csproj
dotnet add .\MyDesktopApp.csproj reference .\Backend\Asterloom.Sdk.Rpc\Asterloom.Sdk.Rpc.csproj
dotnet add .\MyDesktopApp.csproj reference .\Backend\Asterloom.Sdk.Release\Asterloom.Sdk.Release.csproj
```

### 6.1 尽早运行 Velopack Hook

在 `Main` 最前面、UI 和 Host 初始化之前运行：

```csharp
using Velopack;

VelopackApp.Build().Run();
```

### 6.2 创建认证 Transport 与 Release Client

```csharp
using Asterloom.Sdk.Release;
using Asterloom.Sdk.Rpc;
using Velopack;

// 此前已通过 AsterloomIdentityClient 完成 Passport 登录。
using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    identity.GetAccessTokenAsync);

var scope = new AsterloomReleaseScope(tenantId, applicationId, environmentId);
var trustedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [releaseKeyFingerprint] = releasePublicKeyPem,
};

using var releaseClient = new AsterloomReleaseClient(
    transport.HttpClient,
    new AsterloomReleaseClientOptions
    {
        Scope = scope,
        TargetRuntimeId = "win-x64",
        PackageId = "Kirayuuki.MyDesktopApp",
        TrustedPublicKeysByFingerprint = trustedKeys,
    });

var updateSource = new AsterloomVelopackUpdateSource(
    releaseClient,
    currentVersion => AsterloomReleaseContext.Create(
        scope,
        targetingKey: installationId,
        clientVersion: currentVersion,
        platform: "win-x64",
        region: currentRegion,
        language: currentLanguage));

var updateManager = new UpdateManager(
    updateSource,
    new UpdateOptions { ExplicitChannel = "stable" });
```

`installationId` 应在第一次运行时生成随机 UUID，并保存到操作系统保护或应用数据目录。不要每次启动重建；否则灰度分桶会漂移。
如果产品要求用户级灰度，可使用稳定 User ID，但切换账号会改变命中结果。

### 6.3 检查、下载和应用

```csharp
if (!updateManager.IsInstalled)
{
    // IDE/debug/portable 运行不执行真实替换。
    return;
}

var update = await updateManager.CheckForUpdatesAsync();
if (update is null)
{
    return;
}

await updateManager.DownloadUpdatesAsync(
    update,
    progress => ReportUpdateProgress(progress),
    cancellationToken);

await SaveApplicationStateAsync(cancellationToken);
updateManager.ApplyUpdatesAndRestart(update);
```

### 6.4 Delta 选择与 Full 自动回退

当客户端存在精确匹配的 Delta 时，更新响应会保留 `selectedArtifact`/`download` 兼容字段，同时在
`artifactDownloads` 中返回且只返回：

1. 目标版本 Full 包及其短时下载票据；
2. `DeltaFromVersion` 与客户端当前版本完全一致的 Delta 及其下载票据。

`AsterloomVelopackUpdateSource` 会把两个 Asset 一起交给 `UpdateManager`。Velopack 先尝试 Delta；下载、合成
或合成后校验失败时，会自动下载 Full。没有精确 Delta，或者当前版本低于 Minimum Version 时，Asterloom
只返回 Full。Adapter 会分别记录并按需刷新两个 Asset 的下载票据。

使用 Velopack 的应用继续调用 `DownloadUpdatesAsync` 即可，不需要自己编写 Full 回退循环。非 Velopack
客户端可以读取 `decision.ArtifactDownloads`，并通过
`DownloadArtifactToAsync(decision, artifactId, destination)` 下载指定的已签名 Asset；原有
`DownloadToAsync` 仍只下载 `SelectedArtifact`。

Velopack 下载与回退说明：<https://docs.velopack.io/integrating/overview>

`AsterloomReleaseClient` 在返回或写入文件前会验证：

1. Manifest Signing Key Fingerprint 在本地信任列表中；
2. Manifest RSA-PSS Signature；
3. Manifest Payload 与结构化响应一致；
4. Artifact Metadata 与已签名 Manifest 一致；
5. 下载后的 Size、SHA-256 和 Artifact Signature。

不要直接下载 Decision 中的 Signed URL。URL 可能指向 S3 Origin，而且绕过 SDK 会失去完整性验证。

### 6.4 Mandatory 更新

`Mandatory` 是 Asterloom 更新决策中的业务标记，不会自动锁住应用 UI。若需要“不可跳过”的体验，应用应先调用
`AsterloomReleaseClient.CheckForUpdateAsync` 读取 `decision.Mandatory`，然后控制：

- 是否允许关闭更新对话框；
- 是否允许继续进入主界面；
- 下载失败时的重试与离线策略；
- 保存数据后何时重启。

`AsterloomVelopackUpdateSource` 会负责 Velopack 下载，但当前不会把 Mandatory 自动映射成 UI 策略。

## 7. 更新决策顺序

当前服务端按以下顺序决定是否返回更新：

1. Tenant、Application、Environment 必须存在且 Active。
2. Channel 必须存在、Active 且有 Active Release。
3. Paused Release 不返回更新。
4. `currentVersion >= releaseVersion` 返回 `Current`。
5. 可选 Target Segment 必须命中。
6. 稳定分桶必须小于 Rollout Basis Points。
7. 必须存在与 `targetRuntimeId` 精确匹配的 Verified Artifact。
8. 当前版本低于 Minimum Version 时跳过 Delta、选择 Full，并返回 Mandatory。
9. 其他情况下优先选择 `DeltaFromVersion == currentVersion` 的 Delta，没有则回退 Full。

客户端可记录 `Reason` 和 `Trace`，但不要将其中可能包含的 Targeting 信息作为公开用户文案。

## 8. Pause、Promote 与 Rollback 的真实语义

- `Pause`：停止尚未获得更新的客户端继续得到该版本；不会卸载已安装版本。
- `Promote`：只能提高 Rollout 比例，稳定 Targeting Key 的命中结果保持可解释。
- `Rollback`：把 Channel Active Release 指回之前已签名的 Release，阻止更多客户端升级到问题版本。
- 当前 Release 检查与 Velopack 默认都只向更高版本移动，所以 Rollback **不会自动降级已经升级的客户端**。

如果业务需要强制降级，必须设计独立的高风险恢复流程、允许 Velopack Downgrade，并明确处理数据格式兼容；不要把普通
Channel Rollback 当作客户端降级。

## 9. 密钥轮换

旧客户端只信任它内置的公钥。安全轮换顺序：

1. 当前版本仍使用旧私钥签名。
2. 发布一个同时内置旧、新两个公钥的过渡版本。
3. 等待足够比例客户端升级到过渡版本。
4. 新 Release 开始使用新私钥签名。
5. 经过支持周期后，后续客户端再移除旧公钥。

直接归档旧公钥并立即用新私钥发布，会使尚未获得新公钥的旧客户端无法验证更新。

## 10. CI 发布检查表

- [ ] `dotnet publish` 使用正确 RID 与 Release 配置。
- [ ] `vpk` 与应用 Velopack Package 使用同一版本。
- [ ] 构建 Delta 前已恢复/保留上一版本 Velopack Release 输出。
- [ ] Package ID、Channel、Semantic Version 与 Asterloom 字段完全一致。
- [ ] 执行操作系统代码签名。
- [ ] 私钥只存在于受控签名环境。
- [ ] Artifact SHA-256 与 RSA-PSS Signature 已生成。
- [ ] Artifact 在 Asterloom 中状态为 Verified。
- [ ] 每个 RID 都包含一个 Full，每个 Delta 都填写精确来源版本。
- [ ] Release Validate 无 Error。
- [ ] Candidate Manifest SHA-256 已由外部私钥签名。
- [ ] 使用固定测试 Installation ID 完成更新模拟。
- [ ] Canary 安装包完成真实下载、替换和重启测试。
- [ ] 已分别测试 Delta 成功合成与强制 Full 回退。
- [ ] Telemetry/Analytics 监控已准备。
- [ ] Promote 前已确认暂停和回滚目标。

## 11. 相关实现

- Release 客户端：[AsterloomReleaseClient.cs](../../Backend/Asterloom.Sdk.Release/AsterloomReleaseClient.cs)
- Velopack Adapter：[AsterloomVelopackUpdateSource.cs](../../Backend/Asterloom.Sdk.Release/AsterloomVelopackUpdateSource.cs)
- 签名校验：[AsterloomReleaseVerifier.cs](../../Backend/Asterloom.Sdk.Release/AsterloomReleaseVerifier.cs)
- Release Protocol：[release.proto](../../Proto/Asterloom/release/v1/release.proto)
- 管理 Protocol：[release_admin.proto](../../Proto/Asterloom/release/v1/release_admin.proto)
- 可执行签名/上传示例：[ReferenceAppProvisioner.cs](../../Backend/Samples/Asterloom.ReferenceApp.Client/ReferenceAppProvisioner.cs)
- 总功能指南：[Feature-Guide.zh-CN.md](../Feature-Guide.zh-CN.md)
- Velopack C# 指南：<https://docs.velopack.io/getting-started/csharp>
- Velopack UpdateManager：<https://docs.velopack.io/reference/cs/Velopack/UpdateManager>
