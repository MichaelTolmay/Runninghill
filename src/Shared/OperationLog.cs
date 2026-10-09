using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Runninghill.Diagnostics;

/// <summary>
/// Records named operations without logging their input values. Shared source keeps the same
/// event format across hosts without adding an SDK or another assembly.
/// </summary>
internal sealed class OperationLog : IDisposable
{
    private static readonly Action<ILogger, string, long?, int, Exception?> Data =
        LoggerMessage.Define<string, long?, int>(LogLevel.Information, new(1003, "CollectionResult"),
            "Collection event {Operation}; record ID {RecordId}; result count {Count}");
    private static readonly Action<ILogger, string, string, string, double, Exception?> Information =
        LoggerMessage.Define<string, string, string, double>(LogLevel.Information, new(1000, "Operation"),
            "Operation {Operation}; reference {Reference}; outcome {Outcome}; elapsed {ElapsedMs} ms");
    private static readonly Action<ILogger, string, string, string, double, Exception?> Warning =
        LoggerMessage.Define<string, string, string, double>(LogLevel.Warning, new(1001, "OperationFailed"),
            "Operation {Operation}; reference {Reference}; outcome {Outcome}; elapsed {ElapsedMs} ms");
    private static readonly Action<ILogger, string, string, string, double, Exception?> Debug =
        LoggerMessage.Define<string, string, string, double>(LogLevel.Debug, new(1002, "DiagnosticOperation"),
            "Operation {Operation}; reference {Reference}; outcome {Outcome}; elapsed {ElapsedMs} ms");
    private readonly ILogger logger;
    private readonly string operation;
    private readonly string reference;
    private readonly bool diagnostic;
    private readonly CancellationToken cancellation;
    private readonly long started = Stopwatch.GetTimestamp();
    private string outcome = "Failed";
    private bool disposed;

    /// <summary>Starts an operation using a fixed code name and the current trace or a new reference.</summary>
    public OperationLog(ILogger logger, string operation, bool diagnostic = false, string? reference = null,
        CancellationToken cancellation = default)
    {
        this.logger = logger;
        this.operation = operation;
        this.diagnostic = diagnostic;
        this.cancellation = cancellation;
        this.reference = reference ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        (diagnostic ? Debug : Information)(logger, operation, this.reference, "Started", 0, null);
    }

    /// <summary>Marks a successful result and returns it unchanged to the caller.</summary>
    public T Complete<T>(T result) { Complete(); return result; }

    /// <summary>Records a fixed outcome such as Completed, NotFound, Cancelled or Rejected.</summary>
    public void Complete(string result = "Completed") => outcome = result;

    /// <summary>Writes the final outcome once, including failures that leave a using block early.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (outcome == "Failed" && cancellation.IsCancellationRequested) outcome = "Cancelled";
        var write = outcome == "Failed" ? Warning : diagnostic ? Debug : Information;
        write(logger, operation, reference, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds, null);
    }

    /// <summary>Records a local action by its code name, never by visible user text or input.</summary>
    public static void Event(ILogger logger, [CallerMemberName] string operation = "", bool diagnostic = false)
        => (diagnostic ? Debug : Information)(logger, operation, Activity.Current?.TraceId.ToString() ?? "local", "Performed", 0, null);

    /// <summary>Links a client operation to the server reply without logging the URL, headers or body.</summary>
    public static void Response(ILogger logger, int status, string? reference)
        => Information(logger, "ServiceResponse", reference ?? "unavailable", $"HTTP {status}", 0, null);

    /// <summary>Records numeric result metadata without exposing a word, sentence or search string.</summary>
    public static void Record(ILogger logger, string operation, long? recordId, int count)
        => Data(logger, operation, recordId, count, null);
}
