using Asterloom.Protocol.Targeting.Admin.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProtocolSegment = Asterloom.Protocol.Targeting.V1.Segment;
using ProtocolSimulationResult = Asterloom.Protocol.Targeting.V1.TargetingSimulationResult;

namespace Asterloom.Modules.Targeting;

internal sealed class TargetingAdminGrpcService(
    TargetingManagementService managementService)
    : TargetingAdminService.TargetingAdminServiceBase
{
    public override Task<ListTargetingAttributesResponse> ListTargetingAttributes(
        Empty request,
        ServerCallContext context)
    {
        var catalog = TargetingManagementService.GetCatalog();
        var response = new ListTargetingAttributesResponse
        {
            MaximumCustomAttributes = catalog.MaximumCustomAttributes,
            MaximumConditions = catalog.MaximumConditions,
            BucketingVersion = catalog.BucketingVersion,
            BucketCount = catalog.BucketCount,
        };
        response.Attributes.AddRange(catalog.Attributes.Select(TargetingProtocolMapper.ToProtocol));
        response.Operators.AddRange(catalog.Operators.Select(TargetingProtocolMapper.ToProtocol));
        return Task.FromResult(response);
    }

    public override async Task<ListSegmentsResponse> ListSegments(
        ListSegmentsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListSegmentsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListSegmentsResponse { NextPageToken = result.NextPageToken };
        response.Segments.AddRange(result.Items.Select(TargetingProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolSegment> GetSegment(
        GetSegmentRequest request,
        ServerCallContext context) =>
        (await managementService.GetSegmentAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SegmentId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSegment> CreateSegment(
        CreateSegmentRequest request,
        ServerCallContext context) =>
        (await managementService.CreateSegmentAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.Key,
            request.DisplayName,
            request.Description,
            request.Rule.ToDomain(),
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSegment> UpdateSegment(
        UpdateSegmentRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateSegmentAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SegmentId,
            request.DisplayName,
            request.Description,
            request.Rule.ToDomain(),
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSegment> ArchiveSegment(
        ArchiveSegmentRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveSegmentAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SegmentId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSegment> RestoreSegment(
        RestoreSegmentRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreSegmentAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SegmentId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSimulationResult> SimulateTargeting(
        SimulateTargetingRequest request,
        ServerCallContext context)
    {
        var applicationId = ParseId(request.ApplicationId, "applicationId");
        var environmentId = ParseId(request.EnvironmentId, "environmentId");
        return (await managementService.SimulateAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SegmentId,
            request.Context.ToDomain(applicationId, environmentId),
            request.BucketPreview.ToDomain(),
            context.CancellationToken)).ToProtocol();
    }

    private static Guid ParseId(string value, string field)
    {
        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            return id;
        }

        throw new Asterloom.Modules.Errors.AsterloomException(
            Asterloom.Modules.Errors.AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [field] = ["A valid identifier is required."],
            });
    }
}
