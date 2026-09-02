namespace Asterloom.ReferenceApp.Backend;

internal sealed record ReferenceMailOptions(
    bool Enabled,
    Guid SmtpAccountId,
    Guid TenantId,
    Guid ApplicationId)
{
    public static ReferenceMailOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Asterloom:Mail");
        var enabled = section.GetValue<bool>("Enabled");
        if (!enabled)
        {
            return new(false, Guid.Empty, Guid.Empty, Guid.Empty);
        }

        return new(
            true,
            ReadId(section["SmtpAccountId"], "Asterloom:Mail:SmtpAccountId"),
            ReadId(
                section["TenantId"] ?? configuration["Asterloom:TenantId"],
                "Asterloom:Mail:TenantId"),
            ReadId(
                section["ApplicationId"] ?? configuration["Asterloom:ApplicationId"],
                "Asterloom:Mail:ApplicationId"));
    }

    private static Guid ReadId(string? value, string name) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new InvalidOperationException($"{name} must be a non-empty UUID when mail is enabled.");
}
