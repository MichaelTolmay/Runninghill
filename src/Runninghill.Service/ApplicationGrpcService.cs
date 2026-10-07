using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Runninghill.Application;

namespace Runninghill.Service;

public sealed class ApplicationGrpcService(IRunninghillApplication application) : Grpc.Application.ApplicationBase
{
    public override async Task<StringValue> GetStatus(Empty request, ServerCallContext context)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            return new StringValue { Value = await application.GetStatusAsync(deadline.Token) };
        }
        catch (ApplicationUnavailableException)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "The application is temporarily unavailable."));
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.DeadlineExceeded, "The operation timed out."));
        }
    }
}
