using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Mail.Model;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Mail;

internal static partial class MailValidation
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;
    public const int MaximumBodyLength = 1_048_576;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientMessageIdPattern();

    public static MailScope ParseScope(string tenantId, string applicationId) => new(
        ParseId(tenantId, "tenantId"),
        ParseId(applicationId, "applicationId"));

    public static Guid ParseId(string value, string fieldName)
    {
        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
        {
            throw Invalid(fieldName, $"{fieldName} must be a non-empty UUID.");
        }

        return parsed;
    }

    public static string RequireText(string? value, string fieldName, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maximumLength)
        {
            throw Invalid(
                fieldName,
                $"{fieldName} must contain between 1 and {maximumLength} characters.");
        }

        return normalized;
    }

    public static string NormalizeOptionalText(
        string? value,
        string fieldName,
        int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maximumLength)
        {
            throw Invalid(fieldName, $"{fieldName} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    public static string NormalizeHost(string? value)
    {
        var host = RequireText(value, "host", 255).ToLowerInvariant();
        if (host.Contains('/', StringComparison.Ordinal)
            || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            throw Invalid("host", "host must be a DNS name or IP address without a URL scheme.");
        }

        return host;
    }

    public static int ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw Invalid("port", "port must be between 1 and 65535.");
        }

        return port;
    }

    public static SmtpSecurityMode ValidateSecurity(SmtpSecurityMode security)
    {
        if (security is not SmtpSecurityMode.StartTls and not SmtpSecurityMode.SslOnConnect)
        {
            throw Invalid(
                "security",
                "security must require STARTTLS or SSL/TLS on connect.");
        }

        return security;
    }

    public static string NormalizeEmail(string? value, string fieldName)
    {
        var candidate = RequireText(value, fieldName, 320);
        try
        {
            var address = new MailAddress(candidate);
            if (!string.Equals(address.Address, candidate, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid(fieldName, $"{fieldName} must contain only an email address.");
            }

            return address.Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw Invalid(fieldName, $"{fieldName} must be a valid email address.");
        }
    }

    public static IReadOnlyList<string> NormalizeRecipients(
        IEnumerable<string> values,
        string fieldName)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values
            .Select(value => NormalizeEmail(value, fieldName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizeClientMessageId(string? value)
    {
        var normalized = RequireText(value, "clientMessageId", 128);
        if (!ClientMessageIdPattern().IsMatch(normalized))
        {
            throw Invalid(
                "clientMessageId",
                "clientMessageId may contain letters, digits, dot, underscore, colon, and hyphen.");
        }

        return normalized;
    }

    public static string NormalizeSubject(string? value)
    {
        var subject = RequireText(value, "subject", 200);
        if (subject.Contains('\r', StringComparison.Ordinal)
            || subject.Contains('\n', StringComparison.Ordinal))
        {
            throw Invalid("subject", "subject cannot contain line breaks.");
        }

        return subject;
    }

    public static string NormalizeBody(string? value, string fieldName)
    {
        var body = value ?? string.Empty;
        if (body.Length > MaximumBodyLength)
        {
            throw Invalid(
                fieldName,
                $"{fieldName} cannot exceed {MaximumBodyLength} characters.");
        }

        return body;
    }

    public static MailPageRequest CreatePageRequest(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeInactive)
    {
        var normalizedPageSize = pageSize == 0 ? DefaultPageSize : pageSize;
        if (normalizedPageSize is < 1 or > MaximumPageSize)
        {
            throw Invalid(
                "pageSize",
                $"pageSize must be between 1 and {MaximumPageSize}.");
        }

        var offset = 0;
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            try
            {
                var bytes = WebEncoders.Base64UrlDecode(pageToken);
                if (bytes.Length != sizeof(int))
                {
                    throw new FormatException();
                }

                offset = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bytes));
                if (offset < 0)
                {
                    throw new FormatException();
                }
            }
            catch (FormatException)
            {
                throw Invalid("pageToken", "pageToken is invalid.");
            }
        }

        return new(
            offset,
            normalizedPageSize,
            NormalizeOptionalText(query, "query", 200),
            includeInactive);
    }

    public static string NextPageToken(MailPageRequest request, int itemCount)
    {
        var bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(request.Offset + itemCount));
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public static AsterloomException Invalid(string fieldName, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "mail_invalid_argument",
        message,
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [fieldName] = [message],
        });
}
