using System.Security.Cryptography;
using System.Text;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Release;
using Asterloom.Modules.Release.Model;
using Asterloom.Modules.Storage;
using Asterloom.Modules.Storage.Transport;
using Asterloom.Modules.Targeting;
using Asterloom.Sdk.Release;
using Asterloom.Targeting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class ReleaseManagementTests
{
    [Fact]
    public async Task SignedArtifactPublishPausePromoteRollbackAndUpdateCheckAreComplete()
    {
        await using var provider = CreateProvider();
        await using var serviceScope = provider.CreateAsyncScope();
        var platform = serviceScope.ServiceProvider.GetRequiredService<PlatformManagementService>();
        var releases = serviceScope.ServiceProvider.GetRequiredService<ReleaseManagementService>();
        var evaluator = serviceScope.ServiceProvider.GetRequiredService<ReleaseEvaluationService>();
        var transport = serviceScope.ServiceProvider.GetRequiredService<IObjectStorageTransport>();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await platform.CreateTenantAsync(
            "release-" + suffix,
            "Release Team",
            CancellationToken.None);
        var application = await platform.CreateApplicationAsync(
            tenant.Id.ToString(),
            "desktop-" + suffix,
            "Desktop App",
            CancellationToken.None);
        var environment = await platform.CreateEnvironmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            "production",
            "Production",
            PlatformEnvironmentType.Production,
            isProtected: true,
            CancellationToken.None);
        var route = new RouteScope(tenant.Id, application.Id, environment.Id);

        using var rsa = RSA.Create(2048);
        var signingKey = await releases.CreateSigningKeyAsync(
            route.Tenant,
            "desktop-production",
            "Desktop production",
            rsa.ExportSubjectPublicKeyInfoPem(),
            CancellationToken.None);
        var channel = await releases.CreateChannelAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            "stable",
            "Stable",
            "Production desktop updates.",
            CancellationToken.None);
        channel = await releases.UpdateChannelAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            channel.Id.ToString(),
            "Stable channel",
            channel.Description,
            channel.Version,
            CancellationToken.None);

        var firstArtifact = await UploadArtifactAsync(
            releases,
            transport,
            route,
            signingKey,
            rsa,
            "1.0.0",
            "first signed package"u8.ToArray());
        var firstRelease = await releases.CreateReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            channel.Id.ToString(),
            "1.0.0",
            "Version 1.0.0",
            "First stable release.",
            [firstArtifact.Id.ToString()],
            100_000,
            targetSegmentId: null,
            mandatory: false,
            minimumVersion: "0.9.0",
            CancellationToken.None);
        firstRelease = await releases.UpdateReleaseDraftAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            firstRelease.Id.ToString(),
            "Version 1.0.0 stable",
            firstRelease.ReleaseNotes,
            firstRelease.ArtifactIds.Select(static id => id.ToString()).ToArray(),
            firstRelease.RolloutBasisPoints,
            targetSegmentId: null,
            firstRelease.Mandatory,
            firstRelease.MinimumVersion,
            firstRelease.Version,
            CancellationToken.None);
        var validation = await releases.ValidateReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            firstRelease.Id.ToString(),
            CancellationToken.None);
        Assert.True(validation.Valid);
        channel = await releases.GetChannelAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            channel.Id.ToString(),
            CancellationToken.None);
        firstRelease = await releases.PublishReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            firstRelease.Id.ToString(),
            signingKey.Id.ToString(),
            SignDigest(rsa, validation.CandidateManifest.Sha256),
            firstRelease.Version,
            channel.Version,
            CancellationToken.None);

        var context = new TargetingEvaluationContext(
            "release-user",
            application.Id,
            environment.Id,
            clientVersion: "0.9.0",
            platform: "windows");
        var decision = await evaluator.CheckForUpdateAsync(
            new(
                new(tenant.Id, application.Id, environment.Id),
                "stable",
                "0.9.0",
                "win-x64",
                context),
            CancellationToken.None);
        Assert.True(decision.UpdateAvailable);
        Assert.Equal(firstArtifact.Id, decision.SelectedArtifact!.Id);
        Assert.Equal(firstRelease.Id, decision.Manifest!.ReleaseId);

        firstRelease = await releases.PauseReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            firstRelease.Id.ToString(),
            firstRelease.Version,
            CancellationToken.None);
        var paused = await evaluator.CheckForUpdateAsync(
            new(
                new(tenant.Id, application.Id, environment.Id),
                "stable",
                "0.9.0",
                "win-x64",
                context),
            CancellationToken.None);
        Assert.Equal(UpdateDecisionReason.ReleasePaused, paused.Reason);
        firstRelease = await releases.PromoteReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            firstRelease.Id.ToString(),
            100_000,
            firstRelease.Version,
            CancellationToken.None);

        var secondFullArtifact = await UploadArtifactAsync(
            releases,
            transport,
            route,
            signingKey,
            rsa,
            "2.0.0",
            "second signed package"u8.ToArray());
        var secondDeltaArtifact = await UploadArtifactAsync(
            releases,
            transport,
            route,
            signingKey,
            rsa,
            "2.0.0",
            "second delta package"u8.ToArray(),
            ReleaseArtifactKind.Delta,
            deltaFromVersion: "1.0.0");
        var secondRelease = await releases.CreateReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            channel.Id.ToString(),
            "2.0.0",
            "Version 2.0.0",
            "Second stable release.",
            [secondFullArtifact.Id.ToString(), secondDeltaArtifact.Id.ToString()],
            100_000,
            targetSegmentId: null,
            mandatory: true,
            minimumVersion: "1.0.0",
            CancellationToken.None);
        var secondValidation = await releases.ValidateReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            secondRelease.Id.ToString(),
            CancellationToken.None);
        channel = await releases.GetChannelAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            channel.Id.ToString(),
            CancellationToken.None);
        secondRelease = await releases.PublishReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            secondRelease.Id.ToString(),
            signingKey.Id.ToString(),
            SignDigest(rsa, secondValidation.CandidateManifest.Sha256),
            secondRelease.Version,
            channel.Version,
            CancellationToken.None);

        var deltaContext = new TargetingEvaluationContext(
            "release-user",
            application.Id,
            environment.Id,
            clientVersion: "1.0.0",
            platform: "windows");
        var deltaDecision = await evaluator.CheckForUpdateAsync(
            new(
                new(tenant.Id, application.Id, environment.Id),
                "stable",
                "1.0.0",
                "win-x64",
                deltaContext),
            CancellationToken.None);
        Assert.True(deltaDecision.UpdateAvailable);
        Assert.Equal(secondDeltaArtifact.Id, deltaDecision.SelectedArtifact!.Id);
        Assert.Collection(
            deltaDecision.ArtifactDownloads,
            item => Assert.Equal(secondFullArtifact.Id, item.Artifact.Id),
            item => Assert.Equal(secondDeltaArtifact.Id, item.Artifact.Id));
        Assert.Equal(
            deltaDecision.SelectedArtifact.Id,
            deltaDecision.ArtifactDownloads.Single(item =>
                item.Download == deltaDecision.Download).Artifact.Id);
        VerifyWithSdk(deltaDecision, signingKey.PublicKeyPem);

        var belowMinimumDecision = await evaluator.CheckForUpdateAsync(
            new(
                new(tenant.Id, application.Id, environment.Id),
                "stable",
                "0.9.0",
                "win-x64",
                context),
            CancellationToken.None);
        Assert.Equal(secondFullArtifact.Id, belowMinimumDecision.SelectedArtifact!.Id);
        Assert.Single(belowMinimumDecision.ArtifactDownloads);
        Assert.True(belowMinimumDecision.Mandatory);

        channel = await releases.GetChannelAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            channel.Id.ToString(),
            CancellationToken.None);
        firstRelease = await releases.GetReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            firstRelease.Id.ToString(),
            CancellationToken.None);
        var restored = await releases.RollbackReleaseAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            secondRelease.Id.ToString(),
            firstRelease.Id.ToString(),
            secondRelease.Version,
            firstRelease.Version,
            channel.Version,
            CancellationToken.None);
        Assert.Equal(firstRelease.Id, restored.Id);
        Assert.Equal(DesktopReleaseStatus.Published, restored.Status);

        var rolledBackDecision = await evaluator.CheckForUpdateAsync(
            new(
                new(tenant.Id, application.Id, environment.Id),
                "stable",
                "0.9.0",
                "win-x64",
                context),
            CancellationToken.None);
        Assert.Equal(firstRelease.Id, rolledBackDecision.Release!.Id);
        VerifyWithSdk(rolledBackDecision, signingKey.PublicKeyPem);

        var artifactInUse = await Assert.ThrowsAsync<AsterloomException>(() =>
            releases.ArchiveArtifactAsync(
                route.Tenant,
                route.Application,
                route.Environment,
                firstArtifact.Id.ToString(),
                firstArtifact.Version,
                CancellationToken.None));
        Assert.Equal("release_artifact_in_use", artifactInUse.ErrorCode);
        var activeChannel = await releases.GetChannelAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            channel.Id.ToString(),
            CancellationToken.None);
        var channelInUse = await Assert.ThrowsAsync<AsterloomException>(() =>
            releases.ArchiveChannelAsync(
                route.Tenant,
                route.Application,
                route.Environment,
                activeChannel.Id.ToString(),
                activeChannel.Version,
                CancellationToken.None));
        Assert.Equal("release_channel_has_active_release", channelInUse.ErrorCode);

        signingKey = await releases.ArchiveSigningKeyAsync(
            route.Tenant,
            signingKey.Id.ToString(),
            signingKey.Version,
            CancellationToken.None);
        signingKey = await releases.RestoreSigningKeyAsync(
            route.Tenant,
            signingKey.Id.ToString(),
            signingKey.Version,
            CancellationToken.None);
        Assert.Equal(ReleaseSigningKeyStatus.Active, signingKey.Status);
        Assert.NotEmpty((await releases.ListSigningKeysAsync(
            route.Tenant,
            20,
            null,
            "desktop",
            includeArchived: true,
            CancellationToken.None)).Items);
        Assert.Equal(3, (await releases.ListArtifactsAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            20,
            null,
            null,
            includeArchived: true,
            CancellationToken.None)).Items.Count);
        Assert.Equal(2, (await releases.ListReleasesAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            20,
            null,
            null,
            includeInactive: true,
            CancellationToken.None)).Items.Count);
    }

    private static async Task<ReleaseArtifact> UploadArtifactAsync(
        ReleaseManagementService releases,
        IObjectStorageTransport transport,
        RouteScope route,
        ReleaseSigningKey signingKey,
        RSA rsa,
        string version,
        byte[] content,
        ReleaseArtifactKind artifactKind = ReleaseArtifactKind.Full,
        string? deltaFromVersion = null)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var upload = await releases.CreateArtifactUploadAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            version,
            "win-x64",
            artifactKind,
            deltaFromVersion,
            $"desktop-{version}-{(artifactKind == ReleaseArtifactKind.Full ? "full" : $"delta-{deltaFromVersion}")}.nupkg",
            "application/octet-stream",
            content.LongLength,
            hash,
            signingKey.Id.ToString(),
            SignDigest(rsa, hash),
            CancellationToken.None);
        var transferUri = new Uri("http://localhost" + upload.UploadSession.Session.Transfer.Url);
        var query = QueryHelpers.ParseQuery(transferUri.Query);
        Assert.True(await transport.TryAcceptLocalUploadAsync(
            upload.UploadSession.Session.Id,
            query["token"].ToString(),
            new MemoryStream(content, writable: false),
            "application/octet-stream",
            content.LongLength,
            CancellationToken.None));
        var artifact = await releases.CompleteArtifactUploadAsync(
            route.Tenant,
            route.Application,
            route.Environment,
            upload.Artifact.Id.ToString(),
            upload.Artifact.Version,
            CancellationToken.None);
        Assert.Equal(ReleaseArtifactStatus.Verified, artifact.Status);
        return artifact;
    }

    private static string SignDigest(RSA rsa, string sha256) =>
        Convert.ToBase64String(rsa.SignData(
            Encoding.UTF8.GetBytes(sha256.ToLowerInvariant()),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss));

    private static void VerifyWithSdk(UpdateDecision source, string publicKeyPem)
    {
        var manifest = source.Manifest!;
        var sdkManifest = new AsterloomReleaseManifest(
            manifest.ReleaseId,
            manifest.ChannelKey,
            manifest.ReleaseVersion,
            manifest.DisplayName,
            manifest.ReleaseNotes,
            manifest.Mandatory,
            manifest.MinimumVersion,
            manifest.Revision,
            manifest.Artifacts.Select(artifact => new AsterloomReleaseManifestArtifact(
                artifact.ArtifactId,
                artifact.TargetRuntimeId,
                artifact.ArtifactKind == ReleaseArtifactKind.Full
                    ? AsterloomReleaseArtifactKind.Full
                    : AsterloomReleaseArtifactKind.Delta,
                string.IsNullOrEmpty(artifact.DeltaFromVersion) ? null : artifact.DeltaFromVersion,
                artifact.FileName,
                artifact.ContentType,
                artifact.SizeBytes,
                artifact.Sha256,
                artifact.Signature,
                artifact.SigningKeyId,
                artifact.SigningKeyFingerprint)).ToArray(),
            manifest.PayloadJson,
            manifest.Sha256,
            manifest.Signature,
            manifest.SigningKeyId!.Value,
            manifest.SigningKeyFingerprint,
            manifest.GeneratedAt);
        var selected = source.SelectedArtifact!;
        var sdkArtifact = new AsterloomReleaseArtifact(
            selected.Id,
            selected.ReleaseVersion,
            selected.TargetRuntimeId,
            selected.ArtifactKind == ReleaseArtifactKind.Full
                ? AsterloomReleaseArtifactKind.Full
                : AsterloomReleaseArtifactKind.Delta,
            string.IsNullOrEmpty(selected.DeltaFromVersion) ? null : selected.DeltaFromVersion,
            selected.FileName,
            selected.ContentType,
            selected.SizeBytes,
            selected.Sha256,
            selected.Signature,
            selected.SigningKeyId);
        var sdkDecision = new AsterloomUpdateDecision(
            true,
            AsterloomUpdateDecisionReason.UpdateAvailable,
            sdkManifest,
            sdkArtifact,
            new(
                new Uri(source.Download!.Url, UriKind.RelativeOrAbsolute),
                new HttpMethod(source.Download.Method),
                source.Download.RequiredHeaders,
                source.Download.ExpiresAt),
            source.Mandatory,
            source.BucketEvaluated,
            source.Bucket,
            source.RolloutBasisPoints,
            source.Trace)
        {
            ArtifactDownloads = source.ArtifactDownloads.Select(item =>
            {
                var artifact = item.Artifact;
                return new AsterloomReleaseArtifactDownload(
                    new AsterloomReleaseArtifact(
                        artifact.Id,
                        artifact.ReleaseVersion,
                        artifact.TargetRuntimeId,
                        artifact.ArtifactKind == ReleaseArtifactKind.Full
                            ? AsterloomReleaseArtifactKind.Full
                            : AsterloomReleaseArtifactKind.Delta,
                        string.IsNullOrEmpty(artifact.DeltaFromVersion)
                            ? null
                            : artifact.DeltaFromVersion,
                        artifact.FileName,
                        artifact.ContentType,
                        artifact.SizeBytes,
                        artifact.Sha256,
                        artifact.Signature,
                        artifact.SigningKeyId),
                    new AsterloomReleaseTransferTicket(
                        new Uri(item.Download.Url, UriKind.RelativeOrAbsolute),
                        new HttpMethod(item.Download.Method),
                        item.Download.RequiredHeaders,
                        item.Download.ExpiresAt));
            }).ToArray(),
        };
        AsterloomReleaseVerifier.VerifyDecision(
            sdkDecision,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [manifest.SigningKeyFingerprint] = publicKeyPem,
            });
        Assert.Throws<AsterloomReleaseIntegrityException>(() =>
            AsterloomReleaseVerifier.VerifyDecision(
                sdkDecision with
                {
                    SelectedArtifact = sdkArtifact with { Sha256 = new string('0', 64) },
                },
                new Dictionary<string, string>
                {
                    [manifest.SigningKeyFingerprint] = publicKeyPem,
                }));
        if (sdkArtifact.ArtifactKind == AsterloomReleaseArtifactKind.Delta)
        {
            Assert.Throws<AsterloomReleaseIntegrityException>(() =>
                AsterloomReleaseVerifier.VerifyDecision(
                    sdkDecision with
                    {
                        ArtifactDownloads = sdkDecision.ArtifactDownloads
                            .Where(item =>
                                item.Artifact.ArtifactKind == AsterloomReleaseArtifactKind.Delta)
                            .ToArray(),
                    },
                    new Dictionary<string, string>
                    {
                        [manifest.SigningKeyFingerprint] = publicKeyPem,
                    }));
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "Memory",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAsterloomModules(
            configuration,
            new PlatformModule(),
            new TargetingModule(),
            new StorageModule(),
            new ReleaseModule(),
            new InfrastructureModule());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed record RouteScope(Guid TenantId, Guid ApplicationId, Guid EnvironmentId)
    {
        public string Tenant => TenantId.ToString("D");

        public string Application => ApplicationId.ToString("D");

        public string Environment => EnvironmentId.ToString("D");
    }
}
