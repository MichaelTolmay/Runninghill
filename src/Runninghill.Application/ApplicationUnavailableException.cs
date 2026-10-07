namespace Runninghill.Application;

// A known temporary failure. Each transport turns this into its own friendly unavailable reply.
public sealed class ApplicationUnavailableException()
    : Exception("The application is temporarily unavailable.");
