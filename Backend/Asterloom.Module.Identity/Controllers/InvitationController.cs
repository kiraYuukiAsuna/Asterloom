using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using Asterloom.Modules.Identity.Management;
using Asterloom.Modules.Identity.Model;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Identity.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InvitationController(
    IAntiforgery antiforgery,
    UserManager<AsterloomUser> userManager,
    TimeProvider timeProvider) : Controller
{
    [HttpGet("/passport/invitation")]
    public async Task<IActionResult> Invitation(
        [FromQuery] string? userId,
        [FromQuery] string? token)
    {
        var user = await FindPendingUserAsync(userId);
        if (user is null || !TryDecodeToken(token, out _))
        {
            return MessagePage(
                "邀请不可用",
                "邀请链接无效、已过期，或账户已完成激活。请联系管理员重新发送邀请。",
                isError: true);
        }

        return InvitationPage(user, token!, error: null);
    }

    [HttpPost("/passport/invitation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(
        [FromForm] InvitationInput input,
        CancellationToken cancellationToken)
    {
        var user = await FindPendingUserAsync(input.UserId);
        if (user is null || !TryDecodeToken(input.Token, out var decodedToken))
        {
            return MessagePage(
                "邀请不可用",
                "邀请链接无效、已过期，或账户已完成激活。请联系管理员重新发送邀请。",
                isError: true);
        }

        if (string.IsNullOrEmpty(input.Password)
            || !string.Equals(input.Password, input.ConfirmPassword, StringComparison.Ordinal))
        {
            return InvitationPage(user, input.Token, "两次输入的密码不一致。");
        }

        if (!await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<AsterloomUser>.ConfirmEmailTokenPurpose,
            decodedToken))
        {
            return MessagePage(
                "邀请已过期",
                "该邀请已失效，请联系管理员重新发送邀请。",
                isError: true);
        }

        user.EmailConfirmed = true;
        user.Status = AsterloomUserStatus.Active;
        user.Version++;
        user.UpdatedAt = timeProvider.GetUtcNow();
        var passwordResult = await userManager.AddPasswordAsync(user, input.Password);
        if (!passwordResult.Succeeded)
        {
            user.EmailConfirmed = false;
            user.Status = AsterloomUserStatus.Pending;
            user.Version--;
            return InvitationPage(
                user,
                input.Token,
                string.Join(" ", passwordResult.Errors.Select(static error => error.Description)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return MessagePage(
            "账户已激活",
            "密码设置成功。现在可以返回 Asterloom 控制台登录。",
            isError: false);
    }

    private async Task<AsterloomUser?> FindPendingUserAsync(string? userId)
    {
        if (!Guid.TryParse(userId, out var parsed) || parsed == Guid.Empty)
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(parsed.ToString("D"));
        return user is
        {
            Status: AsterloomUserStatus.Pending,
            EmailConfirmed: false,
            ArchivedAt: null,
        }
            ? user
            : null;
    }

    private ContentResult InvitationPage(
        AsterloomUser user,
        string token,
        string? error)
    {
        SetSecurityHeaders();
        var antiforgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken
            ?? string.Empty;
        var errorMarkup = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"<div class=\"error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</div>";
        var html = $$"""
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>接受邀请 · Asterloom Passport</title>
            <style>{{PassportController.Styles}}</style></head><body><main>
            <section class="brand"><div class="mark">A</div><p class="eyebrow">ASTERLOOM PASSPORT</p>
            <h1>加入 Asterloom</h1><p class="intro">完成密码设置后，这个邀请账户即可用于登录。</p></section>
            <section class="card"><p class="eyebrow">ACCEPT INVITATION</p><h2>激活账户</h2>
            <p class="help">{{WebUtility.HtmlEncode(user.DisplayName)}} · {{WebUtility.HtmlEncode(user.Email)}}</p>
            {{errorMarkup}}<form method="post" action="/passport/invitation">
            <input type="hidden" name="__RequestVerificationToken" value="{{HtmlEncoder.Default.Encode(antiforgeryToken)}}">
            <input type="hidden" name="UserId" value="{{user.Id:D}}">
            <input type="hidden" name="Token" value="{{HtmlEncoder.Default.Encode(token)}}">
            <label>新密码<input name="Password" type="password" autocomplete="new-password" minlength="12" required autofocus></label>
            <label>确认密码<input name="ConfirmPassword" type="password" autocomplete="new-password" minlength="12" required></label>
            <button type="submit">激活账户</button></form>
            <p class="help">密码至少 12 位，并包含大小写字母、数字和特殊字符。</p>
            </section></main></body></html>
            """;
        return Content(html, "text/html; charset=utf-8");
    }

    private ContentResult MessagePage(string title, string message, bool isError)
    {
        SetSecurityHeaders();
        var accent = isError ? "#fb7185" : "#5eead4";
        var html = $$"""
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}} · Asterloom Passport</title>
            <style>{{PassportController.Styles}}.message h1{color:{{accent}}}</style></head><body><main>
            <section class="card message"><div class="mark">A</div>
            <h1>{{WebUtility.HtmlEncode(title)}}</h1><p class="intro">{{WebUtility.HtmlEncode(message)}}</p>
            <a class="button" href="/passport/login">前往登录</a></section></main></body></html>
            """;
        return Content(html, "text/html; charset=utf-8");
    }

    private void SetSecurityHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XFrameOptions = "DENY";
        Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; " +
            "base-uri 'none'; frame-ancestors 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private static bool TryDecodeToken(string? encoded, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
            return !string.IsNullOrWhiteSpace(token);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public sealed record InvitationInput(
        string UserId,
        string Token,
        string Password,
        string ConfirmPassword);
}
