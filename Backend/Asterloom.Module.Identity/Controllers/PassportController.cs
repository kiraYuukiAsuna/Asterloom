using System.Net;
using System.Text.Encodings.Web;
using Asterloom.Modules.Identity.Model;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Identity.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public sealed class PassportController(
    IAntiforgery antiforgery,
    SignInManager<AsterloomUser> signInManager,
    UserManager<AsterloomUser> userManager) : Controller
{
    [HttpGet("/passport/login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated is true)
        {
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        return LoginPage(returnUrl, error: null, email: null);
    }

    [HttpPost("/passport/login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(IdentityModule.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login(
        [FromForm] LoginInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Email)
            || string.IsNullOrEmpty(input.Password))
        {
            var copy = PassportCopy.For(IsChinese(input.ReturnUrl));
            return LoginPage(
                input.ReturnUrl,
                copy.RequiredCredentials,
                input.Email);
        }

        var user = await userManager.FindByEmailAsync(input.Email.Trim());
        if (user is null
            || user.Status != AsterloomUserStatus.Active
            || user.ArchivedAt is not null)
        {
            await DelayFailedLoginAsync(cancellationToken);
            return LoginPage(
                input.ReturnUrl,
                PassportCopy.For(IsChinese(input.ReturnUrl)).InvalidCredentials,
                input.Email);
        }

        var result = await signInManager.CheckPasswordSignInAsync(
            user,
            input.Password,
            lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return LoginPage(
                input.ReturnUrl,
                PassportCopy.For(IsChinese(input.ReturnUrl)).InvalidCredentials,
                input.Email);
        }

        await signInManager.SignInAsync(user, input.RememberMe);
        return LoginCompletedPage(SafeReturnUrl(input.ReturnUrl));
    }

    [HttpGet("/passport/denied")]
    public IActionResult AccessDenied()
    {
        var copy = PassportCopy.For(IsChinese(returnUrl: null));
        return Content(
            RenderMessagePage(
                copy.Language,
                copy.AccessDeniedTitle,
                copy.AccessDeniedMessage,
                "/",
                copy.Back),
            "text/html; charset=utf-8");
    }

    private ContentResult LoginPage(string? returnUrl, string? error, string? email)
    {
        var copy = PassportCopy.For(IsChinese(returnUrl));
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XFrameOptions = "DENY";
        Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; " +
            "base-uri 'none'; frame-ancestors 'none'";

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var encodedReturnUrl = WebUtility.HtmlEncode(SafeReturnUrl(returnUrl));
        var encodedEmail = WebUtility.HtmlEncode(email ?? string.Empty);
        var errorMarkup = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"<div class=\"error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</div>";
        var html = $$"""
            <!doctype html>
            <html lang="{{copy.Language}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{copy.LoginTitle}} · Asterloom Passport</title>
              <style>{{Styles}}</style>
            </head>
            <body>
              <main>
                <section class="brand" aria-label="Asterloom Passport">
                  <div class="mark">A</div>
                  <p class="eyebrow">ASTERLOOM PASSPORT</p>
                  <h1>{{copy.HeroTitle}}</h1>
                  <p class="intro">{{copy.HeroDescription}}</p>
                </section>
                <section class="card">
                  <div><p class="eyebrow">SIGN IN</p><h2>{{copy.AccountSignIn}}</h2></div>
                  {{errorMarkup}}
                  <form method="post" action="/passport/login">
                    <input type="hidden" name="__RequestVerificationToken" value="{{HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty)}}">
                    <input type="hidden" name="ReturnUrl" value="{{encodedReturnUrl}}">
                    <label>{{copy.Email}}<input name="Email" type="email" autocomplete="username" required autofocus value="{{encodedEmail}}"></label>
                    <label>{{copy.Password}}<input name="Password" type="password" autocomplete="current-password" required></label>
                    <label class="remember"><input name="RememberMe" type="checkbox" value="true"><span>{{copy.RememberMe}}</span></label>
                    <button type="submit">{{copy.Continue}}</button>
                  </form>
                  <p class="help">{{copy.Help}}</p>
                </section>
              </main>
            </body>
            </html>
            """;

        return Content(html, "text/html; charset=utf-8");
    }

    private ContentResult LoginCompletedPage(string returnUrl)
    {
        var copy = PassportCopy.For(IsChinese(returnUrl));
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XFrameOptions = "DENY";
        Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; " +
            "frame-ancestors 'none'";

        var encodedReturnUrl = WebUtility.HtmlEncode(returnUrl);
        var html = $$"""
            <!doctype html>
            <html lang="{{copy.Language}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta http-equiv="refresh" content="0;url={{encodedReturnUrl}}">
              <title>{{copy.ContinuingTitle}} · Asterloom Passport</title>
              <style>{{Styles}}</style>
            </head>
            <body>
              <main><section class="card message">
                <div class="mark">A</div>
                <h1>{{copy.SuccessTitle}}</h1>
                <p class="intro">{{copy.ReturningToApplication}}</p>
                <a class="button" href="{{encodedReturnUrl}}">{{copy.Continue}}</a>
              </section></main>
            </body>
            </html>
            """;

        return Content(html, "text/html; charset=utf-8");
    }

    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/";

    private bool IsChinese(string? returnUrl)
    {
        string? requestedLocale = null;
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            var queryStart = returnUrl.IndexOf('?', StringComparison.Ordinal);
            if (queryStart >= 0)
            {
                var query = QueryHelpers.ParseQuery(returnUrl[queryStart..]);
                if (query.TryGetValue("ui_locales", out var locales))
                {
                    requestedLocale = locales.FirstOrDefault();
                }
            }
        }

        requestedLocale ??= Request.Cookies["asterloom-locale"];
        requestedLocale ??= Request.Headers.AcceptLanguage.FirstOrDefault();
        return requestedLocale?.Trim().StartsWith("zh", StringComparison.OrdinalIgnoreCase) is true;
    }

    private static async Task DelayFailedLoginAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(80, 161), cancellationToken);
    }

    private static string RenderMessagePage(
        string language,
        string title,
        string message,
        string returnUrl,
        string backLabel) =>
        $$"""
          <!doctype html><html lang="{{WebUtility.HtmlEncode(language)}}"><head><meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{WebUtility.HtmlEncode(title)}} · Asterloom Passport</title>
          <style>{{Styles}}</style></head><body><main><section class="card message">
          <div class="mark">A</div><h1>{{WebUtility.HtmlEncode(title)}}</h1>
          <p class="intro">{{WebUtility.HtmlEncode(message)}}</p>
          <a class="button" href="{{WebUtility.HtmlEncode(returnUrl)}}">{{WebUtility.HtmlEncode(backLabel)}}</a>
          </section></main></body></html>
          """;

    internal const string Styles = """
        :root{color-scheme:dark;font-family:Inter,ui-sans-serif,system-ui,sans-serif;background:#080b10;color:#f6f7f9}*{box-sizing:border-box}body{margin:0;min-height:100vh;background:radial-gradient(circle at 18% 18%,#152b35 0,transparent 36%),radial-gradient(circle at 85% 85%,#2a1728 0,transparent 38%),#080b10}main{min-height:100vh;display:grid;grid-template-columns:minmax(18rem,1fr) minmax(22rem,30rem);gap:5rem;align-items:center;max-width:74rem;margin:auto;padding:3rem}.brand{max-width:36rem}.mark{display:grid;place-items:center;width:3rem;height:3rem;border:1px solid #5eead4;border-radius:.9rem;color:#5eead4;font-weight:700;background:#0d1719;box-shadow:0 0 2rem #2dd4bf22}.eyebrow{font-size:.72rem;letter-spacing:.18em;color:#8a98a8;font-weight:700}.brand .eyebrow{margin-top:2rem}h1{font-size:clamp(2rem,5vw,4.8rem);line-height:1;margin:.8rem 0 1.2rem;letter-spacing:-.05em}.intro{color:#aab4c0;line-height:1.7;max-width:32rem}.card{border:1px solid #26303b;background:#0e131acc;backdrop-filter:blur(18px);border-radius:1.4rem;padding:2rem;box-shadow:0 1.5rem 5rem #0008}.card h2{margin:.35rem 0 1.5rem;font-size:1.7rem}.error{padding:.8rem 1rem;border:1px solid #fb718566;background:#3b1018;color:#fecdd3;border-radius:.7rem;margin:0 0 1rem;font-size:.9rem}form{display:grid;gap:1rem}label{display:grid;gap:.5rem;color:#c7d0da;font-size:.85rem;font-weight:600}input[type=email],input[type=password]{width:100%;border:1px solid #303b48;border-radius:.7rem;background:#090d12;color:#fff;padding:.78rem .9rem;outline:none;font:inherit}input:focus{border-color:#5eead4;box-shadow:0 0 0 3px #2dd4bf20}.remember{display:flex;align-items:center;gap:.55rem;font-weight:400}.remember input{accent-color:#2dd4bf}button,.button{display:block;width:100%;border:0;border-radius:.7rem;padding:.85rem 1rem;background:#5eead4;color:#06201d;font-weight:800;text-align:center;text-decoration:none;cursor:pointer}button:hover,.button:hover{background:#99f6e4}.help{margin:1.2rem 0 0;color:#728090;font-size:.8rem;line-height:1.5}.message{max-width:30rem;text-align:center}.message .mark{margin:auto}.message h1{font-size:2.2rem;margin-top:1.5rem}@media(max-width:760px){main{grid-template-columns:1fr;gap:2rem;padding:1.25rem}.brand h1{font-size:2.8rem}.brand .intro{display:none}.card{padding:1.4rem}}
        """;

    public sealed record LoginInput(
        string Email,
        string Password,
        bool RememberMe,
        string? ReturnUrl);

    private sealed record PassportCopy(
        string Language,
        string LoginTitle,
        string HeroTitle,
        string HeroDescription,
        string AccountSignIn,
        string Email,
        string Password,
        string RememberMe,
        string Continue,
        string Help,
        string RequiredCredentials,
        string InvalidCredentials,
        string ContinuingTitle,
        string SuccessTitle,
        string ReturningToApplication,
        string AccessDeniedTitle,
        string AccessDeniedMessage,
        string Back)
    {
        public static PassportCopy For(bool chinese) => chinese
            ? new(
                "zh-CN",
                "登录",
                "回到你的控制台",
                "一套身份，安全访问 Asterloom 的平台能力。",
                "账户登录",
                "邮箱",
                "密码",
                "在此设备保持登录",
                "继续",
                "账户由你的 Asterloom 管理员创建或邀请。",
                "请输入邮箱和密码。",
                "邮箱或密码不正确。",
                "正在继续",
                "登录成功",
                "正在安全返回你的应用。",
                "访问被拒绝",
                "当前账户没有完成此操作所需的权限。",
                "返回")
            : new(
                "en",
                "Sign in",
                "Return to your console",
                "One identity for secure access to Asterloom platform capabilities.",
                "Account sign in",
                "Email",
                "Password",
                "Keep me signed in on this device",
                "Continue",
                "Your Asterloom administrator creates or invites accounts.",
                "Enter your email and password.",
                "The email or password is incorrect.",
                "Continuing",
                "Signed in",
                "Securely returning to your application.",
                "Access denied",
                "Your account does not have permission to complete this operation.",
                "Back");
    }
}
