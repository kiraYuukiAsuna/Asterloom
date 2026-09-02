namespace Asterloom.Sdk.Mail;

public sealed record AsterloomMailScope(Guid TenantId, Guid ApplicationId);

public sealed record AsterloomMailMessage(
    Guid SmtpAccountId,
    string ClientMessageId,
    IReadOnlyList<string> To,
    string Subject,
    string TextBody = "",
    string HtmlBody = "",
    IReadOnlyList<string>? Cc = null,
    IReadOnlyList<string>? Bcc = null,
    string ReplyTo = "");

public enum AsterloomMailDeliveryStatus
{
    Pending,
    Sent,
    Failed,
}

public sealed record AsterloomMailDelivery(
    Guid Id,
    Guid SmtpAccountId,
    string ClientMessageId,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string ReplyTo,
    string Subject,
    AsterloomMailDeliveryStatus Status,
    string ProviderMessageId,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed class AsterloomMailDeliveryException(AsterloomMailDelivery delivery)
    : Exception(
        string.IsNullOrWhiteSpace(delivery.ErrorMessage)
            ? "Asterloom could not deliver the email."
            : delivery.ErrorMessage)
{
    public AsterloomMailDelivery Delivery { get; } = delivery;
}
