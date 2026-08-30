using Asterloom.Protocol.Release.V1;
using Asterloom.Modules.Targeting;
using Grpc.Core;

namespace Asterloom.Modules.Release;

public sealed class ReleaseGrpcService(ReleaseEvaluationService evaluationService)
    : ReleaseService.ReleaseServiceBase
{
    public override async Task<UpdateDecision> CheckForUpdate(
        CheckForUpdateRequest request,
        ServerCallContext context)
    {
        var scope = ReleaseProtocolMapper.ToReleaseScope(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId);
        return (await evaluationService.CheckForUpdateAsync(
            new(
                scope,
                request.ChannelKey,
                request.CurrentVersion,
                request.TargetRuntimeId,
                request.Context.ToDomain(scope.ApplicationId, scope.EnvironmentId)),
            context.CancellationToken)).ToProtocol();
    }
}
