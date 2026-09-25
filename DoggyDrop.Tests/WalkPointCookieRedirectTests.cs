using DoggyDrop.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WalkPointCookieRedirectTests
{
    [Theory]
    [InlineData("/Walks/AddPoint/42", "POST", 401)]
    [InlineData("/walks/addpoint/42", "POST", 401)]
    public async Task AddPointChallengeReturnsUnauthorizedWithoutLoginRedirect(string path, string method, int expected)
    {
        var context = CreateContext(path, method);
        await WalkPointCookieRedirects.ToLogin(context);
        Assert.Equal(expected, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task AddPointAccessDeniedReturnsForbiddenWithoutRedirect()
    {
        var context = CreateContext("/Walks/AddPoint/42", "POST");
        await WalkPointCookieRedirects.ToAccessDenied(context);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Theory]
    [InlineData("/Walks/Active/42", "GET")]
    [InlineData("/Walks/Finish/42", "POST")]
    [InlineData("/Walks/AddPoint/42", "GET")]
    public async Task OtherMvcRequestsKeepNormalLoginRedirect(string path, string method)
    {
        var context = CreateContext(path, method);
        await WalkPointCookieRedirects.ToLogin(context);
        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/Identity/Account/Login", context.Response.Headers.Location.ToString());
    }

    private static RedirectContext<CookieAuthenticationOptions> CreateContext(string path, string method)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Request.Method = method;
        return new RedirectContext<CookieAuthenticationOptions>(
            http,
            new AuthenticationScheme(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationProperties(),
            "/Identity/Account/Login");
    }
}
