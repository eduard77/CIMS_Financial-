using System.Security.Claims;
using Financials.Infrastructure.Common;
using Microsoft.AspNetCore.Http;

namespace Financials.Infrastructure.Tests.Common;

public class HttpContextCurrentUserServiceTests
{
    [Fact]
    public void Returns_null_when_http_context_is_null()
    {
        var accessor = new HttpContextAccessor();

        var sut = new HttpContextCurrentUserService(accessor);

        sut.UserId.Should().BeNull();
        sut.Email.Should().BeNull();
        sut.DisplayName.Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_user_is_unauthenticated()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) },
        };

        var sut = new HttpContextCurrentUserService(accessor);

        sut.UserId.Should().BeNull();
    }

    [Fact]
    public void Reads_sub_email_and_name_claims_from_authenticated_user()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("sub", "user-42"),
                new Claim("email", "alice@example.com"),
                new Claim("name", "Alice Apple"),
            },
            authenticationType: "Test");

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        var sut = new HttpContextCurrentUserService(accessor);

        sut.UserId.Should().Be("user-42");
        sut.Email.Should().Be("alice@example.com");
        sut.DisplayName.Should().Be("Alice Apple");
    }
}
