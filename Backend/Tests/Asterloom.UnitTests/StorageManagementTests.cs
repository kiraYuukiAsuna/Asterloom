using System.Security.Cryptography;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Storage;
using Asterloom.Modules.Storage.Model;
using Asterloom.Modules.Storage.Transport;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class StorageManagementTests
{
    [Fact]
    public async Task BucketUploadIntegrityMetadataCopyDownloadAndLifecycleAreComplete()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformManagementService>();
        var storage = scope.ServiceProvider.GetRequiredService<StorageManagementService>();
        var transport = scope.ServiceProvider.GetRequiredService<IObjectStorageTransport>();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await platform.CreateTenantAsync(
            "storage-" + suffix,
            "Storage Team",
            CancellationToken.None);
        var sourceBucket = await storage.CreateBucketAsync(
            tenant.Id.ToString(),
            "packages",
            "Packages",
            "Unit test packages.",
            1_000_000,
            500_000,
            ["application/octet-stream"],
            StorageAccessPolicy.Private,
            CancellationToken.None);
        sourceBucket = await storage.UpdateBucketAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            "Packages updated",
            sourceBucket.Description,
            sourceBucket.QuotaBytes,
            sourceBucket.MaxObjectSizeBytes,
            sourceBucket.AllowedContentTypes,
            sourceBucket.AccessPolicy,
            sourceBucket.Version,
            CancellationToken.None);
        var targetBucket = await storage.CreateBucketAsync(
            tenant.Id.ToString(),
            "copies",
            "Copies",
            null,
            1_000_000,
            500_000,
            ["application/octet-stream"],
            StorageAccessPolicy.Private,
            CancellationToken.None);

        var content = "Asterloom storage contract"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var upload = await storage.CreateUploadSessionAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            null,
            null,
            "releases/app-1.0.0.nupkg",
            "app-1.0.0.nupkg",
            "application/octet-stream",
            content.LongLength,
            hash,
            new Dictionary<string, string> { ["purpose"] = "release" },
            CancellationToken.None);
        var transferUri = new Uri("http://localhost" + upload.Session.Transfer.Url);
        var query = QueryHelpers.ParseQuery(transferUri.Query);
        Assert.True(await transport.TryAcceptLocalUploadAsync(
            upload.Session.Id,
            query["token"].ToString(),
            new MemoryStream(content, writable: false),
            "application/octet-stream",
            content.LongLength,
            CancellationToken.None));
        var stored = await storage.CompleteUploadAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            upload.Session.Id.ToString(),
            upload.StorageObject.Version,
            CancellationToken.None);
        Assert.Equal(StorageObjectStatus.Available, stored.Status);
        Assert.Equal(hash, stored.Sha256);

        stored = await storage.UpdateObjectMetadataAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            stored.Id.ToString(),
            "renamed.nupkg",
            new Dictionary<string, string> { ["stage"] = "verified" },
            stored.Version,
            CancellationToken.None);
        var downloadTicket = await storage.CreateDownloadUrlAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            stored.Id.ToString(),
            60,
            CancellationToken.None);
        var downloadUri = new Uri("http://localhost" + downloadTicket.Url);
        var downloadQuery = QueryHelpers.ParseQuery(downloadUri.Query);
        var download = await transport.TryOpenLocalDownloadAsync(
            Guid.Parse(downloadUri.Segments[^1]),
            downloadQuery["token"].ToString(),
            CancellationToken.None);
        Assert.NotNull(download);
        using var downloaded = new MemoryStream();
        await download!.Content.CopyToAsync(downloaded);
        Assert.Equal(content, downloaded.ToArray());

        var copy = await storage.CopyObjectAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            stored.Id.ToString(),
            targetBucket.Id.ToString(),
            "copies/app-1.0.0.nupkg",
            "copy.nupkg",
            new Dictionary<string, string> { ["copied"] = "true" },
            CancellationToken.None);
        Assert.Equal(targetBucket.Id, copy.BucketId);

        stored = await storage.DeleteObjectAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            stored.Id.ToString(),
            stored.Version,
            CancellationToken.None);
        Assert.Equal(StorageObjectStatus.Deleted, stored.Status);
        sourceBucket = await storage.GetBucketAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            CancellationToken.None);
        sourceBucket = await storage.ArchiveBucketAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            sourceBucket.Version,
            CancellationToken.None);
        Assert.Equal(StorageResourceStatus.Archived, sourceBucket.Status);
        sourceBucket = await storage.RestoreBucketAsync(
            tenant.Id.ToString(),
            sourceBucket.Id.ToString(),
            sourceBucket.Version,
            CancellationToken.None);
        Assert.Equal(StorageResourceStatus.Active, sourceBucket.Status);
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
            new StorageModule(),
            new InfrastructureModule());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
