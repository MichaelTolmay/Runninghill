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
using Runninghill.Application;
using Runninghill.Contracts;
using Xunit;

namespace Runninghill.Tests;

public sealed class ServiceTests
{
    [Fact]
    public async Task HttpRejectsUnauthenticatedRequests()
    {
        await using var factory = new ServiceFactory();
        using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task HttpEnforcesScope()
    {
        await using var factory = new ServiceFactory();
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token("other"));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/status")).StatusCode);
    }

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

    [Fact]
    public async Task MissingConnectionStringFailsStartup()
    {
        await using var factory = new ServiceFactory(missingConnection: true);
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("ConnectionStrings:Runninghill is required", exception.Message);
    }

    private static Method<Empty, StringValue> StatusMethod() => new(MethodType.Unary,
        "runninghill.v1.Application", "GetStatus",
        Marshallers.Create<Empty>(Google.Protobuf.MessageExtensions.ToByteArray, bytes => Empty.Parser.ParseFrom(bytes)),
        Marshallers.Create<StringValue>(Google.Protobuf.MessageExtensions.ToByteArray, bytes => StringValue.Parser.ParseFrom(bytes)));
}

public sealed class ServiceFactory(bool ready = true, bool missingConnection = false) : WebApplicationFactory<Program>
{
    private const string Key = "test-only-signing-key-at-least-32-bytes-long";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
        });
    }

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
