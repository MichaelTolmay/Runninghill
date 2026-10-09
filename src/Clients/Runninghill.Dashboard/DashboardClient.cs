using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Runninghill.Clients;
using Runninghill.Contracts;
using static Runninghill.Contracts.AppText;

namespace Runninghill.Dashboard;

/// <summary>One completed HTTP probe, including failed attempts and elapsed wall-clock time.</summary>
public sealed record ProbeResult(bool Healthy, double Milliseconds, string Detail);

/// <summary>A bounded read either supplies current data or explains why it is unavailable.</summary>
public sealed record DashboardReply<T>(T? Data, string Error) where T : class;

/// <summary>A small read-only load sample; latency is not a measurement of server CPU usage.</summary>
public sealed record LoadReport(int Requests, int Failures, double RequestsPerSecond, double P95Milliseconds, string Detail);

/// <summary>Performs bounded HTTP reads for the dashboard, without exposing tokens or database credentials.</summary>
public sealed class DashboardClient(HttpClient client, ILogger<DashboardClient> logger)
{
    public const int LoadRequests = 24;
    public const int LoadConcurrency = 4;

    /// <summary>Finds this checkout's public development certificate without relying on the launch directory.</summary>
    public static string? FindLocalCertificate(params string[] startingDirectories)
    {
        foreach (var start in startingDirectories)
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                // A random .run folder is not a trust source. Only use the known file in a
                // Runninghill checkout, and stop here even if this checkout has no certificate.
                if (!File.Exists(Path.Combine(directory.FullName, "Runninghill.slnx")) ||
                    !File.Exists(Path.Combine(directory.FullName, "scripts", "dev-certificate.py"))) continue;
                var certificate = Path.Combine(directory.FullName, ".run", "tls", "localhost.crt");
                return File.Exists(certificate) ? certificate : null;
            }
        }
        return null;
    }

    /// <summary>Reuses pooled connections; automatically discovered trust applies only to loopback addresses.</summary>
    public static HttpClient CreateHttpClient(string? certificatePath, bool localhostOnly = false)
    {
        var handler = new CertificateHandler(certificatePath, localhostOnly);
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 256 * 1024 };
    }

    /// <summary>Keeps the public trust certificate alive for pooled connections, then releases it with the client.</summary>
    private sealed class CertificateHandler : HttpClientHandler
    {
        private readonly X509Certificate2? trusted;

        /// <summary>Adds one trust source while retaining the platform's normal certificate checks.</summary>
        public CertificateHandler(string? path, bool localhostOnly)
        {
            AllowAutoRedirect = false;
            if (string.IsNullOrWhiteSpace(path)) return;
            trusted = X509CertificateLoader.LoadCertificateFromFile(Path.GetFullPath(path));
            ServerCertificateCustomValidationCallback = (request, certificate, _, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                // A repository certificate must never grant extra trust to a remote server.
                // Name mismatches and missing certificates cannot be fixed by adding trust.
                if (localhostOnly && request.RequestUri?.IsLoopback != true) return false;
                if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(trusted);
                chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1")); // TLS server authentication.
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(certificate); // Includes validity dates; never accept every certificate.
            };
        }

        /// <summary>Releases the public certificate when the HTTP connection pool is closed.</summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) trusted?.Dispose();
        }
    }

    /// <summary>Accepts HTTPS or local development HTTP without hidden credentials, queries or fragments.</summary>
    public static bool TryBaseAddress(string text, out Uri address)
    {
        address = null!;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != "https" && !(parsed.Scheme == "http" && parsed.IsLoopback)) ||
            parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0) return false;
        address = new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/");
        return true;
    }

    /// <summary>Counts one health attempt; cancelling monitoring is not reported as an outage.</summary>
    public async Task<ProbeResult> ProbeAsync(Uri address, CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = Request(address, null);
            using var response = await client.SendAsync(request, cancellation);
            // nginx's SPA fallback can return HTML with HTTP 200 for a wrong URL.
            // Require the health endpoint's actual response instead of treating that as healthy.
            var healthy = response.IsSuccessStatusCode &&
                (await response.Content.ReadAsStringAsync(cancellation)).Trim().Equals("Healthy", StringComparison.OrdinalIgnoreCase);
            return new(healthy, watch.Elapsed.TotalMilliseconds,
                healthy ? T("Healthy") : response.IsSuccessStatusCode ? ClientMessages.InvalidReply : Error(response));
        }
        catch (Exception exception) when (!cancellation.IsCancellationRequested)
        {
            return new(false, watch.Elapsed.TotalMilliseconds, Describe(exception));
        }
    }

    /// <summary>Reads two scalar collection totals with both collection read permissions.</summary>
    public async Task<DashboardReply<CollectionStatistics>> StatisticsAsync(Uri service, string token, CancellationToken cancellation)
    {
        var reply = await ReadAsync(new Uri(service, "api/statistics"), token, ApiJsonContext.Default.CollectionStatistics, cancellation);
        if (reply.Data is { } data && (data.Words < 0 || data.Sentences < 0 || data.CheckedAt == default))
            return new(null, ClientMessages.InvalidReply);
        return reply;
    }

    /// <summary>Reads one permission-protected log page; older pages remain bounded to twenty events.</summary>
    public async Task<DashboardReply<LogPage>> LogsAsync(Uri service, string token, long before, string level, string search, CancellationToken cancellation)
    {
        var reply = await ReadAsync(new Uri(service, $"api/logs?before={before}&level={Uri.EscapeDataString(level)}&search={Uri.EscapeDataString(search)}"),
            token, ApiJsonContext.Default.LogPage, cancellation);
        if (reply.Data is { } page && (page.Items is null || page.Items.Length > 20 ||
            page.Items.Any(item => item is null || item.Level is null || item.Message is null || item.Category is null)))
            return new(null, ClientMessages.InvalidReply);
        return reply;
    }

    /// <summary>Runs at most 24 read-only requests, four at a time, with a 20-second overall deadline.</summary>
    public async Task<LoadReport> LoadAsync(Uri service, string token, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(token)) return new(0, 0, 0, 0, T("Enter an access token with status.read permission to run a load test."));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var watch = Stopwatch.StartNew();
        var samples = new List<double>(LoadRequests);
        var attempts = 0;
        var failures = 0;
        var next = 0;
        var stop = 0;
        var detail = "";
        var gate = new object();
        // Only four worker tasks are allocated. The total request budget is fixed, not duration-driven.
        async Task WorkerAsync()
        {
            while (!deadline.IsCancellationRequested && Volatile.Read(ref stop) == 0 && Interlocked.Increment(ref next) <= LoadRequests)
            {
                var started = Stopwatch.GetTimestamp();
                var failed = false;
                var error = "";
                try
                {
                    using var request = Request(new Uri(service, "api/status"), token);
                    using var response = await client.SendAsync(request, deadline.Token);
                    failed = !response.IsSuccessStatusCode;
                    if (failed) error = Error(response);
                    else
                    {
                        var status = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.StatusResponse, deadline.Token);
                        if (string.IsNullOrWhiteSpace(status?.Message)) { failed = true; error = ClientMessages.InvalidReply; }
                    }
                    // Stop scheduling additional work when access is denied or the service asks us to slow down.
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                        Interlocked.Exchange(ref stop, 1);
                }
                catch (Exception exception) when (!cancellation.IsCancellationRequested)
                { failed = true; error = Describe(exception); }
                lock (gate)
                {
                    attempts++;
                    samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    if (failed) { failures++; detail = error; }
                }
            }
        }
        await Task.WhenAll(Enumerable.Range(0, LoadConcurrency).Select(_ => WorkerAsync()));
        cancellation.ThrowIfCancellationRequested();
        samples.Sort();
        var p95 = samples.Count == 0 ? 0 : samples[(int)Math.Ceiling(samples.Count * .95) - 1];
        return new(attempts, failures, attempts / Math.Max(watch.Elapsed.TotalSeconds, .001), p95, detail);
    }

    /// <summary>Requests small JSON responses with generated serializers and friendly failure details.</summary>
    private async Task<DashboardReply<TValue>> ReadAsync<TValue>(Uri address, string token, JsonTypeInfo<TValue> type, CancellationToken cancellation) where TValue : class
    {
        if (string.IsNullOrWhiteSpace(token)) return new(null, T("Enter a token to read collection totals and service logs."));
        try
        {
            using var request = Request(address, token);
            using var response = await client.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode) return new(null, Error(response));
            var value = await response.Content.ReadFromJsonAsync(type, cancellation);
            return value is null ? new(null, ClientMessages.InvalidReply) : new(value, "");
        }
        catch (Exception exception) when (!cancellation.IsCancellationRequested) { return new(null, Describe(exception)); }
    }

    /// <summary>Attaches credentials to this request only; health checks and other hosts never inherit them.</summary>
    private static HttpRequestMessage Request(Uri address, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.AcceptLanguage.ParseAdd(AppText.Language);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return request;
    }

    /// <summary>Preserves standard HTTP codes and request references without displaying raw server bodies.</summary>
    private static string Error(HttpResponseMessage response) =>
        ClientMessages.WithReference(ClientMessages.ForStatus(response.StatusCode), ClientMessages.ReadReference(response));

    /// <summary>Explains common failures and logs a reference for unforeseen failures, never exception messages or tokens.</summary>
    private string Describe(Exception error)
    {
        if (error is OperationCanceledException) return ClientMessages.TimedOut;
        if (error is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError })
            return T("The HTTPS certificate could not be verified; the token has not been checked. For localhost, set RUNNINGHILL_DASHBOARD_CA_CERT to the full path of .run/tls/localhost.crt and reopen the dashboard. If already set, check that nginx uses that certificate and that its hostname and dates are valid. Error code: HttpRequestError.SecureConnectionError.");
        if (error is HttpRequestException network) return ClientMessages.ForRequestFailure(network);
        if (error is JsonException) return ClientMessages.InvalidReply;
        if (error is FormatException) return T("The access token is not valid. Copy a fresh token and refresh.");
        var reference = Guid.NewGuid().ToString("N");
        logger.LogError("Dashboard request failed. Reference: {Reference}; type: {ErrorType}", reference, error.GetType().Name);
        return ClientMessages.WithReference(ClientMessages.ForUnexpected(error), reference);
    }
}
