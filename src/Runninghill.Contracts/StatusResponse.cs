namespace Runninghill.Contracts;

// Transport contract; persistence entities and UI state do not belong here.
public sealed record StatusResponse(string Message);
