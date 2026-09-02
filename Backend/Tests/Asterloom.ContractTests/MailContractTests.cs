using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Modules.Mail.Transport;
using Asterloom.Sdk.Mail;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class MailContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebApplicationFactory<Program> _factory;

    public MailContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMailTransport>();
            services.AddSingleton<IMailTransport, RecordingMailTransport>();
        }));
    }

    [Fact]
    public async Task JsonTranscodingAndSdkCoverMailAccountsAndDelivery()
    {
        using var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            "/api/v1/tenants",
            new { slug = "mail-" + suffix, displayName = "Mail Tenant" }));
        var application = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications",
            new { slug = "mailer", displayName = "Mailer" }));
        var basePath =
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/mail";

        var account = await SendAsync<AccountJson>(client.PostAsJsonAsync(
            $"{basePath}/accounts",
            new
            {
                name = "QQ notifications",
                host = "smtp.qq.com",
                port = 465,
                security = "SMTP_SECURITY_SSL_ON_CONNECT",
                username = "mailer@qq.com",
                smtpPassword = "qq-smtp-authorization-code",
                fromAddress = "mailer@qq.com",
                fromName = "Asterloom Mail Tests",
            }));
        Assert.DoesNotContain("smtpPassword", JsonSerializer.Serialize(account), StringComparison.OrdinalIgnoreCase);

        var accounts = await client.GetFromJsonAsync<AccountListJson>($"{basePath}/accounts");
        Assert.Contains(accounts!.Accounts, item => item.Id == account.Id);
        var fetched = await client.GetFromJsonAsync<AccountJson>(
            $"{basePath}/accounts/{account.Id}");
        Assert.Equal("smtp.qq.com", fetched!.Host);

        account = await SendAsync<AccountJson>(client.PatchAsJsonAsync(
            $"{basePath}/accounts/{account.Id}",
            new
            {
                name = "QQ application mail",
                host = account.Host,
                port = account.Port,
                security = account.Security,
                username = account.Username,
                smtpPassword = "",
                fromAddress = account.FromAddress,
                fromName = "Asterloom",
                expectedVersion = account.Version,
            }));
        Assert.Equal(2, account.Version);

        var testDelivery = await SendAsync<DeliveryJson>(client.PostAsJsonAsync(
            $"{basePath}/accounts/{account.Id}:test",
            new { recipient = "recipient@example.com" }));
        Assert.Equal("MAIL_DELIVERY_STATUS_SENT", testDelivery.Status);

        var sdk = new AsterloomMailClient(
            client,
            new AsterloomMailScope(Guid.Parse(tenant.Id), Guid.Parse(application.Id)));
        var clientMessageId = "order:" + Guid.NewGuid().ToString("N");
        var delivery = await sdk.SendAsync(new AsterloomMailMessage(
            Guid.Parse(account.Id),
            clientMessageId,
            ["customer@example.com"],
            "Order confirmed",
            TextBody: "Your order is confirmed.",
            HtmlBody: "<strong>Your order is confirmed.</strong>",
            Cc: ["audit@example.com"],
            ReplyTo: "support@example.com"));
        Assert.Equal(AsterloomMailDeliveryStatus.Sent, delivery.Status);

        var repeated = await sdk.SendAsync(new AsterloomMailMessage(
            Guid.Parse(account.Id),
            clientMessageId,
            ["customer@example.com"],
            "Order confirmed",
            TextBody: "Your order is confirmed."));
        Assert.Equal(delivery.Id, repeated.Id);

        var deliveries = await client.GetFromJsonAsync<DeliveryListJson>(
            $"{basePath}/deliveries?pageSize=20");
        Assert.Contains(deliveries!.Deliveries, item => item.Id == delivery.Id.ToString("D"));
        var fetchedDelivery = await client.GetFromJsonAsync<DeliveryJson>(
            $"{basePath}/deliveries/{delivery.Id:D}");
        Assert.Equal(clientMessageId, fetchedDelivery!.ClientMessageId);

        account = await SendAsync<AccountJson>(client.DeleteAsync(
            $"{basePath}/accounts/{account.Id}?expectedVersion={account.Version}"));
        Assert.Equal("MAIL_ACCOUNT_STATUS_ARCHIVED", account.Status);
        account = await SendAsync<AccountJson>(client.PostAsJsonAsync(
            $"{basePath}/accounts/{account.Id}:restore",
            new { expectedVersion = account.Version }));
        Assert.Equal("MAIL_ACCOUNT_STATUS_ACTIVE", account.Status);
    }

    private static async Task<T> SendAsync<T>(Task<HttpResponseMessage> responseTask)
    {
        using var response = await responseTask;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("The JSON response was empty.");
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        const string clientId = "mail-contract-tests";
        const string clientSecret = "Mail-Contract-Tests-Secret!2026";
        using (var scope = _factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            if (await manager.FindByClientIdAsync(clientId) is null)
            {
                await manager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    ClientType = ClientTypes.Confidential,
                    DisplayName = "Mail contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }

            var store = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
            var bindingId = Guid.Parse("abababab-abab-7bab-8bab-abababababab");
            if (await store.GetRoleBindingAsync(bindingId, CancellationToken.None) is null)
            {
                var management = scope.ServiceProvider.GetRequiredService<AuthorizationManagementService>();
                await management.SetRoleBindingAsync(
                    bindingId.ToString(),
                    clientId,
                    AuthorizationCatalog.FindSystemRole("super-administrator")!.Id.ToString(),
                    AuthorizationScope.Global,
                    0,
                    CancellationToken.None);
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

    private sealed class RecordingMailTransport : IMailTransport
    {
        public Task<string> SendAsync(
            SmtpTransportAccount account,
            MailTransportMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("qq-smtp-authorization-code", account.Password);
            Assert.NotEmpty(message.To);
            return Task.FromResult($"<{Guid.NewGuid():N}@asterloom.test>");
        }
    }

    private sealed record ResourceJson(string Id);

    private sealed record AccountJson(
        string Id,
        string Host,
        int Port,
        string Security,
        string Username,
        string FromAddress,
        string Status,
        long Version);

    private sealed record AccountListJson(IReadOnlyList<AccountJson> Accounts);

    private sealed record DeliveryJson(
        string Id,
        string ClientMessageId,
        string Status);

    private sealed record DeliveryListJson(IReadOnlyList<DeliveryJson> Deliveries);
}
