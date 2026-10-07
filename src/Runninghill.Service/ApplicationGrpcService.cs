using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Runninghill.Application;

namespace Runninghill.Service;

/// <summary>Translates application results into gRPC replies and understandable gRPC errors.</summary>
public sealed partial class ApplicationGrpcService(IRunninghillApplication application, ILogger<ApplicationGrpcService> logger)
    : Grpc.Application.ApplicationBase
{
    public override async Task<StringValue> GetStatus(Empty request, ServerCallContext context)
    {
        // A linked token stops work when EITHER the caller leaves OR our ten-second limit ends.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            return new StringValue { Value = await application.GetStatusAsync(deadline.Token) };
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
            LogFailure(logger, context.GetHttpContext().TraceIdentifier, exception);
            throw Error(context, StatusCode.Internal, "Something went wrong in the service. Please try again or contact support.");
        }
    }

    private static RpcException Error(ServerCallContext context, StatusCode code, string message)
    {
        var reference = context.GetHttpContext().TraceIdentifier;
        // gRPC uses trailers (metadata at the end of a reply) instead of a JSON error body.
        return new RpcException(new Status(code, $"{message} Request reference: {reference}."),
            new Metadata { { "request-id", reference } });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database unavailable for gRPC status request. Request reference: {RequestId}")]
    private static partial void LogUnavailable(ILogger logger, string requestId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected gRPC failure. Request reference: {RequestId}")]
    private static partial void LogFailure(ILogger logger, string requestId, Exception exception);
}
