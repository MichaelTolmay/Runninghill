using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Runninghill.Application;
using Runninghill.Contracts;
using Xunit;

namespace Runninghill.Tests;

/// <summary>
/// Checks service authentication, status transport agreement, health probes and safe failure
/// replies.
/// </summary>
public sealed class ServiceTests
{
    /// <summary>
    /// Verifies that the HTTP status route rejects a request without an access token.
    /// </summary>
    [Fact]
    public async Task HttpRejectsUnauthenticatedRequests()
    {
        await using var factory = new ServiceFactory();
        using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/status")).StatusCode);
    }

    /// <summary>
    /// Verifies that an authenticated user still needs the status-read permission.
    /// </summary>
    [Fact]
    public async Task HttpEnforcesScope()
    {
        await using var factory = new ServiceFactory();
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token("other"));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/status")).StatusCode);
    }

    /// <summary>
    /// Verifies that an unavailable database fails readiness and status while the process remains live.
    /// </summary>
    [Fact]
    public async Task LivenessSurvivesDatabaseFailureButReadinessAndApiFail()
    {
        await using var factory = new ServiceFactory(false);
        using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/health/ready")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/api/status")).StatusCode);
    }

    /// <summary>
    /// Checks that authorized gRPC and HTTP callers receive the same application status.
    /// </summary>
    [Fact]
    public async Task GrpcAndHttpReturnTheSameResult()
    {
        await using var factory = new ServiceFactory();
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token());
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        var method = StatusMethod();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null,
            new CallOptions(new Metadata { { "authorization", "Bearer " + ServiceFactory.Token() } }), new Empty());
        var response = await call.ResponseAsync;
        Assert.Equal((await http.GetFromJsonAsync("/api/status", ApiJsonContext.Default.StatusResponse))!.Message, response.Value);
    }

    /// <summary>
    /// Verifies that a gRPC status request without a token is rejected.
    /// </summary>
    [Fact]
    public async Task GrpcRequiresAuthentication()
    {
        await using var factory = new ServiceFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(StatusMethod(), null, new CallOptions(), new Empty());
        var error = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
    }

    /// <summary>
    /// Verifies that the service refuses to start without explicit database configuration.
    /// </summary>
    [Fact]
    public async Task MissingConnectionStringFailsStartup()
    {
        await using var factory = new ServiceFactory(missingConnection: true);
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("ConnectionStrings:Runninghill is required", exception.Message);
    }

    /// <summary>
    /// Verifies that HTTP failures hide private exception text and use matching support references in
    /// headers and JSON.
    /// </summary>
    [Fact]
    public async Task UnexpectedHttpFailureHidesPrivateDetailsAndReturnsRequestReference()
    {
        await using var factory = new ServiceFactory(failure: new InvalidOperationException("private-database-password"));
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token());
        using var response = await http.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-database-password", body);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(response.Headers.GetValues("X-Request-ID").Single(), problem.RootElement.GetProperty("requestId").GetString());
        Assert.Contains("try again", problem.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>
    /// Checks that expected and unexpected gRPC errors expose useful references while keeping private
    /// details hidden.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrpcErrorsHaveFriendlyMessagesAndReferences(bool unexpected)
    {
        await using var factory = new ServiceFactory(ready: false,
            failure: unexpected ? new InvalidOperationException("private-database-password") : null);
        using var http = factory.CreateClient();
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(StatusMethod(), null,
            new CallOptions(new Metadata { { "authorization", "Bearer " + ServiceFactory.Token() } }), new Empty());
        var error = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
        Assert.Equal(unexpected ? StatusCode.Internal : StatusCode.Unavailable, error.StatusCode);
        Assert.DoesNotContain("private-database-password", error.Status.Detail);
        var reference = error.Trailers.GetValue("request-id");
        Assert.False(string.IsNullOrWhiteSpace(reference));
        Assert.Contains(reference!, error.Status.Detail);
    }

    /// <summary>
    /// Builds the unary gRPC status method description and protobuf serializers used by the test
    /// caller.
    /// </summary>
    private static Method<Empty, StringValue> StatusMethod() => new(MethodType.Unary,
        "runninghill.v1.Application", "GetStatus",
        Marshallers.Create<Empty>(Google.Protobuf.MessageExtensions.ToByteArray, bytes => Empty.Parser.ParseFrom(bytes)),
        Marshallers.Create<StringValue>(Google.Protobuf.MessageExtensions.ToByteArray, bytes => StringValue.Parser.ParseFrom(bytes)));
}

/// <summary>
/// Starts an in-process service with test-only authentication settings and replaceable application
/// dependencies.
/// </summary>
public sealed class ServiceFactory(bool ready = true, bool missingConnection = false, Exception? failure = null, IWordRepository? repository = null, ILoggerProvider? logs = null) : WebApplicationFactory<Program>
{
    private const string Key = "test-only-signing-key-at-least-32-bytes-long";

    /// <summary>
    /// Installs test configuration and substitutes readiness, storage or application failures requested
    /// by the test.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (logs is not null) builder.ConfigureLogging(logging => logging.AddProvider(logs));
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Runninghill"] = missingConnection ? "" : "Host=localhost;Database=runninghill;Username=test;Password=test",
            ["Authentication:Audience"] = "runninghill",
            ["Authentication:Authority"] = "",
            ["Authentication:DevelopmentSigningKey"] = Key
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDatabaseReadiness>();
            services.AddSingleton<IDatabaseReadiness>(new StubDatabase(ready));
            if (repository is not null)
            {
                services.RemoveAll<IWordRepository>();
                services.AddSingleton(repository);
            }
            if (failure is not null)
            {
                services.RemoveAll<IRunninghillApplication>();
                services.AddSingleton<IRunninghillApplication>(new FailingApplication(failure));
            }
        });
    }

    // Sign test tokens locally so these tests do not depend on an external login provider.
    // This known key belongs only to the test server. Never use it in a deployed service.
    /// <summary>
    /// Signs a short-lived token for the test server with the requested scopes and a test-only key.
    /// </summary>
    internal static string Token(string scope = "status.read")
    {
        static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = Encode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        { iss = "runninghill-development", aud = "runninghill", sub = "test-user", scope, exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() }));
        var signature = Encode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), Encoding.UTF8.GetBytes(header + "." + payload)));
        return header + "." + payload + "." + signature;
    }
}

// Simulate a bug without depending on an actual database or putting secrets in test configuration.
/// <summary>
/// Simulates a chosen application failure so tests can examine safe transport error replies.
/// </summary>
internal sealed class FailingApplication(Exception failure) : IRunninghillApplication
{
    /// <summary>
    /// Returns a failed task containing the exception supplied by the test.
    /// </summary>
    public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromException<string>(failure);
}
