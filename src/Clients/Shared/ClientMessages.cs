using static Runninghill.Contracts.AppText;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Runninghill.Contracts;

namespace Runninghill.Clients;

// Shared error wording, compiled into each client without another assembly.
// Reading a reply here does not send a request or retry a write.
/// <summary>
/// Provides shared, user-friendly error descriptions and safe support references for all clients.
/// </summary>
internal static class ClientMessages
{
#if DEBUG
    // Give a developer time to inspect a server breakpoint before the client gives up.
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
#else
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
#endif
    public static string InvalidReply => WithCode(T("The service sent a reply this app cannot read. Please try again. If it continues, tell support which app version you are using."), "RH-REPLY-INVALID");
    public static string TimedOut => WithCode(T("The service took too long to reply. Check your connection and try again."), "RH-NETWORK-TIMEOUT");
    public static string Unexpected => WithCode(T("This action could not finish because the app encountered a problem. Refresh the collection before repeating a save. If it continues, send support the error code, error type, reference, and the steps you followed."), "RH-APP-UNEXPECTED");

    /// <summary>Shows a stable diagnostic code separately from translated recovery instructions.</summary>
    public static string WithCode(string message, string code) => F($"{message} Error code: {code}.");

    /// <summary>Uses the actual HTTP status, which can be looked up in standard HTTP documentation.</summary>
    public static string WithStatus(string message, HttpStatusCode status) => WithCode(message, $"HTTP {(int)status} ({status})");

    /// <summary>Names the exception category without exposing its potentially private message or response body.</summary>
    public static string ForUnexpected(Exception error) => Unexpected + " " + F($"Error type: {error.GetType().Name}");

    /// <summary>
    /// Explains an HTTP status with a suggested next step, or describes an unreachable service when no
    /// status exists.
    /// </summary>
    public static string ForStatus(HttpStatusCode? status) => status is { } code
        ? WithStatus(ExplainStatus(code), code)
        : WithCode(ExplainStatus(null), "RH-NETWORK");

    /// <summary>Gives recovery advice without inventing a server status when a connection never reached it.</summary>
    private static string ExplainStatus(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.Unauthorized => T("Your access token is missing, expired, or invalid. Please enter a new access token and try again."),
        HttpStatusCode.Forbidden => T("Your account does not have permission for this action. Ask an administrator for the required access (status.read, words.read/write or sentences.read/write)."),
        HttpStatusCode.NotFound => T("The service address was reached, but this feature was not found. Check the service URL and app version."),
        HttpStatusCode.TooManyRequests => T("The service is busy. Please wait a few seconds and try again."),
        HttpStatusCode.ServiceUnavailable => T("The service cannot reach its data right now. Please try again shortly."),
        HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout => T("The service took too long to reply. Check your connection and try again."),
        null => T("Could not reach the service. Check your internet connection and service address, then try again."),
        _ => T("The service could not complete the request. Please try again. If it continues, contact support.")
    };

    /// <summary>
    /// Turns network, certificate and reply failures into specific guidance without exposing private
    /// exception text.
    /// </summary>
    public static string ForRequestFailure(HttpRequestException error) => error.HttpRequestError == HttpRequestError.Unknown
        ? ForStatus(error.StatusCode)
        : WithCode(ExplainRequestFailure(error), "HttpRequestError." + error.HttpRequestError);

    /// <summary>Retains the real .NET network category so support can distinguish DNS, TLS, and broken replies.</summary>
    private static string ExplainRequestFailure(HttpRequestException error) => error.HttpRequestError switch
    {
        HttpRequestError.ConfigurationLimitExceeded => T("The service reply was larger than this app allows. Contact support and report this message."),
        HttpRequestError.SecureConnectionError => T("Could not verify a secure connection. Check your device's date and time, then ask support to check the service certificate."),
        HttpRequestError.NameResolutionError => T("The service address could not be found. Check the address and your internet connection."),
        HttpRequestError.InvalidResponse or HttpRequestError.ResponseEnded or HttpRequestError.HttpProtocolError => InvalidReply,
        _ => ForStatus(error.StatusCode)
    };

    /// <summary>
    /// Adds an available request reference and support guidance to an existing message.
    /// </summary>
    public static string WithReference(string message, string? requestId) =>
        requestId is null ? message : F($"{message} Request reference: {requestId}. Share this reference with support.");

    /// <summary>
    /// Explains that a write succeeded but the following screen refresh failed, helping the user avoid
    /// repeating the write.
    /// </summary>
    public static string AfterConfirmedChange(string? confirmedChange, string error) =>
        confirmedChange is null ? error :
            F($"{confirmedChange} The screen could not finish updating. {error} Refresh the collection before making another change.");

    /// <summary>
    /// Reads trusted validation wording when available, otherwise keeps the status explanation. Invalid
    /// JSON does not discard the support reference, and cancellation still propagates.
    /// </summary>
    public static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        var detail = ForStatus(response.StatusCode);
        // Only validation/conflict replies contain messages deliberately written for users.
        // A proxy might instead send HTML or broken JSON. Keep the status explanation and
        // request reference in that case; do not replace them with a JSON parser error.
        if ((int)response.StatusCode is 400 or 404 or 409)
        {
            try
            {
                var problem = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.ApiProblem, cancellation);
                if (!string.IsNullOrWhiteSpace(problem?.Detail)) detail = WithStatus(problem.Detail, response.StatusCode);
            }
            catch (JsonException) { /* The status and header still give us a useful error. */ }
        }
        // Do not catch cancellation: leaving a page must still stop its pending work.
        return WithReference(detail, ReadReference(response));
    }

    /// <summary>
    /// Reads a short request reference containing only allowed characters, rejecting header text that
    /// could disrupt a screen or terminal.
    /// </summary>
    public static string? ReadReference(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Request-ID", out var values))
            return null;

        var value = values.FirstOrDefault();
        // Even error headers come from outside the app. Reject long values and control characters
        // so a bad server cannot put extra lines or terminal commands into a user's message.
        if (string.IsNullOrEmpty(value) || value.Length > 128)
            return null;
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not ':' and not '.')
                return null;
        }
        return value;
    }
}
