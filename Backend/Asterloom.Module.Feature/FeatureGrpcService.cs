using Asterloom.Modules.Errors;
using Asterloom.Modules.Feature.Model;
using Asterloom.Modules.Targeting;
using Asterloom.Protocol.Feature.V1;
using Grpc.Core;
using DomainValueKind = Asterloom.Modules.Feature.Model.FeatureValueKind;
using ProtocolEvaluationDetails = Asterloom.Protocol.Feature.V1.FeatureEvaluationDetails;

namespace Asterloom.Modules.Feature;

public sealed class FeatureGrpcService(FeatureEvaluationService evaluationService)
    : FeatureService.FeatureServiceBase
{
    public override async Task<ProtocolEvaluationDetails> EvaluateFlag(
        FeatureEvaluationInput request,
        ServerCallContext context)
    {
        var scope = new FeatureScope(
            ParseId(request.TenantId, "tenantId"),
            ParseId(request.ApplicationId, "applicationId"),
            ParseId(request.EnvironmentId, "environmentId"));
        DomainValueKind? expectedKind = request.ExpectedKind ==
            Asterloom.Protocol.Feature.V1.FeatureValueKind.Unspecified
            ? null
            : request.ExpectedKind.ToDomain();
        return (await evaluationService.EvaluateAsync(
            new FeatureEvaluationRequest(
                scope,
                request.FlagKey,
                expectedKind,
                request.Context.ToDomain(scope.ApplicationId, scope.EnvironmentId)),
            context.CancellationToken)).ToProtocol();
    }

    private static Guid ParseId(string value, string field)
    {
        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            return id;
        }

        throw new AsterloomException(
            AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [field] = ["A valid identifier is required."],
            });
    }
}
