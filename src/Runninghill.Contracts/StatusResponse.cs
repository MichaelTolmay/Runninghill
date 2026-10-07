namespace Runninghill.Contracts;

// The small message sent over the network. Keep database rows and page state out of this model.
public sealed record StatusResponse(string Message);
