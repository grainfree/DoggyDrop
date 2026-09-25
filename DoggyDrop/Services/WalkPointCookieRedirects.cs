using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DoggyDrop.Services;

public static class WalkPointCookieRedirects
{
    public static Task ToLogin(RedirectContext<CookieAuthenticationOptions> context) =>
        RedirectOrReject(context, StatusCodes.Status401Unauthorized);

    public static Task ToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) =>
        RedirectOrReject(context, StatusCodes.Status403Forbidden);

    private static Task RedirectOrReject(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.StartsWithSegments("/Walks/AddPoint", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = statusCode;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }
}
