using static Runninghill.Contracts.AppText;
using Microsoft.AspNetCore.Diagnostics;
using Runninghill.Application;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Runninghill.Service;

/// <summary>Keeps private error details in server logs and gives users a reference to report.</summary>
public sealed partial class ApiErrors(ILogger<ApiErrors> logger) : IExceptionHandler
{
    /// <summary>
    /// Turns expected validation errors and unexpected failures into safe HTTP problem replies, keeping
    /// private diagnostics in server logs.
    /// </summary>
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException invalidRequest)
        {
            // Invalid JSON is a caller mistake, not a broken service. Never echo the raw body.
            await Results.Problem(statusCode: invalidRequest.StatusCode,
                title: "Please check the request.",
                detail: "Send valid JSON with the documented fields and values, then try again.")
                .ExecuteAsync(context);
            return true;
        }
        if (exception is CollectionException expected)
        {
            await Results.Problem(statusCode: expected.StatusCode, title: "Please check your collection.",
                detail: expected.Message).ExecuteAsync(context);
            return true;
        }
        if (exception is DbException or DbUpdateException or TimeoutException)
        {
            LogUnexpectedFailure(logger, context.TraceIdentifier, exception.GetType().Name, exception.StackTrace);
            await Results.Problem(statusCode: 503, title: "Your collection is temporarily unavailable.",
                detail: "Please try again shortly. If a save was interrupted, refresh your collection before repeating it.").ExecuteAsync(context);
            return true;
        }
        // The reference is made by the server, not copied from a caller's untrusted header.
        LogUnexpectedFailure(logger, context.TraceIdentifier, exception.GetType().Name, exception.StackTrace);
        await Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "Something went wrong in the service.",
            detail: "Please try again. If it keeps happening, share the request reference with support.")
            .ExecuteAsync(context);
        return true;
    }

    /// <summary>
    /// Adds the server request reference to a problem reply and replaces unexpected-error details with
    /// safe guidance.
    /// </summary>
    public static void DescribeProblem(ProblemDetailsContext context)
    {
        context.ProblemDetails.Extensions["requestId"] = context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["errorCode"] = "HTTP " + (context.ProblemDetails.Status ?? 500);
        // Do not send exception messages, SQL, passwords, or stack traces to a client.
        if (context.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            context.ProblemDetails.Title = "Something went wrong in the service.";
            context.ProblemDetails.Detail = "Please try again. If it keeps happening, share the request reference with support.";
        }
        // Localize at the transport boundary. Domain exceptions keep stable English messages
        // for diagnostics; request culture is isolated by ASP.NET Core's async context.
        if (context.ProblemDetails.Title is { } title) context.ProblemDetails.Title = T(title);
        if (context.ProblemDetails.Detail is { } detail) context.ProblemDetails.Detail = T(detail);
    }

    /// <summary>
    /// Records an exception with its request reference so operators can match a user report to server
    /// diagnostics.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected service failure. Request reference: {RequestId}; type: {ErrorType}; stack: {StackTrace}")]
    private static partial void LogUnexpectedFailure(ILogger logger, string requestId, string errorType, string? stackTrace);
}
