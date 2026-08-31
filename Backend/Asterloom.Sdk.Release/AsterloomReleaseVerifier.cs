using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Asterloom.Sdk.Release;

public static class AsterloomReleaseVerifier
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public static void ValidateTrustStore(
        IReadOnlyDictionary<string, string> trustedPublicKeysByFingerprint)
    {
        ArgumentNullException.ThrowIfNull(trustedPublicKeysByFingerprint);
        if (trustedPublicKeysByFingerprint.Count == 0)
        {
            throw new ArgumentException("At least one trusted release signing key is required.");
        }
        foreach (var pair in trustedPublicKeysByFingerprint)
        {
            var expected = pair.Key.Trim().ToLowerInvariant();
            if (expected.Length != 64 || expected.Any(static value => !Uri.IsHexDigit(value)))
            {
                throw new ArgumentException("Trusted signing-key fingerprints must be SHA-256 hex digests.");
            }
            if (pair.Value.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The trust store must contain public keys only.");
            }
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pair.Value);
                if (rsa.KeySize < 2_048)
                {
                    throw new ArgumentException("Trusted RSA keys must be at least 2048 bits.");
                }
                var actual = Convert.ToHexStringLower(
                    SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Trusted key fingerprint '{pair.Key}' does not match its public key.");
                }
            }
            catch (CryptographicException exception)
            {
                throw new ArgumentException("A trusted key is not a valid RSA public PEM.", exception);
            }
        }
    }

    public static void VerifyDecision(
        AsterloomUpdateDecision decision,
        IReadOnlyDictionary<string, string> trustedPublicKeysByFingerprint)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (!decision.UpdateAvailable)
        {
            if (decision.Manifest is not null
                || decision.SelectedArtifact is not null
                || decision.Download is not null
                || decision.ArtifactDownloads is { Count: > 0 })
            {
                throw Integrity("A no-update decision unexpectedly contains update material.");
            }
            return;
        }
        if (decision.Reason != AsterloomUpdateDecisionReason.UpdateAvailable
            || decision.Manifest is null
            || decision.SelectedArtifact is null
            || decision.Download is null)
        {
            throw Integrity("An available update is missing its manifest, artifact, or download ticket.");
        }

        VerifyManifest(decision.Manifest, trustedPublicKeysByFingerprint);
        var downloads = GetArtifactDownloads(decision);
        if (downloads.Count == 0
            || downloads.Select(static item => item.Artifact.Id).Distinct().Count()
                != downloads.Count)
        {
            throw Integrity("The update artifact download set is empty or contains duplicates.");
        }

        foreach (var delivery in downloads)
        {
            var signedArtifact = decision.Manifest.Artifacts.FirstOrDefault(
                artifact => artifact.ArtifactId == delivery.Artifact.Id)
                ?? throw Integrity("A downloadable artifact is not part of the signed manifest.");
            if (!string.Equals(
                    delivery.Artifact.ReleaseVersion,
                    decision.Manifest.ReleaseVersion,
                    StringComparison.Ordinal)
                || !string.Equals(
                    delivery.Artifact.TargetRuntimeId,
                    decision.SelectedArtifact.TargetRuntimeId,
                    StringComparison.Ordinal)
                || !ArtifactMatches(delivery.Artifact, signedArtifact))
            {
                throw Integrity("A downloadable artifact differs from the signed manifest entry.");
            }
            if (delivery.Download.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                throw Integrity("An update download ticket has already expired.");
            }
        }

        var selectedDownloads = downloads
            .Where(item => item.Artifact.Id == decision.SelectedArtifact.Id)
            .ToArray();
        if (selectedDownloads.Length != 1
            || !ArtifactMatches(decision.SelectedArtifact, selectedDownloads[0].Artifact)
            || !TicketMatches(decision.Download, selectedDownloads[0].Download))
        {
            throw Integrity(
                "The selected artifact and ticket do not match the downloadable artifact set.");
        }

        // Decisions emitted by current servers deliberately expose only the
        // target full package and, when selected, its exact direct delta. This
        // shape prevents Velopack from interpreting unrelated deltas as a chain.
        if (decision.ArtifactDownloads is { Count: > 0 } advertisedDownloads)
        {
            var fullCount = advertisedDownloads.Count(item =>
                item.Artifact.ArtifactKind == AsterloomReleaseArtifactKind.Full);
            var deltaCount = advertisedDownloads.Count(item =>
                item.Artifact.ArtifactKind == AsterloomReleaseArtifactKind.Delta);
            if (fullCount != 1
                || (decision.SelectedArtifact.ArtifactKind == AsterloomReleaseArtifactKind.Delta
                    ? advertisedDownloads.Count != 2
                        || deltaCount != 1
                        || string.IsNullOrWhiteSpace(decision.SelectedArtifact.DeltaFromVersion)
                    : advertisedDownloads.Count != 1 || deltaCount != 0))
            {
                throw Integrity(
                    "The update artifact set must contain the target full package and only the selected direct delta, when applicable.");
            }
        }
    }

    internal static IReadOnlyList<AsterloomReleaseArtifactDownload> GetArtifactDownloads(
        AsterloomUpdateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.ArtifactDownloads is { Count: > 0 })
        {
            return decision.ArtifactDownloads;
        }
        return decision.SelectedArtifact is not null && decision.Download is not null
            ? [new AsterloomReleaseArtifactDownload(decision.SelectedArtifact, decision.Download)]
            : [];
    }

    public static void VerifyManifest(
        AsterloomReleaseManifest manifest,
        IReadOnlyDictionary<string, string> trustedPublicKeysByFingerprint)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(trustedPublicKeysByFingerprint);
        var actualHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(manifest.PayloadJson)));
        if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Integrity("The release manifest payload does not match its SHA-256 digest.");
        }
        var manifestKey = RequireTrustedKey(
            trustedPublicKeysByFingerprint,
            manifest.SigningKeyFingerprint);
        if (!VerifyDigestSignature(manifestKey, actualHash, manifest.Signature))
        {
            throw Integrity("The release manifest signature is invalid.");
        }

        ManifestPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<ManifestPayload>(
                manifest.PayloadJson,
                PayloadOptions)
                ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new AsterloomReleaseIntegrityException(
                $"The signed manifest payload is invalid JSON: {exception.Message}");
        }
        if (payload.SchemaVersion != 1
            || payload.ReleaseId != manifest.ReleaseId
            || !string.Equals(payload.Channel, manifest.ChannelKey, StringComparison.Ordinal)
            || !string.Equals(payload.Version, manifest.ReleaseVersion, StringComparison.Ordinal)
            || !string.Equals(payload.DisplayName, manifest.DisplayName, StringComparison.Ordinal)
            || !string.Equals(payload.ReleaseNotes, manifest.ReleaseNotes, StringComparison.Ordinal)
            || payload.Mandatory != manifest.Mandatory
            || !string.Equals(payload.MinimumVersion, manifest.MinimumVersion, StringComparison.Ordinal)
            || payload.Revision != manifest.Revision
            || payload.GeneratedAt != manifest.GeneratedAt)
        {
            throw Integrity("Manifest metadata differs from its signed payload.");
        }
        if (payload.Artifacts.Count != manifest.Artifacts.Count
            || payload.Artifacts.Select(static item => item.ArtifactId).Distinct().Count()
                != payload.Artifacts.Count
            || manifest.Artifacts.Select(static item => item.ArtifactId).Distinct().Count()
                != manifest.Artifacts.Count)
        {
            throw Integrity("The manifest artifact set is invalid or contains duplicates.");
        }

        var responseArtifacts = manifest.Artifacts.ToDictionary(static item => item.ArtifactId);
        foreach (var payloadArtifact in payload.Artifacts)
        {
            if (!responseArtifacts.TryGetValue(payloadArtifact.ArtifactId, out var responseArtifact)
                || !ArtifactMatches(payloadArtifact, responseArtifact))
            {
                throw Integrity("Manifest artifact metadata differs from its signed payload.");
            }
            var artifactKey = RequireTrustedKey(
                trustedPublicKeysByFingerprint,
                responseArtifact.SigningKeyFingerprint);
            if (!VerifyDigestSignature(
                    artifactKey,
                    responseArtifact.Sha256,
                    responseArtifact.Signature))
            {
                throw Integrity(
                    $"Artifact '{responseArtifact.ArtifactId:D}' has an invalid detached signature.");
            }
        }
    }

    public static bool VerifyDigestSignature(
        string publicKeyPem,
        string sha256,
        string signature)
    {
        try
        {
            var signatureBytes = Convert.FromBase64String(signature);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(sha256.ToLowerInvariant()),
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static string RequireTrustedKey(
        IReadOnlyDictionary<string, string> trustStore,
        string fingerprint)
    {
        foreach (var pair in trustStore)
        {
            if (string.Equals(pair.Key, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }
        throw Integrity($"Signing-key fingerprint '{fingerprint}' is not trusted.");
    }

    private static bool ArtifactMatches(
        AsterloomReleaseArtifact selected,
        AsterloomReleaseManifestArtifact signed) =>
        selected.Id == signed.ArtifactId
        && string.Equals(selected.TargetRuntimeId, signed.TargetRuntimeId, StringComparison.Ordinal)
        && selected.ArtifactKind == signed.ArtifactKind
        && string.Equals(
            selected.DeltaFromVersion ?? string.Empty,
            signed.DeltaFromVersion ?? string.Empty,
            StringComparison.Ordinal)
        && string.Equals(selected.FileName, signed.FileName, StringComparison.Ordinal)
        && string.Equals(selected.ContentType, signed.ContentType, StringComparison.Ordinal)
        && selected.SizeBytes == signed.SizeBytes
        && string.Equals(selected.Sha256, signed.Sha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(selected.Signature, signed.Signature, StringComparison.Ordinal)
        && selected.SigningKeyId == signed.SigningKeyId;

    private static bool ArtifactMatches(
        AsterloomReleaseArtifact left,
        AsterloomReleaseArtifact right) =>
        left.Id == right.Id
        && string.Equals(left.ReleaseVersion, right.ReleaseVersion, StringComparison.Ordinal)
        && string.Equals(left.TargetRuntimeId, right.TargetRuntimeId, StringComparison.Ordinal)
        && left.ArtifactKind == right.ArtifactKind
        && string.Equals(
            left.DeltaFromVersion ?? string.Empty,
            right.DeltaFromVersion ?? string.Empty,
            StringComparison.Ordinal)
        && string.Equals(left.FileName, right.FileName, StringComparison.Ordinal)
        && string.Equals(left.ContentType, right.ContentType, StringComparison.Ordinal)
        && left.SizeBytes == right.SizeBytes
        && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Signature, right.Signature, StringComparison.Ordinal)
        && left.SigningKeyId == right.SigningKeyId;

    private static bool TicketMatches(
        AsterloomReleaseTransferTicket left,
        AsterloomReleaseTransferTicket right) =>
        left.Url.Equals(right.Url)
        && string.Equals(left.Method.Method, right.Method.Method, StringComparison.OrdinalIgnoreCase)
        && left.ExpiresAt == right.ExpiresAt
        && left.RequiredHeaders.Count == right.RequiredHeaders.Count
        && left.RequiredHeaders.All(pair =>
            right.RequiredHeaders.TryGetValue(pair.Key, out var value)
            && string.Equals(pair.Value, value, StringComparison.Ordinal));

    private static bool ArtifactMatches(
        ManifestPayloadArtifact payload,
        AsterloomReleaseManifestArtifact response) =>
        payload.ArtifactId == response.ArtifactId
        && string.Equals(payload.TargetRuntimeId, response.TargetRuntimeId, StringComparison.Ordinal)
        && string.Equals(
            payload.Kind,
            response.ArtifactKind == AsterloomReleaseArtifactKind.Full ? "full" : "delta",
            StringComparison.Ordinal)
        && string.Equals(
            payload.DeltaFromVersion ?? string.Empty,
            response.DeltaFromVersion ?? string.Empty,
            StringComparison.Ordinal)
        && string.Equals(payload.FileName, response.FileName, StringComparison.Ordinal)
        && string.Equals(payload.ContentType, response.ContentType, StringComparison.Ordinal)
        && payload.SizeBytes == response.SizeBytes
        && string.Equals(payload.Sha256, response.Sha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(payload.Signature, response.Signature, StringComparison.Ordinal)
        && payload.SigningKeyId == response.SigningKeyId
        && string.Equals(
            payload.SigningKeyFingerprint,
            response.SigningKeyFingerprint,
            StringComparison.OrdinalIgnoreCase);

    private static AsterloomReleaseIntegrityException Integrity(string message) => new(message);

    private sealed record ManifestPayload(
        int SchemaVersion,
        Guid ReleaseId,
        string Channel,
        string Version,
        string DisplayName,
        string ReleaseNotes,
        bool Mandatory,
        string MinimumVersion,
        long Revision,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<ManifestPayloadArtifact> Artifacts);

    private sealed record ManifestPayloadArtifact(
        Guid ArtifactId,
        string TargetRuntimeId,
        string Kind,
        string? DeltaFromVersion,
        string FileName,
        string ContentType,
        long SizeBytes,
        string Sha256,
        string Signature,
        Guid SigningKeyId,
        string SigningKeyFingerprint);
}
