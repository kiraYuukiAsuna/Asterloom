using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Asterloom.Sdk.Analytics;
using Asterloom.Sdk.Authorization;
using Asterloom.Sdk.Config;
using Asterloom.Sdk.Feature;
using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Release;
using Asterloom.Sdk.Rpc;
using Asterloom.Sdk.Storage;
using Asterloom.Sdk.Telemetry;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiya.Bilibili.Protocol.V1;
using OpenFeature.Model;

var creatorMid = args.ElementAtOrDefault(0)?.Trim() ?? "2";
var creatorName = args.ElementAtOrDefault(1)?.Trim() ?? "哔哩哔哩弹幕网";
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        ApplicationName = typeof(Program).Assembly.GetName().Name,
        ContentRootPath = AppContext.BaseDirectory,
    });
    var settings = ClientSettings.Load(builder.Configuration);
    var appVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    // ponytail: in-memory tokens keep the demo dependency-free; use an OS-protected
    // IAsterloomTokenStore in a shipped desktop application.
    builder.Services.AddAsterloomIdentityClient(options =>
    {
        options.Issuer = settings.Issuer;
        options.ClientId = settings.InteractiveClientId;
        options.RegistrationId = "momiya-bilibili-client";
        options.EnableInteractiveAuthentication = true;
        options.RequestRefreshTokens = true;
        options.Scopes.Add(settings.ApiScope);
        options.AllowInsecureHttpForDevelopment = settings.AllowInsecureDevelopment;
    });
    var telemetry = AsterloomTelemetryOptions.FromConfiguration(
        builder.Configuration,
        "momiya.bilibili.client",
        typeof(Program).Assembly.GetName().Version?.ToString());
    telemetry.EnvironmentName = builder.Environment.EnvironmentName;
    telemetry.TenantId = settings.TenantId.ToString("D");
    telemetry.ApplicationId = settings.ApplicationId.ToString("D");
    telemetry.EnvironmentId = settings.EnvironmentId.ToString("D");
    telemetry.ActivitySourceNames.Add(ClientTelemetry.ActivitySourceName);
    telemetry.MeterNames.Add(ClientTelemetry.MeterName);
    builder.Services.AddAsterloomTelemetry(telemetry);
    builder.Logging.AddAsterloomTelemetryLogging(telemetry);

    using var host = builder.Build();
    await host.StartAsync(cancellation.Token);
    try
    {
        var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MomiyaBilibiliClient");
        Console.WriteLine("正在浏览器中打开 Asterloom Passport…");
        var tokens = await identity.SignInAsync(cancellationToken: cancellation.Token);
        var subject = tokens.Principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Asterloom token does not contain a sub claim.");
        Console.WriteLine($"Identity       {tokens.Principal.Identity?.Name ?? subject}");

        Task<string> Token(CancellationToken token) => identity.GetAccessTokenAsync(token);
        using var platformHttp = AsterloomAuthenticatedTransport.Create(
            settings.HttpBaseUrl,
            Token,
            allowInsecureHttpForDevelopment: settings.AllowInsecureDevelopment);
        using var platformGrpc = AsterloomAuthenticatedTransport.Create(
            settings.GrpcBaseUrl,
            Token,
            allowInsecureHttpForDevelopment: settings.AllowInsecureDevelopment);
        var scope = new AsterloomAuthorizationScope(
            settings.TenantId,
            settings.ApplicationId,
            settings.EnvironmentId);
        var targeting = EvaluationContext.Builder()
            .SetTargetingKey(subject)
            .Set("userId", subject)
            .Set("clientVersion", appVersion)
            .Set("platform", settings.RuntimeId)
            .Set("region", "CN")
            .Set("language", "zh-CN")
            .Build();

        using var activity = ClientTelemetry.ActivitySource.StartActivity("subscription.sync");
        activity?.SetTag("bilibili.creator.mid", creatorMid);

        var feature = new AsterloomFeatureProvider(
            platformGrpc.CallInvoker,
            new AsterloomFeatureProviderOptions
            {
                Scope = new AsterloomFeatureScope(
                    settings.TenantId,
                    settings.ApplicationId,
                    settings.EnvironmentId),
            });
        var notifications = await feature.ResolveBooleanValueAsync(
            settings.FeatureFlagKey,
            false,
            targeting,
            cancellation.Token);
        if (notifications.ErrorType != OpenFeature.Constant.ErrorType.None)
        {
            throw new InvalidOperationException(
                $"Feature evaluation failed: {notifications.ErrorMessage}");
        }
        Console.WriteLine($"Feature/Target  {settings.FeatureFlagKey}={notifications.Value} ({notifications.Variant})");

        var configScope = new AsterloomConfigScope(
            settings.TenantId,
            settings.ApplicationId,
            settings.EnvironmentId);
        using var config = new AsterloomConfigClient(
            platformHttp.HttpClient,
            new AsterloomConfigClientOptions
            {
                Scope = configScope,
                CacheDuration = TimeSpan.FromSeconds(30),
                LastKnownGoodDuration = TimeSpan.FromHours(24),
            });
        var configContext = AsterloomConfigContext.Create(
            configScope,
            subject,
            userId: subject,
            clientVersion: appVersion,
            platform: settings.RuntimeId,
            region: "CN",
            language: "zh-CN");
        var snapshot = await config.GetSnapshotAsync(
            configContext,
            cancellationToken: cancellation.Token);
        if (!snapshot.Values.ContainsKey(settings.ConfigKey))
        {
            throw new InvalidOperationException(
                $"Dynamic Config '{settings.ConfigKey}' was not published.");
        }
        var pollInterval = await config.GetInt64Async(
            settings.ConfigKey,
            60,
            configContext,
            cancellation.Token);
        Console.WriteLine($"Config          {settings.ConfigKey}={pollInterval}");

        var authorization = new AsterloomAuthorizationClient(platformGrpc.CallInvoker);
        var preview = await authorization.CheckAccessAsync(
            actorId: null,
            permission: "bilibili.subscription.write",
            scope,
            resourceType: "bilibili_creator",
            resourceId: creatorMid,
            cancellationToken: cancellation.Token);
        if (!preview.Allowed)
        {
            throw new UnauthorizedAccessException(preview.Reason);
        }
        Console.WriteLine($"Authorization   allowed ({preview.Reason})");

        using var businessHttp = new HttpClient
        {
            BaseAddress = settings.ServerHttpBaseUrl,
        };
        businessHttp.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var businessGrpcHttp = new HttpClient();
        businessGrpcHttp.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var businessChannel = GrpcChannel.ForAddress(
            settings.ServerGrpcBaseUrl,
            new GrpcChannelOptions
            {
                HttpClient = businessGrpcHttp,
                DisposeHttpClient = false,
            });
        var business = new MomiyaBilibiliService.MomiyaBilibiliServiceClient(businessChannel);
        var subscribed = await business.SubscribeAsync(
            new SubscribeRequest
            {
                CreatorMid = creatorMid,
                CreatorName = creatorName,
            },
            cancellationToken: cancellation.Token);
        Console.WriteLine($"gRPC/DB/Mail    {subscribed.Subscription.CreatorName}, {subscribed.MailStatus}");

        using var listResponse = await businessHttp.GetAsync(
            "api/momiya/v1/subscriptions",
            cancellation.Token);
        var backupBytes = await ReadSuccessfulBodyAsync(listResponse, cancellation.Token);
        using var listDocument = JsonDocument.Parse(backupBytes);
        if (!listDocument.RootElement.GetProperty("subscriptions")
                .EnumerateArray()
                .Any(item => item.GetProperty("creatorMid").GetString() == creatorMid))
        {
            throw new InvalidDataException("The persisted subscription was not returned.");
        }
        Console.WriteLine("HTTP/JSON       persisted subscription returned");

        using var storage = new AsterloomStorageClient(
            platformHttp.HttpClient,
            new AsterloomStorageClientOptions
            {
                Scope = new AsterloomStorageScope(settings.TenantId),
                AllowInsecureTransferUrls = settings.AllowInsecureDevelopment,
            });
        var backupHash = Convert.ToHexStringLower(SHA256.HashData(backupBytes));
        var storedBackup = await storage.UploadAsync(
            new AsterloomStorageUploadRequest(
                settings.StorageBucketId,
                $"subscriptions/{Guid.CreateVersion7():N}.json",
                "subscriptions.json",
                "application/json",
                backupBytes.LongLength,
                backupHash,
                settings.ApplicationId,
                settings.EnvironmentId),
            new MemoryStream(backupBytes, writable: false),
            cancellation.Token);
        using var downloadedBackup = new MemoryStream();
        await storage.DownloadToAsync(
            storedBackup,
            downloadedBackup,
            cancellationToken: cancellation.Token);
        if (!backupBytes.AsSpan().SequenceEqual(downloadedBackup.ToArray()))
        {
            throw new InvalidDataException("The cloud backup differs from the uploaded subscription list.");
        }
        Console.WriteLine($"Storage         backup {storedBackup.Id:D} verified");

        if (settings.ReleaseEnabled)
        {
            var releaseScope = new AsterloomReleaseScope(
                settings.TenantId,
                settings.ApplicationId,
                settings.EnvironmentId);
            using var releases = new AsterloomReleaseClient(
                platformHttp.HttpClient,
                new AsterloomReleaseClientOptions
                {
                    Scope = releaseScope,
                    TargetRuntimeId = settings.RuntimeId,
                    PackageId = settings.PackageId,
                    TrustedPublicKeysByFingerprint = new Dictionary<string, string>
                    {
                        [settings.SigningKeyFingerprint] = settings.PublicKeyPem,
                    },
                    AllowInsecureDownloadUrls = settings.AllowInsecureDevelopment,
                });
            var update = await releases.CheckForUpdateAsync(
                settings.ReleaseChannel,
                appVersion,
                AsterloomReleaseContext.Create(
                    releaseScope,
                    subject,
                    userId: subject,
                    clientVersion: appVersion,
                    platform: settings.RuntimeId,
                    region: "CN",
                    language: "zh-CN"),
                cancellation.Token);
            if (update.UpdateAvailable)
            {
                var updateDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MomiyaBilibili",
                    "updates");
                var updatePath = Path.Combine(
                    updateDirectory,
                    Path.GetFileName(update.SelectedArtifact!.FileName));
                // ponytail: download and cryptographically verify here; hand this file to
                // Velopack only when the demo becomes an installable desktop application.
                await releases.DownloadToFileAsync(
                    update,
                    updatePath,
                    cancellationToken: cancellation.Token);
                Console.WriteLine($"Release         verified {update.Manifest!.ReleaseVersion} -> {updatePath}");
            }
            else
            {
                Console.WriteLine($"Release         current ({update.Reason})");
            }
        }
        else
        {
            Console.WriteLine("Release         skipped (not configured)");
        }

        await using var analytics = new AsterloomAnalyticsClient(
            platformHttp.HttpClient,
            new AsterloomAnalyticsClientOptions
            {
                WriteKey = settings.AnalyticsWriteKey,
                BatchSize = 20,
                CommonContext = new Dictionary<string, object?>
                {
                    ["platform"] = settings.RuntimeId,
                    ["version"] = appVersion,
                },
            });
        await analytics.TrackAsync(
            "bilibili.subscription.synced",
            new
            {
                creatorMid,
                liveNotifications = notifications.Value,
                pollIntervalSeconds = pollInterval,
            },
            new AsterloomAnalyticsIdentity(
                ActorId: subject,
                SessionId: Guid.CreateVersion7().ToString("N")),
            cancellationToken: cancellation.Token);
        var flush = await analytics.FlushAsync(cancellation.Token);
        if (flush.Accepted != 1 || flush.Remaining != 0)
        {
            throw new InvalidOperationException(
                $"Analytics delivery failed: accepted={flush.Accepted}, remaining={flush.Remaining}.");
        }
        ClientTelemetry.Syncs.Add(1);
        logger.LogInformation(
            "Synchronized Bilibili creator {CreatorMid} for user {Subject}.",
            creatorMid,
            subject);
        Console.WriteLine("Analytics/OTel  event accepted; trace, metric and log emitted");
        Console.WriteLine("\n全部已配置业务能力调用成功。");
        return 0;
    }
    finally
    {
        await host.StopAsync(CancellationToken.None);
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("操作已取消。");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"失败：{exception.Message}");
    return 1;
}

