using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Resources;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Runninghill.Contracts;
using Xunit;

namespace Runninghill.Tests;

/// <summary>Checks translation completeness, request isolation, and unchanged collection contracts.</summary>
public sealed class LocalizationTests
{
    /// <summary>gRPC metadata selects the same translated status and failure language as HTTP.</summary>
    [Theory]
    [InlineData("af-ZA")]
    [InlineData("xh-ZA")]
    [InlineData("zu-ZA")]
    [InlineData("tn-ZA")]
    public async Task GrpcUsesTheRequestedLanguage(string language)
    {
        var method = new Method<Empty, StringValue>(MethodType.Unary, "runninghill.v1.Application", "GetStatus",
            Marshallers.Create<Empty>(Google.Protobuf.MessageExtensions.ToByteArray, bytes => Empty.Parser.ParseFrom(bytes)),
            Marshallers.Create<StringValue>(Google.Protobuf.MessageExtensions.ToByteArray, bytes => StringValue.Parser.ParseFrom(bytes)));
        foreach (var ready in new[] { true, false })
        {
            await using var factory = new ServiceFactory(ready);
            using var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token());
            http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
            using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
            using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null,
                new CallOptions(new Metadata { { "authorization", "Bearer " + ServiceFactory.Token() }, { "accept-language", language } }), new Empty());
            if (ready)
            {
                var status = await http.GetFromJsonAsync("/api/status", ApiJsonContext.Default.StatusResponse);
                Assert.Equal(status!.Message, (await call.ResponseAsync).Value);
                Assert.NotEqual("Runninghill is ready. Database schema verified.", status.Message);
            }
            else
            {
                var failure = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
                Assert.Equal(StatusCode.Unavailable, failure.StatusCode);
                Assert.DoesNotContain("The service cannot", failure.Status.Detail);
                Assert.Contains(failure.Trailers.GetValue("request-id")!, failure.Status.Detail);
            }
        }
    }

    /// <summary>Loads every compiled dictionary and exercises every composite format, including native AOT resources.</summary>
    [Theory]
    [InlineData("en-ZA", "")]
    [InlineData("af-ZA", "_af_ZA")]
    [InlineData("xh-ZA", "_xh_ZA")]
    [InlineData("zu-ZA", "_zu_ZA")]
    [InlineData("tn-ZA", "_tn_ZA")]
    public void EveryMessageHasAWorkingTranslation(string language, string suffix)
    {
        var english = new ResourceManager("Runninghill.Contracts.Resources.Text", typeof(AppText).Assembly)
            .GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var translated = new ResourceManager("Runninghill.Contracts.Resources.Text" + suffix, typeof(AppText).Assembly)
            .GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            foreach (DictionaryEntry entry in english)
            {
                var message = translated.GetString((string)entry.Key);
                Assert.False(string.IsNullOrWhiteSpace(message));
                Assert.Equal(message, AppText.T((string)entry.Value!));
                if (language != "en-ZA" && !AppText.LanguageNames.Contains((string)entry.Value!))
                    Assert.NotEqual(entry.Value, message);
                // Formats in these messages use up to three arguments, including date formatting.
                _ = string.Format(AppText.Culture, message!, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 3);
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    /// <summary>Concurrent requests get their own translated errors while the API preserves canonical word types.</summary>
    [Fact]
    public async Task RequestLanguagesDoNotLeakAndWordValuesStayStable()
    {
        await using var factory = new ServiceFactory(repository: new RecordingRepository());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token("words.write"));
        var titles = await Task.WhenAll(AppText.Languages.Select(async language =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/words");
            request.Headers.AcceptLanguage.ParseAdd(language);
            request.Content = JsonContent.Create(new SaveWordRequest("two words", "Noun"), ApiJsonContext.Default.SaveWordRequest);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(language, response.Content.Headers.ContentLanguage);
            Assert.Contains("Accept-Language", response.Headers.Vary);
            var problem = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.ApiProblem);
            Assert.Equal(response.Headers.GetValues("X-Request-ID").Single(), problem!.RequestId);
            return problem.Detail;
        }));
        Assert.Equal(5, titles.Distinct().Count());
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/words");
        create.Headers.AcceptLanguage.ParseAdd("zu-ZA");
        create.Content = JsonContent.Create(new SaveWordRequest("hello", "Noun"), ApiJsonContext.Default.SaveWordRequest);
        using var saved = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        Assert.Equal(new WordResponse(1, "hello", "Noun"), await saved.Content.ReadFromJsonAsync(ApiJsonContext.Default.WordResponse));
    }

    /// <summary>Unsupported language preferences safely fall back to South African English.</summary>
    [Fact]
    public async Task UnsupportedRequestLanguageUsesEnglish()
    {
        await using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-FR");
        using var response = await client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("en-ZA", response.Content.Headers.ContentLanguage);
        var problem = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.ApiProblem);
        Assert.Equal("The request could not be completed.", problem!.Title);
    }
}
