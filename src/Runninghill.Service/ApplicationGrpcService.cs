using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Runninghill.Sdk;

namespace Runninghill.Service;

public sealed class ApplicationGrpcService(IRunninghillSdk sdk) : Grpc.Application.ApplicationBase
{
    public override async Task<StringValue> GetStatus(Empty request, ServerCallContext context)
        => new() { Value = await sdk.GetStatusAsync(context.CancellationToken) };
}
