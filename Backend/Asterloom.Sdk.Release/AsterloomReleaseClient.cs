using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asterloom.Targeting;

namespace Asterloom.Sdk.Release;

public sealed class AsterloomReleaseClient : IDisposable
{
    private readonly HttpClient _apiClient;
    private readonly HttpClient _downloadClient;
    private readonly AsterloomReleaseClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly bool _ownsDownloadClient;

    public AsterloomReleaseClient(
        HttpClient apiClient,
        AsterloomReleaseClientOptions options,
        HttpClient? downloadClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(apiClient);
        _apiClient = apiClient;
        _options = options;
        _jsonOptions = new JsonSerializerOptions(options.SerializerOptions)
        {
            NumberHandling = options.SerializerOptions.NumberHandling
                | JsonNumberHandling.AllowReadingFromString,
        };
        _downloadClient = downloadClient ?? new HttpClient { BaseAddress = apiClient.BaseAddress };
        _ownsDownloadClient = downloadClient is null;
    }

    public AsterloomReleaseClientOptions Options => _options;

    public async Task<AsterloomUpdateDecision> CheckForUpdateAsync(
        string channel,
        string currentVersion,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateCheck(channel, currentVersion, context);
        var scope = _options.Scope;
        var path = $"api/v1/tenants/{scope.TenantId:D}/applications/{scope.ApplicationId:D}"
            + $"/environments/{scope.EnvironmentId:D}/release:check";
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(
                new CheckRequestDto(
                    channel.Trim().ToLowerInvariant(),
                    currentVersion.Trim(),
                    _options.TargetRuntimeId.Trim().ToLowerInvariant(),
                    ToDto(context)),
                options: _jsonOptions),
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        using var response = await _apiClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<DecisionDto>(
            _jsonOptions,
            timeout.Token).ConfigureAwait(false)
            ?? throw new JsonException("The release update decision is empty.");
        var decision = ToModel(dto);
        AsterloomReleaseVerifier.VerifyDecision(
            decision,
            _options.TrustedPublicKeysByFingerprint);
        return decision;
    }

    public async Task DownloadToAsync(
        AsterloomUpdateDecision decision,
        Stream destination,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        AsterloomReleaseVerifier.VerifyDecision(
            decision,
            _options.TrustedPublicKeysByFingerprint);
        var artifact = decision.SelectedArtifact!;
        var ticket = decision.Download!;
        var uri = ResolveDownloadUri(ticket.Url);
        using var request = new HttpRequestMessage(ticket.Method, uri);
        foreach (var header in ticket.RequiredHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        using var response = await _downloadClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long total = 0;
        var lastProgress = -1;
        progress?.Invoke(0);
        while (true)
        {
            var read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total = checked(total + read);
            if (total > artifact.SizeBytes)
            {
                throw new AsterloomReleaseIntegrityException(
                    "The downloaded artifact is larger than the signed manifest permits.");
            }
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token)
                .ConfigureAwait(false);
            var currentProgress = (int)Math.Min(99, total * 100 / artifact.SizeBytes);
            if (currentProgress != lastProgress)
            {
                progress?.Invoke(currentProgress);
                lastProgress = currentProgress;
            }
        }
        var actualHash = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (total != artifact.SizeBytes
            || !string.Equals(actualHash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new AsterloomReleaseIntegrityException(
                "The downloaded artifact failed its signed size or SHA-256 integrity check.");
        }
        var signedArtifact = decision.Manifest!.Artifacts.Single(
            item => item.ArtifactId == artifact.Id);
        var publicKey = _options.TrustedPublicKeysByFingerprint.First(
            pair => string.Equals(
                pair.Key,
                signedArtifact.SigningKeyFingerprint,
                StringComparison.OrdinalIgnoreCase)).Value;
        if (!AsterloomReleaseVerifier.VerifyDigestSignature(
                publicKey,
                actualHash,
                signedArtifact.Signature))
        {
            throw new AsterloomReleaseIntegrityException(
                "The downloaded artifact failed detached-signature verification.");
        }
        progress?.Invoke(100);
    }

    public async Task DownloadToFileAsync(
        AsterloomUpdateDecision decision,
        string destinationPath,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("A destination path is required.", nameof(destinationPath));
        }
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The destination path has no directory.", nameof(destinationPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.download");
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await DownloadToAsync(decision, destination, progress, cancellationToken)
                    .ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Dispose()
    {
        if (_ownsDownloadClient)
        {
            _downloadClient.Dispose();
        }
    }

