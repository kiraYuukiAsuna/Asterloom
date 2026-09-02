namespace Asterloom.ReferenceApp.Backend;

internal static class ReferenceMailEndpoints
{
    public static IEndpointRouteBuilder MapReferenceMailEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/reference/mail/send",
                async (
                    SendReferenceMailRequest request,
                    ReferenceMailGateway gateway,
                    CancellationToken cancellationToken) =>
                {
                    var delivery = await gateway.SendAsync(
                        request.Recipient,
                        request.Subject,
                        request.TextBody,
                        request.HtmlBody,
                        request.ClientMessageId,
                        cancellationToken);
                    return Results.Ok(delivery);
                })
            .RequireAuthorization();
        return endpoints;
    }

    private sealed record SendReferenceMailRequest(
        string Recipient,
        string Subject,
        string TextBody,
        string HtmlBody,
        string ClientMessageId);
}
