using Asterloom.Sdk.Mail;

namespace Asterloom.ReferenceApp.Backend;

internal sealed class ReferenceMailGateway
{
    private readonly AsterloomMailClient _client;
    private readonly ReferenceMailOptions _options;

    public ReferenceMailGateway(HttpClient httpClient, ReferenceMailOptions options)
    {
        _options = options;
        _client = new AsterloomMailClient(
            httpClient,
            new AsterloomMailScope(options.TenantId, options.ApplicationId));
    }

    public Task<AsterloomMailDelivery> SendAsync(
        string recipient,
        string subject,
        string textBody,
        string htmlBody,
        string clientMessageId,
        CancellationToken cancellationToken) => _client.SendAsync(
            new AsterloomMailMessage(
                _options.SmtpAccountId,
                clientMessageId,
                [recipient],
                subject,
                textBody,
                htmlBody),
            cancellationToken);
}
