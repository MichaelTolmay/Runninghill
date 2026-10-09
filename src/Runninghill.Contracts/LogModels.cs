namespace Runninghill.Contracts;

/// <summary>A safe diagnostic record for display; message and category lengths are bounded.</summary>
public sealed record LogEntry(long Id, DateTimeOffset Timestamp, string Level, string Category, int EventId, string Message);

/// <summary>A newest-first log page with a bookmark for older entries still retained in memory.</summary>
public sealed record LogPage(LogEntry[] Items, long? NextBefore, int RetainedCount);
