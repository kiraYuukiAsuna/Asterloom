using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Asterloom.Sdk.Mail;

public sealed class AsterloomMailClient
{
    private readonly HttpClient _httpClient;
    private readonly AsterloomMailScope _scope;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public AsterloomMailClient(HttpClient httpClient, AsterloomMailScope scope)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(scope);
        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException("The HTTP client must have a base address.", nameof(httpClient));
        }
        if (scope.TenantId == Guid.Empty || scope.ApplicationId == Guid.Empty)
        {
            throw new ArgumentException("The mail scope requires tenant and application IDs.", nameof(scope));
        }

        _httpClient = httpClient;
        _scope = scope;
    }

    public async Task<AsterloomMailDelivery> SendAsync(
        AsterloomMailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildPath())
        {
            Content = JsonContent.Create(new
            {
                tenantId = _scope.TenantId.ToString("D"),
                applicationId = _scope.ApplicationId.ToString("D"),
                smtpAccountId = message.SmtpAccountId.ToString("D"),
                message.ClientMessageId,
                message.To,
                cc = message.Cc ?? [],
                bcc = message.Bcc ?? [],
                message.ReplyTo,
                message.Subject,
                message.TextBody,
                message.HtmlBody,
            }, options: _jsonOptions),
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<DeliveryDto>(
            _jsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The mail delivery response is empty.");
        var delivery = ToModel(dto);
        if (delivery.Status == AsterloomMailDeliveryStatus.Failed)
        {
            throw new AsterloomMailDeliveryException(delivery);
        }

        return delivery;
    }

    private string BuildPath() =>
        $"api/v1/tenants/{_scope.TenantId:D}/applications/{_scope.ApplicationId:D}/mail:send";

    private static AsterloomMailDelivery ToModel(DeliveryDto dto) => new(
        Guid.Parse(dto.Id),
        Guid.Parse(dto.SmtpAccountId),
        dto.ClientMessageId,
        dto.To,
        dto.Cc,
        dto.Bcc,
        dto.ReplyTo,
        dto.Subject,
        dto.Status switch
        {
            "MAIL_DELIVERY_STATUS_PENDING" => AsterloomMailDeliveryStatus.Pending,
            "MAIL_DELIVERY_STATUS_SENT" => AsterloomMailDeliveryStatus.Sent,
            "MAIL_DELIVERY_STATUS_FAILED" => AsterloomMailDeliveryStatus.Failed,
            _ => throw new JsonException($"Unknown mail delivery status '{dto.Status}'."),
        },
        dto.ProviderMessageId,
        dto.ErrorCode,
        dto.ErrorMessage,
        dto.CreatedAt,
        dto.CompletedAt);

    private sealed record DeliveryDto(
        string Id,
        string SmtpAccountId,
        string ClientMessageId,
        IReadOnlyList<string> To,
        IReadOnlyList<string> Cc,
        IReadOnlyList<string> Bcc,
        string ReplyTo,
        string Subject,
        string Status,
        string ProviderMessageId,
        string ErrorCode,
        string ErrorMessage,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt);
}
