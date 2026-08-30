using System.Text.RegularExpressions;

namespace Asterloom.Modules.Release;

internal sealed partial class ReleaseVersion : IComparable<ReleaseVersion>
{
    private ReleaseVersion(
        string original,
        string major,
        string minor,
        string patch,
        IReadOnlyList<string> preRelease)
    {
        Original = original;
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public string Original { get; }

    private string Major { get; }

    private string Minor { get; }

    private string Patch { get; }

    private IReadOnlyList<string> PreRelease { get; }

    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = null!;
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 100)
        {
            return false;
        }

        var match = VersionPattern().Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        var preRelease = match.Groups["pre"].Success
            ? match.Groups["pre"].Value.Split('.')
            : [];
        if (preRelease.Any(identifier =>
                IsNumeric(identifier)
                && identifier.Length > 1
                && identifier[0] == '0'))
        {
            return false;
        }

        version = new(
            normalized,
            match.Groups["major"].Value,
            match.Groups["minor"].Value,
            match.Groups["patch"].Value,
            preRelease);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var comparison = CompareNumeric(Major, other.Major);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareNumeric(Minor, other.Minor);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareNumeric(Patch, other.Patch);
        if (comparison != 0)
        {
            return comparison;
        }

        if (PreRelease.Count == 0 || other.PreRelease.Count == 0)
        {
            return PreRelease.Count == other.PreRelease.Count
                ? 0
                : PreRelease.Count == 0 ? 1 : -1;
        }

        for (var index = 0; index < Math.Min(PreRelease.Count, other.PreRelease.Count); index++)
        {
            var left = PreRelease[index];
            var right = other.PreRelease[index];
            var leftNumeric = IsNumeric(left);
            var rightNumeric = IsNumeric(right);
            comparison = (leftNumeric, rightNumeric) switch
            {
                (true, true) => CompareNumeric(left, right),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.Compare(left, right, StringComparison.Ordinal),
            };
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    private static bool IsNumeric(string value) =>
        value.All(static character => character is >= '0' and <= '9');

    private static int CompareNumeric(string left, string right)
    {
        var normalizedLeft = left.TrimStart('0');
        var normalizedRight = right.TrimStart('0');
        normalizedLeft = normalizedLeft.Length == 0 ? "0" : normalizedLeft;
        normalizedRight = normalizedRight.Length == 0 ? "0" : normalizedRight;
        var length = normalizedLeft.Length.CompareTo(normalizedRight.Length);
        return length != 0
            ? length
            : string.Compare(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    [GeneratedRegex(
        "^(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
