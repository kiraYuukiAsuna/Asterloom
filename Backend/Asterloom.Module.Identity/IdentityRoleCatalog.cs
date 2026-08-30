namespace Asterloom.Modules.Identity;

public static class IdentityRoleCatalog
{
    public const string SuperAdministrator = "SuperAdministrator";
    public const string TenantAdministrator = "TenantAdministrator";
    public const string Operator = "Operator";
    public const string Developer = "Developer";
    public const string Viewer = "Viewer";

    public static IReadOnlyList<string> All { get; } =
    [
        SuperAdministrator,
        TenantAdministrator,
        Operator,
        Developer,
        Viewer,
    ];

    public static bool IsKnown(string role) =>
        All.Contains(role, StringComparer.Ordinal);
}
