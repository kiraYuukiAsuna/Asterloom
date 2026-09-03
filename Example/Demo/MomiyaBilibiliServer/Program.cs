using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Asterloom.Sdk.Authorization;
using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Identity.AspNetCore;
using Asterloom.Sdk.Mail;
using Asterloom.Sdk.Rpc;
using Asterloom.Sdk.Telemetry;
using Asterloom.Targeting;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Momiya.Bilibili.Protocol.V1;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var settings = ServerSettings.Load(builder.Configuration);

builder.Services.AddSingleton(settings);
builder.Services.AddGrpc().AddJsonTranscoding();
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    Required(builder.Configuration.GetConnectionString("MomiyaBilibili"),
        "ConnectionStrings:MomiyaBilibili")));
builder.Services.AddSingleton<SubscriptionStore>();
builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = settings.Issuer;
    options.ClientId = settings.ServiceClientId;
    options.ClientSecret = settings.ServiceClientSecret;
    options.RegistrationId = "momiya-bilibili-server";
    options.EnableServiceCredentials = true;
    options.AllowInsecureHttpForDevelopment = settings.AllowInsecureDevelopment;
});
builder.Services.AddSingleton<AsterloomGateway>();
builder.Services.AddAsterloomResourceServer(options =>
{
    options.Issuer = settings.Issuer;
    options.AuthorizationServer = settings.HttpBaseUrl;
    options.Audience = settings.Audience;
    options.TenantId = settings.TenantId;
    options.ApplicationId = settings.ApplicationId;
    options.AllowInsecureHttpForDevelopment = settings.AllowInsecureDevelopment;
});

var telemetry = AsterloomTelemetryOptions.FromConfiguration(
    builder.Configuration,
    "momiya.bilibili.server",
    typeof(Program).Assembly.GetName().Version?.ToString());
telemetry.EnvironmentName = builder.Environment.EnvironmentName;
telemetry.TenantId = settings.TenantId.ToString("D");
telemetry.ApplicationId = settings.ApplicationId.ToString("D");
telemetry.EnvironmentId = settings.EnvironmentId.ToString("D");
telemetry.ActivitySourceNames.Add(ServerTelemetry.ActivitySourceName);
telemetry.MeterNames.Add(ServerTelemetry.MeterName);
builder.Services.AddAsterloomTelemetry(telemetry);
builder.Logging.AddAsterloomTelemetryLogging(telemetry);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// ponytail: startup DDL keeps the demo self-contained; move it to deployment migrations once the schema evolves.
await app.Services.GetRequiredService<SubscriptionStore>()
    .InitializeAsync(app.Lifetime.ApplicationStopping);

app.MapGrpcService<SubscriptionService>().RequireAuthorization();
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "healthy",
    service = "momiya.bilibili.server",
}));

app.Run();

static string Required(string? value, string key) =>
    !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Configuration '{key}' is required.");

public partial class Program;

