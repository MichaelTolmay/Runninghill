using System.Net;
using Runninghill.Clients;
using Xunit;

namespace Runninghill.Tests;

public sealed class ClientMessagesTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "new access token")]
    [InlineData(HttpStatusCode.Forbidden, "administrator")]
    [InlineData(HttpStatusCode.TooManyRequests, "wait a few seconds")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "try again shortly")]
    public void ExpectedFailuresSuggestAnAction(HttpStatusCode code, string action)
        => Assert.Contains(action, ClientMessages.ForStatus(code));

    [Theory]
    [InlineData(HttpRequestError.ConfigurationLimitExceeded, "larger")]
    [InlineData(HttpRequestError.SecureConnectionError, "certificate")]
    [InlineData(HttpRequestError.NameResolutionError, "address")]
    [InlineData(HttpRequestError.ResponseEnded, "cannot read")]
    public void NetworkFailuresHaveSpecificSafeExplanations(HttpRequestError error, string expected)
    {
        var message = ClientMessages.ForRequestFailure(new HttpRequestException(error, "private-error-detail"));
        Assert.Contains(expected, message);
        Assert.DoesNotContain("private-error-detail", message);
    }

    [Fact]
    public void ReferenceCanBeSharedButUntrustedHeaderTextIsNotDisplayed()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("X-Request-ID", "server-123:456");
        Assert.Equal("server-123:456", ClientMessages.ReadReference(response));
        response.Headers.Remove("X-Request-ID");
        response.Headers.TryAddWithoutValidation("X-Request-ID", "bad\u001b[31mreference");
        Assert.Null(ClientMessages.ReadReference(response));
        response.Headers.Remove("X-Request-ID");
        response.Headers.TryAddWithoutValidation("X-Request-ID", new string('x', 129));
        Assert.Null(ClientMessages.ReadReference(response));
    }
}
