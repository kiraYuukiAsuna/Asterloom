using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Release;
using Asterloom.Sdk.Rpc;
using Asterloom.Sdk.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;

namespace Asterloom.ReferenceApp.Client;

internal static class Program
{
    private static readonly JsonSerializerOptions IndentedJsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string? _velopackRestartVersion;

    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build()
            .OnRestarted(version => _velopackRestartVersion = version.ToFullString())
            .Run();

        return MainAsync(args).GetAwaiter().GetResult();
    }

    private static async Task<int> MainAsync(string[] args)
    {
        var command = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "doctor";
        if (command is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var settings = ReferenceAppSettings.Load();
            using var cancellation = CreateCancellationSource();
            return command switch
            {
                "login" => await RunInteractiveLoginAsync(settings, args, cancellation.Token),
                "account-demo" => await RunAccountDemoAsync(
                    settings,
                    args,
                    cancellation.Token),
                "update" => await RunDesktopUpdateAsync(
                    settings,
                    args,
                    cancellation.Token),
                "update-complete" => await CompleteDesktopUpdateAsync(
                    args,
                    cancellation.Token),
                "provision" => await RunServiceCommandAsync(
                    settings,
                    provision: true,
                    json: args.Contains("--json", StringComparer.OrdinalIgnoreCase),
                    cancellation.Token),
                "doctor" => await RunServiceCommandAsync(
                    settings,
                    provision: false,
                    json: args.Contains("--json", StringComparer.OrdinalIgnoreCase),
                    cancellation.Token),
                _ => throw new ArgumentException(
                    $"Unknown command '{command}'. Use help to list commands."),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Reference app operation was cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Reference app failed: {exception.Message}");
            return 2;
        }
    }

    private static async Task<int> RunDesktopUpdateAsync(
        ReferenceAppSettings settings,
        string[] args,
        CancellationToken cancellationToken)
    {
        var resultFile = Path.GetFullPath(RequireArgument(args, 1, "result file"));
        var forceFull = args.Contains("--force-full", StringComparer.OrdinalIgnoreCase);
        var state = await ReferenceAppState.LoadAsync(settings.StateFile, cancellationToken);
        var serviceCredentials = settings.RequireServiceCredentials();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAsterloomIdentityClient(options =>
        {
            options.Issuer = settings.PassportIssuer;
            options.ClientId = serviceCredentials.ClientId;
            options.ClientSecret = serviceCredentials.ClientSecret;
            options.RegistrationId = "asterloom-reference-updater";
            options.EnableServiceCredentials = true;
            options.AllowInsecureHttpForDevelopment = settings.AllowInsecureDevelopment;
        });
        using var host = builder.Build();
        await host.StartAsync(cancellationToken);
        try
        {
            var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
            await identity.GetServiceAccessTokenAsync(cancellationToken: cancellationToken);
            using var transport = AsterloomAuthenticatedTransport.Create(
                settings.AsterloomBaseAddress,
                identity.GetAccessTokenAsync,
                allowInsecureHttpForDevelopment: settings.AllowInsecureDevelopment);
            var scope = new AsterloomReleaseScope(
                state.TenantId,
                state.ApplicationId,
                state.EnvironmentId);
            using var releaseClient = new AsterloomReleaseClient(
                transport.HttpClient,
                new AsterloomReleaseClientOptions
                {
                    Scope = scope,
                    TargetRuntimeId = state.ReleaseRuntimeId,
                    PackageId = state.ReleasePackageId,
                    TrustedPublicKeysByFingerprint = new Dictionary<string, string>
                    {
                        [state.ReleaseSigningKeyFingerprint] = state.ReleasePublicKeyPem,
                    },
                    AllowInsecureDownloadUrls = settings.AllowInsecureDevelopment,
                });
            var downloadedAssets = new ConcurrentQueue<VelopackAsset>();
            var updateSource = new AsterloomVelopackUpdateSource(
                releaseClient,
                currentVersion => AsterloomReleaseContext.Create(
                    scope,
                    "reference-installed-update-client",
                    clientVersion: currentVersion,
                    platform: state.ReleaseRuntimeId,
                    region: "CN"),
                downloadedAssets.Enqueue);
            var updateManager = new UpdateManager(
                updateSource,
                new UpdateOptions
                {
                    ExplicitChannel = state.ReleaseChannelKey,
                    MaximumDeltasBeforeFallback = forceFull ? -1 : 10,
                });
            if (!updateManager.IsInstalled || updateManager.CurrentVersion is null)
            {
                throw new InvalidOperationException(
                    "The update command must run from a real Velopack installation.");
            }

            var currentVersion = updateManager.CurrentVersion.ToFullString();
            var update = await updateManager.CheckForUpdatesAsync();
            if (update is null)
            {
                throw new InvalidOperationException(
                    $"No update was offered to installed version {currentVersion}.");
            }

            await updateManager.DownloadUpdatesAsync(
                update,
                progress => Console.WriteLine($"Update download: {progress}%"),
                cancellationToken);
            var downloadedKinds = downloadedAssets
                .Select(static asset => asset.Type.ToString())
                .ToArray();
            if (forceFull)
            {
                if (!downloadedAssets.Any(static asset => asset.Type == VelopackAssetType.Full)
                    || downloadedAssets.Any(static asset => asset.Type == VelopackAssetType.Delta))
                {
                    throw new InvalidOperationException(
                        "The forced Full path did not exclusively download the Full package.");
                }
            }
            else if (state.ReleaseHasDelta)
            {
                if (update.DeltasToTarget.Length == 0
                    || !downloadedAssets.Any(static asset => asset.Type == VelopackAssetType.Delta)
                    || downloadedAssets.Any(static asset => asset.Type == VelopackAssetType.Full))
                {
                    throw new InvalidOperationException(
                        "Velopack did not complete the update through the expected Delta-only download path.");
                }
            }

            await WriteJsonAsync(
                resultFile,
                new
                {
                    completed = false,
                    mode = forceFull ? "full" : "delta",
                    currentVersion,
                    targetVersion = update.TargetFullRelease.Version.ToFullString(),
                    offeredDeltaCount = update.DeltasToTarget.Length,
                    downloadedKinds,
                    downloadedFiles = downloadedAssets.Select(static asset => asset.FileName).ToArray(),
                    downloadedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken);

            Console.WriteLine(
                $"Applying {currentVersion} -> {update.TargetFullRelease.Version.ToFullString()} "
                + $"through the {(forceFull ? "Full" : "Delta")} path.");
            await host.StopAsync(CancellationToken.None);
            updateManager.ApplyUpdatesAndRestart(
                update.TargetFullRelease,
                ["update-complete", resultFile, update.TargetFullRelease.Version.ToFullString()]);
            return 0;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<int> CompleteDesktopUpdateAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        var resultFile = Path.GetFullPath(RequireArgument(args, 1, "result file"));
        var expectedVersion = RequireArgument(args, 2, "expected version");
        using var previousDocument = JsonDocument.Parse(
            await File.ReadAllTextAsync(resultFile, cancellationToken));
        var actualVersion = typeof(Program).Assembly.GetName().Version?.ToString(3)
            ?? throw new InvalidOperationException("The running application version is unavailable.");
        if (!string.Equals(actualVersion, expectedVersion, StringComparison.Ordinal)
            || !string.Equals(_velopackRestartVersion, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Velopack restarted version '{_velopackRestartVersion ?? "unknown"}', "
                + $"assembly '{actualVersion}', expected '{expectedVersion}'.");
        }

        await WriteJsonAsync(
            resultFile,
            new
            {
                completed = true,
                expectedVersion,
                actualVersion,
                velopackRestartVersion = _velopackRestartVersion,
                update = previousDocument.RootElement.Clone(),
                completedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken);
        Console.WriteLine($"Velopack update and restart completed at {actualVersion}.");
        return 0;
    }

    private static async Task WriteJsonAsync(
        string path,
        object value,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, IndentedJsonOptions),
            cancellationToken);
    }

    private static async Task<int> RunServiceCommandAsync(
        ReferenceAppSettings settings,
        bool provision,
        bool json,
        CancellationToken cancellationToken)
    {
        var serviceCredentials = settings.RequireServiceCredentials();
        var current = provision
            ? null
            : await ReferenceAppState.LoadAsync(settings.StateFile, cancellationToken);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAsterloomIdentityClient(options =>
        {
            options.Issuer = settings.PassportIssuer;
            options.ClientId = serviceCredentials.ClientId;
            options.ClientSecret = serviceCredentials.ClientSecret;
            options.RegistrationId = "asterloom-reference-service";
            options.EnableServiceCredentials = true;
            options.AllowInsecureHttpForDevelopment = settings.AllowInsecureDevelopment;
        });
        builder.Services.AddSingleton<ReferenceClientInstrumentation>();
        var telemetry = AsterloomTelemetryOptions.FromConfiguration(
            builder.Configuration,
            "asterloom.reference.client",
            typeof(Program).Assembly.GetName().Version?.ToString());
        if (current is not null)
        {
            telemetry.TenantId = current.TenantId.ToString("D");
            telemetry.ApplicationId = current.ApplicationId.ToString("D");
            telemetry.EnvironmentId = current.EnvironmentId.ToString("D");
        }
        telemetry.ActivitySourceNames.Add(ReferenceClientInstrumentation.ActivitySourceName);
        telemetry.MeterNames.Add(ReferenceClientInstrumentation.MeterName);
        builder.Services.AddAsterloomTelemetry(telemetry);
        builder.Logging.AddAsterloomTelemetryLogging(telemetry);

        using var host = builder.Build();
        await host.StartAsync(cancellationToken);
        try
        {
            var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
            await identity.GetServiceAccessTokenAsync(cancellationToken: cancellationToken);
            using var transport = AsterloomAuthenticatedTransport.Create(
                settings.AsterloomBaseAddress,
                identity.GetAccessTokenAsync,
                allowInsecureHttpForDevelopment: settings.AllowInsecureDevelopment);
            if (provision)
            {
                var state = await new ReferenceAppProvisioner(
                        transport.HttpClient,
                        settings.DesktopRelease)
                    .ProvisionAsync(cancellationToken);
                await state.SaveAsync(settings.StateFile, cancellationToken);
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(
                        new
                        {
                            provisioned = true,
                            state.RunId,
                            state.TenantId,
                            state.ApplicationId,
                            state.EnvironmentId,
                            state.ProvisionedAt,
                            stateFile = settings.StateFile,
                        },
                        IndentedJsonOptions));
                }
                else
                {
                    Console.WriteLine("Reference data provisioned successfully.");
                    Console.WriteLine($"  Run:         {state.RunId}");
                    Console.WriteLine($"  Tenant:      {state.TenantId:D}");
                    Console.WriteLine($"  Application: {state.ApplicationId:D}");
                    Console.WriteLine($"  Environment: {state.EnvironmentId:D}");
                    Console.WriteLine($"  State:       {settings.StateFile}");
                }

                return 0;
            }

            var runner = new ReferenceDiagnosticRunner(
                settings,
                current!,
                transport,
                host.Services.GetRequiredService<ReferenceClientInstrumentation>(),
                host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                    .CreateLogger<ReferenceDiagnosticRunner>());
            var results = await runner.RunAsync(cancellationToken);
            PrintResults(results, json);
            return results.All(static result => result.Succeeded) ? 0 : 1;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<int> RunInteractiveLoginAsync(
        ReferenceAppSettings settings,
        string[] args,
        CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAsterloomIdentityClient(options =>
        {
            options.Issuer = settings.PassportIssuer;
            options.ClientId = settings.InteractiveClientId;
            options.RegistrationId = "asterloom-reference-native";
            options.EnableInteractiveAuthentication = true;
            options.RequestRefreshTokens = true;
            options.Scopes.Add(settings.InteractiveApiScope);
            options.AllowInsecureHttpForDevelopment = settings.AllowInsecureDevelopment;
        });
        using var host = builder.Build();
        await host.StartAsync(cancellationToken);
        try
        {
            Console.WriteLine("Opening Passport in the system browser…");
            var tokens = await host.Services.GetRequiredService<AsterloomIdentityClient>()
                .SignInAsync(cancellationToken: cancellationToken);
            var subject = tokens.Principal.FindFirst("sub")?.Value ?? "unknown";
            var name = tokens.Principal.Identity?.Name ?? subject;
            Console.WriteLine($"Passport login succeeded: {name} ({subject}).");
            Console.WriteLine($"Access token expires at: {tokens.AccessTokenExpiresAt:O}");
            Console.WriteLine(tokens.RefreshToken is null
                ? "No refresh token was returned."
                : "Refresh token flow is available.");
            using var apiClient = new HttpClient
            {
                BaseAddress = settings.ReferenceBackendAddress,
            };
            apiClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            using var response = await apiClient.GetAsync(
                "api/reference/me",
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"The reference business API rejected the user access token with HTTP {(int)response.StatusCode}: {body}");
            }

            Console.WriteLine("Business API accepted the same user Access Token:");
            using var document = JsonDocument.Parse(body);
            Console.WriteLine(JsonSerializer.Serialize(
                document.RootElement,
                IndentedJsonOptions));
            if (args.Contains("--authorization-demo", StringComparer.OrdinalIgnoreCase))
            {
                await RunBusinessAuthorizationDemoAsync(apiClient, cancellationToken);
            }

            return 0;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static async Task RunBusinessAuthorizationDemoAsync(
        HttpClient apiClient,
        CancellationToken cancellationToken)
    {
        const string orderId = "reference-order-42";
        using var fixtureResponse = await apiClient.PostAsJsonAsync(
            "api/reference/authorization/fixture",
            new { department = "finance", orderId, amount = 1200D },
            cancellationToken);
        var fixtureBody = await fixtureResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!fixtureResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Authorization fixture failed with HTTP {(int)fixtureResponse.StatusCode}: {fixtureBody}");
        }

        using var refundResponse = await apiClient.PostAsync(
            $"api/reference/authorization/orders/{orderId}/refund",
            content: null,
            cancellationToken);
        var refundBody = await refundResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!refundResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"RBAC/ACL/ABAC refund check failed with HTTP {(int)refundResponse.StatusCode}: {refundBody}");
        }

        Console.WriteLine("Business backend RBAC/ACL/ABAC decision succeeded:");
        using var refundDocument = JsonDocument.Parse(refundBody);
        Console.WriteLine(JsonSerializer.Serialize(
            refundDocument.RootElement,
            IndentedJsonOptions));
    }

    private static async Task<int> RunAccountDemoAsync(
        ReferenceAppSettings settings,
        string[] args,
        CancellationToken cancellationToken)
    {
        var email = RequireArgument(args, 1, "email");
        var displayName = RequireArgument(args, 2, "display name");
        var password = RequireEnvironment("ASTERLOOM_REFERENCE_ACCOUNT_PASSWORD");
        using var accounts = new ReferenceAccountClient(settings.ReferenceBackendAddress);
        var registration = await accounts.RegisterAsync(
            email,
            displayName,
            password,
            cancellationToken);
        Console.WriteLine("Registration result:");
        Console.WriteLine(JsonSerializer.Serialize(registration, IndentedJsonOptions));
        var token = registration.TryGetProperty("emailVerificationToken", out var tokenProperty)
            ? tokenProperty.GetString()
            : null;
        if (registration.GetProperty("verificationRequired").GetBoolean())
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(
                    "Email confirmation is required. Configure the business backend's email delivery, "
                    + "or enable ExposeEmailVerificationToken for a local-only demo.");
            }

            await accounts.ConfirmEmailAsync(email, token, cancellationToken);
            Console.WriteLine("Email confirmed through the business backend.");
        }

        Console.WriteLine(
            "Account provisioning completed. Run the login command to authenticate "
            + "with Authorization Code + S256 PKCE and call the protected business API.");
        return 0;
    }

    private static void PrintResults(IReadOnlyList<DiagnosticResult> results, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    succeeded = results.All(static result => result.Succeeded),
                    passed = results.Count(static result => result.Succeeded),
                    failed = results.Count(static result => !result.Succeeded),
                    results,
                },
                IndentedJsonOptions));
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Asterloom full-capability diagnostics");
        Console.WriteLine(new string('-', 92));
        foreach (var result in results)
        {
            var outcome = result.Succeeded ? "PASS" : "FAIL";
            Console.WriteLine($"{outcome,-4} {result.Capability,-28} {result.DurationMilliseconds,6} ms");
            Console.WriteLine("     " + (result.Succeeded
                ? result.Detail
                : $"[{result.ErrorCode}] {result.Error}"));
        }

        Console.WriteLine(new string('-', 92));
        Console.WriteLine(
            $"Passed {results.Count(static item => item.Succeeded)}/{results.Count}; "
            + $"failed {results.Count(static item => !item.Succeeded)}.");
    }

    private static CancellationTokenSource CreateCancellationSource()
    {
        var source = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            source.Cancel();
        };
        return source;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Asterloom reference client");
        Console.WriteLine();
        Console.WriteLine("  provision [--json]  Create complete platform test data and save local state.");
        Console.WriteLine("  doctor [--json]     Execute all capability diagnostics independently.");
        Console.WriteLine("  login [--authorization-demo]");
        Console.WriteLine("                      Sign in with PKCE and optionally test backend RBAC/ACL/ABAC.");
        Console.WriteLine("  account-demo EMAIL NAME");
        Console.WriteLine("                      Register and confirm an account through the sample business backend.");
        Console.WriteLine("  update RESULT_FILE [--force-full]");
        Console.WriteLine("                      Download, apply, restart, and prove an installed Velopack update.");
        Console.WriteLine();
        Console.WriteLine("Required environment variables:");
        Console.WriteLine("  ASTERLOOM_REFERENCE_CLIENT_ID (service commands only)");
        Console.WriteLine("  ASTERLOOM_REFERENCE_CLIENT_SECRET (service commands only)");
        Console.WriteLine("  ASTERLOOM_REFERENCE_ACCOUNT_PASSWORD (account commands only)");
        Console.WriteLine("Optional: ASTERLOOM_BASE_URL, ASTERLOOM_ISSUER,");
        Console.WriteLine("  ASTERLOOM_REFERENCE_BACKEND_URL, ASTERLOOM_REFERENCE_BACKEND_GRPC_URL,");
        Console.WriteLine("  ASTERLOOM_REFERENCE_INTERACTIVE_CLIENT_ID, ASTERLOOM_REFERENCE_API_SCOPE,");
        Console.WriteLine("  ASTERLOOM_REFERENCE_STATE_FILE.");
    }

    private static string RequireArgument(string[] args, int index, string name) =>
        args.ElementAtOrDefault(index)?.Trim() is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"The {name} argument is required.");

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Environment variable {name} is required.");
}
