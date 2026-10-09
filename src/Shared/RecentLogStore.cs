using Microsoft.Extensions.Logging;
using Runninghill.Contracts;

namespace Runninghill.Diagnostics;

/// <summary>Keeps a fixed-size ring of application diagnostics for a viewer, independent of console/file output.</summary>
public sealed class RecentLogStore : ILoggerProvider
{
    public const int Capacity = 1000;
    public const int PageSize = 20;
    private readonly object gate = new();
    private readonly LogEntry?[] entries = new LogEntry[Capacity];
    private long lastId;
    private int count;

    /// <summary>Appends a bounded record; the oldest record is replaced when the ring fills.</summary>
    public void Add(DateTimeOffset timestamp, string level, string category, int eventId, string message)
    {
        lock (gate)
        {
            var id = ++lastId;
            entries[(id - 1) % Capacity] = new(id, timestamp, level, Limit(category, 120), eventId, Limit(message, 1500));
            count = Math.Min(count + 1, Capacity);
        }
    }

    /// <summary>Returns a small, newest-first page, optionally filtered by exact severity and text.</summary>
    public LogPage Read(long before = 0, string level = "", string search = "")
    {
        if (before < 0 || search.Length > 120 || !IsLevel(level)) throw new ArgumentException("Invalid log filter.");
        var result = new List<LogEntry>(PageSize);
        lock (gate)
        {
            var newest = before == 0 ? lastId : Math.Min(before - 1, lastId);
            var oldest = lastId - count + 1;
            for (var id = newest; id >= oldest; id--)
            {
                var entry = entries[(id - 1) % Capacity]!;
                if (level.Length != 0 && entry.Level != level) continue;
                if (search.Length != 0 && !entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && !entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                // Look ahead one match so the final page never advertises a nonexistent next page.
                if (result.Count == PageSize) return new(result.ToArray(), result[^1].Id, count);
                result.Add(entry);
            }
            return new(result.ToArray(), null, count);
        }
    }

    /// <summary>Accepts only the fixed severity names displayed by the viewer.</summary>
    public static bool IsLevel(string level) => level is "" or "Trace" or "Debug" or "Information" or "Warning" or "Error" or "Critical";

    /// <summary>Shortens viewer text so twenty records fit within the clients' bounded reply buffer.</summary>
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";

    /// <summary>Creates a logger that captures only application-owned messages, not framework payloads.</summary>
    public ILogger CreateLogger(string categoryName) => new ViewerLogger(this, categoryName);

    /// <summary>The ring owns no external resources and can remain readable after provider shutdown.</summary>
    public void Dispose() { }

    /// <summary>Copies safe application events into the bounded viewer ring.</summary>
    private sealed class ViewerLogger(RecentLogStore store, string category) : ILogger
    {
        /// <summary>Application messages carry their own diagnostic references.</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>Ignores framework messages and disabled severity while preserving application events.</summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && category.StartsWith("Runninghill", StringComparison.Ordinal);

        /// <summary>Captures the formatted message without appending raw exception details.</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) store.Add(DateTimeOffset.UtcNow, logLevel.ToString(), category, eventId.Id, formatter(state, null));
        }
    }
}
