namespace Asterloom.ReferenceApp.Client;

internal sealed record ReferenceDesktopReleaseSettings(
    string PackageId,
    string RuntimeId,
    string BaselineVersion,
    string TargetVersion,
    string? BaselineFullPackage,
    string? TargetFullPackage,
    string? TargetDeltaPackage)
{
    public bool UsesVelopackPackages => BaselineFullPackage is not null;

    public static ReferenceDesktopReleaseSettings Load()
    {
        var baseline = ReadOptionalPath("ASTERLOOM_REFERENCE_RELEASE_BASE_FULL");
        var target = ReadOptionalPath("ASTERLOOM_REFERENCE_RELEASE_TARGET_FULL");
        var delta = ReadOptionalPath("ASTERLOOM_REFERENCE_RELEASE_TARGET_DELTA");
        var suppliedCount = new[] { baseline, target, delta }.Count(static path => path is not null);
        if (suppliedCount is not 0 and not 3)
        {
            throw new InvalidOperationException(
                "ASTERLOOM_REFERENCE_RELEASE_BASE_FULL, _TARGET_FULL, and _TARGET_DELTA "
                + "must either all be set or all be omitted.");
        }

        return new ReferenceDesktopReleaseSettings(
            ReadValue("ASTERLOOM_REFERENCE_RELEASE_PACKAGE_ID", "Asterloom.ReferenceApp"),
            ReadValue(
                "ASTERLOOM_REFERENCE_RELEASE_RUNTIME_ID",
                ReferenceAppProvisioner.GetRuntimeIdentifier()),
            ReadValue("ASTERLOOM_REFERENCE_RELEASE_BASE_VERSION", baseline is null ? "0.0.0" : "1.0.0"),
            ReadValue("ASTERLOOM_REFERENCE_RELEASE_TARGET_VERSION", baseline is null ? "1.0.0" : "1.1.0"),
            baseline,
            target,
            delta);
    }

    private static string ReadValue(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name)?.Trim() is { Length: > 0 } value
            ? value
            : fallback;

    private static string? ReadOptionalPath(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var path = Path.GetFullPath(value);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"{name} does not identify an existing package.", path);
    }
}