internal sealed partial class SubscriptionService(
    SubscriptionStore store,
    AsterloomGateway asterloom,
    ServerSettings settings,
    ILogger<SubscriptionService> logger)
    : MomiyaBilibiliService.MomiyaBilibiliServiceBase
{
    public override async Task<SubscribeReply> Subscribe(
        SubscribeRequest request,
        ServerCallContext context)
    {
        var mid = RequireMid(request.CreatorMid);
        var name = RequireText(request.CreatorName, "creator_name", 100);
        var user = context.GetHttpContext().User;
        var subject = user.FindFirstValue("sub")
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing sub claim."));
        var email = user.FindFirstValue("email")
            ?? throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "The account needs an email address for subscription notifications."));
        var existingCount = await store.CountAsync(subject, context.CancellationToken);
        var attributes = new Dictionary<string, TargetingValue>(StringComparer.Ordinal)
        {
            ["subject.subscriptionCount"] = TargetingValue.From((double)existingCount),
            ["context.emailVerified"] = TargetingValue.From(
                string.Equals(user.FindFirstValue("email_verified"), "true",
                    StringComparison.OrdinalIgnoreCase)),
        };
        var decision = await asterloom.Authorization.CheckAccessAsync(
            subject,
            "bilibili.subscription.write",
            new AsterloomAuthorizationScope(
                settings.TenantId,
                settings.ApplicationId,
                settings.EnvironmentId),
            "bilibili_creator",
            mid,
            attributes,
            context.CancellationToken);
        if (!decision.Allowed)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, decision.Reason));
        }

        using var activity = ServerTelemetry.ActivitySource.StartActivity("subscription.upsert");
        activity?.SetTag("bilibili.creator.mid", mid);
        var stored = await store.UpsertAsync(subject, mid, name, context.CancellationToken);
        var messageId = "subscription:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{subject}:{mid}")));
        var mail = await asterloom.Mail.SendAsync(
            new AsterloomMailMessage(
                settings.SmtpAccountId,
                messageId,
                [email],
                $"已订阅 UP 主：{name}",
                $"你已在 Momiya Bilibili 中订阅 {name}（MID {mid}）。",
                $"<p>你已在 Momiya Bilibili 中订阅 <strong>{WebUtility.HtmlEncode(name)}</strong>"
                    + $"（MID {WebUtility.HtmlEncode(mid)}）。</p>"),
            context.CancellationToken);
        ServerTelemetry.Subscriptions.Add(1);
        LogSubscription(logger, subject, mid);

        return new SubscribeReply
        {
            Subscription = ToProtocol(stored),
            AuthorizationReason = decision.Reason,
            MailStatus = mail.Status.ToString(),
        };
    }

    public override async Task<ListSubscriptionsReply> ListSubscriptions(
        ListSubscriptionsRequest request,
        ServerCallContext context)
    {
        var subject = context.GetHttpContext().User.FindFirstValue("sub")
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing sub claim."));
        var reply = new ListSubscriptionsReply();
        reply.Subscriptions.AddRange(
            (await store.ListAsync(subject, context.CancellationToken)).Select(ToProtocol));
        return reply;
    }

    private static Subscription ToProtocol(StoredSubscription item) => new()
    {
        CreatorMid = item.CreatorMid,
        CreatorName = item.CreatorName,
        CreatedAt = Timestamp.FromDateTimeOffset(item.CreatedAt),
    };

    private static string RequireMid(string value)
    {
        var mid = value.Trim();
        if (mid.Length is 0 or > 20 || !mid.All(char.IsAsciiDigit))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "creator_mid must contain 1-20 digits."));
        }

        return mid;
    }

    private static string RequireText(string value, string field, int maximumLength)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximumLength)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"{field} must contain 1-{maximumLength} characters."));
        }

        return normalized;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "User {Subject} subscribed to Bilibili creator {CreatorMid}.")]
    private static partial void LogSubscription(ILogger logger, string subject, string creatorMid);
}

internal sealed class AsterloomGateway : IDisposable
{
    private readonly AsterloomAuthenticatedTransport _http;
    private readonly AsterloomAuthenticatedTransport _grpc;

    public AsterloomGateway(AsterloomIdentityClient identity, ServerSettings settings)
    {
        Task<string> Token(CancellationToken cancellationToken) =>
            identity.GetServiceAccessTokenAsync(cancellationToken: cancellationToken);

        _http = AsterloomAuthenticatedTransport.Create(
            settings.HttpBaseUrl,
            Token,
            allowInsecureHttpForDevelopment: settings.AllowInsecureDevelopment);
        _grpc = AsterloomAuthenticatedTransport.Create(
            settings.GrpcBaseUrl,
            Token,
            allowInsecureHttpForDevelopment: settings.AllowInsecureDevelopment);
        Authorization = new AsterloomAuthorizationClient(_grpc.CallInvoker);
        Mail = new AsterloomMailClient(
            _http.HttpClient,
            new AsterloomMailScope(settings.TenantId, settings.ApplicationId));
    }

    public AsterloomAuthorizationClient Authorization { get; }

