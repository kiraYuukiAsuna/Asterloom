using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asterloom.Modules.Release.Model;

namespace Asterloom.Modules.Release;

internal static class ReleaseManifestBuilder
{
    public static ReleaseManifest Build(
        DesktopRelease release,
        ReleaseChannel channel,
        IReadOnlyList<ReleaseArtifact> artifacts,
        IReadOnlyDictionary<Guid, ReleaseSigningKey> signingKeys)
    {
        var manifestArtifacts = artifacts
            .OrderBy(static artifact => artifact.TargetRuntimeId, StringComparer.Ordinal)
            .ThenBy(static artifact => artifact.ArtifactKind)
            .ThenBy(static artifact => artifact.DeltaFromVersion, StringComparer.Ordinal)
            .Select(artifact => new ReleaseManifestArtifact(
                artifact.Id,
                artifact.TargetRuntimeId,
                artifact.ArtifactKind,
                artifact.DeltaFromVersion,
                artifact.FileName,
                artifact.ContentType,
                artifact.SizeBytes,
                artifact.Sha256,
                artifact.Signature,
                artifact.SigningKeyId,
                signingKeys[artifact.SigningKeyId].Fingerprint))
            .ToArray();
        var generatedAt = release.ManifestGeneratedAt ?? release.UpdatedAt;
        var payload = WriteCanonicalPayload(release, channel, manifestArtifacts, generatedAt);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new ReleaseManifest(
            release.Id,
            channel.Key,
            release.ReleaseVersion,
            release.DisplayName,
            release.ReleaseNotes,
            release.Mandatory,
            release.MinimumVersion,
            release.Revision,
            manifestArtifacts,
            payload,
            hash,
            release.ManifestSignature,
            release.ManifestSigningKeyId,
            release.ManifestSigningKeyFingerprint,
            generatedAt);
    }

    private static string WriteCanonicalPayload(
        DesktopRelease release,
        ReleaseChannel channel,
        IReadOnlyList<ReleaseManifestArtifact> artifacts,
        DateTimeOffset generatedAt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("releaseId", release.Id.ToString("D"));
            writer.WriteString("channel", channel.Key);
            writer.WriteString("version", release.ReleaseVersion);
            writer.WriteString("displayName", release.DisplayName);
            writer.WriteString("releaseNotes", release.ReleaseNotes);
            writer.WriteBoolean("mandatory", release.Mandatory);
            writer.WriteString("minimumVersion", release.MinimumVersion);
            writer.WriteNumber("revision", release.Revision);
            writer.WriteString("generatedAt", generatedAt.ToUniversalTime().ToString("O"));
            writer.WriteStartArray("artifacts");
            foreach (var artifact in artifacts)
            {
                writer.WriteStartObject();
                writer.WriteString("artifactId", artifact.ArtifactId.ToString("D"));
                writer.WriteString("targetRuntimeId", artifact.TargetRuntimeId);
                writer.WriteString(
                    "kind",
                    artifact.ArtifactKind == ReleaseArtifactKind.Full ? "full" : "delta");
                writer.WriteString("deltaFromVersion", artifact.DeltaFromVersion);
                writer.WriteString("fileName", artifact.FileName);
                writer.WriteString("contentType", artifact.ContentType);
                writer.WriteNumber("sizeBytes", artifact.SizeBytes);
                writer.WriteString("sha256", artifact.Sha256);
                writer.WriteString("signature", artifact.Signature);
                writer.WriteString("signingKeyId", artifact.SigningKeyId.ToString("D"));
                writer.WriteString("signingKeyFingerprint", artifact.SigningKeyFingerprint);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
