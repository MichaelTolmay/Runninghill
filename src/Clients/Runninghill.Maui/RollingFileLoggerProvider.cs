using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Runninghill.Maui;

/// <summary>
/// Keeps a bounded local diagnostic log in Debug and Release. A background reader writes files
/// so taps and network callbacks never wait for disk access. This is not a durable audit ledger.
/// </summary>
internal sealed class RollingFileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private readonly string directory;
    private readonly long maxBytes;
    private readonly Channel<string> queue = Channel.CreateBounded<string>(new BoundedChannelOptions(1024)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task writer;
    private long dropped;
    private int stopped;

    /// <summary>Starts a background writer; a missing or unwritable directory does not prevent startup.</summary>
    public RollingFileLoggerProvider(string directory, long maxBytes = 2 * 1024 * 1024)
    {
        this.directory = directory;
        this.maxBytes = maxBytes;
        writer = Task.Run(WriteAsync);
    }

    /// <summary>Creates a lightweight logger for an application category.</summary>
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>Queues a bounded JSON line, counting overflow instead of blocking the calling thread.</summary>
    private void Enqueue(string category, LogLevel level, EventId eventId, string message)
    {
        // A malformed third-party message must not fill storage with a single enormous record.
        if (message.Length > 8192) message = message[..8192];
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("timestamp", DateTimeOffset.UtcNow);
            json.WriteString("level", level.ToString());
            json.WriteString("category", category);
            json.WriteNumber("eventId", eventId.Id);
            json.WriteString("message", message);
            json.WriteEndObject();
        }
        if (!queue.Writer.TryWrite(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length)))
            Interlocked.Increment(ref dropped);
    }

    /// <summary>Appends queued records and retains the current file plus three older files.</summary>
    private async Task WriteAsync()
    {
        while (await queue.Reader.WaitToReadAsync())
        {
            long lost = 0;
            var attempted = 0;
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "events.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length >= maxBytes)
                {
                    File.Delete(path + ".3");
                    for (var index = 2; index >= 1; index--)
                        if (File.Exists(path + "." + index)) File.Move(path + "." + index, path + "." + (index + 1));
                    File.Move(path, path + ".1");
                }
                lost = Interlocked.Exchange(ref dropped, 0);
                // One file open and flush per bounded batch keeps rapid actions inexpensive.
                await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                    16 * 1024, FileOptions.Asynchronous);
                await using var output = new StreamWriter(stream, new UTF8Encoding(false));
                if (lost > 0)
                    await output.WriteLineAsync($"{{\"event\":\"LogRecordsDropped\",\"count\":{lost}}}");
                var bytes = stream.Length;
                for (var count = 0; count < 64 && queue.Reader.TryRead(out var line); count++)
                {
                    attempted++;
                    await output.WriteLineAsync(line);
                    bytes += Encoding.UTF8.GetByteCount(line) + 1;
                    if (bytes >= maxBytes) break;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Disk failures must not break the app. Count missed entries and try the next one.
                // Drain the current backlog so a full disk cannot cause a tight retry loop.
                while (queue.Reader.TryRead(out _)) Interlocked.Increment(ref dropped);
                Interlocked.Add(ref dropped, lost + attempted);
                System.Diagnostics.Debug.WriteLine("Runninghill could not write a diagnostic log: " + error.GetType().Name);
            }
        }
    }

    /// <summary>Closes the queue; already queued records can finish without blocking the UI thread.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) == 0) queue.Writer.TryComplete();
    }

    /// <summary>Waits for queued records to finish when the host can perform an asynchronous shutdown.</summary>
    public async ValueTask DisposeAsync() { Dispose(); await writer.ConfigureAwait(false); }

    /// <summary>Forwards application messages to the bounded queue without retaining exception objects.</summary>
    private sealed class FileLogger(RollingFileLoggerProvider owner, string category) : ILogger
    {
        /// <summary>Scopes are represented by explicit operation references in application records.</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>Only application categories are persisted; framework payloads may contain private data.</summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None
            && category.StartsWith("Runninghill", StringComparison.Ordinal);

        /// <summary>Formats enabled messages only; callers must use safe fixed event fields.</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Enqueue(category, logLevel, eventId, formatter(state, null));
        }
    }
}
