using System.Net.Http.Json;
using System.Text.Json;

namespace Asterloom.ReferenceApp.Client;

internal sealed class ReferenceAccountClient : IDisposable
{
    private readonly HttpClient _client;

    public ReferenceAccountClient(Uri baseAddress)
    {
        _client = new HttpClient
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
