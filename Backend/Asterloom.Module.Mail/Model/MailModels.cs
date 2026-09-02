namespace Asterloom.Modules.Mail.Model;

public readonly record struct MailScope(Guid TenantId, Guid ApplicationId);

public enum SmtpSecurityMode
{
    StartTls = 1,
    SslOnConnect = 2,
}

public enum MailAccountStatus
{
    Active = 1,
    Archived = 2,
}

public enum MailDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
}

public sealed record SmtpAccount(
    Guid Id,
    MailScope Scope,
    string Name,
    string Host,
    int Port,
    SmtpSecurityMode Security,
    string Username,
    string CredentialCiphertext,
    string FromAddress,
    string FromName,
    MailAccountStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record MailDelivery(
    Guid Id,
    MailScope Scope,
    Guid SmtpAccountId,
    string ClientMessageId,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string ReplyTo,
    string Subject,
    MailDeliveryStatus Status,
    string ProviderMessageId,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record MailPageRequest(
    int Offset,
    int PageSize,
    string Query,
    bool IncludeInactive);

public sealed record MailPage<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed record MailListResult<T>(IReadOnlyList<T> Items, string NextPageToken);

public sealed record MailMessageDraft(
    MailScope Scope,
    Guid SmtpAccountId,
    string ClientMessageId,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string ReplyTo,
    string Subject,
    string TextBody,
    string HtmlBody);
