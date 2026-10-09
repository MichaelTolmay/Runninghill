using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Runninghill.Service;

/// <summary>Records request outcomes without capturing tokens, query strings, bodies or raw URLs.</summary>
public static partial class RequestEventLogging
{
    /// <summary>Wraps the full request pipeline, including authentication and exception handling.</summary>
    public static void UseRequestEventLogging(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Runninghill.Requests");
        app.Use(async (context, next) =>
        {
            var started = Stopwatch.GetTimestamp();
            var aborted = false;
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["RequestId"] = context.TraceIdentifier,
                ["TraceId"] = Activity.Current?.TraceId.ToString()
            });
            RequestStarted(logger, context.TraceIdentifier);
            try { await next(context); }
            catch { aborted = true; throw; }
            finally
            {
                // A route template is code-owned. Raw paths and queries can contain user text.
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
                var grpc = context.Features.Get<IHttpResponseTrailersFeature>()?.Trailers["grpc-status"].ToString();
                if (string.IsNullOrEmpty(grpc)) grpc = context.Response.Headers["grpc-status"].ToString();
                var grpcStatus = int.TryParse(grpc, out var code) ? code : (int?)null;
                var status = context.RequestAborted.IsCancellationRequested ? 499 : aborted ? 500 : context.Response.StatusCode;
                var method = context.Request.Method switch
                {
                    "GET" or "POST" or "PUT" or "DELETE" or "PATCH" or "HEAD" or "OPTIONS" => context.Request.Method,
                    _ => "OTHER"
                };
                RequestFinished(logger, method, route, status, grpcStatus, context.TraceIdentifier,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        });
    }

    /// <summary>Records arrival so an interrupted request can be distinguished from one never received.</summary>
    [LoggerMessage(2000, LogLevel.Information, "Request started. Reference: {RequestId}")]
    private static partial void RequestStarted(ILogger logger, string requestId);

    /// <summary>Records completion, rejection, cancellation or failure with a safe route and timing.</summary>
    [LoggerMessage(2001, LogLevel.Information, "Request finished. Method: {Method}; route: {Route}; HTTP: {Status}; gRPC: {GrpcStatus}; reference: {RequestId}; elapsed: {ElapsedMs} ms")]
    private static partial void RequestFinished(ILogger logger, string method, string route, int status, int? grpcStatus, string requestId, double elapsedMs);
}
