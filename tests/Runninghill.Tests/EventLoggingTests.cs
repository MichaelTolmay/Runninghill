using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Runninghill.Database;
using Runninghill.Contracts;
using Runninghill.Diagnostics;
using Runninghill.Maui;
using Xunit;

namespace Runninghill.Tests;

/// <summary>Checks useful event coverage, privacy and the native log writer's storage limits.</summary>
public sealed class EventLoggingTests
{
    /// <summary>Real storage logs each collection operation and retry without recording stored text.</summary>
    [Fact]
    public async Task DatabaseEventsIncludeResultsButNotStoredWords()
    {
        var file = Path.Combine(Path.GetTempPath(), "runninghill-event-test-" + Guid.NewGuid() + ".db");
        var logs = new RecordingLogProvider();
        await using var services = new ServiceCollection().AddLogging(b => b.AddProvider(logs))
            .AddCollectionDatabase(_ => DatabaseSettings.Parse("SQLite", "Data Source=" + file)).BuildServiceProvider();
        try
        {
            await services.GetRequiredService<DatabaseMigrator>().MigrateAsync();
            var repository = services.GetRequiredService<Runninghill.Application.IWordRepository>();
            var word = await repository.CreateAsync("privatespellingmarker", "Noun", default);
            await repository.ListAsync(0, "private", [], 51, default);
            await repository.GetAsync(word.Id, default);
            await repository.UpdateAsync(word.Id, "privateupdatedmarker", "Verb", default);
            var receipt = Guid.NewGuid();
            await repository.SaveSentenceAsync([word.Id], receipt, default);
            await repository.SaveSentenceAsync([word.Id], receipt, default);
            await repository.ListSentencesAsync(0, 11, default);
            await repository.DeleteAsync(word.Id, default);
            var messages = string.Join('\n', logs.Messages);
            foreach (var name in new[] { "WordCreated", "WordsListed", "WordRead", "WordUpdated", "SentenceSaved", "SentenceRetry", "SentencesListed", "WordDeleted" })
                Assert.Contains(name, messages);
            Assert.DoesNotContain("privatespellingmarker", messages);
            Assert.DoesNotContain("privateupdatedmarker", messages);
        }
        finally
        {
            await using var db = await services.GetRequiredService<CollectionContextFactory>().CreateAsync();
            await db.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>Every started operation has one final record, even when a using block exits on failure.</summary>
    [Fact]
    public void OperationsRecordSuccessFailureAndCancellationExactlyOnce()
    {
        var logs = new RecordingLogProvider();
        var logger = logs.CreateLogger("Runninghill.Test");
        using (var completed = new OperationLog(logger, "Save", reference: "ref-1")) completed.Complete();
        var failed = new OperationLog(logger, "Read", reference: "ref-2");
        failed.Dispose();
        failed.Dispose();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using (new OperationLog(logger, "Search", cancellation: cancellation.Token)) { }
        Assert.Equal(6, logs.Messages.Count);
        Assert.Single(logs.Messages, value => value.Contains("outcome Completed"));
        Assert.Single(logs.Messages, value => value.Contains("outcome Failed"));
        Assert.Single(logs.Messages, value => value.Contains("outcome Cancelled"));
        Assert.Equal(2, logs.Messages.Count(value => value.Contains("ref-1")));
    }

    /// <summary>Rejected requests still get completion logs without exposing tokens, query strings or bodies.</summary>
    [Fact]
    public async Task RequestLogsCoverAuthenticationAndValidationWithoutPrivateInputs()
    {
        var logs = new RecordingLogProvider();
        await using var factory = new ServiceFactory(repository: new RecordingRepository(), logs: logs);
        using var client = factory.CreateClient();
        using var unauthorized = await client.GetAsync("/api/words?search=private-query-marker");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var token = ServiceFactory.Token("words.write");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var invalid = await client.PostAsJsonAsync("/api/words",
            new SaveWordRequest("private body marker", "Noun"), ApiJsonContext.Default.SaveWordRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var messages = string.Join('\n', logs.Messages);
        Assert.Contains("HTTP: 401", messages);
        Assert.Contains("HTTP: 400", messages);
        Assert.Contains(invalid.Headers.GetValues("X-Request-ID").Single(), messages);
        Assert.DoesNotContain(token, messages);
        Assert.DoesNotContain("private-query-marker", messages);
        Assert.DoesNotContain("private body marker", messages);
    }

    /// <summary>Unexpected failures retain their type and reference without writing private exception text.</summary>
    [Fact]
    public async Task ServerFailureLogsDoNotExposeExceptionMessages()
    {
        var logs = new RecordingLogProvider();
        await using var factory = new ServiceFactory(failure: new InvalidOperationException("private-password-marker"), logs: logs);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token());
        using var response = await client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var messages = string.Join('\n', logs.Messages);
        Assert.Contains("InvalidOperationException", messages);
        Assert.Contains("HTTP: 500", messages);
        Assert.Contains(response.Headers.GetValues("X-Request-ID").Single(), messages);
        Assert.DoesNotContain("private-password-marker", messages);
    }

    /// <summary>Native logs flush on disposal, remain valid JSON and rotate instead of growing forever.</summary>
    [Fact]
    public async Task NativeFilesRotateAndKeepJsonValid()
    {
        var directory = Path.Combine(Path.GetTempPath(), "runninghill-logs-" + Guid.NewGuid());
        try
        {
            await using (var provider = new RollingFileLoggerProvider(directory, maxBytes: 512))
            {
                var logger = provider.CreateLogger("Runninghill.Test");
                for (var index = 0; index < 100; index++)
                    logger.LogInformation("Event {Index}: newline\nquote\"", index);
                provider.CreateLogger("Framework.Test").LogError("private-framework-message");
            }
            var files = Directory.GetFiles(directory);
            Assert.InRange(files.Length, 2, 4);
            var content = string.Join('\n', files.Select(File.ReadAllText));
            Assert.Contains("Event 99", content);
            Assert.DoesNotContain("private-framework-message", content);
            foreach (var line in files.SelectMany(File.ReadAllLines))
                using (JsonDocument.Parse(line)) { }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    /// <summary>An unwritable destination does not turn a user action into an application failure.</summary>
    [Fact]
    public async Task UnavailableLogDirectoryDoesNotCrashTheWriter()
    {
        var file = Path.GetTempFileName();
        try
        {
            await using var provider = new RollingFileLoggerProvider(file);
            provider.CreateLogger("Runninghill.Test").LogInformation("Attempted action");
        }
        finally { File.Delete(file); }
    }
}

/// <summary>Captures application event messages for assertions without storing them on disk.</summary>
internal sealed class RecordingLogProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    /// <summary>Creates a recorder for one category.</summary>
    public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

    /// <summary>The in-memory recorder has no external resources to release.</summary>
    public void Dispose() { }

    /// <summary>Records only application categories so assertions focus on the controlled event format.</summary>
    private sealed class Recorder(RecordingLogProvider owner, string category) : ILogger
    {
        /// <summary>References are explicit in records, so a separate scope store is unnecessary here.</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>Enables application logs at every severity for tests.</summary>
        public bool IsEnabled(LogLevel logLevel) => category.StartsWith("Runninghill", StringComparison.Ordinal);

        /// <summary>Captures formatted records, including exception text if a caller accidentally supplies it.</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Messages.Enqueue(formatter(state, exception) + (exception?.ToString() ?? ""));
        }
    }
}
