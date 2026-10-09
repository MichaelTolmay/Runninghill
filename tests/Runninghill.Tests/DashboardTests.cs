using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Runninghill.Contracts;
using Runninghill.Dashboard;
using Xunit;

namespace Runninghill.Tests;

/// <summary>Tests real transport boundaries: permissions, malformed data, outages, cancellation and load limits.</summary>
public sealed class DashboardTests
{
    /// <summary>Exercises real TLS: adding local trust must not accept expired, unrelated or misnamed certificates.</summary>
    [Theory]
    [InlineData("matching", true)]
    [InlineData("unconfigured", false)]
    [InlineData("unrelated", false)]
    [InlineData("expired", false)]
    [InlineData("wrong-host", false)]
    public async Task LocalCertificateTrustPreservesTlsChecks(string scenario, bool healthy)
    {
        using var serverCertificate = Certificate(scenario == "wrong-host" ? "another-host.invalid" : "localhost", scenario == "expired");
        using var unrelatedCertificate = Certificate("localhost", false);
        var path = Path.Combine(Path.GetTempPath(), $"runninghill-trust-{Guid.NewGuid():N}.crt");
        var builder = WebApplication.CreateSlimBuilder();
        // Do not inherit the service's configured ports from copied appsettings files.
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(serverCertificate)));
        await using var app = builder.Build();
        app.MapGet("/health/live", () => "Healthy");
        try
        {
            File.WriteAllText(path, (scenario == "unrelated" ? unrelatedCertificate : serverCertificate).ExportCertificatePem());
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = DashboardClient.CreateHttpClient(scenario == "unconfigured" ? null : path, localhostOnly: true);
            var result = await Client(http).ProbeAsync(new Uri(address + "/health/live"), default);
            Assert.True(result.Healthy == healthy, $"TLS scenario {scenario}: {result.Detail}");
            if (!healthy)
            {
                Assert.Contains("HttpRequestError.SecureConnectionError", result.Detail);
                Assert.Contains("RUNNINGHILL_DASHBOARD_CA_CERT", result.Detail);
                Assert.Contains("token has not been checked", result.Detail);
            }
        }
        finally { await app.StopAsync(); File.Delete(path); }
    }

    /// <summary>Matches the development script's self-signed leaf certificate without changing system trust.</summary>
    private static X509Certificate2 Certificate(string hostname, bool expired)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + hostname, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(hostname);
        if (hostname == "localhost") names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(expired ? -1 : 1));
        // Schannel cannot serve TLS with CreateSelfSigned's ephemeral private-key handle.
        // Import through PKCS#12 with default key storage, as a real Windows HTTPS host does.
        // Disposing the imported certificate releases its temporary key container.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
    }

    /// <summary>Both build-output and launch-directory discovery work, but unrelated folders grant no trust.</summary>
    [Fact]
    public void CertificateDiscoveryRequiresTheRunninghillCheckout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"runninghill-discovery-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "artifacts", "dashboard");
        var certificate = Path.Combine(root, ".run", "tls", "localhost.crt");
        try
        {
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(Path.GetDirectoryName(certificate)!);
            File.WriteAllText(certificate, "Public certificate placeholder; discovery never reads private keys.");
            Assert.Null(DashboardClient.FindLocalCertificate(nested));
            File.WriteAllText(Path.Combine(root, "Runninghill.slnx"), "");
            Directory.CreateDirectory(Path.Combine(root, "scripts"));
            File.WriteAllText(Path.Combine(root, "scripts", "dev-certificate.py"), "");
            Assert.Equal(certificate, DashboardClient.FindLocalCertificate(nested));
            Assert.Equal(certificate, DashboardClient.FindLocalCertificate(Path.GetTempPath(), root));
            File.Delete(certificate);
            Assert.Null(DashboardClient.FindLocalCertificate(nested));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Totals require both collection read permissions; neither one alone is enough.</summary>
    [Theory]
    [InlineData(null, 401)]
    [InlineData("status.read", 403)]
    [InlineData("words.read", 403)]
    [InlineData("sentences.read", 403)]
    [InlineData("words.read sentences.read", 200)]
    public async Task TotalsAreProtected(string? scope, int expected)
    {
        await using var factory = new ServiceFactory(repository: new RecordingRepository());
        using var client = factory.CreateClient();
        if (scope is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token(scope));
        using var response = await client.GetAsync("/api/statistics");
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected == 200)
        {
            var totals = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.CollectionStatistics);
            Assert.Equal(12, totals!.Words);
            Assert.Equal(3, totals.Sentences);
            Assert.True(totals.CheckedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
    }

    /// <summary>Endpoint configuration cannot hide credentials or send bearer tokens over remote plaintext HTTP.</summary>
    [Theory]
    [InlineData("https://example.org/base", true)]
    [InlineData("http://localhost:5080", true)]
    [InlineData("http://127.0.0.1:5080", true)]
    [InlineData("http://example.org", false)]
    [InlineData("https://name:secret@example.org", false)]
    [InlineData("https://example.org/?token=secret", false)]
    [InlineData("file:///etc/passwd", false)]
    public void AddressesAreExplicitAndSafe(string address, bool expected) => Assert.Equal(expected, DashboardClient.TryBaseAddress(address, out _));

    /// <summary>Unhealthy HTTP replies retain their standard code and support reference.</summary>
    [Fact]
    public async Task HealthFailureIncludesStatusAndReferenceWithoutCredentials()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.Add("X-Request-ID", "test-request-123");
            return Task.FromResult(response);
        }));
        var result = await Client(http).ProbeAsync(new Uri("https://example.org/health/ready"), default);
        Assert.False(result.Healthy);
        Assert.Contains("HTTP 503", result.Detail);
        Assert.Contains("test-request-123", result.Detail);
    }

    /// <summary>Unavailable totals have no data, so the UI cannot confuse stale values with a current zero.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"words\":-1,\"sentences\":2,\"checkedAt\":\"2026-01-01T00:00:00Z\"}")]
    public async Task RejectsMalformedTotals(string body)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) })));
        var reply = await Client(http).StatisticsAsync(new Uri("https://example.org/"), "test-token", default);
        Assert.Null(reply.Data);
        Assert.Contains("RH-REPLY-INVALID", reply.Error);
    }

    /// <summary>Stopping monitoring propagates cancellation rather than counting an intentional stop as an outage.</summary>
    [Fact]
    public async Task StopCancelsInflightHealthCheck()
    {
        using var started = new SemaphoreSlim(0);
        using var http = new HttpClient(new Handler(async (_, cancellation) =>
        {
            started.Release();
            await Task.Delay(Timeout.Infinite, cancellation);
            return new(HttpStatusCode.OK);
        }));
        using var cancellation = new CancellationTokenSource();
        var pending = Client(http).ProbeAsync(new Uri("https://example.org/health/live"), cancellation.Token);
        await started.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    /// <summary>A short load sample has a fixed request budget, a concurrency ceiling and read-only authenticated traffic.</summary>
    [Fact]
    public async Task LoadIsBoundedAndReadOnly()
    {
        var count = 0; var active = 0; var maximum = 0;
        var gate = new object();
        using var http = new HttpClient(new Handler(async (request, cancellation) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/status", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-token", request.Headers.Authorization!.Parameter);
            Interlocked.Increment(ref count);
            lock (gate) { active++; maximum = Math.Max(maximum, active); }
            await Task.Delay(15, cancellation);
            lock (gate) active--;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new StatusResponse("Ready"), ApiJsonContext.Default.StatusResponse) };
        }));
        var result = await Client(http).LoadAsync(new Uri("https://example.org/"), "test-token", default);
        Assert.Equal(DashboardClient.LoadRequests, count);
        Assert.Equal(count, result.Requests);
        Assert.Equal(0, result.Failures);
        Assert.InRange(maximum, 2, DashboardClient.LoadConcurrency);
        Assert.True(result.P95Milliseconds > 0);
        Assert.True(result.RequestsPerSecond > 0);
    }

    /// <summary>Missing credentials send no test traffic; rate limiting stops new work promptly.</summary>
    [Fact]
    public async Task LoadDoesNotFloodRejectedEndpoint()
    {
        var count = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            Interlocked.Increment(ref count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        }));
        var client = Client(http);
        Assert.Equal(0, (await client.LoadAsync(new Uri("https://example.org/"), "", default)).Requests);
        Assert.Equal(0, count);
        var result = await client.LoadAsync(new Uri("https://example.org/"), "test-token", default);
        Assert.InRange(count, 1, DashboardClient.LoadConcurrency);
        Assert.Equal(count, result.Failures);
        Assert.Contains("HTTP 429", result.Detail);
    }

    /// <summary>A generic HTML fallback with HTTP 200 is not a successful health check.</summary>
    [Theory]
    [InlineData("Healthy", true)]
    [InlineData("Healthy\n", true)]
    [InlineData("<html>Sign in</html>", false)]
    public async Task HealthChecksRequireTheHealthResponse(string body, bool healthy)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) })));
        Assert.Equal(healthy, (await Client(http).ProbeAsync(new Uri("https://example.org/health/live"), default)).Healthy);
    }

    /// <summary>Creates the production HTTP adapter with a silent test logger.</summary>
    private static DashboardClient Client(HttpClient http) => new(http, NullLogger<DashboardClient>.Instance);

    /// <summary>Replaces the network while retaining real HttpClient request and cancellation behaviour.</summary>
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        /// <summary>Routes a request to this test's controlled endpoint.</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
