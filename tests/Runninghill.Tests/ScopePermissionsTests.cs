using System.Security.Claims;
using Runninghill.Service;
using Xunit;

namespace Runninghill.Tests;

/// <summary>
/// Checks exact permission matching across space-separated and repeated token claims.
/// </summary>
public sealed class ScopePermissionsTests
{
    /// <summary>
    /// Verifies that only a complete case-sensitive scope matches, including when surrounded by spaces
    /// or other scopes.
    /// </summary>
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

    /// <summary>
    /// Verifies that the permission check examines later scope claims as well as the first one.
    /// </summary>
    [Fact]
    public void PermissionCanBeInALaterClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("scope", "words.read"), new Claim("scope", "status.read")]));
        Assert.True(ScopePermissions.HasScope(user, "status.read"));
    }
}
