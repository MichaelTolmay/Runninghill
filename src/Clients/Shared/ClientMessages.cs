using System.Net;

namespace Runninghill.Clients;

// Shared wording, compiled into each client. This contains no network calls or business rules.
internal static class ClientMessages
{
    public const string InvalidReply = "The service sent a reply this app cannot read. Please try again. If it continues, tell support which app version you are using.";
    public const string TimedOut = "The service took too long to reply. Check your connection and try again.";
    public const string Unexpected = "Something went wrong in this app. Please try again. If it continues, contact support and describe what you were doing.";

    public static string ForStatus(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.Unauthorized => "Your access token is missing, expired, or invalid. Please enter a new access token and try again.",
        HttpStatusCode.Forbidden => "Your account does not have permission to check the service. Ask an administrator for status.read access.",
        HttpStatusCode.NotFound => "The service address was reached, but this feature was not found. Check the service URL and app version.",
        HttpStatusCode.TooManyRequests => "The service is busy. Please wait a few seconds and try again.",
        HttpStatusCode.ServiceUnavailable => "The service cannot reach its data right now. Please try again shortly.",
        HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout => TimedOut,
        null => "Could not reach the service. Check your internet connection and service address, then try again.",
        _ => "The service could not complete the request. Please try again. If it continues, contact support."
    };

    public static string ForRequestFailure(HttpRequestException error) => error.HttpRequestError switch
    {
        HttpRequestError.ConfigurationLimitExceeded => "The service reply was larger than this app allows. Contact support and report this message.",
        HttpRequestError.SecureConnectionError => "Could not verify a secure connection. Check your device's date and time, then ask support to check the service certificate.",
        HttpRequestError.NameResolutionError => "The service address could not be found. Check the address and your internet connection.",
        HttpRequestError.InvalidResponse or HttpRequestError.ResponseEnded or HttpRequestError.HttpProtocolError => InvalidReply,
        _ => ForStatus(error.StatusCode)
    };

    public static string WithReference(string message, string? requestId) =>
        requestId is null ? message : $"{message} Request reference: {requestId}. Share this reference with support.";

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
