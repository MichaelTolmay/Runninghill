using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Runninghill.Web.Components;

/// <summary>Keeps rendering failures readable and links the browser's report to a safe app log entry.</summary>
public sealed class AppErrorBoundary : ErrorBoundary
{
    [Inject] private ILogger<AppErrorBoundary> Logger { get; set; } = null!;
    [Inject] private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>Records the exception category and stack, never private messages, tokens, or form values.</summary>
    protected override async Task OnErrorAsync(Exception exception)
    {
        var reference = Guid.NewGuid().ToString("N");
        Logger.LogError("Page rendering failed. Code: RH-WEB-RENDER; Reference: {Reference}; type: {ErrorType}; stack: {StackTrace}",
            reference, exception.GetType().Name, exception.StackTrace);
        try
        {
            await JavaScript.InvokeVoidAsync("runninghillErrors.report", "RH-WEB-RENDER", exception.GetType().Name, reference);
        }
        catch (JSException)
        {
            // The boundary's HTML still offers recovery when JavaScript itself is unavailable.
        }
    }
}
