using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Sdk.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class StorageContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] BinaryContentTypes = ["application/octet-stream"];
    private readonly WebApplicationFactory<Program> _factory;

    public StorageContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task JsonTranscodingCoversCompleteStorageSurfaceAndBinaryTransfers()
    {
        using var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            "/api/v1/tenants",
            new { slug = "storage-" + suffix, displayName = "Storage Tenant" }));
        var basePath = $"/api/v1/tenants/{tenant.Id}/storage/buckets";
        var source = await SendAsync<BucketJson>(client.PostAsJsonAsync(
            basePath,
            new
            {
                key = "packages",
                displayName = "Packages",
                description = "Storage contract packages.",
                quotaBytes = 1_000_000,
                maxObjectSizeBytes = 500_000,
                allowedContentTypes = BinaryContentTypes,
                accessPolicy = "STORAGE_ACCESS_POLICY_PRIVATE",
            }));
        var target = await SendAsync<BucketJson>(client.PostAsJsonAsync(
            basePath,
            new
            {
                key = "copies",
                displayName = "Copies",
                quotaBytes = 1_000_000,
                maxObjectSizeBytes = 500_000,
                allowedContentTypes = BinaryContentTypes,
                accessPolicy = "STORAGE_ACCESS_POLICY_PRIVATE",
            }));
        var buckets = await client.GetFromJsonAsync<BucketListJson>($"{basePath}?pageSize=20");
        Assert.Contains(buckets!.Buckets, item => item.Id == source.Id);
        var fetchedBucket = await client.GetFromJsonAsync<BucketJson>($"{basePath}/{source.Id}");
        Assert.Equal(source.Id, fetchedBucket!.Id);
        source = await SendAsync<BucketJson>(client.PatchAsJsonAsync(
            $"{basePath}/{source.Id}",
            new
            {
                displayName = "Packages updated",
                description = source.Description,
                quotaBytes = source.QuotaBytes,
                maxObjectSizeBytes = source.MaxObjectSizeBytes,
                allowedContentTypes = source.AllowedContentTypes,
                accessPolicy = source.AccessPolicy,
                expectedVersion = source.Version,
            }));

        var bytes = "Asterloom JSON transcoding storage"u8.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var upload = await SendAsync<UploadSessionJson>(client.PostAsJsonAsync(
            $"{basePath}/{source.Id}/uploads",
            new
            {
                objectKey = "contract/package.nupkg",
                fileName = "package.nupkg",
                contentType = "application/octet-stream",
                sizeBytes = bytes.LongLength,
                sha256,
                customMetadata = new Dictionary<string, string> { ["purpose"] = "contract" },
            }));
        using (var uploadRequest = new HttpRequestMessage(HttpMethod.Put, upload.Transfer.Url))
        {
            uploadRequest.Content = new ByteArrayContent(bytes);
            foreach (var header in upload.Transfer.RequiredHeaders)
            {
                if (!uploadRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    uploadRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            using var uploadResponse = await client.SendAsync(uploadRequest);
            Assert.Equal(System.Net.HttpStatusCode.NoContent, uploadResponse.StatusCode);
        }
        var storageObject = await SendAsync<ObjectJson>(client.PostAsJsonAsync(
            $"{basePath}/{source.Id}/uploads/{upload.Id}:complete",
            new { expectedObjectVersion = upload.Item.Version }));
        Assert.Equal("STORAGE_OBJECT_STATUS_AVAILABLE", storageObject.Status);
        Assert.Equal(sha256, storageObject.Sha256);

        var listedObjects = await client.GetFromJsonAsync<ObjectListJson>(
            $"{basePath}/{source.Id}/objects?pageSize=20");
        Assert.Contains(listedObjects!.Objects, item => item.Id == storageObject.Id);
        var fetchedObject = await client.GetFromJsonAsync<ObjectJson>(
            $"{basePath}/{source.Id}/objects/{storageObject.Id}");
        Assert.Equal(storageObject.Id, fetchedObject!.Id);
        storageObject = await SendAsync<ObjectJson>(client.PatchAsJsonAsync(
            $"{basePath}/{source.Id}/objects/{storageObject.Id}/metadata",
            new
            {
                fileName = "renamed.nupkg",
                customMetadata = new Dictionary<string, string> { ["verified"] = "true" },
                expectedVersion = storageObject.Version,
            }));
        var download = await SendAsync<TransferJson>(client.PostAsJsonAsync(
            $"{basePath}/{source.Id}/objects/{storageObject.Id}:download",
            new { lifetimeSeconds = 60 }));
        Assert.Equal("GET", download.Method);
        Assert.Equal(bytes, await client.GetByteArrayAsync(download.Url));

        var copy = await SendAsync<ObjectJson>(client.PostAsJsonAsync(
            $"{basePath}/{source.Id}/objects/{storageObject.Id}:copy",
            new
            {
                targetBucketId = target.Id,
                objectKey = "copies/package.nupkg",
                fileName = "copy.nupkg",
                customMetadata = new Dictionary<string, string> { ["copied"] = "true" },
            }));
        Assert.Equal(target.Id, copy.BucketId);
        copy = await SendAsync<ObjectJson>(client.DeleteAsync(
            $"{basePath}/{target.Id}/objects/{copy.Id}?expectedVersion={copy.Version}"));
        Assert.Equal("STORAGE_OBJECT_STATUS_DELETED", copy.Status);
        storageObject = await SendAsync<ObjectJson>(client.DeleteAsync(
            $"{basePath}/{source.Id}/objects/{storageObject.Id}?expectedVersion={storageObject.Version}"));
        Assert.Equal("STORAGE_OBJECT_STATUS_DELETED", storageObject.Status);

        source = (await client.GetFromJsonAsync<BucketJson>($"{basePath}/{source.Id}"))!;
        source = await SendAsync<BucketJson>(client.DeleteAsync(
            $"{basePath}/{source.Id}?expectedVersion={source.Version}"));
        Assert.Equal("STORAGE_RESOURCE_STATUS_ARCHIVED", source.Status);
        source = await SendAsync<BucketJson>(client.PostAsJsonAsync(
            $"{basePath}/{source.Id}:restore",
            new { expectedVersion = source.Version }));
        Assert.Equal("STORAGE_RESOURCE_STATUS_ACTIVE", source.Status);

        using var sdk = new AsterloomStorageClient(
            client,
            new AsterloomStorageClientOptions
            {
                Scope = new AsterloomStorageScope(Guid.Parse(tenant.Id)),
                AllowInsecureTransferUrls = true,
            },
            client);
        var sdkBytes = "Asterloom storage SDK"u8.ToArray();
        var sdkHash = Convert.ToHexString(SHA256.HashData(sdkBytes)).ToLowerInvariant();
        var sdkObject = await sdk.UploadAsync(
            new AsterloomStorageUploadRequest(
                Guid.Parse(source.Id),
                "sdk/package.nupkg",
                "sdk-package.nupkg",
                "application/octet-stream",
                sdkBytes.LongLength,
                sdkHash),
            new MemoryStream(sdkBytes, writable: false));
        using var sdkDownload = new MemoryStream();
        await sdk.DownloadToAsync(sdkObject, sdkDownload, TimeSpan.FromSeconds(60));
        Assert.Equal(sdkBytes, sdkDownload.ToArray());
    }

    private static async Task<T> SendAsync<T>(Task<HttpResponseMessage> responseTask)
    {
        using var response = await responseTask;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("The JSON response was empty.");
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        const string clientId = "storage-contract-tests";
        const string clientSecret = "Storage-Contract-Tests-Secret!2026";
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
                    DisplayName = "Storage contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }
            var store = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
            var bindingId = Guid.Parse("dddddddd-dddd-7ddd-8ddd-dddddddddddd");
            if (await store.GetRoleBindingAsync(bindingId, CancellationToken.None) is null)
            {
                var management = scope.ServiceProvider.GetRequiredService<AuthorizationManagementService>();
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

    private sealed record ResourceJson(string Id);

    private sealed record BucketJson(
        string Id,
        string Description,
        long QuotaBytes,
        long MaxObjectSizeBytes,
        IReadOnlyList<string> AllowedContentTypes,
        string AccessPolicy,
        string Status,
        long Version);

    private sealed record BucketListJson(IReadOnlyList<BucketJson> Buckets);

    private sealed record ObjectJson(
        string Id,
        string BucketId,
        string Sha256,
        string Status,
        long Version);

    private sealed record ObjectListJson(IReadOnlyList<ObjectJson> Objects);

    private sealed record TransferJson(
        string Url,
        string Method,
        IReadOnlyDictionary<string, string> RequiredHeaders);

    private sealed record UploadSessionJson(
        string Id,
        [property: JsonPropertyName("object")] ObjectJson Item,
        TransferJson Transfer);
}