    private void ValidateCheck(
        string channel,
        string currentVersion,
        TargetingEvaluationContext context)
    {
        if (string.IsNullOrWhiteSpace(channel) || channel.Length > 100)
        {
            throw new ArgumentException("A release channel is required.", nameof(channel));
        }
        if (string.IsNullOrWhiteSpace(currentVersion) || currentVersion.Length > 100)
        {
            throw new ArgumentException("A current Semantic Version is required.", nameof(currentVersion));
        }
        ArgumentNullException.ThrowIfNull(context);
        TargetingContract.ValidateContext(context);
        if (context.ApplicationId != _options.Scope.ApplicationId
            || context.EnvironmentId != _options.Scope.EnvironmentId)
        {
            throw new ArgumentException(
                "Evaluation context scope must match the release client scope.",
                nameof(context));
        }
    }

    private Uri ResolveDownloadUri(Uri value)
    {
        var resolved = value.IsAbsoluteUri
            ? value
            : new Uri(_apiClient.BaseAddress!, value);
        if (!_options.AllowInsecureDownloadUrls
            && !string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Release download URLs must use HTTPS unless insecure development downloads are explicitly enabled.");
        }
        if (!string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(resolved.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Release download URLs must use HTTP or HTTPS.");
        }
        return resolved;
    }

    private static AsterloomUpdateDecision ToModel(DecisionDto dto) =>
        new(
            dto.UpdateAvailable,
            ParseReason(dto.Reason),
            dto.Manifest is null ? null : ToModel(dto.Manifest),
            dto.SelectedArtifact is null ? null : ToModel(dto.SelectedArtifact),
            dto.Download is null ? null : ToModel(dto.Download),
            dto.Mandatory,
            dto.BucketEvaluated,
            dto.Bucket,
            dto.RolloutBasisPoints,
            dto.Trace ?? []);

    private static AsterloomReleaseManifest ToModel(ManifestDto dto) =>
        new(
            ParseId(dto.ReleaseId, "manifest release"),
            dto.ChannelKey,
            dto.ReleaseVersion,
            dto.DisplayName,
            dto.ReleaseNotes,
            dto.Mandatory,
            dto.MinimumVersion,
            dto.Revision,
            (dto.Artifacts ?? []).Select(ToModel).ToArray(),
            dto.PayloadJson,
            dto.Sha256.ToLowerInvariant(),
            dto.Signature,
            ParseId(dto.SigningKeyId, "manifest signing key"),
            dto.SigningKeyFingerprint.ToLowerInvariant(),
            dto.GeneratedAt);

    private static AsterloomReleaseManifestArtifact ToModel(ManifestArtifactDto dto) =>
        new(
            ParseId(dto.ArtifactId, "manifest artifact"),
            dto.TargetRuntimeId,
            ParseArtifactKind(dto.ArtifactKind),
            EmptyToNull(dto.DeltaFromVersion),
            dto.FileName,
            dto.ContentType,
            dto.SizeBytes,
            dto.Sha256.ToLowerInvariant(),
            dto.Signature,
            ParseId(dto.SigningKeyId, "artifact signing key"),
            dto.SigningKeyFingerprint.ToLowerInvariant());

    private static AsterloomReleaseArtifact ToModel(ArtifactDto dto) =>
        new(
            ParseId(dto.Id, "selected artifact"),
            dto.ReleaseVersion,
            dto.TargetRuntimeId,
            ParseArtifactKind(dto.ArtifactKind),
            EmptyToNull(dto.DeltaFromVersion),
            dto.FileName,
            dto.ContentType,
            dto.SizeBytes,
            dto.Sha256.ToLowerInvariant(),
            dto.Signature,
            ParseId(dto.SigningKeyId, "artifact signing key"));

    private static AsterloomReleaseTransferTicket ToModel(TransferDto dto) =>
        new(
            new Uri(dto.Url, UriKind.RelativeOrAbsolute),
            new HttpMethod(dto.Method),
            dto.RequiredHeaders ?? new Dictionary<string, string>(StringComparer.Ordinal),
            dto.ExpiresAt);

    private static AsterloomUpdateDecisionReason ParseReason(string value) => value switch
    {
        "UPDATE_DECISION_REASON_UPDATE_AVAILABLE" => AsterloomUpdateDecisionReason.UpdateAvailable,
        "UPDATE_DECISION_REASON_CURRENT" => AsterloomUpdateDecisionReason.Current,
        "UPDATE_DECISION_REASON_CHANNEL_EMPTY" => AsterloomUpdateDecisionReason.ChannelEmpty,
        "UPDATE_DECISION_REASON_RELEASE_PAUSED" => AsterloomUpdateDecisionReason.ReleasePaused,
        "UPDATE_DECISION_REASON_TARGETING_MISS" => AsterloomUpdateDecisionReason.TargetingMiss,
        "UPDATE_DECISION_REASON_ROLLOUT_EXCLUDED" => AsterloomUpdateDecisionReason.RolloutExcluded,
        "UPDATE_DECISION_REASON_NO_COMPATIBLE_ARTIFACT" =>
            AsterloomUpdateDecisionReason.NoCompatibleArtifact,
        _ => throw new JsonException($"Unsupported update decision reason '{value}'."),
    };

    private static AsterloomReleaseArtifactKind ParseArtifactKind(string value) => value switch
    {
        "RELEASE_ARTIFACT_KIND_FULL" => AsterloomReleaseArtifactKind.Full,
        "RELEASE_ARTIFACT_KIND_DELTA" => AsterloomReleaseArtifactKind.Delta,
        _ => throw new JsonException($"Unsupported release artifact kind '{value}'."),
    };

    private static Guid ParseId(string value, string name) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new JsonException($"The {name} identifier is invalid.");

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ContextDto ToDto(TargetingEvaluationContext context) =>
        new(
            context.TargetingKey,
            context.UserId ?? string.Empty,
            context.ClientVersion ?? string.Empty,
            context.Platform ?? string.Empty,
            context.Region ?? string.Empty,
            context.Language ?? string.Empty,
            context.Attributes
                .OrderBy(static item => item.Key, StringComparer.Ordinal)
                .Select(item => new AttributeDto(item.Key, ToDto(item.Value)))
                .ToArray());

    private static TargetingValueDto ToDto(TargetingValue value) => value.Kind switch
    {
        TargetingValueKind.Text => new(Text: value.StringValue),
        TargetingValueKind.Truth => new(Truth: value.BooleanValue),
        TargetingValueKind.Numeric => new(Numeric: value.NumberValue),
        _ => throw new ArgumentException("A targeting attribute has an invalid value kind."),
    };

    private sealed record CheckRequestDto(
        string ChannelKey,
        string CurrentVersion,
        string TargetRuntimeId,
        ContextDto Context);

    private sealed record ContextDto(
        string TargetingKey,
        string UserId,
        string ClientVersion,
        string Platform,
        string Region,
        string Language,
        IReadOnlyList<AttributeDto> Attributes);

    private sealed record AttributeDto(string Key, TargetingValueDto Value);

    private sealed record TargetingValueDto(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Truth = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Numeric = null);

    private sealed record DecisionDto(
        bool UpdateAvailable,
        string Reason,
        ManifestDto? Manifest,
        ArtifactDto? SelectedArtifact,
        TransferDto? Download,
        bool Mandatory,
        bool BucketEvaluated,
        uint Bucket,
        uint RolloutBasisPoints,
        IReadOnlyList<string>? Trace);

    private sealed record ManifestDto(
        string ReleaseId,
        string ChannelKey,
        string ReleaseVersion,
        string DisplayName,
        string ReleaseNotes,
        bool Mandatory,
        string MinimumVersion,
        long Revision,
        IReadOnlyList<ManifestArtifactDto>? Artifacts,
        string PayloadJson,
        string Sha256,
        string Signature,
        string SigningKeyId,
        string SigningKeyFingerprint,
        DateTimeOffset GeneratedAt);

    private sealed record ManifestArtifactDto(
        string ArtifactId,
        string TargetRuntimeId,
        string ArtifactKind,
        string DeltaFromVersion,
        string FileName,
        string ContentType,
        long SizeBytes,
        string Sha256,
        string Signature,
        string SigningKeyId,
        string SigningKeyFingerprint);

    private sealed record ArtifactDto(
        string Id,
        string ReleaseVersion,
        string TargetRuntimeId,
        string ArtifactKind,
        string DeltaFromVersion,
        string FileName,
        string ContentType,
        long SizeBytes,
        string Sha256,
        string Signature,
        string SigningKeyId);

    private sealed record TransferDto(
        string Url,
        string Method,
        IReadOnlyDictionary<string, string>? RequiredHeaders,
        DateTimeOffset ExpiresAt);
}
