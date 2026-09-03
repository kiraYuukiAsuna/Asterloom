using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Sdk.Authorization;
using Asterloom.Sdk.Targeting;
using Asterloom.Targeting;
using Asterloom.Protocol.Platform.Admin.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class PlatformApiContractTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] EnvironmentUpdatePermission =
        ["platform.environment.update"];
    private static readonly string[] OrdersRefundPermission = ["orders.refund"];
    private static readonly string[] IgnoredRequestRoles =
        ["ThisRequestValueMustBeIgnored"];
    private static readonly string[] InjectedSuperRole = ["SuperAdministrator"];

    private readonly WebApplicationFactory<Program> _factory;

    public PlatformApiContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task JsonTranscodingReturnsPlatformInfo()
    {
        using var anonymousClient = _factory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync("/api/v1/platform/info");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var client = await CreateAuthorizedClientAsync();

        var response = await client.GetAsync("/api/v1/platform/info");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<PlatformInfoJson>();

        Assert.NotNull(payload);
        Assert.Equal("Asterloom", payload.Name);
        Assert.Equal("PLATFORM_STATUS_OPERATIONAL", payload.Status);
        Assert.Contains(payload.Capabilities, capability => capability.Key == "rpc");
    }

    [Fact]
    public async Task NativeGrpcAndJsonTranscodingReturnEquivalentCapabilityCatalogs()
    {
        using var httpClient = await CreateAuthorizedClientAsync();
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient });

        var grpcClient = new PlatformAdminService.PlatformAdminServiceClient(channel);
        var grpcResponse = await grpcClient.GetPlatformInfoAsync(new Empty());

        var jsonResponse = await httpClient.GetFromJsonAsync<PlatformInfoJson>(
            "/api/v1/platform/info");

        Assert.NotNull(jsonResponse);
        Assert.Equal(grpcResponse.Name, jsonResponse.Name);
        Assert.Equal(
            grpcResponse.Capabilities.Select(capability => capability.Key),
            jsonResponse.Capabilities.Select(capability => capability.Key));
    }

    [Fact]
    public async Task JsonTranscodingManagesTheCompletePlatformHierarchy()
    {
        using var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");

        var tenant = await SendAsync<ResourceJson>(
            client.PostAsJsonAsync(
                "/api/v1/tenants",
                new { slug = "tenant-" + suffix, displayName = "Contract Tenant" }));
        tenant = await SendAsync<ResourceJson>(
            client.PatchAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}",
                new { displayName = "Updated Tenant", expectedVersion = tenant.Version }));

        var application = await SendAsync<ResourceJson>(
            client.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications",
                new { slug = "app-" + suffix, displayName = "Contract App" }));
        application = await SendAsync<ResourceJson>(
            client.PatchAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}",
                new { displayName = "Updated App", expectedVersion = application.Version }));

        var environment = await SendAsync<EnvironmentJson>(
            client.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments",
                new
                {
                    slug = "production",
                    displayName = "Production",
                    environmentType = "ENVIRONMENT_TYPE_PRODUCTION",
                    isProtected = true,
                }));
        using var protectedArchive = await client.DeleteAsync(
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments/{environment.Id}" +
            $"?expectedVersion={environment.Version}");
        Assert.True(
            protectedArchive.StatusCode == HttpStatusCode.BadRequest,
            $"Expected a protected-environment rejection, received " +
            $"{protectedArchive.StatusCode}: {await protectedArchive.Content.ReadAsStringAsync()}");

        environment = await SendAsync<EnvironmentJson>(
            client.PatchAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments/{environment.Id}",
                new
                {
                    displayName = "Production EU",
                    environmentType = "ENVIRONMENT_TYPE_PRODUCTION",
                    isProtected = false,
                    expectedVersion = environment.Version,
                }));

        var actorId = Guid.NewGuid();
        var membership = await SendAsync<MembershipJson>(
            client.PutAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/memberships/{actorId}",
                new { expectedVersion = 0 }));
        membership = await SendAsync<MembershipJson>(
            client.DeleteAsync(
                $"/api/v1/tenants/{tenant.Id}/memberships/{actorId}" +
                $"?expectedVersion={membership.Version}"));
        membership = await SendAsync<MembershipJson>(
            client.PutAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/memberships/{actorId}",
                new { expectedVersion = membership.Version }));

        environment = await SendAsync<EnvironmentJson>(
            client.DeleteAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments/{environment.Id}" +
                $"?expectedVersion={environment.Version}"));
        environment = await SendAsync<EnvironmentJson>(
            client.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments/{environment.Id}:restore",
                new { expectedVersion = environment.Version }));
        application = await SendAsync<ResourceJson>(
            client.DeleteAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}" +
                $"?expectedVersion={application.Version}"));
        application = await SendAsync<ResourceJson>(
            client.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}:restore",
                new { expectedVersion = application.Version }));
        tenant = await SendAsync<ResourceJson>(
            client.DeleteAsync(
                $"/api/v1/tenants/{tenant.Id}?expectedVersion={tenant.Version}"));
        tenant = await SendAsync<ResourceJson>(
            client.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}:restore",
                new { expectedVersion = tenant.Version }));

        var tenants = await client.GetFromJsonAsync<ResourceListJson>(
            "/api/v1/tenants?query=Updated%20Tenant&includeArchived=true");
        var applications = await client.GetFromJsonAsync<ApplicationListJson>(
            $"/api/v1/tenants/{tenant.Id}/applications?includeArchived=true");
        var environments = await client.GetFromJsonAsync<EnvironmentListJson>(
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments?includeArchived=true");
        var memberships = await client.GetFromJsonAsync<MembershipListJson>(
            $"/api/v1/tenants/{tenant.Id}/memberships?includeRemoved=true");

        Assert.Contains(tenants!.Tenants, candidate => candidate.Id == tenant.Id);
        Assert.Contains(applications!.Applications, candidate => candidate.Id == application.Id);
        Assert.Contains(environments!.Environments, candidate => candidate.Id == environment.Id);
        Assert.Contains(memberships!.Memberships, candidate => candidate.ActorId == actorId);
        Assert.Equal("MEMBERSHIP_STATUS_ACTIVE", membership.Status);
    }

    [Fact]
    public async Task JsonTranscodingManagesTheCompleteAuthorizationSurface()
    {
        using var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = await SendAsync<ResourceJson>(
            client.PostAsJsonAsync(
                "/api/v1/tenants",
                new { slug = "auth-" + suffix, displayName = "Authorization Tenant" }));
        var application = await SendAsync<ResourceJson>(
            client.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications",
                new { slug = "auth-" + suffix, displayName = "Authorization App" }));
        var environment = await SendAsync<EnvironmentJson>(
            client.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments",
                new
                {
                    slug = "development",
                    displayName = "Development",
                    environmentType = "ENVIRONMENT_TYPE_DEVELOPMENT",
                    isProtected = false,
                }));
        var tenantId = Guid.Parse(tenant.Id);
        var applicationId = Guid.Parse(application.Id);
        var environmentId = Guid.Parse(environment.Id);
        var actorId = "authorization-contract-" + suffix;

        var permissions = await client.GetFromJsonAsync<PermissionListJson>(
            "/api/v1/authorization/permissions?pageSize=100");
        Assert.Contains(
            permissions!.Permissions,
            permission => permission.Key == "authorization.role.create");

        var applicationPermission = await SendAsync<PermissionJson>(
            client.PostAsJsonAsync(
                "/api/v1/authorization/permissions",
                new
                {
                    scope = new { tenantId, applicationId },
                    key = "orders.refund",
                    displayName = "Refund orders",
                    description = "Refund an order owned by this application.",
                }));
        applicationPermission = await SendAsync<PermissionJson>(
            client.PatchAsJsonAsync(
                $"/api/v1/authorization/permissions/{applicationPermission.Id}",
                new
                {
                    displayName = "Refund business orders",
                    description = applicationPermission.Description,
                    expectedVersion = applicationPermission.Version,
                }));
        applicationPermission = await SendAsync<PermissionJson>(
            client.DeleteAsync(
                $"/api/v1/authorization/permissions/{applicationPermission.Id}" +
                $"?expectedVersion={applicationPermission.Version}"));
        applicationPermission = await SendAsync<PermissionJson>(
            client.PostAsJsonAsync(
                $"/api/v1/authorization/permissions/{applicationPermission.Id}:restore",
                new { expectedVersion = applicationPermission.Version }));
        var applicationPermissions = await client.GetFromJsonAsync<PermissionListJson>(
            $"/api/v1/authorization/permissions?tenantId={tenantId}" +
            $"&applicationId={applicationId}&includeArchived=true");
        Assert.Contains(
            applicationPermissions!.Permissions,
            permission => permission.Id == applicationPermission.Id);

        var initialRoles = await client.GetFromJsonAsync<RoleListJson>(
            "/api/v1/authorization/roles?pageSize=100");
        Assert.Contains(initialRoles!.Roles, role => role.Key == "super-administrator");

        var role = await SendAsync<AuthorizationRoleJson>(
            client.PostAsJsonAsync(
                "/api/v1/authorization/roles",
                new
                {
                    key = "contract-role-" + suffix,
                    displayName = "Contract Role",
                    description = "Created by the protocol contract test.",
                    permissions = OrdersRefundPermission,
                    scope = new { tenantId, applicationId },
                }));
        role = await SendAsync<AuthorizationRoleJson>(
            client.PatchAsJsonAsync(
                $"/api/v1/authorization/roles/{role.Id}",
                new
                {
                    displayName = "Updated Contract Role",
                    description = "Updated by the protocol contract test.",
                    permissions = OrdersRefundPermission,
                    expectedVersion = role.Version,
                }));
        role = await SendAsync<AuthorizationRoleJson>(
            client.DeleteAsync(
                $"/api/v1/authorization/roles/{role.Id}?expectedVersion={role.Version}"));
        role = await SendAsync<AuthorizationRoleJson>(
            client.PostAsJsonAsync(
                $"/api/v1/authorization/roles/{role.Id}:restore",
                new { expectedVersion = role.Version }));

        var bindingId = Guid.CreateVersion7();
        var binding = await SendAsync<RoleBindingJson>(
            client.PutAsJsonAsync(
                $"/api/v1/authorization/role-bindings/{bindingId}",
                new
                {
                    actorId,
                    roleId = role.Id,
                    scope = new
                    {
                        tenantId,
                        applicationId,
                    },
                    expectedVersion = 0,
                }));
        var bindings = await client.GetFromJsonAsync<RoleBindingListJson>(
            $"/api/v1/authorization/role-bindings?actorId={actorId}&tenantId={tenantId}" +
            "&includeArchived=true");
        Assert.Contains(bindings!.RoleBindings, candidate => candidate.Id == binding.Id);
        binding = await SendAsync<RoleBindingJson>(
            client.DeleteAsync(
                $"/api/v1/authorization/role-bindings/{binding.Id}" +
                $"?expectedVersion={binding.Version}"));
        binding = await SendAsync<RoleBindingJson>(
            client.PutAsJsonAsync(
                $"/api/v1/authorization/role-bindings/{binding.Id}",
                new
                {
                    actorId,
                    roleId = role.Id,
                    scope = new
                    {
                        tenantId,
                        applicationId,
                    },
                    expectedVersion = binding.Version,
                }));

        var policy = await SendAsync<PolicyRuleJson>(
            client.PostAsJsonAsync(
                "/api/v1/authorization/policies",
                new
                {
                    name = "Contract policy",
                    effect = "POLICY_EFFECT_ALLOW",
                    subjectType = "POLICY_SUBJECT_TYPE_ACTOR",
                    subject = actorId,
                    scope = new
                    {
                        tenantId,
                        applicationId,
                    },
                    permission = "orders.refund",
                    resourceType = "order",
                    resourceId = "order-42",
                    condition = AuthorizationFinanceConditionPayload(),
                }));
        policy = await SendAsync<PolicyRuleJson>(
            client.PatchAsJsonAsync(
                $"/api/v1/authorization/policies/{policy.Id}",
                new
                {
                    name = "Contract deny policy",
                    effect = "POLICY_EFFECT_DENY",
                    subjectType = "POLICY_SUBJECT_TYPE_ACTOR",
                    subject = actorId,
                    scope = new
                    {
                        tenantId,
                        applicationId,
                    },
                    permission = "orders.refund",
                    resourceType = "order",
                    resourceId = "order-42",
                    condition = AuthorizationFinanceConditionPayload(),
                    expectedVersion = policy.Version,
                }));
        policy = await SendAsync<PolicyRuleJson>(
            client.DeleteAsync(
                $"/api/v1/authorization/policies/{policy.Id}" +
                $"?expectedVersion={policy.Version}"));
        policy = await SendAsync<PolicyRuleJson>(
            client.PostAsJsonAsync(
                $"/api/v1/authorization/policies/{policy.Id}:restore",
                new { expectedVersion = policy.Version }));
        var policies = await client.GetFromJsonAsync<PolicyRuleListJson>(
            $"/api/v1/authorization/policies?query=Contract%20deny&tenantId={tenantId}" +
            "&includeArchived=true");
        Assert.Contains(policies!.PolicyRules, candidate => candidate.Id == policy.Id);

        var revisions = await client.GetFromJsonAsync<RevisionListJson>(
            $"/api/v1/authorization/revisions?resourceType=policy_rule" +
            $"&resourceId={policy.Id}");
        Assert.True(revisions!.Revisions.Count >= 4);

        var simulation = await SendAsync<DecisionJson>(
            client.PostAsJsonAsync(
                "/api/v1/authorization:simulate",
                new
                {
                    input = new
                    {
                        actorId,
                        scope = new
                        {
                            tenantId,
                            applicationId,
                            environmentId,
                        },
                        permission = "orders.refund",
                        resourceType = "order",
                        resourceId = "order-42",
                        attributes = new[]
                        {
                            new
                            {
                                key = "subject.department",
                                value = new { text = "finance" },
                            },
                        },
                    },
                }));
        Assert.False(simulation.Allowed);

        var runtimeDecision = await SendAsync<DecisionJson>(
            client.PostAsJsonAsync(
                "/api/v1/authorization:check",
                new
                {
                    actorId = "platform-contract-tests",
                    permission = "authorization.role.read",
                    trustedRoles = IgnoredRequestRoles,
                }));
        Assert.True(runtimeDecision.Allowed);
        Assert.Equal("AUTHORIZATION_RESOURCE_STATUS_ACTIVE", role.Status);
        Assert.Equal("AUTHORIZATION_RESOURCE_STATUS_ACTIVE", binding.Status);
        Assert.Equal("AUTHORIZATION_RESOURCE_STATUS_ACTIVE", policy.Status);
        Assert.Equal("AUTHORIZATION_RESOURCE_STATUS_ACTIVE", applicationPermission.Status);
        Assert.Equal("POLICY_EFFECT_DENY", policy.Effect);
    }

    [Fact]
    public async Task JsonTranscodingManagesTheCompleteTargetingSurface()
    {
        using var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = await SendAsync<ResourceJson>(
            client.PostAsJsonAsync(
                "/api/v1/tenants",
                new { slug = "target-" + suffix, displayName = "Targeting Tenant" }));
        var application = await SendAsync<ResourceJson>(
            client.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications",
                new { slug = "target-" + suffix, displayName = "Targeting App" }));
        var environment = await SendAsync<EnvironmentJson>(
            client.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments",
                new
                {
                    slug = "development",
                    displayName = "Development",
                    environmentType = "ENVIRONMENT_TYPE_DEVELOPMENT",
                    isProtected = false,
                }));
        var basePath =
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}" +
            $"/environments/{environment.Id}";

        var catalog = await client.GetFromJsonAsync<TargetingCatalogJson>(
            "/api/v1/targeting/attributes");
        Assert.NotNull(catalog);
        Assert.Equal("v1", catalog.BucketingVersion);
        Assert.Equal(16, catalog.Operators.Count);

        var segment = await SendAsync<TargetingSegmentJson>(
            client.PostAsJsonAsync(
                basePath + "/targeting/segments",
                new
                {
                    key = "early-access-" + suffix,
                    displayName = "Early access",
                    description = "Targeting contract segment",
                    rule = TargetingRulePayload("cn"),
                }));
        var fetched = await client.GetFromJsonAsync<TargetingSegmentJson>(
            basePath + $"/targeting/segments/{segment.Id}");
        Assert.Equal(segment.Id, fetched!.Id);

        segment = await SendAsync<TargetingSegmentJson>(
            client.PatchAsJsonAsync(
                basePath + $"/targeting/segments/{segment.Id}",
                new
                {
                    displayName = "Early access users",
                    description = "Updated targeting contract segment",
                    rule = TargetingRulePayload("CN"),
                    expectedVersion = segment.Version,
                }));

        var simulation = await SendAsync<TargetingSimulationJson>(
            client.PostAsJsonAsync(
                basePath + "/targeting:simulate",
                new
                {
                    segmentId = segment.Id,
                    context = new
                    {
                        targetingKey = "contract-user-42",
                        clientVersion = "2.1.0",
                        region = "cn",
                    },
                    bucketPreview = new
                    {
                        resourceType = "feature",
                        resourceKey = "new-home",
                        salt = "stable-salt",
                        allocations = new[]
                        {
                            new { variant = "enabled", start = 0, end = 100_000 },
                        },
                    },
                }));
        Assert.True(simulation.Matched);
        Assert.True(simulation.BucketEvaluated);
        Assert.Equal("enabled", simulation.SelectedVariant);
        Assert.Single(simulation.ConditionTraces);

        segment = await SendAsync<TargetingSegmentJson>(
            client.DeleteAsync(
                basePath + $"/targeting/segments/{segment.Id}" +
                $"?expectedVersion={segment.Version}"));
        var segments = await client.GetFromJsonAsync<TargetingSegmentListJson>(
            basePath + "/targeting/segments?includeArchived=true&query=Early");
        Assert.Contains(segments!.Segments, candidate => candidate.Id == segment.Id);
        segment = await SendAsync<TargetingSegmentJson>(
            client.PostAsJsonAsync(
                basePath + $"/targeting/segments/{segment.Id}:restore",
                new { expectedVersion = segment.Version }));

        Assert.Equal("TARGETING_RESOURCE_STATUS_ACTIVE", segment.Status);
    }

    [Fact]
    public async Task NativeGrpcTargetingSdkCoversTheCompleteAdminSurface()
    {
        using var httpClient = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = await SendAsync<ResourceJson>(
            httpClient.PostAsJsonAsync(
                "/api/v1/tenants",
                new { slug = "sdk-target-" + suffix, displayName = "SDK Targeting" }));
        var application = await SendAsync<ResourceJson>(
            httpClient.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications",
                new { slug = "sdk-target-" + suffix, displayName = "SDK App" }));
        var environment = await SendAsync<EnvironmentJson>(
            httpClient.PostAsJsonAsync(
                $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments",
                new
                {
                    slug = "testing",
                    displayName = "Testing",
                    environmentType = "ENVIRONMENT_TYPE_DEVELOPMENT",
                    isProtected = false,
                }));
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient });
        var sdk = new AsterloomTargetingAdminClient(channel.CreateCallInvoker());
        var scope = new AsterloomTargetingScope(
            Guid.Parse(tenant.Id),
            Guid.Parse(application.Id),
            Guid.Parse(environment.Id));
        var rule = new TargetingRule(
            TargetingMatchMode.All,
            [
                new TargetingCondition(
                    "platform",
                    "platform",
                    TargetingValueKind.Text,
                    TargetingOperator.Equals,
                    [TargetingValue.From("windows")]),
            ]);

        var catalog = await sdk.ListTargetingAttributesAsync();
        var segment = await sdk.CreateSegmentAsync(
            scope,
            new AsterloomTargetingSegmentRegistration(
                "sdk-segment-" + suffix,
                "SDK segment",
                "Created through native gRPC.",
                rule));
        var listed = await sdk.ListSegmentsAsync(scope, query: "SDK segment");
        var fetched = await sdk.GetSegmentAsync(scope, segment.Id);
        segment = await sdk.UpdateSegmentAsync(
            segment,
            new AsterloomTargetingSegmentUpdate(
                "Updated SDK segment",
                segment.Description,
                segment.Rule));
        var simulation = await sdk.SimulateAsync(
            segment,
            new TargetingEvaluationContext(
                "sdk-device",
                scope.ApplicationId,
                scope.EnvironmentId,
                platform: "Windows"),
            new AsterloomTargetingBucketPreview(
                "feature",
                "sdk-preview",
                "sdk-salt",
                [new TargetingBucketAllocation("enabled", 0, 100_000)]));
        segment = await sdk.ArchiveSegmentAsync(segment);
        segment = await sdk.RestoreSegmentAsync(segment);

        Assert.Equal(16, catalog.Operators.Count);
        Assert.Contains(listed.Items, candidate => candidate.Id == segment.Id);
        Assert.Equal(segment.Id, fetched.Id);
        Assert.True(simulation.Matched);
        Assert.Equal("enabled", simulation.SelectedVariant);
        Assert.Equal(AsterloomTargetingResourceStatus.Active, segment.Status);
    }

    [Fact]
    public async Task RuntimePermissionCheckRejectsRoleInjectionAndActorImpersonation()
    {
        const string clientId = "authorization-unprivileged-contract-tests";
        using var client = await CreateAuthorizedClientAsync(
            clientId,
            "Authorization-Unprivileged-Contract-Tests!2026",
            grantSuperAdministrator: false);
        using var channel = GrpcChannel.ForAddress(
            client.BaseAddress!,
            new GrpcChannelOptions { HttpClient = client });
        var sdk = new AsterloomAuthorizationClient(channel.CreateCallInvoker());
        var sdkDecision = await sdk.CheckPermissionAsync("platform.tenant.create");
        Assert.False(sdkDecision.Allowed);

        var decision = await SendAsync<DecisionJson>(
            client.PostAsJsonAsync(
                "/api/v1/authorization:check",
                new
                {
                    actorId = clientId,
                    permission = "platform.tenant.create",
                    trustedRoles = InjectedSuperRole,
                }));

        Assert.False(decision.Allowed);

        using var impersonationResponse = await client.PostAsJsonAsync(
            "/api/v1/authorization:check",
            new
            {
                actorId = "another-actor",
                permission = "platform.tenant.read",
            });
        Assert.Equal(HttpStatusCode.Forbidden, impersonationResponse.StatusCode);
    }

    [Fact]
    public async Task AuditSurfaceCapturesSuccessDenialDetailAndExport()
    {
        using var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var successRequestId = "audit-success-" + suffix;
        using var successRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        {
            Content = JsonContent.Create(
                new { slug = "audit-" + suffix, displayName = "Audit Contract" }),
        };
        successRequest.Headers.Add("X-Request-ID", successRequestId);
        using var successResponse = await client.SendAsync(successRequest);
        successResponse.EnsureSuccessStatusCode();
        var tenant = await successResponse.Content.ReadFromJsonAsync<ResourceJson>();
        Assert.NotNull(tenant);

        using var unprivilegedClient = await CreateAuthorizedClientAsync(
            "audit-unprivileged-contract-tests",
            "Audit-Unprivileged-Contract-Tests!2026",
            grantSuperAdministrator: false);
        var deniedRequestId = "audit-denied-" + suffix;
        using var deniedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        {
            Content = JsonContent.Create(
                new { slug = "denied-" + suffix, displayName = "Denied Audit" }),
        };
        deniedRequest.Headers.Add("X-Request-ID", deniedRequestId);
        using var deniedResponse = await unprivilegedClient.SendAsync(deniedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var succeeded = await client.GetFromJsonAsync<AuditListJson>(
            $"/api/v1/audit/events?requestId={successRequestId}");
        var denied = await client.GetFromJsonAsync<AuditListJson>(
            $"/api/v1/audit/events?requestId={deniedRequestId}");
        var succeededEvent = Assert.Single(succeeded!.AuditEvents);
        var deniedEvent = Assert.Single(denied!.AuditEvents);

        Assert.Equal("AUDIT_OUTCOME_SUCCEEDED", succeededEvent.Outcome);
        Assert.Equal("platform-contract-tests", succeededEvent.ActorId);
        Assert.Equal("tenant", succeededEvent.ResourceType);
        Assert.Equal(tenant.Id, succeededEvent.ResourceId);
        Assert.Contains("CreateTenant", succeededEvent.Operation, StringComparison.Ordinal);
        Assert.Contains("slug", succeededEvent.ChangeSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("audit-" + suffix, succeededEvent.ChangeSummary, StringComparison.Ordinal);
        Assert.Equal("AUDIT_OUTCOME_DENIED", deniedEvent.Outcome);
        Assert.Equal("permission_denied", deniedEvent.ErrorCode);

        var detail = await client.GetFromJsonAsync<AuditEventJson>(
            $"/api/v1/audit/events/{succeededEvent.Id}");
        Assert.Equal(successRequestId, detail!.RequestId);

        var export = await SendAsync<AuditExportJson>(
            client.PostAsJsonAsync(
                "/api/v1/audit/events:export",
                new { operation = "CreateTenant", maximumRows = 100 }));
        Assert.True(export.ExportedRows >= 2);
        var csv = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(export.Content));
        Assert.Contains(successRequestId, csv, StringComparison.Ordinal);
        Assert.Contains(deniedRequestId, csv, StringComparison.Ordinal);
    }

    private static async Task<T> SendAsync<T>(Task<HttpResponseMessage> responseTask)
    {
        using var response = await responseTask;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException("The JSON response was empty.");
    }

    private static object TargetingRulePayload(string region) => new
    {
        matchMode = "TARGETING_MATCH_MODE_ALL",
        conditions = new[]
        {
            new
            {
                id = "region",
                attribute = "region",
                valueKind = "TARGETING_VALUE_KIND_TEXT",
                @operator = "TARGETING_OPERATOR_EQUALS",
                values = new[] { new { text = region } },
                caseSensitive = false,
            },
        },
    };

    private static object AuthorizationFinanceConditionPayload() => new
    {
        matchMode = "TARGETING_MATCH_MODE_ALL",
        conditions = new[]
        {
            new
            {
                id = "finance-department",
                attribute = "subject.department",
                valueKind = "TARGETING_VALUE_KIND_TEXT",
                @operator = "TARGETING_OPERATOR_EQUALS",
                values = new[] { new { text = "finance" } },
                caseSensitive = false,
            },
        },
    };

    private Task<HttpClient> CreateAuthorizedClientAsync() =>
        CreateAuthorizedClientAsync(
            "platform-contract-tests",
            "Platform-Contract-Tests-Secret!2026",
            grantSuperAdministrator: true);

    private async Task<HttpClient> CreateAuthorizedClientAsync(
        string clientId,
        string clientSecret,
        bool grantSuperAdministrator)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider
                .GetRequiredService<IOpenIddictApplicationManager>();
            if (await manager.FindByClientIdAsync(clientId) is null)
            {
                await manager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    ClientType = ClientTypes.Confidential,
                    DisplayName = "Platform contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }

            if (grantSuperAdministrator)
            {
                var authorizationStore = scope.ServiceProvider
                    .GetRequiredService<IAuthorizationStore>();
                var bindingId = Guid.Parse("aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa");
                if (await authorizationStore.GetRoleBindingAsync(
                        bindingId,
                        CancellationToken.None) is null)
                {
                    var management = scope.ServiceProvider
                        .GetRequiredService<AuthorizationManagementService>();
                    var superAdministrator = AuthorizationCatalog.FindSystemRole(
                        "super-administrator")!;
                    await management.SetRoleBindingAsync(
                        bindingId.ToString("D"),
                        clientId,
                        superAdministrator.Id.ToString("D"),
                        AuthorizationScope.Global,
                        expectedVersion: 0,
                        CancellationToken.None);
                }
            }
        }

        var client = _factory.CreateClient();
        using var tokenResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.ClientCredentials,
                [Parameters.ClientId] = clientId,
                [Parameters.ClientSecret] = clientSecret,
                [Parameters.Scope] = "asterloom.api",
            }));
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.GetProperty(Parameters.AccessToken).GetString());
        return client;
    }

    private sealed record PlatformInfoJson(
        string Name,
        string Version,
        string Status,
        DateTimeOffset ServerTime,
        IReadOnlyList<PlatformCapabilityJson> Capabilities);

    private sealed record PlatformCapabilityJson(
        string Key,
        string DisplayName,
        string Lifecycle);

    private sealed record ResourceJson(
        string Id,
        string Slug,
        string DisplayName,
        string Status,
        long Version);

    private sealed record EnvironmentJson(
        string Id,
        string Slug,
        string DisplayName,
        string EnvironmentType,
        bool IsProtected,
        string Status,
        long Version);

    private sealed record MembershipJson(
        Guid ActorId,
        string Status,
        long Version);

    private sealed record ResourceListJson(IReadOnlyList<ResourceJson> Tenants);

    private sealed record ApplicationListJson(IReadOnlyList<ResourceJson> Applications);

    private sealed record EnvironmentListJson(IReadOnlyList<EnvironmentJson> Environments);

    private sealed record MembershipListJson(IReadOnlyList<MembershipJson> Memberships);

    private sealed record PermissionJson(
        string Id,
        string Key,
        string DisplayName,
        string Description,
        string Status,
        long Version);

    private sealed record PermissionListJson(IReadOnlyList<PermissionJson> Permissions);

    private sealed record AuthorizationRoleJson(
        string Id,
        string Key,
        string DisplayName,
        string Status,
        long Version);

    private sealed record RoleListJson(IReadOnlyList<AuthorizationRoleJson> Roles);

    private sealed record RoleBindingJson(
        string Id,
        string ActorId,
        string RoleId,
        string RoleKey,
        string Status,
        long Version);

    private sealed record RoleBindingListJson(IReadOnlyList<RoleBindingJson> RoleBindings);

    private sealed record PolicyRuleJson(
        string Id,
        string Name,
        string Effect,
        string Status,
        long Version);

    private sealed record PolicyRuleListJson(IReadOnlyList<PolicyRuleJson> PolicyRules);

    private sealed record RevisionJson(long RevisionNumber, string ResourceId);

    private sealed record RevisionListJson(IReadOnlyList<RevisionJson> Revisions);

    private sealed record DecisionJson(bool Allowed, string Reason);

    private sealed record AuditEventJson(
        string Id,
        string ActorId,
        string Operation,
        string ResourceType,
        string ResourceId,
        string RequestId,
        string Outcome,
        string ErrorCode,
        string ChangeSummary);

    private sealed record AuditListJson(IReadOnlyList<AuditEventJson> AuditEvents);

    private sealed record AuditExportJson(string Content, int ExportedRows);

    private sealed record TargetingCatalogJson(
        IReadOnlyList<JsonElement> Attributes,
        IReadOnlyList<JsonElement> Operators,
        string BucketingVersion,
        uint BucketCount);

    private sealed record TargetingSegmentJson(
        string Id,
        string Key,
        string DisplayName,
        string Status,
        long Version);

    private sealed record TargetingSegmentListJson(
        IReadOnlyList<TargetingSegmentJson> Segments);

    private sealed record TargetingConditionTraceJson(
        string ConditionId,
        bool Matched,
        string Reason);

    private sealed record TargetingSimulationJson(
        bool Matched,
        string Reason,
        IReadOnlyList<TargetingConditionTraceJson> ConditionTraces,
        bool BucketEvaluated,
        uint Bucket,
        string SelectedVariant);
}
