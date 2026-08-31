using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Sdk.Release;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class ReleaseContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebApplicationFactory<Program> _factory;

    public ReleaseContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task JsonTranscodingAndSdkCoverCompleteSignedReleaseLifecycle()
    {
        using var client = await CreateAuthorizedClientAsync();
        var resources = await CreateScopeAsync(client);
        var scopePath = ScopePath(resources);
        using var rsa = RSA.Create(2048);
        var signingKey = await SendAsync<SigningKeyJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{resources.TenantId}/release/signing-keys",
            new
            {
                key = "desktop-production",
                displayName = "Desktop production",
                publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            }));
        var signingKeys = await client.GetFromJsonAsync<SigningKeyListJson>(
            $"/api/v1/tenants/{resources.TenantId}/release/signing-keys?pageSize=20");
        Assert.Contains(signingKeys!.SigningKeys, item => item.Id == signingKey.Id);
        signingKey = await SendAsync<SigningKeyJson>(client.DeleteAsync(
            $"/api/v1/tenants/{resources.TenantId}/release/signing-keys/{signingKey.Id}"
            + $"?expectedVersion={signingKey.Version}"));
        Assert.Equal("RELEASE_SIGNING_KEY_STATUS_ARCHIVED", signingKey.Status);
        signingKey = await SendAsync<SigningKeyJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{resources.TenantId}/release/signing-keys/{signingKey.Id}:restore",
            new { expectedVersion = signingKey.Version }));

        var channelPath = $"{scopePath}/release/channels";
        var channel = await SendAsync<ChannelJson>(client.PostAsJsonAsync(
            channelPath,
            new
            {
                key = "stable",
                displayName = "Stable",
                description = "Stable desktop updates.",
            }));
        channel = await SendAsync<ChannelJson>(client.PatchAsJsonAsync(
            $"{channelPath}/{channel.Id}",
            new
            {
                displayName = "Stable channel",
                description = channel.Description,
                expectedVersion = channel.Version,
            }));
        var fetchedChannel = await client.GetFromJsonAsync<ChannelJson>(
            $"{channelPath}/{channel.Id}");
        Assert.Equal(channel.Id, fetchedChannel!.Id);
        var channels = await client.GetFromJsonAsync<ChannelListJson>(
            $"{channelPath}?pageSize=20");
        Assert.Contains(channels!.Channels, item => item.Id == channel.Id);

        var unusedChannel = await SendAsync<ChannelJson>(client.PostAsJsonAsync(
            channelPath,
            new { key = "canary", displayName = "Canary", description = "Canary updates." }));
        unusedChannel = await SendAsync<ChannelJson>(client.DeleteAsync(
            $"{channelPath}/{unusedChannel.Id}?expectedVersion={unusedChannel.Version}"));
        Assert.Equal("RELEASE_CHANNEL_STATUS_ARCHIVED", unusedChannel.Status);
        unusedChannel = await SendAsync<ChannelJson>(client.PostAsJsonAsync(
            $"{channelPath}/{unusedChannel.Id}:restore",
            new { expectedVersion = unusedChannel.Version }));
        Assert.Equal("RELEASE_CHANNEL_STATUS_ACTIVE", unusedChannel.Status);

        var firstBytes = "contract release one"u8.ToArray();
        var firstArtifact = await UploadArtifactAsync(
            client,
            resources,
            signingKey,
            rsa,
            "1.0.0",
            firstBytes);
        var artifactPath = $"{scopePath}/release/artifacts";
        var artifacts = await client.GetFromJsonAsync<ArtifactListJson>(
            $"{artifactPath}?pageSize=20");
        Assert.Contains(artifacts!.Artifacts, item => item.Id == firstArtifact.Id);
        var fetchedArtifact = await client.GetFromJsonAsync<ArtifactJson>(
            $"{artifactPath}/{firstArtifact.Id}");
        Assert.Equal(firstArtifact.Id, fetchedArtifact!.Id);

        var unusedArtifact = await UploadArtifactAsync(
            client,
            resources,
            signingKey,
            rsa,
            "9.0.0",
            "unused contract release"u8.ToArray());
        unusedArtifact = await SendAsync<ArtifactJson>(client.DeleteAsync(
            $"{artifactPath}/{unusedArtifact.Id}?expectedVersion={unusedArtifact.Version}"));
        Assert.Equal("RELEASE_ARTIFACT_STATUS_ARCHIVED", unusedArtifact.Status);

        var releasePath = $"{scopePath}/releases";
        var firstRelease = await SendAsync<ReleaseJson>(client.PostAsJsonAsync(
            releasePath,
            new
            {
                channelId = channel.Id,
                releaseVersion = "1.0.0",
                displayName = "Version 1.0.0",
                releaseNotes = "First contract release.",
                artifactIds = new[] { firstArtifact.Id },
                rolloutBasisPoints = 100_000,
                mandatory = false,
                minimumVersion = "0.9.0",
            }));
        firstRelease = await SendAsync<ReleaseJson>(client.PatchAsJsonAsync(
            $"{releasePath}/{firstRelease.Id}",
            new
            {
                displayName = "Version 1.0.0 stable",
                releaseNotes = firstRelease.ReleaseNotes,
                artifactIds = firstRelease.ArtifactIds,
                rolloutBasisPoints = firstRelease.RolloutBasisPoints,
                mandatory = firstRelease.Mandatory,
                minimumVersion = firstRelease.MinimumVersion,
                expectedVersion = firstRelease.Version,
            }));
        var fetchedRelease = await client.GetFromJsonAsync<ReleaseJson>(
            $"{releasePath}/{firstRelease.Id}");
        Assert.Equal(firstRelease.Id, fetchedRelease!.Id);
        var validation = await SendAsync<ValidationJson>(client.PostAsJsonAsync(
            $"{releasePath}/{firstRelease.Id}:validate",
            new { }));
        Assert.True(validation.Valid);
        channel = (await client.GetFromJsonAsync<ChannelJson>($"{channelPath}/{channel.Id}"))!;
        firstRelease = await SendAsync<ReleaseJson>(client.PostAsJsonAsync(
            $"{releasePath}/{firstRelease.Id}:publish",
            new
            {
                manifestSigningKeyId = signingKey.Id,
                manifestSignature = SignDigest(rsa, validation.CandidateManifest.Sha256),
                expectedVersion = firstRelease.Version,
                expectedChannelVersion = channel.Version,
            }));
        Assert.Equal("DESKTOP_RELEASE_STATUS_PUBLISHED", firstRelease.Status);
        var manifest = await client.GetFromJsonAsync<ManifestJson>(
            $"{releasePath}/{firstRelease.Id}/manifest");
        Assert.Equal(validation.CandidateManifest.Sha256, manifest!.Sha256);
        var simulation = await SendAsync<DecisionJson>(client.PostAsJsonAsync(
            $"{scopePath}/release:simulate",
            new
            {
                channelKey = "stable",
                currentVersion = "0.9.0",
                targetRuntimeId = "win-x64",
                context = ContextPayload("contract-release-user", "0.9.0"),
            }));
        Assert.True(simulation.UpdateAvailable);

        var sdkScope = new AsterloomReleaseScope(
            Guid.Parse(resources.TenantId),
            Guid.Parse(resources.ApplicationId),
            Guid.Parse(resources.EnvironmentId));
        using var sdk = new AsterloomReleaseClient(
            client,
            new AsterloomReleaseClientOptions
            {
                Scope = sdkScope,
                TargetRuntimeId = "win-x64",
                PackageId = "asterloom-contract-app",
                TrustedPublicKeysByFingerprint = new Dictionary<string, string>
                {
                    [signingKey.Fingerprint] = signingKey.PublicKeyPem,
                },
                AllowInsecureDownloadUrls = true,
            },
            client);
        var sdkDecision = await sdk.CheckForUpdateAsync(
            "stable",
            "0.9.0",
            AsterloomReleaseContext.Create(
                sdkScope,
                "sdk-release-user",
                clientVersion: "0.9.0",
                platform: "windows"));
        Assert.True(sdkDecision.UpdateAvailable);
        using var downloaded = new MemoryStream();
        await sdk.DownloadToAsync(sdkDecision, downloaded);
        Assert.Equal(firstBytes, downloaded.ToArray());

        firstRelease = await SendAsync<ReleaseJson>(client.PostAsJsonAsync(
            $"{releasePath}/{firstRelease.Id}:pause",
            new { expectedVersion = firstRelease.Version }));
        Assert.Equal("DESKTOP_RELEASE_STATUS_PAUSED", firstRelease.Status);
        firstRelease = await SendAsync<ReleaseJson>(client.PostAsJsonAsync(
            $"{releasePath}/{firstRelease.Id}:promote",
            new
            {
                rolloutBasisPoints = 100_000,
                expectedVersion = firstRelease.Version,
            }));
        Assert.Equal("DESKTOP_RELEASE_STATUS_PUBLISHED", firstRelease.Status);

        var secondFullBytes = "contract release two full"u8.ToArray();
        var secondDeltaBytes = "contract release two delta from one"u8.ToArray();
        var secondFullArtifact = await UploadArtifactAsync(
            client,
            resources,
            signingKey,
            rsa,
            "2.0.0",
            secondFullBytes);
        var secondDeltaArtifact = await UploadArtifactAsync(
            client,
            resources,
            signingKey,
            rsa,
            "2.0.0",
            secondDeltaBytes,
            AsterloomReleaseArtifactKind.Delta,
            deltaFromVersion: "1.0.0");
        var secondRelease = await SendAsync<ReleaseJson>(client.PostAsJsonAsync(
            releasePath,
            new
            {
                channelId = channel.Id,
                releaseVersion = "2.0.0",
                displayName = "Version 2.0.0",
                releaseNotes = "Second contract release.",
                artifactIds = new[] { secondFullArtifact.Id, secondDeltaArtifact.Id },
                rolloutBasisPoints = 100_000,
                mandatory = true,
                minimumVersion = "1.0.0",
            }));
        var secondValidation = await SendAsync<ValidationJson>(client.PostAsJsonAsync(
            $"{releasePath}/{secondRelease.Id}:validate",
            new { }));
        channel = (await client.GetFromJsonAsync<ChannelJson>($"{channelPath}/{channel.Id}"))!;
        secondRelease = await SendAsync<ReleaseJson>(client.PostAsJsonAsync(
            $"{releasePath}/{secondRelease.Id}:publish",
            new
            {
                manifestSigningKeyId = signingKey.Id,
                manifestSignature = SignDigest(rsa, secondValidation.CandidateManifest.Sha256),
                expectedVersion = secondRelease.Version,
                expectedChannelVersion = channel.Version,
            }));

        var deltaDecision = await sdk.CheckForUpdateAsync(
            "stable",
            "1.0.0",
            AsterloomReleaseContext.Create(
                sdkScope,
                "sdk-delta-user",
                clientVersion: "1.0.0",
                platform: "windows"));
        Assert.Equal(AsterloomReleaseArtifactKind.Delta, deltaDecision.SelectedArtifact!.ArtifactKind);
        Assert.Equal(Guid.Parse(secondDeltaArtifact.Id), deltaDecision.SelectedArtifact.Id);
        Assert.Collection(
            deltaDecision.ArtifactDownloads.OrderBy(item => item.Artifact.ArtifactKind),
            item => Assert.Equal(AsterloomReleaseArtifactKind.Full, item.Artifact.ArtifactKind),
            item => Assert.Equal(AsterloomReleaseArtifactKind.Delta, item.Artifact.ArtifactKind));

        await using (var fullDownload = new MemoryStream())
        {
            await sdk.DownloadArtifactToAsync(
                deltaDecision,
                Guid.Parse(secondFullArtifact.Id),
                fullDownload);
            Assert.Equal(secondFullBytes, fullDownload.ToArray());
        }
        await using (var deltaDownload = new MemoryStream())
        {
            await sdk.DownloadArtifactToAsync(
                deltaDecision,
                Guid.Parse(secondDeltaArtifact.Id),
                deltaDownload);
            Assert.Equal(secondDeltaBytes, deltaDownload.ToArray());
        }

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"asterloom-velopack-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var logger = new TestLogger();
            var updateSource = new AsterloomVelopackUpdateSource(
                sdk,
                version => AsterloomReleaseContext.Create(
                    sdkScope,
                    "velopack-delta-user",
                    clientVersion: version,
                    platform: "windows"));
            var localRelease = new VelopackAsset
            {
                PackageId = "asterloom-contract-app",
                Version = SemanticVersion.Parse("1.0.0"),
                Type = VelopackAssetType.Full,
                FileName = "desktop-1.0.0-full.nupkg",
                SHA256 = Convert.ToHexString(SHA256.HashData(firstBytes)),
                Size = firstBytes.LongLength,
            };
            await File.WriteAllBytesAsync(
                Path.Combine(tempDirectory, localRelease.FileName),
                firstBytes);
            var locator = new TestVelopackLocator(
                "asterloom-contract-app",
                "1.0.0",
                tempDirectory,
                tempDirectory,
                tempDirectory,
                Path.Combine(tempDirectory, "Update.exe"),
                "stable",
                logger,
                localRelease,
                Path.Combine(tempDirectory, "asterloom-contract-app.exe"));
            var updateManager = new UpdateManager(
                updateSource,
                new UpdateOptions { ExplicitChannel = "stable" },
                locator);
            var update = await updateManager.CheckForUpdatesAsync();
            Assert.NotNull(update);
            Assert.Equal(SemanticVersion.Parse("2.0.0"), update.TargetFullRelease.Version);
            Assert.Equal(VelopackAssetType.Full, update.TargetFullRelease.Type);
            Assert.Single(update.DeltasToTarget);
            Assert.Equal(VelopackAssetType.Delta, update.DeltasToTarget[0].Type);
            await updateManager.DownloadUpdatesAsync(update, _ => { });
            Assert.Equal(
                secondFullBytes,
                await File.ReadAllBytesAsync(
                    Path.Combine(tempDirectory, update.TargetFullRelease.FileName)));

            var feed = await updateSource.GetReleaseFeed(
                logger,
                "asterloom-contract-app",
                "stable",
                latestLocalRelease: localRelease);
            Assert.Equal(2, feed.Assets.Length);
            foreach (var asset in feed.Assets)
            {
                var destination = Path.Combine(tempDirectory, asset.FileName);
                await updateSource.DownloadReleaseEntry(
                    logger,
                    asset,
                    destination,
                    _ => { });
                Assert.Equal(
                    asset.Type == VelopackAssetType.Full ? secondFullBytes : secondDeltaBytes,
                    await File.ReadAllBytesAsync(destination));
            }
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }

        channel = (await client.GetFromJsonAsync<ChannelJson>($"{channelPath}/{channel.Id}"))!;
        firstRelease = (await client.GetFromJsonAsync<ReleaseJson>(
            $"{releasePath}/{firstRelease.Id}"))!;
        var restored = await SendAsync<ReleaseJson>(client.PostAsJsonAsync(
            $"{releasePath}/{secondRelease.Id}:rollback",
            new
            {
                targetReleaseId = firstRelease.Id,
                expectedVersion = secondRelease.Version,
                expectedTargetVersion = firstRelease.Version,
                expectedChannelVersion = channel.Version,
            }));
        Assert.Equal(firstRelease.Id, restored.Id);
        var releases = await client.GetFromJsonAsync<ReleaseListJson>(
            $"{releasePath}?pageSize=20&includeInactive=true");
        Assert.Contains(releases!.Releases, item => item.Id == secondRelease.Id);
    }

    private static async Task<ArtifactJson> UploadArtifactAsync(
        HttpClient client,
        ScopeResources resources,
        SigningKeyJson signingKey,
        RSA rsa,
        string version,
        byte[] content,
        AsterloomReleaseArtifactKind artifactKind = AsterloomReleaseArtifactKind.Full,
        string? deltaFromVersion = null)
    {
        var scopePath = ScopePath(resources);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var upload = await SendAsync<ArtifactUploadJson>(client.PostAsJsonAsync(
            $"{scopePath}/release/artifacts:begin-upload",
            new
            {
                releaseVersion = version,
                targetRuntimeId = "win-x64",
                artifactKind = artifactKind == AsterloomReleaseArtifactKind.Full
                    ? "RELEASE_ARTIFACT_KIND_FULL"
                    : "RELEASE_ARTIFACT_KIND_DELTA",
                deltaFromVersion = deltaFromVersion ?? string.Empty,
                fileName = $"desktop-{version}-{(artifactKind == AsterloomReleaseArtifactKind.Full ? "full" : $"delta-{deltaFromVersion}")}.nupkg",
                contentType = "application/octet-stream",
                sizeBytes = content.LongLength,
                sha256,
                signingKeyId = signingKey.Id,
                signature = SignDigest(rsa, sha256),
            }));
        using (var uploadRequest = new HttpRequestMessage(
                   HttpMethod.Put,
                   upload.UploadSession.Transfer.Url))
        {
            uploadRequest.Content = new ByteArrayContent(content);
            foreach (var header in upload.UploadSession.Transfer.RequiredHeaders)
            {
                if (!uploadRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    uploadRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            using var uploadResponse = await client.SendAsync(uploadRequest);
            Assert.Equal(System.Net.HttpStatusCode.NoContent, uploadResponse.StatusCode);
        }
        var artifact = await SendAsync<ArtifactJson>(client.PostAsJsonAsync(
            $"{scopePath}/release/artifacts/{upload.Artifact.Id}:complete",
            new { expectedVersion = upload.Artifact.Version }));
        Assert.Equal("RELEASE_ARTIFACT_STATUS_VERIFIED", artifact.Status);
        return artifact;
    }

    private static async Task<ScopeResources> CreateScopeAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            "/api/v1/tenants",
            new { slug = $"release-{suffix}", displayName = "Release Tenant" }));
        var application = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications",
            new { slug = $"desktop-{suffix}", displayName = "Desktop App" }));
        var environment = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments",
            new
            {
                slug = "production",
                displayName = "Production",
                environmentType = "ENVIRONMENT_TYPE_PRODUCTION",
                isProtected = false,
            }));
        return new(tenant.Id, application.Id, environment.Id);
    }

    private static object ContextPayload(string targetingKey, string clientVersion) => new
    {
        targetingKey,
        clientVersion,
        platform = "windows",
        attributes = Array.Empty<object>(),
    };

    private static string ScopePath(ScopeResources resources) =>
        $"/api/v1/tenants/{resources.TenantId}/applications/{resources.ApplicationId}"
        + $"/environments/{resources.EnvironmentId}";

    private static string SignDigest(RSA rsa, string sha256) =>
        Convert.ToBase64String(rsa.SignData(
            Encoding.UTF8.GetBytes(sha256.ToLowerInvariant()),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss));

    private static async Task<T> SendAsync<T>(Task<HttpResponseMessage> responseTask)
    {
        using var response = await responseTask;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected success but got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("The JSON response was empty.");
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        const string clientId = "release-contract-tests";
        const string clientSecret = "Release-Contract-Tests-Secret!2026";
        using (var scope = _factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            if (await manager.FindByClientIdAsync(clientId) is null)
            {
                await manager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    ClientType = ClientTypes.Confidential,
                    DisplayName = "Release contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }
            var store = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
            var bindingId = Guid.Parse("eeeeeeee-eeee-7eee-8eee-eeeeeeeeeeee");
            if (await store.GetRoleBindingAsync(bindingId, CancellationToken.None) is null)
            {
                var management = scope.ServiceProvider
                    .GetRequiredService<AuthorizationManagementService>();
                await management.SetRoleBindingAsync(
                    bindingId.ToString(),
                    clientId,
                    AuthorizationCatalog.FindSystemRole("super-administrator")!.Id.ToString(),
                    AuthorizationScope.Global,
                    0,
                    CancellationToken.None);
            }
        }
        var client = _factory.CreateClient();
        using var tokenResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.ClientCredentials,
                [Parameters.ClientId] = clientId,
                [Parameters.ClientSecret] = clientSecret,
                [Parameters.Scope] = "asterloom.api",
            }));
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.GetProperty(Parameters.AccessToken).GetString());
        return client;
    }

    private sealed record ScopeResources(
        string TenantId,
        string ApplicationId,
        string EnvironmentId);

    private sealed record ResourceJson(string Id);

    private sealed record SigningKeyJson(
        string Id,
        string Fingerprint,
        string PublicKeyPem,
        string Status,
        long Version);

    private sealed record SigningKeyListJson(IReadOnlyList<SigningKeyJson> SigningKeys);

    private sealed record ChannelJson(
        string Id,
        string Description,
        string Status,
        long Version);

    private sealed record ChannelListJson(IReadOnlyList<ChannelJson> Channels);

    private sealed record ArtifactJson(
        string Id,
        string Status,
        long Version);

    private sealed record ArtifactListJson(IReadOnlyList<ArtifactJson> Artifacts);

    private sealed record TransferJson(
        string Url,
        string Method,
        IReadOnlyDictionary<string, string> RequiredHeaders);

    private sealed record UploadSessionJson(string Id, TransferJson Transfer);

    private sealed record ArtifactUploadJson(
        ArtifactJson Artifact,
        UploadSessionJson UploadSession);

    private sealed record ReleaseJson(
        string Id,
        string ReleaseNotes,
        IReadOnlyList<string> ArtifactIds,
        uint RolloutBasisPoints,
        bool Mandatory,
        string MinimumVersion,
        string Status,
        long Version);

    private sealed record ReleaseListJson(IReadOnlyList<ReleaseJson> Releases);

    private sealed record ManifestJson(string Sha256);

    private sealed record ValidationJson(bool Valid, ManifestJson CandidateManifest);

    private sealed record DecisionJson(bool UpdateAvailable, string Reason);

    private sealed class TestLogger : IVelopackLogger
    {
        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
        }
    }
}
