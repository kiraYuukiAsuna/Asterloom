using System.Net.Mail;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Identity.Model;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Asterloom.Modules.Identity.Management;

internal sealed class IdentityAccountService(
    UserManager<AsterloomUser> userManager,
    IOpenIddictApplicationManager applicationManager,
    IdentityMembershipService memberships,
    TimeProvider timeProvider)
{
    public async Task<ManagedAccountRegistration> RegisterAsync(
        string clientId,
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        var binding = await RequireBindingAsync(clientId, cancellationToken);
        if (!binding.AllowUserRegistration)
        {
            throw new AsterloomException(
                AsterloomErrorKind.PermissionDenied,
                "identity_registration_disabled",
                "This application is not allowed to register accounts.");
        }

        var normalizedEmail = NormalizeEmail(email);
        var normalizedDisplayName = RequireText(displayName, "displayName", 200);
        var normalizedPassword = RequireSecret(password, "password", 2_048);
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        var accountCreated = false;
        if (user is null)
        {
            var now = timeProvider.GetUtcNow();
            user = new AsterloomUser
            {
                Id = Guid.CreateVersion7(),
                UserName = normalizedEmail,
                Email = normalizedEmail,
                EmailConfirmed = false,
                DisplayName = normalizedDisplayName,
                Status = AsterloomUserStatus.Pending,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            EnsureSucceeded(
                await userManager.CreateAsync(user, normalizedPassword),
                "identity_account_create_failed",
                "The account could not be created.");
            accountCreated = true;
        }
        else
        {
            if (user.Status is AsterloomUserStatus.Suspended or AsterloomUserStatus.Archived
                || !await userManager.CheckPasswordAsync(user, normalizedPassword))
            {
                throw new AsterloomException(
                    AsterloomErrorKind.Unauthenticated,
                    "identity_registration_credentials_invalid",
                    "The account cannot be registered with these credentials.");
            }
        }

        try
        {
            var existing = await memberships.FindAsync(
                user.Id,
                binding.ApplicationId,
                cancellationToken);
            var membership = await memberships.SetAsync(
                user.Id,
                binding.TenantId,
                binding.ApplicationId,
                existing?.Version ?? 0,
                cancellationToken);
            var verificationRequired = !user.EmailConfirmed;
            var token = verificationRequired
                ? await userManager.GenerateEmailConfirmationTokenAsync(user)
                : string.Empty;
            return new ManagedAccountRegistration(
                await ToManagedUserAsync(user),
                membership,
                accountCreated,
                verificationRequired,
                token);
        }
        catch
        {
            if (accountCreated)
            {
                await userManager.DeleteAsync(user);
            }

            throw;
        }
    }

    public async Task<ManagedIdentityUser> ConfirmEmailAsync(
        string clientId,
        string email,
        string token,
        CancellationToken cancellationToken)
    {
        var binding = await RequireBindingAsync(clientId, cancellationToken);
        var user = await userManager.FindByEmailAsync(NormalizeEmail(email))
            ?? throw InvalidConfirmation();
        await memberships.RequireActiveAsync(
            user.Id,
            binding.ApplicationId,
            cancellationToken);
        if (!user.EmailConfirmed)
        {
            var result = await userManager.ConfirmEmailAsync(
                user,
                RequireText(token, "token", 8_192));
            if (!result.Succeeded)
            {
                throw InvalidConfirmation();
            }
        }

        if (user.Status == AsterloomUserStatus.Pending)
        {
            user.Status = AsterloomUserStatus.Active;
            user.Version++;
            user.UpdatedAt = timeProvider.GetUtcNow();
            EnsureSucceeded(
                await userManager.UpdateAsync(user),
                "identity_account_confirmation_failed",
                "The account confirmation could not be completed.");
        }

        return await ToManagedUserAsync(user);
    }

    public async Task<ManagedApplicationAccount> GetAsync(
        string clientId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var binding = await RequireBindingAsync(clientId, cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString("D"))
            ?? throw new AsterloomException(
                AsterloomErrorKind.NotFound,
                "identity_user_not_found",
                "The account was not found.");
        var membership = await memberships.RequireActiveAsync(
            user.Id,
            binding.ApplicationId,
            cancellationToken);
        return new ManagedApplicationAccount(await ToManagedUserAsync(user), membership);
    }

    public async Task<ManagedApplicationMembership> RemoveMembershipAsync(
        string clientId,
        Guid userId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var binding = await RequireBindingAsync(clientId, cancellationToken);
        return await memberships.RemoveAsync(
            userId,
            binding.ApplicationId,
            expectedVersion,
            cancellationToken);
    }

    private async Task<IdentityClientApplicationBinding> RequireBindingAsync(
        string clientId,
        CancellationToken cancellationToken) =>
        await IdentityClientApplicationMetadata.FindAsync(
            applicationManager,
            RequireText(clientId, "clientId", 100),
            cancellationToken)
        ?? throw new AsterloomException(
            AsterloomErrorKind.PermissionDenied,
            "identity_application_binding_required",
            "The calling OIDC client is not bound to a platform application.");

    private async Task<ManagedIdentityUser> ToManagedUserAsync(AsterloomUser user) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.Status,
            user.Version,
            [.. (await userManager.GetRolesAsync(user)).Order(StringComparer.Ordinal)],
            user.CreatedAt,
            user.UpdatedAt,
            user.ArchivedAt,
            user.EmailConfirmed);

    private static string NormalizeEmail(string email)
    {
        var normalized = RequireText(email, "email", 320).ToLowerInvariant();
        if (!MailAddress.TryCreate(normalized, out var parsed)
            || !string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("email", "A valid email address is required.");
        }

        return normalized;
    }

    private static string RequireText(string? value, string field, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maximumLength)
        {
            throw Invalid(
                field,
                $"A non-empty value of at most {maximumLength} characters is required.");
        }

        return normalized;
    }

    private static string RequireSecret(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximumLength)
        {
            throw Invalid(
                field,
                $"A non-empty value of at most {maximumLength} characters is required.");
        }

        return value;
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        string errorCode,
        string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new AsterloomException(
            AsterloomErrorKind.InvalidArgument,
            errorCode,
            message,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["account"] = result.Errors.Select(error => error.Description).ToArray(),
            });
    }

    private static AsterloomException InvalidConfirmation() => new(
        AsterloomErrorKind.InvalidArgument,
        "identity_confirmation_invalid",
        "The email confirmation token is invalid or expired.");

    private static AsterloomException Invalid(string field, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "validation_failed",
        "One or more fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        });
}
