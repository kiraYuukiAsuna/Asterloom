using Asterloom.Modules.Mail.Model;
using Asterloom.Protocol.Mail.V1;
using Grpc.Core;
using ProtocolMailDelivery = Asterloom.Protocol.Mail.V1.MailDelivery;

namespace Asterloom.Modules.Mail;

internal sealed class MailGrpcService(MailDeliveryService deliveryService)
    : MailService.MailServiceBase
{
    public override async Task<ProtocolMailDelivery> SendEmail(
        SendEmailRequest request,
        ServerCallContext context)
    {
        var scope = MailValidation.ParseScope(request.TenantId, request.ApplicationId);
        return (await deliveryService.SendAsync(
            new MailMessageDraft(
                scope,
                MailValidation.ParseId(request.SmtpAccountId, "smtpAccountId"),
                request.ClientMessageId,
                request.To,
                request.Cc,
                request.Bcc,
                request.ReplyTo,
                request.Subject,
                request.TextBody,
                request.HtmlBody),
            context.CancellationToken)).ToProtocol();
    }
}
