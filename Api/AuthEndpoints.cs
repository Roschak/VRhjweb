using HajjVR.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HajjVR.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // Login via form post klasik (agar cookie ter-set sebelum sirkuit Blazor dimulai)
        app.MapPost("/auth/login", async (HttpContext ctx, AuthService auth, AnalyticsService analytics) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var userName = form["username"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();
            if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/')) returnUrl = "/dashboard";

            var user = await auth.ValidateAsync(userName, password);
            if (user is null)
                return Results.Redirect("/login?error=1");

            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                AuthService.ToPrincipal(user),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
            await analytics.LogActivityAsync(user.Id, "Login", $"{user.UserName} masuk");
            return Results.Redirect(returnUrl);
        }).DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        app.MapGet("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        });
    }
}
