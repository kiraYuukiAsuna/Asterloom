using Asterloom.Modules.Rpc.Requests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RequestDecompression;

namespace Asterloom.Modules.Rpc;

public static class RpcApplicationBuilderExtensions
{
    public static IApplicationBuilder UseAsterloomRpcFoundation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseRequestDecompression();
        return app.UseMiddleware<RequestIdMiddleware>();
    }
}
