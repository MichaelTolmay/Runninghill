using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Runninghill.Contracts;
using Xunit;

namespace Runninghill.Tests;

/// <summary>
/// Checks HTTP collection permissions, response shapes and user-friendly validation errors.
/// </summary>
public sealed class WordEndpointTests
{
    /// <summary>
    /// Verifies that unrelated, read-only and similarly named scopes cannot create a word.
    /// </summary>
    [Theory]
    [InlineData("status.read")]
    [InlineData("words.read")]
    [InlineData("words.write.extra")]
    public async Task WritingRequiresItsExactScope(string scope)
    {
        await using var factory = new ServiceFactory(repository: new RecordingRepository());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token(scope));
        using var response = await client.PostAsJsonAsync("/api/words", new SaveWordRequest("hello", "Noun"), ApiJsonContext.Default.SaveWordRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Checks that creating a word returns 201, its resource URL and the documented response fields.
    /// </summary>
    [Fact]
    public async Task CreateReturnsSmallContractAndLocation()
    {
        await using var factory = new ServiceFactory(repository: new RecordingRepository());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token("words.write"));
        using var response = await client.PostAsJsonAsync("/api/words", new SaveWordRequest("hello", "Noun"), ApiJsonContext.Default.SaveWordRequest);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/words/1", response.Headers.Location!.ToString());
        Assert.Equal(new WordResponse(1, "hello", "Noun"), await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.WordResponse));
    }

    /// <summary>
    /// Checks that invalid words produce clear guidance with the same request reference in headers and
    /// JSON.
    /// </summary>
    [Fact]
    public async Task ValidationReturnsHelpfulProblemAndMatchingReference()
    {
        await using var factory = new ServiceFactory(repository: new RecordingRepository());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token("words.write"));
        using var response = await client.PostAsJsonAsync("/api/words", new SaveWordRequest("two words", "Noun"), ApiJsonContext.Default.SaveWordRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.ApiProblem);
        Assert.Contains("one word", error!.Detail);
        Assert.Equal(response.Headers.GetValues("X-Request-ID").Single(), error.RequestId);
    }

    /// <summary>
    /// Verifies that broken JSON returns a 400 reply with useful guidance and a support reference.
    /// </summary>
    [Fact]
    public async Task MalformedJsonIsAClientErrorRatherThanAnOutage()
    {
        await using var factory = new ServiceFactory(repository: new RecordingRepository());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token("words.write"));
        using var response = await client.PostAsync("/api/words", new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.ApiProblem);
        Assert.Contains("JSON", problem!.Detail);
        Assert.Equal(response.Headers.GetValues("X-Request-ID").Single(), problem.RequestId);
    }

    /// <summary>
    /// Verifies that read permissions permit listing but do not authorize saving a sentence.
    /// </summary>
    [Fact]
    public async Task ReadScopeCanListButNotSubmitSentences()
    {
        await using var factory = new ServiceFactory(repository: new RecordingRepository());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token("words.read sentences.read"));
        var words = await client.GetFromJsonAsync("/api/words", ApiJsonContext.Default.WordPage);
        Assert.Empty(words!.Items);
        using var response = await client.PostAsJsonAsync("/api/sentences", new SaveSentenceRequest([1], Guid.NewGuid()), ApiJsonContext.Default.SaveSentenceRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
