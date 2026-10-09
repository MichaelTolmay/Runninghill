using System.Net;
using Runninghill.Clients;
using Xunit;

namespace Runninghill.Tests;

/// <summary>
/// Checks that clients show useful, safe failure descriptions and retain valid support references.
/// </summary>
public sealed class ClientMessagesTests
{
    /// <summary>
    /// Verifies that HTML, malformed JSON and empty details do not hide status guidance or the request
    /// reference.
    /// </summary>
    [Theory]
    [InlineData("<html>upstream error</html>")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"detail\":\"  \"}")]
    public async Task BrokenErrorBodiesKeepTheStatusAndSupportReference(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(body) };
        response.Headers.Add("X-Request-ID", "request-123");
        var message = await ClientMessages.ReadErrorAsync(response, default);
        Assert.Contains("Check the service URL", message);
        Assert.Contains("request-123", message);
        Assert.DoesNotContain("<html>", message);
    }

    /// <summary>
    /// Verifies that a valid conflict response preserves its specific corrective advice.
    /// </summary>
    [Fact]
    public async Task ValidConflictKeepsItsActionableExplanation()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"detail\":\"This word already exists. Choose another type.\"}")
        };
        Assert.Contains("Choose another type", await ClientMessages.ReadErrorAsync(response, default));
    }

    /// <summary>
    /// Verifies that cancelling an error-body read still stops the operation.
    /// </summary>
    [Fact]
    public async Task ErrorReadingDoesNotSwallowCancellation()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{}") };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientMessages.ReadErrorAsync(response, cancellation.Token));
    }

    /// <summary>
    /// Checks that common HTTP failures tell the user a practical next step.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "new access token")]
    [InlineData(HttpStatusCode.Forbidden, "administrator")]
    [InlineData(HttpStatusCode.TooManyRequests, "wait a few seconds")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "try again shortly")]
    public void ExpectedFailuresSuggestAnAction(HttpStatusCode code, string action)
        => Assert.Contains(action, ClientMessages.ForStatus(code));

    /// <summary>
    /// Checks that network errors produce specific guidance without exposing private exception
    /// messages.
    /// </summary>
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

    /// <summary>
    /// Accepts a valid reference while rejecting terminal control codes and oversized header values.
    /// </summary>
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
