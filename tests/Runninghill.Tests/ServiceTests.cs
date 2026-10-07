using System.Net.Http.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Runninghill.Tests;

public sealed class ServiceTests : IClassFixture<ServiceFactory>
{
    private readonly ServiceFactory factory;

    public ServiceTests(ServiceFactory factory) => this.factory = factory;

    [Fact]
    public async Task HttpEndpointReturnsJsonFromSdk()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/status");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Runninghill is ready. Database connection verified.",
            await response.Content.ReadFromJsonAsync<string>());
    }

    [Fact]
    public async Task GrpcAndHttpEndpointsReturnTheSameResult()
    {
        using var httpClient = factory.CreateClient();
        using var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler()
        });
        var method = new Method<Empty, StringValue>(MethodType.Unary,
            "runninghill.v1.Application", "GetStatus",
            Marshallers.Create<Empty>(Google.Protobuf.MessageExtensions.ToByteArray, bytes => Empty.Parser.ParseFrom(bytes)),
            Marshallers.Create<StringValue>(Google.Protobuf.MessageExtensions.ToByteArray, bytes => StringValue.Parser.ParseFrom(bytes)));
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new CallOptions(), new Empty());

        var response = await call.ResponseAsync;

        Assert.Equal(await httpClient.GetFromJsonAsync<string>("/api/status"), response.Value);
    }
}

public sealed class ServiceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Runninghill"] = "Data Source=:memory:"
            }));
}
