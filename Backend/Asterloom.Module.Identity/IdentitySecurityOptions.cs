using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Asterloom.Modules.Identity;

public sealed record IdentitySecurityOptions(
    Uri Issuer,
    bool IsDevelopment,
    string DataProtectionKeysPath,
    string? SigningCertificatePath,
    string? SigningCertificatePassword,
    string? EncryptionCertificatePath,
    string? EncryptionCertificatePassword)
{
    public static IdentitySecurityOptions FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var isDevelopment = environment.IsDevelopment()
            || environment.IsEnvironment("Testing");
        var issuerValue = configuration["Identity:Issuer"];
        if (string.IsNullOrWhiteSpace(issuerValue))
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    "Production Identity requires a stable Identity:Issuer URI.");
            }

            issuerValue = "http://localhost:5080/";
        }

        if (!Uri.TryCreate(issuerValue, UriKind.Absolute, out var issuer)
            || (issuer.Scheme != Uri.UriSchemeHttps
                && (!isDevelopment || issuer.Scheme != Uri.UriSchemeHttp))
            || !string.IsNullOrEmpty(issuer.Query)
            || !string.IsNullOrEmpty(issuer.Fragment))
        {
            throw new InvalidOperationException(
                "Identity:Issuer must be an absolute HTTPS URI without a query or fragment. " +
                "HTTP is accepted only in Development or Testing.");
        }

        var keyRingPath = configuration["Identity:DataProtectionKeysPath"];
        if (string.IsNullOrWhiteSpace(keyRingPath))
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    "Production Identity requires Identity:DataProtectionKeysPath.");
            }

            keyRingPath = Path.Combine(
                AppContext.BaseDirectory,
                ".data",
                "dataprotection-keys");
        }

        var signingPath = configuration["Identity:Certificates:Signing:Path"];
        var encryptionPath = configuration["Identity:Certificates:Encryption:Path"];
        if (!isDevelopment)
        {
            ValidateCertificatePath(signingPath, "Identity:Certificates:Signing:Path");
            ValidateCertificatePath(encryptionPath, "Identity:Certificates:Encryption:Path");
        }

        return new IdentitySecurityOptions(
            EnsureTrailingSlash(issuer),
            isDevelopment,
            Path.GetFullPath(keyRingPath),
            ResolvePath(signingPath),
            configuration["Identity:Certificates:Signing:Password"],
            ResolvePath(encryptionPath),
            configuration["Identity:Certificates:Encryption:Password"]);
    }

    public X509Certificate2 LoadSigningCertificate() =>
        LoadCertificate(SigningCertificatePath!, SigningCertificatePassword);

    public X509Certificate2 LoadEncryptionCertificate() =>
        LoadCertificate(EncryptionCertificatePath!, EncryptionCertificatePassword);

    private static X509Certificate2 LoadCertificate(string path, string? password) =>
        X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.EphemeralKeySet);

    private static void ValidateCertificatePath(string? path, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(Path.GetFullPath(path)))
        {
            throw new InvalidOperationException(
                $"Production Identity requires an existing PKCS#12 certificate at {configurationKey}.");
        }
    }

    private static string? ResolvePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static Uri EnsureTrailingSlash(Uri issuer)
    {
        var value = issuer.AbsoluteUri;
        return value.EndsWith('/')
            ? issuer
            : new Uri(value + '/', UriKind.Absolute);
    }
}
