using System.Text.Json;

namespace Asterloom.ReferenceApp.Client;

internal sealed record ReferenceAppState(
    string RunId,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    Guid SegmentId,
    string FeatureFlagKey,
    string ConfigKey,
    Guid StorageBucketId,
    string ReleaseChannelKey,
    string ReleasePackageId,
    string ReleaseRuntimeId,
    string ReleaseBaselineVersion,
    string ReleaseTargetVersion,
    bool ReleaseHasDelta,
    string ReleaseSigningKeyFingerprint,
    string ReleasePublicKeyPem,
    string AnalyticsWriteKey,
    Guid TelemetrySourceId,
    DateTimeOffset ProvisionedAt)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task<ReferenceAppState> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Reference state is missing. Run the provision command first.",
                path);
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ReferenceAppState>(
            stream,
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidDataException("The reference state file is empty or invalid.");
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, this, JsonOptions, cancellationToken);
    }
}
