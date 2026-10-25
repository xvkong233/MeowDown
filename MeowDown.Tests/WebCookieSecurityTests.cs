using Microsoft.AspNetCore.Http;

namespace MeowDown.Tests;

public class WebCookieSecurityTests
{
    [Fact]
    public void SecureCookie_RequiredWhenLaunchFlagSet()
    {
        var context = new DefaultHttpContext();

        Assert.True(WebApiEndpoints.ShouldUseSecureCookie(context, new WebLaunchOptions { WebSecureCookie = true }));
    }

    [Theory]
    [InlineData("https", true)]
    [InlineData("HTTPS", true)]
    [InlineData("http", false)]
    public void SecureCookie_HonorsForwardedProtoFromReverseProxy(string proto, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-Proto"] = proto;

        Assert.Equal(expected, WebApiEndpoints.ShouldUseSecureCookie(context, new WebLaunchOptions()));
    }

    [Fact]
    public void SecureCookie_OffByDefaultForDirectHttp()
    {
        Assert.False(WebApiEndpoints.ShouldUseSecureCookie(new DefaultHttpContext(), new WebLaunchOptions()));
    }

    [Fact]
    public void SessionCookie_IsHttpOnlyStrictPathRootedWithSlidingMaxAge()
    {
        var options = WebApiEndpoints.SessionCookieOptions(
            new DefaultHttpContext(), new WebLaunchOptions(), maxAgeSeconds: 604800);

        Assert.True(options.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, options.SameSite);
        Assert.Equal("/", options.Path);
        Assert.Equal(TimeSpan.FromSeconds(604800), options.MaxAge);
        Assert.False(options.Secure);
    }

    [Fact]
    public void RoleName_MapsRolesToContractStrings()
    {
        Assert.Equal("user", WebApiEndpoints.RoleName(WebUserRole.User));
        Assert.Equal("admin", WebApiEndpoints.RoleName(WebUserRole.Admin));
    }
}
