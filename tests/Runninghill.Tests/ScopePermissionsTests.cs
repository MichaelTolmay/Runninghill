using System.Security.Claims;
using Runninghill.Service;
using Xunit;

namespace Runninghill.Tests;

public sealed class ScopePermissionsTests
{
    [Theory]
    [InlineData("status.read", true)]
    [InlineData("words.write status.read words.read", true)]
    [InlineData("  status.read  ", true)]
    [InlineData("status.read.extra", false)]
    [InlineData("prefixstatus.read", false)]
    [InlineData("STATUS.READ", false)]
    [InlineData("", false)]
    public void OnlyExactPermissionsMatch(string scopes, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", scopes)]));
        Assert.Equal(expected, ScopePermissions.HasScope(user, "status.read"));
    }

    [Fact]
    public void PermissionCanBeInALaterClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("scope", "words.read"), new Claim("scope", "status.read")]));
        Assert.True(ScopePermissions.HasScope(user, "status.read"));
    }
}
