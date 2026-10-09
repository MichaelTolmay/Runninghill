namespace Runninghill.Application;

// A known temporary failure. Each transport turns this into its own friendly unavailable reply.
/// <summary>
/// Signals a temporary application outage that each transport can turn into a friendly unavailable
/// reply.
/// </summary>
public sealed class ApplicationUnavailableException()
    : Exception("The application is temporarily unavailable.");
