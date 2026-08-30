using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asterloom.Protocol.Platform.Admin.V1;
using Google.Api;
using Google.Protobuf.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

return await ApiCoverageVerifier.RunAsync(args);

internal static class ApiCoverageVerifier
{
    private const string ManifestRelativePath = "Docs/Protocol/admin-api-coverage.yaml";
    private const string OpenApiRelativePath = "Docs/Protocol/openapi/asterloom-v1.json";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var repositoryRoot = ResolveRepositoryRoot(args);
            var manifest = await ReadManifestAsync(repositoryRoot);
            var openApiEndpoints = await ReadOpenApiEndpointsAsync(repositoryRoot);
            var contracts = DiscoverRpcContracts();
            var errors = Verify(repositoryRoot, manifest, contracts, openApiEndpoints);

            if (errors.Count > 0)
            {
                Console.Error.WriteLine("Admin API coverage verification failed:");
                foreach (var error in errors)
                {
                    Console.Error.WriteLine($"  - {error}");
                }

                return 1;
            }

            var adminRpcCount = contracts.Count(static contract => contract.IsAdmin);
            Console.WriteLine(
                $"Admin API coverage: {adminRpcCount}/{adminRpcCount} (100%). " +
                $"Verified {contracts.Count} custom RPC(s) against {ManifestRelativePath}.");
            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or InvalidOperationException
                or JsonException or YamlDotNet.Core.YamlException)
        {
            Console.Error.WriteLine($"Admin API coverage verification could not run: {exception.Message}");
            return 2;
        }
    }

    private static string ResolveRepositoryRoot(string[] args)
    {
        if (args.Length > 0)
        {
            if (args.Length != 2 || !string.Equals(args[0], "--repo-root", StringComparison.Ordinal))
            {
                throw new ArgumentException("Usage: Asterloom.ApiCoverage [--repo-root <path>]");
            }

            return RequireRepositoryRoot(Path.GetFullPath(args[1]));
        }

        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ManifestRelativePath)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find the repository root containing {ManifestRelativePath}. " +
            "Pass it explicitly with --repo-root.");
    }

    private static string RequireRepositoryRoot(string candidate)
    {
        if (!File.Exists(Path.Combine(candidate, ManifestRelativePath)))
        {
            throw new ArgumentException(
                $"The repository root '{candidate}' does not contain {ManifestRelativePath}.");
        }

        return candidate;
    }

    private static async Task<ApiCoverageManifest> ReadManifestAsync(string repositoryRoot)
    {
        var manifestPath = Path.Combine(repositoryRoot, ManifestRelativePath);
        var yaml = await File.ReadAllTextAsync(manifestPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<ApiCoverageManifest>(yaml)
            ?? throw new InvalidOperationException($"{ManifestRelativePath} is empty.");
    }

    private static async Task<HashSet<string>> ReadOpenApiEndpointsAsync(string repositoryRoot)
    {
        var openApiPath = Path.Combine(repositoryRoot, OpenApiRelativePath);
        await using var stream = File.OpenRead(openApiPath);
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("paths", out var paths)
            || paths.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{OpenApiRelativePath} has no paths object.");
        }

        var endpoints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (IsHttpVerb(operation.Name))
                {
                    endpoints.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
                }
            }
        }

        return endpoints;
    }

    private static bool IsHttpVerb(string value) => value is
        "get" or "put" or "post" or "delete" or "patch" or "head" or "options" or "trace";

    private static List<RpcContract> DiscoverRpcContracts()
    {
        var protocolAssembly = typeof(PlatformAdminReflection).Assembly;
        var fileDescriptors = protocolAssembly.DefinedTypes
            .Where(static type => type.IsAbstract && type.IsSealed && type.Name.EndsWith("Reflection", StringComparison.Ordinal))
            .Select(static type => type.GetProperty(
                "Descriptor",
                BindingFlags.Public | BindingFlags.Static))
            .Where(static property => property?.PropertyType == typeof(FileDescriptor))
            .Select(static property => property!.GetValue(null))
            .OfType<FileDescriptor>()
            .Where(static descriptor => descriptor.Name.StartsWith("Asterloom/", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(static descriptor => descriptor.Name, StringComparer.Ordinal)
            .OrderBy(static descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToArray();

        if (fileDescriptors.Length == 0)
        {
            throw new InvalidOperationException("No Asterloom protocol descriptors were discovered.");
        }

        var contracts = new List<RpcContract>();
        foreach (var file in fileDescriptors)
        {
            foreach (var service in file.Services)
            {
                foreach (var method in service.Methods)
                {
                    var options = method.GetOptions();
                    var rule = options.HasExtension(AnnotationsExtensions.Http)
                        ? options.GetExtension(AnnotationsExtensions.Http)
                        : null;
                    var http = rule is null ? null : ToHttpEndpoint(rule);
                    contracts.Add(new RpcContract(
                        service.FullName,
                        method.Name,
                        http,
                        service.FullName.Contains(".admin.", StringComparison.Ordinal)));
                }
            }
        }

        return contracts;
    }

    private static HttpEndpoint? ToHttpEndpoint(HttpRule rule)
    {
        return rule.PatternCase switch
        {
            HttpRule.PatternOneofCase.Get => new HttpEndpoint("GET", rule.Get),
            HttpRule.PatternOneofCase.Put => new HttpEndpoint("PUT", rule.Put),
            HttpRule.PatternOneofCase.Post => new HttpEndpoint("POST", rule.Post),
            HttpRule.PatternOneofCase.Delete => new HttpEndpoint("DELETE", rule.Delete),
            HttpRule.PatternOneofCase.Patch => new HttpEndpoint("PATCH", rule.Patch),
            HttpRule.PatternOneofCase.Custom => new HttpEndpoint(
                rule.Custom.Kind.ToUpperInvariant(),
                rule.Custom.Path),
            _ => null,
        };
    }

    private static List<string> Verify(
        string repositoryRoot,
        ApiCoverageManifest manifest,
        IReadOnlyList<RpcContract> contracts,
        HashSet<string> openApiEndpoints)
    {
        var errors = new List<string>();
        if (manifest.Version != 1)
        {
            errors.Add($"Manifest version must be 1, but was {manifest.Version}.");
        }

        var entries = new Dictionary<string, ApiCoverageEntry>(StringComparer.Ordinal);
        foreach (var entry in manifest.Apis)
        {
            var key = CreateKey(entry.Service, entry.Rpc);
            if (!entries.TryAdd(key, entry))
            {
                errors.Add($"Manifest contains duplicate RPC entry '{key}'.");
            }
        }

        var contractKeys = contracts
            .Select(static contract => CreateKey(contract.Service, contract.Rpc))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var contract in contracts)
        {
            var key = CreateKey(contract.Service, contract.Rpc);
            if (contract.Http is null)
            {
                errors.Add($"RPC '{key}' has no google.api.http mapping, so browsers cannot call it.");
                continue;
            }

            if (!openApiEndpoints.Contains(ToOpenApiEndpoint(contract.Http)))
            {
                errors.Add(
                    $"RPC '{key}' HTTP mapping '{contract.Http}' is missing from {OpenApiRelativePath}.");
            }

            if (!entries.TryGetValue(key, out var entry))
            {
                errors.Add($"RPC '{key}' is missing from {ManifestRelativePath}.");
                continue;
            }

            VerifyEntry(repositoryRoot, contract, entry, errors);
        }

        foreach (var key in entries.Keys.Where(key => !contractKeys.Contains(key)))
        {
            errors.Add($"Manifest entry '{key}' does not match a compiled Asterloom RPC.");
        }

        return errors;
    }

    private static void VerifyEntry(
        string repositoryRoot,
        RpcContract contract,
        ApiCoverageEntry entry,
        List<string> errors)
    {
        var key = CreateKey(contract.Service, contract.Rpc);
        var expectedCategory = contract.IsAdmin ? "admin" : "runtime";
        if (!string.Equals(entry.Category, expectedCategory, StringComparison.Ordinal))
        {
            errors.Add($"RPC '{key}' category must be '{expectedCategory}'.");
        }

        var expectedHttp = contract.Http!.ToString();
        if (!string.Equals(entry.Http, expectedHttp, StringComparison.Ordinal))
        {
            errors.Add($"RPC '{key}' HTTP mapping must be '{expectedHttp}', but was '{entry.Http}'.");
        }

        if (!contract.IsAdmin)
        {
            VerifyRuntimeEntry(repositoryRoot, key, entry, errors);
            return;
        }

        RequireValue(entry.Permission, "permission", key, errors);
        RequireValue(entry.UiRoute, "uiRoute", key, errors);
        RequireValue(entry.UiAction, "uiAction", key, errors);
        RequireValue(entry.E2eTest, "e2eTest", key, errors);

        if (!string.IsNullOrWhiteSpace(entry.UiRoute) && !NextRouteExists(repositoryRoot, entry.UiRoute))
        {
            errors.Add($"RPC '{key}' points to missing Next.js route '{entry.UiRoute}'.");
        }

        if (!string.IsNullOrWhiteSpace(entry.UiAction) && !UiActionExists(repositoryRoot, entry.UiAction))
        {
            errors.Add(
                $"RPC '{key}' has no data-ui-action=\"{entry.UiAction}\" marker in Frontend source.");
        }

        if (!string.IsNullOrWhiteSpace(entry.E2eTest))
        {
            VerifyEndToEndTest(repositoryRoot, key, entry, errors);
        }
    }

    private static string ToOpenApiEndpoint(HttpEndpoint endpoint) =>
        $"{endpoint.Verb} {Regex.Replace(
            endpoint.Path,
            @"\{([^{}]+)\}",
            static match => $"{{{ToLowerCamelCase(match.Groups[1].Value)}}}",
            RegexOptions.CultureInvariant)}";

    private static string ToLowerCamelCase(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return value;
        }

        return parts[0] + string.Concat(
            parts.Skip(1).Select(static part =>
                char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static void VerifyRuntimeEntry(
        string repositoryRoot,
        string rpcKey,
        ApiCoverageEntry entry,
        List<string> errors)
    {
        RequireValue(entry.SdkTest, "sdkTest", rpcKey, errors);
        if (!string.IsNullOrWhiteSpace(entry.SdkTest))
        {
            VerifyRepositoryTest(repositoryRoot, rpcKey, entry.SdkTest, errors);
        }

        var hasManagementSurface = !string.IsNullOrWhiteSpace(entry.UiRoute)
            || !string.IsNullOrWhiteSpace(entry.UiAction)
            || !string.IsNullOrWhiteSpace(entry.E2eTest);

        if (!hasManagementSurface)
        {
            RequireValue(entry.NotApplicableReason, "notApplicableReason", rpcKey, errors);
            return;
        }

        RequireValue(entry.UiRoute, "uiRoute", rpcKey, errors);
        RequireValue(entry.UiAction, "uiAction", rpcKey, errors);
        RequireValue(entry.E2eTest, "e2eTest", rpcKey, errors);

        if (!string.IsNullOrWhiteSpace(entry.UiRoute) && !NextRouteExists(repositoryRoot, entry.UiRoute))
        {
            errors.Add($"Runtime RPC '{rpcKey}' points to missing Next.js route '{entry.UiRoute}'.");
        }

        if (!string.IsNullOrWhiteSpace(entry.UiAction) && !UiActionExists(repositoryRoot, entry.UiAction))
        {
            errors.Add(
                $"Runtime RPC '{rpcKey}' has no data-ui-action=\"{entry.UiAction}\" marker in Frontend source.");
        }

        if (!string.IsNullOrWhiteSpace(entry.E2eTest))
        {
            VerifyEndToEndTest(repositoryRoot, rpcKey, entry, errors);
        }
    }

    private static void VerifyRepositoryTest(
        string repositoryRoot,
        string rpcKey,
        string relativePath,
        List<string> errors)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var testPath = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root + Path.DirectorySeparatorChar;

        if (!testPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Runtime RPC '{rpcKey}' sdkTest escapes the repository root.");
            return;
        }

        if (!File.Exists(testPath))
        {
            errors.Add($"Runtime RPC '{rpcKey}' points to missing SDK test '{relativePath}'.");
        }
    }

    private static void RequireValue(
        string value,
        string field,
        string rpcKey,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Admin RPC '{rpcKey}' must declare {field}.");
        }
    }

    private static bool NextRouteExists(string repositoryRoot, string route)
    {
        var expectedRoute = "/" + route.Split('?', '#')[0].Trim('/');
        var appRoot = Path.Combine(repositoryRoot, "Frontend", "app");

        return Directory.EnumerateFiles(appRoot, "page.tsx", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(appRoot, Path.GetDirectoryName(path)!))
            .Select(static relativePath => relativePath
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(static segment => segment != "."
                    && !(segment.StartsWith('(') && segment.EndsWith(')'))
                    && !segment.StartsWith('@')))
            .Select(static segments => "/" + string.Join('/', segments))
            .Any(candidate => string.Equals(candidate, expectedRoute, StringComparison.Ordinal));
    }

    private static bool UiActionExists(string repositoryRoot, string uiAction)
    {
        var frontendRoot = Path.Combine(repositoryRoot, "Frontend");
        var doubleQuotedMarker = $"data-ui-action=\"{uiAction}\"";
        var singleQuotedMarker = $"data-ui-action='{uiAction}'";

        return Directory.EnumerateFiles(frontendRoot, "*.*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !IsGeneratedOrBuildPath(path))
            .Any(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains(doubleQuotedMarker, StringComparison.Ordinal)
                    || source.Contains(singleQuotedMarker, StringComparison.Ordinal);
            });
    }

    private static void VerifyEndToEndTest(
        string repositoryRoot,
        string rpcKey,
        ApiCoverageEntry entry,
        List<string> errors)
    {
        var frontendRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "Frontend"));
        var testPath = Path.GetFullPath(Path.Combine(
            frontendRoot,
            entry.E2eTest.Replace('/', Path.DirectorySeparatorChar)));
        var frontendPrefix = frontendRoot + Path.DirectorySeparatorChar;

        if (!testPath.StartsWith(frontendPrefix, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"RPC '{rpcKey}' e2eTest escapes the Frontend directory.");
            return;
        }

        if (!File.Exists(testPath))
        {
            errors.Add($"RPC '{rpcKey}' points to missing E2E test '{entry.E2eTest}'.");
            return;
        }

        var testSource = File.ReadAllText(testPath);
        if (!testSource.Contains(entry.UiAction, StringComparison.Ordinal))
        {
            errors.Add(
                $"RPC '{rpcKey}' E2E test '{entry.E2eTest}' does not exercise UI action '{entry.UiAction}'.");
        }
    }

    private static bool IsGeneratedOrBuildPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.next/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/lib/api/generated/", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateKey(string service, string rpc) => $"{service}/{rpc}";
}

internal sealed class ApiCoverageManifest
{
    public int Version { get; init; }

    public List<ApiCoverageEntry> Apis { get; init; } = [];
}

internal sealed class ApiCoverageEntry
{
    public string Service { get; init; } = string.Empty;

    public string Rpc { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Http { get; init; } = string.Empty;

    public string Permission { get; init; } = string.Empty;

    public string UiRoute { get; init; } = string.Empty;

    public string UiAction { get; init; } = string.Empty;

    public string E2eTest { get; init; } = string.Empty;

    public string SdkTest { get; init; } = string.Empty;

    public string NotApplicableReason { get; init; } = string.Empty;
}

internal sealed record RpcContract(
    string Service,
    string Rpc,
    HttpEndpoint? Http,
    bool IsAdmin);

internal sealed record HttpEndpoint(string Verb, string Path)
{
    public override string ToString() => $"{Verb} {Path}";
}
