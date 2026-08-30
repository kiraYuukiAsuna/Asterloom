using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Asterloom.Modules.Errors;
using Asterloom.Protocol.Operations.V1;
using Asterloom.Protocol.Platform.Admin.V1;
using Google.Api;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;
using ProtocolDependencyStatus = Asterloom.Protocol.Operations.V1.DependencyHealthStatus;

namespace Asterloom.Modules.Rpc.Operations;

internal sealed class OperationsMetadataService(
    HealthCheckService healthCheckService,
    ISwaggerProvider swaggerProvider,
    TimeProvider timeProvider)
{
    private static readonly ApiEndpoint[] ApiCatalog = DiscoverApis();
    private readonly Lock _openApiGate = new();
    private OpenApiDocument? _openApiDocument;

    public static IReadOnlyList<ApiEndpoint> ListApis(string? query, string? category)
    {
        var normalizedQuery = Normalize(query, "query", 200);
        var normalizedCategory = Normalize(category, "category", 20).ToLowerInvariant();
        if (normalizedCategory is not ("" or "admin" or "runtime"))
        {
            throw Invalid("category", "Category must be admin, runtime, or empty.");
        }

        return ApiCatalog
            .Where(item => normalizedCategory.Length == 0
                || string.Equals(item.Category, normalizedCategory, StringComparison.Ordinal))
            .Where(item => normalizedQuery.Length == 0
                || item.Service.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || item.Rpc.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || item.HttpPath.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.Clone())
            .ToArray();
    }

    public async Task<OperationsHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(cancellationToken);
        var result = new OperationsHealth
        {
            Status = ToProtocol(report.Status),
            CheckedAt = Timestamp.FromDateTimeOffset(timeProvider.GetUtcNow()),
            DurationMilliseconds = (long)report.TotalDuration.TotalMilliseconds,
        };
        result.Dependencies.AddRange(report.Entries
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(static entry =>
            {
                var dependency = new DependencyHealth
                {
                    Name = entry.Key,
                    Status = ToProtocol(entry.Value.Status),
                    DurationMilliseconds = (long)entry.Value.Duration.TotalMilliseconds,
                    Description = entry.Value.Description ?? string.Empty,
                };
                dependency.Tags.AddRange(entry.Value.Tags.OrderBy(static tag => tag, StringComparer.Ordinal));
                return dependency;
            }));
        return result;
    }

    public OpenApiDocument GetOpenApiDocument()
    {
        lock (_openApiGate)
        {
            if (_openApiDocument is not null)
            {
                return _openApiDocument.Clone();
            }

            var document = swaggerProvider.GetSwagger("v1");
            using var textWriter = new StringWriter(CultureInfo.InvariantCulture);
            var jsonWriter = new OpenApiJsonWriter(textWriter);
            document.SerializeAsV3(jsonWriter);
            jsonWriter.Flush();
            var content = textWriter.ToString();
            _openApiDocument = new OpenApiDocument
            {
                ContentType = "application/vnd.oai.openapi+json;version=3.0",
                Content = content,
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
                    .ToLowerInvariant(),
                GeneratedAt = Timestamp.FromDateTimeOffset(timeProvider.GetUtcNow()),
            };
            return _openApiDocument.Clone();
        }
    }

    private static ApiEndpoint[] DiscoverApis()
    {
        var protocolAssembly = typeof(PlatformAdminReflection).Assembly;
        var files = protocolAssembly.DefinedTypes
            .Where(static type => type.IsAbstract
                && type.IsSealed
                && type.Name.EndsWith("Reflection", StringComparison.Ordinal))
            .Select(static type => type.GetProperty(
                "Descriptor",
                BindingFlags.Public | BindingFlags.Static))
            .Where(static property => property?.PropertyType == typeof(FileDescriptor))
            .Select(static property => property!.GetValue(null))
            .OfType<FileDescriptor>()
            .Where(static descriptor => descriptor.Name.StartsWith(
                "Asterloom/",
                StringComparison.OrdinalIgnoreCase))
            .DistinctBy(static descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToArray();
        return files
            .SelectMany(static file => file.Services)
            .SelectMany(static service => service.Methods.Select(method => (service, method)))
            .Select(static item => ToApiEndpoint(item.service, item.method))
            .OrderBy(static item => item.Service, StringComparer.Ordinal)
            .ThenBy(static item => item.Rpc, StringComparer.Ordinal)
            .ToArray();
    }

    private static ApiEndpoint ToApiEndpoint(
        ServiceDescriptor service,
        MethodDescriptor method)
    {
        var options = method.GetOptions();
        var rule = options.HasExtension(AnnotationsExtensions.Http)
            ? options.GetExtension(AnnotationsExtensions.Http)
            : null;
        var (httpMethod, httpPath) = rule is null
            ? (string.Empty, string.Empty)
            : ToHttpEndpoint(rule);
        return new ApiEndpoint
        {
            Service = service.FullName,
            Rpc = method.Name,
            Category = service.FullName.Contains(".admin.", StringComparison.Ordinal)
                ? "admin"
                : "runtime",
            HttpMethod = httpMethod,
            HttpPath = httpPath,
            RequestType = method.InputType.FullName,
            ResponseType = method.OutputType.FullName,
            Deprecated = options.Deprecated,
        };
    }

    private static (string Method, string Path) ToHttpEndpoint(HttpRule rule) =>
        rule.PatternCase switch
        {
            HttpRule.PatternOneofCase.Get => ("GET", rule.Get),
            HttpRule.PatternOneofCase.Put => ("PUT", rule.Put),
            HttpRule.PatternOneofCase.Post => ("POST", rule.Post),
            HttpRule.PatternOneofCase.Delete => ("DELETE", rule.Delete),
            HttpRule.PatternOneofCase.Patch => ("PATCH", rule.Patch),
            HttpRule.PatternOneofCase.Custom =>
                (rule.Custom.Kind.ToUpperInvariant(), rule.Custom.Path),
            _ => (string.Empty, string.Empty),
        };

    private static ProtocolDependencyStatus ToProtocol(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => ProtocolDependencyStatus.Healthy,
        HealthStatus.Degraded => ProtocolDependencyStatus.Degraded,
        HealthStatus.Unhealthy => ProtocolDependencyStatus.Unhealthy,
        _ => ProtocolDependencyStatus.Unspecified,
    };

    private static string Normalize(string? value, string field, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw Invalid(field, $"The value cannot exceed {maximumLength} characters.");
    }

    private static AsterloomException Invalid(string field, string description) => new(
        AsterloomErrorKind.InvalidArgument,
        "validation_failed",
        "One or more fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [description],
        });
}
