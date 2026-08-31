using System.Net.Mail;
using Asterloom.Protocol.Identity.Access.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProtocolMembership = Asterloom.Protocol.Identity.V1.ApplicationMembership;
using ProtocolMembershipStatus = Asterloom.Protocol.Identity.V1.ApplicationMembershipStatus;
using ProtocolUser = Asterloom.Protocol.Identity.V1.IdentityUser;
using ProtocolUserStatus = Asterloom.Protocol.Identity.V1.IdentityUserStatus;

namespace Asterloom.Sdk.Identity;

/// <summary>
/// Headless account operations intended for a trusted business backend. The
/// authenticated confidential client determines the application membership.
/// </summary>
public sealed class AsterloomIdentityAccessClient
{
    private readonly IdentityAccessService.IdentityAccessServiceClient _client;

    public AsterloomIdentityAccessClient(CallInvoker callInvoker)
        : this(new IdentityAccessService.IdentityAccessServiceClient(
            callInvoker ?? throw new ArgumentNullException(nameof(callInvoker))))
    {
    }

    public AsterloomIdentityAccessClient(
        IdentityAccessService.IdentityAccessServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<AsterloomAccountRegistrationResult> RegisterAccountAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.RegisterAccountAsync(
            new RegisterAccountRequest
            {
                Email = ValidateEmail(email),
                DisplayName = RequireText(displayName, nameof(displayName), 200),
                Password = RequireSecret(password, nameof(password), 2_048),
            },
            cancellationToken: cancellationToken);
        return new AsterloomAccountRegistrationResult(
            ToModel(response.User ?? throw InvalidProtocol("registration user")),
            ToModel(response.Membership ?? throw InvalidProtocol("registration membership")),
            response.AccountCreated,
            response.VerificationRequired,
            string.IsNullOrEmpty(response.EmailVerificationToken)
                ? null
                : response.EmailVerificationToken);
    }

    public async Task<AsterloomIdentityUser> ConfirmEmailAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default) =>
        ToModel(await _client.ConfirmEmailAsync(
            new ConfirmEmailRequest
            {
                Email = ValidateEmail(email),
                Token = RequireSecret(token, nameof(token), 8_192),
            },
            cancellationToken: cancellationToken));

    public async Task<AsterloomApplicationAccount> GetAccountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAccountAsync(
            new GetAccountRequest { UserId = FormatId(userId, nameof(userId)) },
            cancellationToken: cancellationToken);
        return new AsterloomApplicationAccount(
            ToModel(response.User ?? throw InvalidProtocol("application account user")),
            ToModel(response.Membership
                ?? throw InvalidProtocol("application account membership")));
    }

    public async Task<AsterloomApplicationMembership> RemoveMembershipAsync(
        AsterloomApplicationMembership membership,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);
        return ToModel(await _client.RemoveMembershipAsync(
            new RemoveMembershipRequest
            {
                UserId = FormatId(membership.UserId, nameof(membership)),
                ExpectedVersion = membership.Version > 0
                    ? membership.Version
                    : throw new ArgumentOutOfRangeException(nameof(membership)),
            },
            cancellationToken: cancellationToken));
    }

    private static AsterloomIdentityUser ToModel(ProtocolUser user) => new(
        ParseGuid(user.Id, "user.id"),
        user.Email,
        user.DisplayName,
        user.Status switch
        {
            ProtocolUserStatus.Pending => AsterloomIdentityUserStatus.Pending,
            ProtocolUserStatus.Active => AsterloomIdentityUserStatus.Active,
            ProtocolUserStatus.Suspended => AsterloomIdentityUserStatus.Suspended,
            ProtocolUserStatus.Archived => AsterloomIdentityUserStatus.Archived,
            _ => throw InvalidProtocol("identity user status"),
        },
        user.Version,
        user.Roles.Select(ToRole).ToArray(),
        ToDateTimeOffset(user.CreatedAt, "user.created_at"),
        ToDateTimeOffset(user.UpdatedAt, "user.updated_at"),
        user.ArchivedAt is null ? null : user.ArchivedAt.ToDateTimeOffset(),
        user.EmailConfirmed);

    private static AsterloomApplicationMembership ToModel(
        ProtocolMembership membership) => new(
        ParseGuid(membership.UserId, "membership.user_id"),
        ParseGuid(membership.TenantId, "membership.tenant_id"),
        ParseGuid(membership.ApplicationId, "membership.application_id"),
        membership.Status switch
        {
            ProtocolMembershipStatus.Active => AsterloomApplicationMembershipStatus.Active,
            ProtocolMembershipStatus.Removed => AsterloomApplicationMembershipStatus.Removed,
            _ => throw InvalidProtocol("application membership status"),
        },
        membership.Version,
        ToDateTimeOffset(membership.CreatedAt, "membership.created_at"),
        ToDateTimeOffset(membership.UpdatedAt, "membership.updated_at"));

    private static AsterloomPassportRole ToRole(string role) => role switch
    {
        "SuperAdministrator" => AsterloomPassportRole.SuperAdministrator,
        "TenantAdministrator" => AsterloomPassportRole.TenantAdministrator,
        "Operator" => AsterloomPassportRole.Operator,
        "Developer" => AsterloomPassportRole.Developer,
        "Viewer" => AsterloomPassportRole.Viewer,
        _ => throw InvalidProtocol($"Passport role '{role}'"),
    };

    private static string ValidateEmail(string value)
    {
        var email = RequireText(value, nameof(value), 320).ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out var parsed)
            || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A valid email address is required.", nameof(value));
        }

        return email;
    }

    private static string RequireText(string? value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A non-empty value of at most {maximumLength} characters is required.",
                parameterName);
        }

        return normalized;
    }

    private static string RequireSecret(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A non-empty value of at most {maximumLength} characters is required.",
                parameterName);
        }

        return value;
    }

    private static string FormatId(Guid value, string parameterName) =>
        value != Guid.Empty
            ? value.ToString("D")
            : throw new ArgumentException("The identifier cannot be empty.", parameterName);

    private static Guid ParseGuid(string value, string field) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw InvalidProtocol(field);

    private static DateTimeOffset ToDateTimeOffset(Timestamp? value, string field) =>
        value?.ToDateTimeOffset() ?? throw InvalidProtocol(field);

    private static InvalidDataException InvalidProtocol(string field) =>
        new($"The Identity service returned an invalid {field} value.");
}