    public AsterloomMailClient Mail { get; }

    public void Dispose()
    {
        _grpc.Dispose();
        _http.Dispose();
    }
}

internal sealed record StoredSubscription(
    string CreatorMid,
    string CreatorName,
    DateTimeOffset CreatedAt);

internal sealed class SubscriptionStore(NpgsqlDataSource dataSource)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE SCHEMA IF NOT EXISTS momiya_bilibili;

            CREATE TABLE IF NOT EXISTS momiya_bilibili.subscriptions (
                subject_id text NOT NULL,
                creator_mid text NOT NULL,
                creator_name text NOT NULL,
                created_at timestamptz NOT NULL,
                PRIMARY KEY (subject_id, creator_mid)
            );
            """;
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> CountAsync(string subject, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM momiya_bilibili.subscriptions
            WHERE subject_id = @subject_id;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("subject_id", subject);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task<StoredSubscription> UpsertAsync(
        string subject,
        string creatorMid,
        string creatorName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO momiya_bilibili.subscriptions (
                subject_id, creator_mid, creator_name, created_at)
            VALUES (@subject_id, @creator_mid, @creator_name, @created_at)
            ON CONFLICT (subject_id, creator_mid) DO UPDATE
                SET creator_name = EXCLUDED.creator_name
            RETURNING creator_mid, creator_name, created_at;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("subject_id", subject);
        command.Parameters.AddWithValue("creator_mid", creatorMid);
        command.Parameters.AddWithValue("creator_name", creatorName);
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Read(reader);
    }

    public async Task<IReadOnlyList<StoredSubscription>> ListAsync(
        string subject,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT creator_mid, creator_name, created_at
            FROM momiya_bilibili.subscriptions
            WHERE subject_id = @subject_id
            ORDER BY created_at DESC;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("subject_id", subject);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<StoredSubscription>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Read(reader));
        }

        return items;
    }

    private static StoredSubscription Read(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetFieldValue<DateTimeOffset>(2));
}

internal sealed record ServerSettings(
    Uri HttpBaseUrl,
    Uri GrpcBaseUrl,
    Uri Issuer,
    bool AllowInsecureDevelopment,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    string Audience,
    string ServiceClientId,
    string ServiceClientSecret,
    Guid SmtpAccountId)
{
    public static ServerSettings Load(IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection("Asterloom");
        return new ServerSettings(
            UriValue(section, "HttpBaseUrl"),
            UriValue(section, "GrpcBaseUrl"),
            UriValue(section, "Issuer"),
            section.GetValue<bool>("AllowInsecureHttpForDevelopment"),
            GuidValue(section, "TenantId"),
            GuidValue(section, "ApplicationId"),
            GuidValue(section, "EnvironmentId"),
            Text(section, "Audience"),
            Text(section, "ServiceClientId"),
            Text(section, "ServiceClientSecret"),
            GuidValue(section, "SmtpAccountId"));
    }

    private static string Text(IConfiguration section, string key) =>
        !string.IsNullOrWhiteSpace(section[key])
            ? section[key]!
            : throw new InvalidOperationException($"Configuration 'Asterloom:{key}' is required.");

    private static Guid GuidValue(IConfiguration section, string key) =>
        Guid.TryParse(Text(section, key), out var value) && value != Guid.Empty
            ? value
            : throw new InvalidOperationException($"Configuration 'Asterloom:{key}' must be a non-empty UUID.");

    private static Uri UriValue(IConfiguration section, string key) =>
        Uri.TryCreate(Text(section, key), UriKind.Absolute, out var value)
            ? value
            : throw new InvalidOperationException($"Configuration 'Asterloom:{key}' must be an absolute URI.");
}

internal static class ServerTelemetry
{
    public const string ActivitySourceName = "Momiya.Bilibili.Server";
    public const string MeterName = "Momiya.Bilibili.Server";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> Subscriptions = Meter.CreateCounter<long>(
        "momiya.bilibili.subscriptions");
}
