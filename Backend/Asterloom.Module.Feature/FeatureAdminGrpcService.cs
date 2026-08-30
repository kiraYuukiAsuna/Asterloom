using Asterloom.Protocol.Feature.Admin.V1;
using Asterloom.Modules.Targeting;
using Grpc.Core;
using ProtocolFlag = Asterloom.Protocol.Feature.V1.FeatureFlag;
using ProtocolEvaluation = Asterloom.Protocol.Feature.V1.FeatureEvaluationDetails;
using ProtocolValidation = Asterloom.Protocol.Feature.V1.FeatureValidationResult;

namespace Asterloom.Modules.Feature;

public sealed class FeatureAdminGrpcService(FeatureManagementService managementService)
    : FeatureAdminService.FeatureAdminServiceBase
{
    public override async Task<ListFlagsResponse> ListFlags(
        ListFlagsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListFlagsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListFlagsResponse { NextPageToken = result.NextPageToken };
        response.Flags.AddRange(result.Items.Select(FeatureProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolFlag> GetFlag(
        GetFlagRequest request,
        ServerCallContext context) =>
        (await managementService.GetFlagAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.FlagId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolFlag> CreateFlag(
        CreateFlagRequest request,
        ServerCallContext context) =>
        (await managementService.CreateFlagAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.Key,
            request.DisplayName,
            request.Description,
            request.ValueKind.ToDomain(),
            request.Definition.ToDomain(),
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolFlag> UpdateFlagDraft(
        UpdateFlagDraftRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateDraftAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.FlagId,
            request.DisplayName,
            request.Description,
            request.Definition.ToDomain(),
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolValidation> ValidateFlagDraft(
        ValidateFlagDraftRequest request,
        ServerCallContext context) =>
        (await managementService.ValidateDraftAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.FlagId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolFlag> PublishFlag(
        PublishFlagRequest request,
        ServerCallContext context) =>
        (await managementService.PublishAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.FlagId,
            request.ExpectedVersion,
            context.GetHttpContext().TraceIdentifier,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListFlagRevisionsResponse> ListFlagRevisions(
        ListFlagRevisionsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListRevisionsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.FlagId,
            request.PageSize,
            request.PageToken,
            context.CancellationToken);
        var response = new ListFlagRevisionsResponse
        {
            NextPageToken = result.NextPageToken,
        };
        response.Revisions.AddRange(result.Items.Select(FeatureProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolFlag> RollbackFlag(
        RollbackFlagRequest request,
        ServerCallContext context) =>
        (await managementService.RollbackAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.FlagId,
            request.Revision,
            request.ExpectedVersion,
            context.GetHttpContext().TraceIdentifier,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolFlag> ArchiveFlag(
        ArchiveFlagRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.FlagId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolFlag> RestoreFlag(
        RestoreFlagRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.FlagId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEvaluation> SimulateFlag(
        SimulateFlagRequest request,
        ServerCallContext context)
    {
        var applicationId = ParseId(request.ApplicationId, "applicationId");
        var environmentId = ParseId(request.EnvironmentId, "environmentId");
        return (await managementService.SimulateAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.FlagId,
            request.UseDraft,
            request.Context.ToDomain(applicationId, environmentId),
            context.CancellationToken)).ToProtocol();
    }

    private static Guid ParseId(string value, string field) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new Asterloom.Modules.Errors.AsterloomException(
                Asterloom.Modules.Errors.AsterloomErrorKind.InvalidArgument,
                "validation_failed",
                "One or more fields are invalid.",
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [field] = ["A valid identifier is required."],
                });
}
