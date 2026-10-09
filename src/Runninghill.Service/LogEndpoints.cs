using Runninghill.Contracts;
using Runninghill.Diagnostics;

namespace Runninghill.Service;

/// <summary>Exposes only the bounded, safe application log buffer to callers with diagnostic permission.</summary>
public static class LogEndpoints
{
    /// <summary>Maps an authenticated log page; raw files, Docker output and arbitrary paths are never exposed.</summary>
    public static void MapLogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/logs", (HttpContext context, RecentLogStore logs, long? before, string? level, string? search) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            level ??= "";
            search ??= "";
            if (before < 0 || search.Length > 120 || !RecentLogStore.IsLevel(level))
                return Results.Problem(statusCode: 400, title: "Check the log filters.",
                    detail: "Use a valid severity, a positive page bookmark and search text up to 120 characters.");
            return Results.Json(logs.Read(before ?? 0, level, search), ApiJsonContext.Default.LogPage);
        }).RequireAuthorization("logs.read").RequireRateLimiting("api").WithRequestTimeout(TimeSpan.FromSeconds(10));
    }
}
