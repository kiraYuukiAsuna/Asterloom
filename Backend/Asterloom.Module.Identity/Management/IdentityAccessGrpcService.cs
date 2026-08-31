using System.Security.Claims;
using Asterloom.Modules.Errors;
using Asterloom.Protocol.Identity.Access.V1;
using Grpc.Core;
using ProtocolMembership = Asterloom.Protocol.Identity.V1.ApplicationMembership;
using ProtocolUser = Asterloom.Protocol.Identity.V1.IdentityUser;

namespace Asterloom.Modules.Identity.Management;

internal sealed class IdentityAccessGrpcService(IdentityAccountService accounts)
    : IdentityAccessService.IdentityAccessServiceBase
{
    public override async Task<RegisterAccountResponse> RegisterAccount(
        RegisterAccountRequest request,
        ServerCallContext context)
    {
        var result = await accounts.RegisterAsync(
            RequireClientId(context),
            request.Email,
            request.DisplayName,
            request.Password,
            context.CancellationToken);
        return new RegisterAccountResponse
        {
            User = result.User.ToProtocol(),
            Membership = result.Membership.ToProtocol(),
            AccountCreated = result.AccountCreated,
            VerificationRequired = result.VerificationRequired,
            EmailVerificationToken = result.EmailVerificationToken,
        };
    }

    public override async Task<ProtocolUser> ConfirmEmail(
        ConfirmEmailRequest request,
        ServerCallContext context) =>
        (await accounts.ConfirmEmailAsync(
            RequireClientId(context),
            request.Email,
            request.Token,
            context.CancellationToken)).ToProtocol();

    public override async Task<ApplicationAccount> GetAccount(
        GetAccountRequest request,
        ServerCallContext context)
    {
        var result = await accounts.GetAsync(
            RequireClientId(context),
            ParseUserId(request.UserId),
            context.CancellationToken);
        return new ApplicationAccount
        {
            User = result.User.ToProtocol(),
            Membership = result.Membership.ToProtocol(),
        };
    }

    public override async Task<ProtocolMembership> RemoveMembership(
        RemoveMembershipRequest request,
        ServerCallContext context) =>
        (await accounts.RemoveMembershipAsync(
            RequireClientId(context),
            ParseUserId(request.UserId),
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    private static string RequireClientId(ServerCallContext context)
    {
        var principal = context.GetHttpContext().User;
        if (!string.Equals(
            principal.FindFirstValue(IdentityClaimTypes.ActorType),
            IdentityClaimTypes.ClientActor,
            StringComparison.Ordinal))
        {
            throw new AsterloomException(
                AsterloomErrorKind.PermissionDenied,
                "identity_trusted_backend_required",
                "This operation requires a trusted business backend identity.");
        }

        return principal.FindFirstValue("sub")
        ?? throw new AsterloomException(
            AsterloomErrorKind.Unauthenticated,
            "identity_client_missing",
            "The caller has no stable client identity.");
    }

    private static Guid ParseUserId(string value) =>
        Guid.TryParse(value, out var userId) && userId != Guid.Empty
            ? userId
            : throw new AsterloomException(
                AsterloomErrorKind.InvalidArgument,
                "validation_failed",
                "A valid user identifier is required.");
}
