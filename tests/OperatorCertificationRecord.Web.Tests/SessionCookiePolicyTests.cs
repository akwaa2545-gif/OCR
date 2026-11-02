using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using OperatorCertificationRecord.Web.Services.Authentication;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class SessionCookiePolicyTests
{
    [Theory]
    [InlineData("Production", null, "http", true)]
    [InlineData("Production", "false", "http", true)]
    [InlineData("Production", "true", "http", false)]
    [InlineData("Production", "true", "https", true)]
    [InlineData("Staging", null, "http", true)]
    [InlineData("Development", null, "http", false)]
    [InlineData("Development", null, "https", true)]
    public void CookiesRequireHttpsUnlessHttpSessionsAreExplicitlyAllowed(
        string environment, string? allowHttp, string scheme, bool secure)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Authentication:AllowHttpSession"] = allowHttp }).Build();
        var options = new SessionOptions();
        SessionCookiePolicy.Configure(options, new HostingEnvironment { EnvironmentName = environment }, configuration);
        var http = new DefaultHttpContext();
        http.Request.Scheme = scheme;

        var cookie = options.Cookie.Build(http);

        Assert.Equal(secure, cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.IsEssential);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
    }
}
