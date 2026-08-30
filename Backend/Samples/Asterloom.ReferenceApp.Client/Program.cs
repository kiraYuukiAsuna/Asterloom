using System.Text.Json;
using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Rpc;
using Asterloom.Sdk.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Asterloom.ReferenceApp.Client;

internal static class Program
{
    private static readonly JsonSerializerOptions IndentedJsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [STAThread]
    public static async Task<int> Main(string[] args)
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
                "login" => await RunInteractiveLoginAsync(settings, cancellation.Token),
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

    private static async Task<int> RunServiceCommandAsync(
        ReferenceAppSettings settings,
        bool provision,
        bool json,
        CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAsterloomIdentityClient(options =>
        {
            options.Issuer = settings.PassportIssuer;
            options.ClientId = settings.ServiceClientId;
            options.ClientSecret = settings.ServiceClientSecret;
            options.RegistrationId = "asterloom-reference-service";
            options.EnableServiceCredentials = true;
            options.AllowInsecureHttpForDevelopment = settings.AllowInsecureDevelopment;
        });
        builder.Services.AddSingleton<ReferenceClientInstrumentation>();
        var telemetry = AsterloomTelemetryOptions.FromConfiguration(
            builder.Configuration,
            "asterloom.reference.client",
            typeof(Program).Assembly.GetName().Version?.ToString());
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
                var state = await new ReferenceAppProvisioner(transport.HttpClient)
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

            var current = await ReferenceAppState.LoadAsync(
                settings.StateFile,
                cancellationToken);
            var runner = new ReferenceDiagnosticRunner(
                settings,
                current,
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
            return 0;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
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
        Console.WriteLine("  login               Test interactive Passport authorization-code + PKCE login.");
        Console.WriteLine();
        Console.WriteLine("Required environment variables:");
        Console.WriteLine("  ASTERLOOM_REFERENCE_CLIENT_ID");
        Console.WriteLine("  ASTERLOOM_REFERENCE_CLIENT_SECRET");
        Console.WriteLine("Optional: ASTERLOOM_BASE_URL, ASTERLOOM_ISSUER,");
        Console.WriteLine("  ASTERLOOM_REFERENCE_BACKEND_URL, ASTERLOOM_REFERENCE_BACKEND_GRPC_URL,");
        Console.WriteLine("  ASTERLOOM_REFERENCE_INTERACTIVE_CLIENT_ID, ASTERLOOM_REFERENCE_STATE_FILE.");
    }
}
