using Asterloom.Modules.Authorization.Model;
using Asterloom.Protocol.Authorization.Admin.V1;
using Grpc.Core;
using ProtocolRole = Asterloom.Protocol.Authorization.V1.Role;
using ProtocolRoleBinding = Asterloom.Protocol.Authorization.V1.RoleBinding;
using ProtocolPolicyRule = Asterloom.Protocol.Authorization.V1.PolicyRule;
using ProtocolDecision = Asterloom.Protocol.Authorization.V1.AuthorizationDecision;

namespace Asterloom.Modules.Authorization;

internal sealed class AuthorizationAdminGrpcService(
    AuthorizationManagementService managementService)
    : AuthorizationAdminService.AuthorizationAdminServiceBase
{
    public override Task<ListPermissionsResponse> ListPermissions(
        ListPermissionsRequest request,
        ServerCallContext context)
    {
        var result = AuthorizationManagementService.ListPermissions(
            request.PageSize,
            request.PageToken,
            request.Query);
        var response = new ListPermissionsResponse { NextPageToken = result.NextPageToken };
        response.Permissions.AddRange(result.Items.Select(AuthorizationProtocolMapper.ToProtocol));
        return Task.FromResult(response);
    }

    public override async Task<ListRolesResponse> ListRoles(
        ListRolesRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListRolesAsync(
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListRolesResponse { NextPageToken = result.NextPageToken };
        response.Roles.AddRange(result.Items.Select(AuthorizationProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolRole> CreateRole(
        CreateRoleRequest request,
        ServerCallContext context) =>
        (await managementService.CreateRoleAsync(
            request.Key,
            request.DisplayName,
            request.Description,
            request.Permissions,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRole> UpdateRole(
        UpdateRoleRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateRoleAsync(
            request.RoleId,
            request.DisplayName,
            request.Description,
            request.Permissions,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRole> ArchiveRole(
        ArchiveRoleRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveRoleAsync(
            request.RoleId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRole> RestoreRole(
        RestoreRoleRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreRoleAsync(
            request.RoleId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListRoleBindingsResponse> ListRoleBindings(
        ListRoleBindingsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListRoleBindingsAsync(
            request.PageSize,
            request.PageToken,
            request.ActorId,
            request.TenantId,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListRoleBindingsResponse { NextPageToken = result.NextPageToken };
        response.RoleBindings.AddRange(
            result.Items.Select(AuthorizationProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolRoleBinding> SetRoleBinding(
        SetRoleBindingRequest request,
        ServerCallContext context) =>
        (await managementService.SetRoleBindingAsync(
            request.BindingId,
            request.ActorId,
            request.RoleId,
            request.Scope.ToDomain(),
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRoleBinding> RemoveRoleBinding(
        RemoveRoleBindingRequest request,
        ServerCallContext context) =>
        (await managementService.RemoveRoleBindingAsync(
            request.BindingId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListPolicyRulesResponse> ListPolicyRules(
        ListPolicyRulesRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListPolicyRulesAsync(
            request.PageSize,
            request.PageToken,
            request.Query,
            request.TenantId,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListPolicyRulesResponse { NextPageToken = result.NextPageToken };
        response.PolicyRules.AddRange(
            result.Items.Select(AuthorizationProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolPolicyRule> CreatePolicyRule(
        CreatePolicyRuleRequest request,
        ServerCallContext context) =>
        (await managementService.CreatePolicyRuleAsync(
            request.Name,
            request.Effect.ToDomain(),
            request.SubjectType.ToDomain(),
            request.Subject,
            request.Scope.ToDomain(),
            request.Permission,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolPolicyRule> UpdatePolicyRule(
        UpdatePolicyRuleRequest request,
        ServerCallContext context) =>
        (await managementService.UpdatePolicyRuleAsync(
            request.PolicyRuleId,
            request.Name,
            request.Effect.ToDomain(),
            request.SubjectType.ToDomain(),
            request.Subject,
            request.Scope.ToDomain(),
            request.Permission,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolPolicyRule> ArchivePolicyRule(
        ArchivePolicyRuleRequest request,
        ServerCallContext context) =>
        (await managementService.ArchivePolicyRuleAsync(
            request.PolicyRuleId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolPolicyRule> RestorePolicyRule(
        RestorePolicyRuleRequest request,
        ServerCallContext context) =>
        (await managementService.RestorePolicyRuleAsync(
            request.PolicyRuleId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListPolicyRevisionsResponse> ListPolicyRevisions(
        ListPolicyRevisionsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListPolicyRevisionsAsync(
            request.PageSize,
            request.PageToken,
            request.ResourceType,
            request.ResourceId,
            context.CancellationToken);
        var response = new ListPolicyRevisionsResponse { NextPageToken = result.NextPageToken };
        response.Revisions.AddRange(
            result.Items.Select(AuthorizationProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolDecision> SimulateAuthorization(
        SimulateAuthorizationRequest request,
        ServerCallContext context)
    {
        if (request.Input is null)
        {
            throw MissingInput();
        }

        return (await managementService.SimulateAsync(
            request.Input.ToDomain(),
            context.CancellationToken)).ToProtocol();
    }

    private static Asterloom.Modules.Errors.AsterloomException MissingInput() =>
        new(
            Asterloom.Modules.Errors.AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["input"] = ["A decision input is required."],
            });
}
