using System.Security.Claims;
using Financials.Infrastructure.Common;
using Microsoft.AspNetCore.Http;

namespace Financials.Infrastructure.Tests.Common;

public class ClaimsPermissionServiceTests
{
    private static HttpContextAccessor AccessorFor(params string[] permissions)
    {
        var claims = permissions.Select(p => new Claim("permissions", p));
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    [Fact]
    public void Returns_true_when_permission_present_in_claims()
    {
        var accessor = AccessorFor("financials.projects.confirm", "financials.projects.read");
        var sut = new ClaimsPermissionService(accessor);

        sut.Has("financials.projects.confirm").Should().BeTrue();
        sut.Has("financials.projects.read").Should().BeTrue();
    }

    [Fact]
    public void Returns_false_when_permission_absent()
    {
        var accessor = AccessorFor("financials.projects.read");
        var sut = new ClaimsPermissionService(accessor);

        sut.Has("financials.projects.confirm").Should().BeFalse();
    }

    [Fact]
    public void Returns_false_when_user_unauthenticated()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) },
        };
        var sut = new ClaimsPermissionService(accessor);

        sut.Has("financials.projects.confirm").Should().BeFalse();
    }

    [Fact]
    public void Returns_false_when_http_context_is_null()
    {
        var accessor = new HttpContextAccessor();
        var sut = new ClaimsPermissionService(accessor);

        sut.Has("financials.projects.confirm").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Throws_when_permission_argument_invalid(string? permission)
    {
        var sut = new ClaimsPermissionService(new HttpContextAccessor());

        var act = () => sut.Has(permission!);

        act.Should().Throw<ArgumentException>();
    }
}
