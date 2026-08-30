namespace Asterloom.Modules.Requests;

public sealed record AsterloomRequestContext(
    string RequestId,
    string? ActorId,
    string? TenantId,
    string? ApplicationId,
    string? EnvironmentId);

public interface IAsterloomRequestContextAccessor
{
    AsterloomRequestContext? Current { get; }
}
