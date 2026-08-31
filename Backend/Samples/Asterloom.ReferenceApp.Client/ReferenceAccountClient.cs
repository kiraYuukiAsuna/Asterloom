using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Asterloom.ReferenceApp.Client;

internal sealed class ReferenceAccountClient : IDisposable
{
    private readonly HttpClient _client;

    public ReferenceAccountClient(Uri baseAddress)
    {
        _client = new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        })
        {
            BaseAddress = baseAddress,
        };
    }

    public async Task<JsonElement> RegisterAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync(
            "api/reference/account/register",
            new { email, displayName, password },
            cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<JsonElement> ConfirmEmailAsync(
        string email,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync(
            "api/reference/account/confirm-email",
            new { email, token },
            cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<JsonElement> LoginAndReadSessionAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using (var login = await _client.PostAsJsonAsync(
            "api/reference/account/login",
            new { email, password },
            cancellationToken))
        {
            await ReadAsync(login, cancellationToken);
        }

        using var current = await _client.GetAsync(
            "api/reference/account/me",
            cancellationToken);
        return await ReadAsync(current, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsync(
            "api/reference/account/logout",
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _client.Dispose();

    private static async Task<JsonElement> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Reference account API returned {(int)response.StatusCode}: {content}");
        }

        return JsonSerializer.Deserialize<JsonElement>(content);
    }
}
