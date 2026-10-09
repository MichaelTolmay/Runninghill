namespace Runninghill.Contracts;

// The small message sent over the network. Keep database rows and page state out of this model.
/// <summary>
/// Contains the application status message sent to clients over HTTP.
/// </summary>
public sealed record StatusResponse(string Message);
