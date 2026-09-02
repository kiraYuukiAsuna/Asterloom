using Asterloom.Protocol.Mail.Admin.V1;
using Grpc.Core;

namespace Asterloom.Modules.Mail;

internal sealed class MailAdminGrpcService(MailAccountManagementService managementService)
    : MailAdminService.MailAdminServiceBase
{
    public override async Task<ListSmtpAccountsResponse> ListSmtpAccounts(
        ListSmtpAccountsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListAccountsAsync(
            request.TenantId,
            request.ApplicationId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListSmtpAccountsResponse { NextPageToken = result.NextPageToken };
        response.Accounts.AddRange(result.Items.Select(MailProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<Asterloom.Protocol.Mail.V1.SmtpAccount> GetSmtpAccount(
        GetSmtpAccountRequest request,
        ServerCallContext context) =>
        (await managementService.GetAccountAsync(
            request.TenantId,
            request.ApplicationId,
            request.SmtpAccountId,
            context.CancellationToken)).ToProtocol();

    public override async Task<Asterloom.Protocol.Mail.V1.SmtpAccount> CreateSmtpAccount(
        CreateSmtpAccountRequest request,
        ServerCallContext context) =>
        (await managementService.CreateAccountAsync(
            request.TenantId,
            request.ApplicationId,
            request.Name,
            request.Host,
            request.Port,
            request.Security.ToModel(),
            request.Username,
            request.SmtpPassword,
            request.FromAddress,
            request.FromName,
            context.CancellationToken)).ToProtocol();

    public override async Task<Asterloom.Protocol.Mail.V1.SmtpAccount> UpdateSmtpAccount(
        UpdateSmtpAccountRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateAccountAsync(
            request.TenantId,
            request.ApplicationId,
            request.SmtpAccountId,
            request.Name,
            request.Host,
            request.Port,
            request.Security.ToModel(),
            request.Username,
            request.SmtpPassword,
            request.FromAddress,
            request.FromName,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<Asterloom.Protocol.Mail.V1.SmtpAccount> ArchiveSmtpAccount(
        ArchiveSmtpAccountRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveAccountAsync(
            request.TenantId,
            request.ApplicationId,
            request.SmtpAccountId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<Asterloom.Protocol.Mail.V1.SmtpAccount> RestoreSmtpAccount(
        RestoreSmtpAccountRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreAccountAsync(
            request.TenantId,
            request.ApplicationId,
            request.SmtpAccountId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<Asterloom.Protocol.Mail.V1.MailDelivery> TestSmtpAccount(
        TestSmtpAccountRequest request,
        ServerCallContext context) =>
        (await managementService.TestAccountAsync(
            request.TenantId,
            request.ApplicationId,
            request.SmtpAccountId,
            request.Recipient,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListMailDeliveriesResponse> ListMailDeliveries(
        ListMailDeliveriesRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListDeliveriesAsync(
            request.TenantId,
            request.ApplicationId,
            request.PageSize,
            request.PageToken,
            request.Status.ToOptionalModel(),
            context.CancellationToken);
        var response = new ListMailDeliveriesResponse { NextPageToken = result.NextPageToken };
        response.Deliveries.AddRange(result.Items.Select(MailProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<Asterloom.Protocol.Mail.V1.MailDelivery> GetMailDelivery(
        GetMailDeliveryRequest request,
        ServerCallContext context) =>
        (await managementService.GetDeliveryAsync(
            request.TenantId,
            request.ApplicationId,
            request.DeliveryId,
            context.CancellationToken)).ToProtocol();
}
