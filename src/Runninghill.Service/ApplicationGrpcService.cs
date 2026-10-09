using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Runninghill.Application;
using Runninghill.Diagnostics;

namespace Runninghill.Service;

/// <summary>Translates application results into gRPC replies and understandable gRPC errors.</summary>
public sealed partial class ApplicationGrpcService(IRunninghillApplication application, ILogger<ApplicationGrpcService> logger)
    : Grpc.Application.ApplicationBase
{
    /// <summary>
    /// Returns application status over gRPC with caller cancellation and a service deadline,
    /// translating outages and failures into friendly gRPC errors.
    /// </summary>
    public override async Task<StringValue> GetStatus(Empty request, ServerCallContext context)
    {
        using var operation = new OperationLog(logger, "Grpc.GetStatus", reference: context.GetHttpContext().TraceIdentifier);
        // A linked token stops work when EITHER the caller leaves OR our ten-second limit ends.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
#if DEBUG
        // A breakpoint pauses the server, not the caller's clock. Preserve the caller's
        // own cancellation, but do not make our timer abort a paused debugging session.
        if (!System.Diagnostics.Debugger.IsAttached)
#endif
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            return operation.Complete(new StringValue { Value = await application.GetStatusAsync(deadline.Token) });
        }
        catch (ApplicationUnavailableException)
        {
            LogUnavailable(logger, context.GetHttpContext().TraceIdentifier);
            throw Error(context, StatusCode.Unavailable, "The service cannot reach its data right now. Please try again shortly.");
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // A caller's expired deadline is different from a caller deliberately cancelling.
            var code = context.Deadline <= DateTime.UtcNow ? StatusCode.DeadlineExceeded : StatusCode.Cancelled;
            throw Error(context, code, "The request was cancelled or its time limit was reached.");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw Error(context, StatusCode.DeadlineExceeded, "The service took too long to reply. Please try again.");
        }
        catch (Exception exception)
        {
            LogFailure(logger, context.GetHttpContext().TraceIdentifier, exception.GetType().Name, exception.StackTrace);
            throw Error(context, StatusCode.Internal, "Something went wrong in the service. Please try again or contact support.");
        }
    }

    /// <summary>
    /// Builds a gRPC error containing the same support reference in its message and response trailers.
    /// </summary>
    private static RpcException Error(ServerCallContext context, StatusCode code, string message)
    {
        var reference = context.GetHttpContext().TraceIdentifier;
        // gRPC uses trailers (metadata at the end of a reply) instead of a JSON error body.
        return new RpcException(new Status(code, $"{message} Request reference: {reference}."),
            new Metadata { { "request-id", reference } });
    }

    /// <summary>
    /// Logs a database outage with the affected gRPC request reference.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database unavailable for gRPC status request. Request reference: {RequestId}")]
    private static partial void LogUnavailable(ILogger logger, string requestId);

    /// <summary>
    /// Logs an unexpected gRPC failure and request reference without sending exception details to the
    /// caller.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected gRPC failure. Request reference: {RequestId}; type: {ErrorType}; stack: {StackTrace}")]
    private static partial void LogFailure(ILogger logger, string requestId, string errorType, string? stackTrace);
}
