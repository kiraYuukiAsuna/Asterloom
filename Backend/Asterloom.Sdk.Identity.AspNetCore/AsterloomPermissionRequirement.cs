using Microsoft.AspNetCore.Authorization;

namespace Asterloom.Sdk.Identity.AspNetCore;

public sealed record AsterloomPermissionRequirement(
    string Permission,
    Guid? EnvironmentId = null) : IAuthorizationRequirement;