static async Task<byte[]> ReadSuccessfulBodyAsync(
    HttpResponseMessage response,
    CancellationToken cancellationToken)
{
    var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        throw new HttpRequestException(
            $"Business API returned HTTP {(int)response.StatusCode}: "
                + System.Text.Encoding.UTF8.GetString(body),
            null,
            response.StatusCode);
    }

    return body;
}

internal sealed record ClientSettings(
    Uri HttpBaseUrl,
    Uri GrpcBaseUrl,
    Uri Issuer,
    Uri ServerHttpBaseUrl,
    Uri ServerGrpcBaseUrl,
    bool AllowInsecureDevelopment,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    string InteractiveClientId,
    string ApiScope,
    string FeatureFlagKey,
    string ConfigKey,
    Guid StorageBucketId,
    string AnalyticsWriteKey,
    bool ReleaseEnabled,
    string ReleaseChannel,
    string PackageId,
    string RuntimeId,
    string SigningKeyFingerprint,
    string PublicKeyPem)
{
    public static ClientSettings Load(IConfiguration configuration)
    {
        var asterloom = configuration.GetRequiredSection("Asterloom");
        var release = asterloom.GetRequiredSection("Release");
        var server = configuration.GetRequiredSection("Server");
        var releaseEnabled = release.GetValue<bool>("Enabled");
        var keyPath = releaseEnabled
            ? Path.GetFullPath(Text(release, "PublicKeyPemPath"), AppContext.BaseDirectory)
            : string.Empty;
        if (releaseEnabled && !File.Exists(keyPath))
        {
            throw new InvalidOperationException($"Release public key file does not exist: {keyPath}");
        }

        return new ClientSettings(
            UriValue(asterloom, "HttpBaseUrl"),
            UriValue(asterloom, "GrpcBaseUrl"),
            UriValue(asterloom, "Issuer"),
            UriValue(server, "HttpBaseUrl", "Server"),
            UriValue(server, "GrpcBaseUrl", "Server"),
            asterloom.GetValue<bool>("AllowInsecureHttpForDevelopment"),
            GuidValue(asterloom, "TenantId"),
            GuidValue(asterloom, "ApplicationId"),
            GuidValue(asterloom, "EnvironmentId"),
            Text(asterloom, "InteractiveClientId"),
            Text(asterloom, "ApiScope"),
            Text(asterloom, "FeatureFlagKey"),
            Text(asterloom, "ConfigKey"),
            GuidValue(asterloom, "StorageBucketId"),
            Text(asterloom, "AnalyticsWriteKey"),
            releaseEnabled,
            releaseEnabled ? Text(release, "Channel") : string.Empty,
            releaseEnabled ? Text(release, "PackageId") : string.Empty,
            Text(release, "RuntimeId"),
            releaseEnabled ? Text(release, "SigningKeyFingerprint") : string.Empty,
            releaseEnabled ? File.ReadAllText(keyPath) : string.Empty);
    }

    private static string Text(IConfiguration section, string key, string prefix = "Asterloom") =>
        !string.IsNullOrWhiteSpace(section[key])
            ? section[key]!
            : throw new InvalidOperationException($"Configuration '{prefix}:{key}' is required.");

    private static Guid GuidValue(IConfiguration section, string key) =>
        Guid.TryParse(Text(section, key), out var value) && value != Guid.Empty
            ? value
            : throw new InvalidOperationException($"Configuration 'Asterloom:{key}' must be a non-empty UUID.");

    private static Uri UriValue(IConfiguration section, string key, string prefix = "Asterloom") =>
        Uri.TryCreate(Text(section, key, prefix), UriKind.Absolute, out var value)
            ? value
            : throw new InvalidOperationException($"Configuration '{prefix}:{key}' must be an absolute URI.");
}

internal static class ClientTelemetry
{
    public const string ActivitySourceName = "Momiya.Bilibili.Client";
    public const string MeterName = "Momiya.Bilibili.Client";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> Syncs = Meter.CreateCounter<long>("momiya.bilibili.syncs");
}
