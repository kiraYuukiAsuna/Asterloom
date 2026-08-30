using System.Security.Cryptography;
using System.Text;

namespace Asterloom.Modules.Release;

internal static class ReleaseCryptography
{
    public const string Algorithm = "RSA-PSS-SHA256";

    public static (string PublicKeyPem, string Fingerprint) NormalizePublicKey(
        string publicKeyPem)
    {
        var value = publicKeyPem?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 16_384
            || value.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException("A public RSA key in PEM format is required.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(value);
        if (rsa.KeySize < 2_048)
        {
            throw new CryptographicException("Release signing keys must be at least 2048 bits.");
        }

        var subjectPublicKeyInfo = rsa.ExportSubjectPublicKeyInfo();
        return (
            PemEncoding.WriteString("PUBLIC KEY", subjectPublicKeyInfo),
            Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo)));
    }

    public static bool VerifyDigestSignature(
        string publicKeyPem,
        string sha256,
        string signature)
    {
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signature?.Trim() ?? string.Empty);
        }
        catch (FormatException)
        {
            return false;
        }

        if (signatureBytes.Length is < 256 or > 1_024)
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(sha256),
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
