using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace Asterloom.ReferenceApp.Client;

internal sealed class ReferenceAppProvisioner(HttpClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] StorageContentTypes =
        ["application/octet-stream", "application/json"];

    public async Task<ReferenceAppState> ProvisionAsync(CancellationToken cancellationToken)
    {
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            + "-"
            + Guid.NewGuid().ToString("N")[..6];
        var slug = "reference-" + runId;
        var tenant = await PostAsync<ResourceVersion>(
            "/api/v1/tenants",
            new { slug, displayName = "Asterloom Reference " + runId },
            cancellationToken);
        var application = await PostAsync<ResourceVersion>(
            $"/api/v1/tenants/{tenant.Id}/applications",
            new { slug = "diagnostic-client", displayName = "Diagnostic Client" },
            cancellationToken);
        var environment = await PostAsync<ResourceVersion>(
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments",
            new
            {
                slug = "production",
                displayName = "Production",
                environmentType = "ENVIRONMENT_TYPE_PRODUCTION",
                isProtected = false,
            },
            cancellationToken);
        var scopePath = $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}"
            + $"/environments/{environment.Id}";

        var segment = await PostAsync<ResourceVersion>(
            scopePath + "/targeting/segments",
            new
            {
                key = "china-reference-users",
                displayName = "China reference users",
                description = "Matches the CN diagnostic context.",
                rule = new
                {
                    matchMode = "TARGETING_MATCH_MODE_ALL",
                    conditions = new[]
                    {
                        new
                        {
                            id = "region-cn",
                            attribute = "region",
                            valueKind = "TARGETING_VALUE_KIND_TEXT",
                            @operator = "TARGETING_OPERATOR_EQUALS",
                            values = new[] { new { text = "cn" } },
                            caseSensitive = false,
                        },
                    },
                },
            },
            cancellationToken);

        const string featureFlagKey = "reference.new-dashboard";
        var flag = await PostAsync<ResourceVersion>(
            scopePath + "/flags",
            new
            {
                key = featureFlagKey,
                displayName = "Reference new dashboard",
                description = "Published flag used by the full-capability reference app.",
                valueKind = "FEATURE_VALUE_KIND_BOOLEAN",
                definition = FeatureDefinition(segment.Id),
            },
            cancellationToken);
        flag = await PostAsync<ResourceVersion>(
            scopePath + $"/flags/{flag.Id}:publish",
            new { expectedVersion = flag.Version },
            cancellationToken);

        const string configKey = "reference.banner";
        var entry = await PostAsync<ResourceVersion>(
            scopePath + "/config/entries",
            new
            {
                key = configKey,
                displayName = "Reference banner",
                description = "Dynamic configuration used by the reference client.",
                valueKind = "CONFIG_VALUE_KIND_STRING",
                visibility = "CONFIG_VISIBILITY_CLIENT",
                definition = new
                {
                    schemaJson = "{\"type\":\"string\",\"minLength\":1}",
                    defaultValue = new { stringValue = "Asterloom is healthy" },
                    targetingRules = new[]
                    {
                        new
                        {
                            id = "cn-banner",
                            segmentId = segment.Id,
                            value = new { stringValue = "Asterloom CN diagnostic is healthy" },
                        },
                    },
                },
            },
            cancellationToken);
        entry = await PostAsync<ResourceVersion>(
            scopePath + $"/config/entries/{entry.Id}:publish",
            new { expectedVersion = entry.Version },
            cancellationToken);

        var bucket = await PostAsync<ResourceVersion>(
            $"/api/v1/tenants/{tenant.Id}/storage/buckets",
            new
            {
                key = "reference-files",
                displayName = "Reference files",
                description = "SDK upload/download diagnostics.",
                quotaBytes = 10_000_000,
                maxObjectSizeBytes = 2_000_000,
                allowedContentTypes = StorageContentTypes,
                accessPolicy = "STORAGE_ACCESS_POLICY_PRIVATE",
            },
            cancellationToken);

        using var rsa = RSA.Create(2048);
        var signingKey = await PostAsync<SigningKeyResource>(
            $"/api/v1/tenants/{tenant.Id}/release/signing-keys",
            new
            {
                key = "reference-" + runId,
                displayName = "Reference signing key " + runId,
                publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            },
            cancellationToken);
        var channel = await PostAsync<ResourceVersion>(
            scopePath + "/release/channels",
            new
            {
                key = "stable",
                displayName = "Stable",
                description = "Reference desktop update channel.",
            },
            cancellationToken);
        var artifactContent = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                package = "asterloom-reference-client",
                version = "1.0.0",
                provisionedAt = DateTimeOffset.UtcNow,
            }, JsonOptions));
        var artifact = await UploadReleaseArtifactAsync(
            scopePath,
            signingKey,
            rsa,
            artifactContent,
            cancellationToken);
        var release = await PostAsync<ResourceVersion>(
            scopePath + "/releases",
            new
            {
                channelId = channel.Id,
                releaseVersion = "1.0.0",
                displayName = "Reference 1.0.0",
                releaseNotes = "Signed update used by the reference diagnostics.",
                artifactIds = new[] { artifact.Id },
                rolloutBasisPoints = 100_000,
                mandatory = false,
                minimumVersion = "0.0.0",
            },
            cancellationToken);
        var validation = await PostAsync<ReleaseValidation>(
            scopePath + $"/releases/{release.Id}:validate",
            new { },
            cancellationToken);
        if (!validation.Valid)
        {
            throw new InvalidOperationException("The reference release failed platform validation.");
        }

        await PostAsync<ResourceVersion>(
            scopePath + $"/releases/{release.Id}:publish",
            new
            {
                manifestSigningKeyId = signingKey.Id,
                manifestSignature = SignDigest(rsa, validation.CandidateManifest.Sha256),
                expectedVersion = release.Version,
                expectedChannelVersion = channel.Version,
            },
            cancellationToken);

        var analyticsPath = scopePath + "/analytics";
        await PostAsync<ResourceVersion>(
            analyticsPath + "/schemas",
            new
            {
                key = "reference.diagnostic.completed",
                displayName = "Reference diagnostic completed",
                description = "Emitted whenever the sample client executes its diagnostic suite.",
                schemaJson =
                    """
                    {
                      "type":"object",
                      "additionalProperties":false,
                      "required":["runId","success"],
                      "properties":{
                        "runId":{"type":"string"},
                        "success":{"type":"boolean"},
                        "durationMs":{"type":"number"}
                      }
                    }
                    """,
                retentionDays = 30,
            },
            cancellationToken);
        var writeKey = await PostAsync<WriteKeyCredential>(
            analyticsPath + "/write-keys",
            new { name = "Reference client " + runId },
            cancellationToken);

        var telemetrySource = await PostAsync<ResourceVersion>(
            scopePath + "/telemetry/sources",
            new
            {
                key = "reference-client-" + runId,
                displayName = "Reference client " + runId,
                description = "OpenTelemetry signals emitted by the reference application.",
                serviceName = "asterloom.reference.client",
                resourceAttributesJson = "{\"diagnostic.kind\":\"full-capability\"}",
            },
            cancellationToken);

        return new ReferenceAppState(
            runId,
            Guid.Parse(tenant.Id),
            Guid.Parse(application.Id),
            Guid.Parse(environment.Id),
            Guid.Parse(segment.Id),
            featureFlagKey,
            configKey,
            Guid.Parse(bucket.Id),
            "stable",
            signingKey.Fingerprint,
            signingKey.PublicKeyPem,
            writeKey.Secret,
            Guid.Parse(telemetrySource.Id),
            DateTimeOffset.UtcNow);
    }

    private async Task<ResourceVersion> UploadReleaseArtifactAsync(
        string scopePath,
        SigningKeyResource signingKey,
        RSA rsa,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var upload = await PostAsync<ArtifactUpload>(
            scopePath + "/release/artifacts:begin-upload",
            new
            {
                releaseVersion = "1.0.0",
                targetRuntimeId = GetRuntimeIdentifier(),
                artifactKind = "RELEASE_ARTIFACT_KIND_FULL",
                fileName = "asterloom-reference-client-1.0.0-full.nupkg",
                contentType = "application/octet-stream",
                sizeBytes = content.LongLength,
                sha256,
                signingKeyId = signingKey.Id,
                signature = SignDigest(rsa, sha256),
            },
            cancellationToken);
        await UploadBytesAsync(upload.UploadSession.Transfer, content, cancellationToken);
        return await PostAsync<ResourceVersion>(
            scopePath + $"/release/artifacts/{upload.Artifact.Id}:complete",
            new { expectedVersion = upload.Artifact.Version },
            cancellationToken);
    }

    private async Task UploadBytesAsync(
        TransferTicket transfer,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var transferUri = new Uri(transfer.Url, UriKind.RelativeOrAbsolute);
        if (!transferUri.IsAbsoluteUri)
        {
            transferUri = new Uri(client.BaseAddress!, transferUri);
        }

        using var request = new HttpRequestMessage(new HttpMethod(transfer.Method), transferUri)
        {
            Content = new ByteArrayContent(content),
        };
        foreach (var header in transfer.RequiredHeaders)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var transferClient = new HttpClient();
        using var response = await transferClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(request, response, cancellationToken);
        }
    }

    private Task<T> PostAsync<T>(
        string path,
        object body,
        CancellationToken cancellationToken) =>
        SendAsync<T>(HttpMethod.Post, path, body, cancellationToken);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body, options: JsonOptions),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(request, response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"{method} {path} returned an empty JSON response.");
    }

    private static async Task<PlatformApiException> CreateExceptionAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        new(
            request.Method,
            request.RequestUri,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken));

    private static object FeatureDefinition(string segmentId) => new
    {
        enabled = true,
        defaultVariantKey = "off",
        variants = new object[]
        {
            new { key = "off", displayName = "Off", value = new { booleanValue = false } },
            new { key = "on", displayName = "On", value = new { booleanValue = true } },
        },
        prerequisites = Array.Empty<object>(),
        targetingRules = new[]
        {
            new { id = "reference-cn", segmentId, variantKey = "on" },
        },
        allocations = new[]
        {
            new { variantKey = "off", start = 0, end = 50_000 },
            new { variantKey = "on", start = 50_000, end = 100_000 },
        },
        bucketingSalt = "asterloom-reference-stable",
    };

    private static string SignDigest(RSA rsa, string sha256) =>
        Convert.ToBase64String(rsa.SignData(
            Encoding.UTF8.GetBytes(sha256.ToLowerInvariant()),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss));

    internal static string GetRuntimeIdentifier() =>
        OperatingSystem.IsWindows()
            ? Environment.Is64BitProcess ? "win-x64" : "win-x86"
            : OperatingSystem.IsLinux()
                ? Environment.Is64BitProcess ? "linux-x64" : "linux-x86"
                : OperatingSystem.IsMacOS()
                    ? Environment.Is64BitProcess ? "osx-x64" : "osx-x86"
                    : "unknown";

    private sealed record ResourceVersion(string Id, long Version);

    private sealed record SigningKeyResource(
        string Id,
        string Fingerprint,
        string PublicKeyPem,
        long Version);

    private sealed record TransferTicket(
        string Url,
        string Method,
        IReadOnlyDictionary<string, string> RequiredHeaders);

    private sealed record ArtifactUploadSession(string Id, TransferTicket Transfer);

    private sealed record ArtifactUpload(
        ResourceVersion Artifact,
        ArtifactUploadSession UploadSession);

    private sealed record ReleaseManifestCandidate(string Sha256);

    private sealed record ReleaseValidation(bool Valid, ReleaseManifestCandidate CandidateManifest);

    private sealed record WriteKeyCredential(ResourceVersion WriteKey, string Secret);
}
