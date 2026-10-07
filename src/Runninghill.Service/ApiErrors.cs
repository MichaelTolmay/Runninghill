using Microsoft.AspNetCore.Diagnostics;

namespace Runninghill.Service;

/// <summary>Keeps private error details in server logs and gives users a reference to report.</summary>
public sealed partial class ApiErrors(ILogger<ApiErrors> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        // The reference is made by the server, not copied from a caller's untrusted header.
        LogUnexpectedFailure(logger, context.TraceIdentifier, exception);
        await Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "Something went wrong in the service.",
            detail: "Please try again. If it keeps happening, share the request reference with support.")
            .ExecuteAsync(context);
        return true;
    }

    public static void DescribeProblem(ProblemDetailsContext context)
    {
        context.ProblemDetails.Extensions["requestId"] = context.HttpContext.TraceIdentifier;
        // Do not send exception messages, SQL, passwords, or stack traces to a client.
        if (context.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            context.ProblemDetails.Title = "Something went wrong in the service.";
            context.ProblemDetails.Detail = "Please try again. If it keeps happening, share the request reference with support.";
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected service failure. Request reference: {RequestId}")]
    private static partial void LogUnexpectedFailure(ILogger logger, string requestId, Exception exception);
}
