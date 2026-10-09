using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Runninghill.Contracts;
using Runninghill.Diagnostics;
using Xunit;

namespace Runninghill.Tests;

/// <summary>Checks bounded log storage, stable paging and diagnostic permission at the HTTP boundary.</summary>
public sealed class LogViewerTests
{
    /// <summary>Replacing old entries bounds memory; new writes do not shift an older page bookmark.</summary>
    [Fact]
    public void RingRemainsBoundedAndPagingDoesNotRepeatNewerRecords()
    {
        using var store = new RecentLogStore();
        for (var i = 0; i < 1050; i++) store.Add(DateTimeOffset.UtcNow, "Information", "Runninghill.Test", 1, "test");
        var first = store.Read();
        Assert.Equal(1000, first.RetainedCount);
        Assert.Equal(20, first.Items.Length);
        Assert.Equal(1050, first.Items[0].Id);
        store.Add(DateTimeOffset.UtcNow, "Information", "Runninghill.Test", 1, "new");
        var next = store.Read(first.NextBefore!.Value);
        Assert.Equal(1030, next.Items[0].Id);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(next.Items.Select(item => item.Id)));
        var last = store.Read(60);
        Assert.Equal(52, last.Items[^1].Id);
        Assert.Null(last.NextBefore);
    }

    /// <summary>Search ignores case, severity is exact, and both stored text and result count have limits.</summary>
    [Fact]
    public void FiltersAndMessageLimitsWorkTogether()
    {
        using var store = new RecentLogStore();
        store.Add(DateTimeOffset.UtcNow, "Warning", "Runninghill.Collection", 1001, "Failed reference-123");
        store.Add(DateTimeOffset.UtcNow, "Information", "Runninghill.Collection", 1000, "Completed reference-123");
        Assert.Single(store.Read(level: "Warning", search: "REFERENCE-123").Items);
        Assert.Equal(2, store.Read(search: "collection").Items.Length);
        Assert.Empty(store.Read(search: "missing").Items);
        store.Add(DateTimeOffset.UtcNow, "Error", new string('a', 500), 1, new string('b', 9000));
        var clipped = store.Read().Items[0];
        Assert.Equal(120, clipped.Category.Length);
        Assert.Equal(1500, clipped.Message.Length);
        Assert.Throws<ArgumentException>(() => store.Read(before: -1));
        Assert.Throws<ArgumentException>(() => store.Read(level: "Invalid"));
        Assert.Throws<ArgumentException>(() => store.Read(search: new string('x', 121)));
    }

    /// <summary>Framework payloads and raw exception messages do not enter the viewer provider.</summary>
    [Fact]
    public void ProviderOnlyCapturesApplicationMessagesWithoutExceptionText()
    {
        using var store = new RecentLogStore();
        store.CreateLogger("Microsoft.Test").LogInformation("private framework payload");
        store.CreateLogger("Runninghill.Test").LogError(new Exception("private exception payload"), "Safe outcome");
        var entry = Assert.Single(store.Read().Items);
        Assert.Equal("Error", entry.Level);
        Assert.Equal("Safe outcome", entry.Message);
    }

    /// <summary>Concurrent requests cannot leave holes or duplicate IDs in the ring.</summary>
    [Fact]
    public void ConcurrentWritesKeepConsistentPages()
    {
        using var store = new RecentLogStore();
        Parallel.For(0, 4000, i =>
        {
            store.Add(DateTimeOffset.UtcNow, "Information", "Runninghill.Test", i, "test");
            Assert.InRange(store.Read().Items.Length, 1, 20);
        });
        var page = store.Read();
        Assert.Equal(1000, page.RetainedCount);
        Assert.Equal(Enumerable.Range(3981, 20).Reverse().Select(i => (long)i), page.Items.Select(item => item.Id));
    }

    /// <summary>A normal collection token, unrelated scope or lookalike permission cannot read service logs.</summary>
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("words.read sentences.read", HttpStatusCode.Forbidden)]
    [InlineData("logs.read.extra", HttpStatusCode.Forbidden)]
    public async Task ReadingServiceLogsRequiresExactPermission(string? scope, HttpStatusCode status)
    {
        await using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        if (scope is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token(scope));
        using var response = await client.GetAsync("/api/logs");
        Assert.Equal(status, response.StatusCode);
    }

    /// <summary>Authorized callers receive twenty safe records with a stable next page and no cache storage.</summary>
    [Fact]
    public async Task AuthorizedViewerHasSmallPagesAndNoCache()
    {
        await using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        var token = ServiceFactory.Token("logs.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var store = factory.Services.GetRequiredService<RecentLogStore>();
        for (var i = 0; i < 25; i++) store.Add(DateTimeOffset.UtcNow, "Warning", "Runninghill.Test", i, "viewer-test");
        using var response = await client.GetAsync("/api/logs?level=Warning&search=VIEWER-TEST");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
        var first = (await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.LogPage))!;
        Assert.Equal(20, first.Items.Length);
        var next = (await client.GetFromJsonAsync($"/api/logs?level=Warning&search=viewer-test&before={first.NextBefore}", ApiJsonContext.Default.LogPage))!;
        Assert.Equal(5, next.Items.Length);
        Assert.Null(next.NextBefore);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(next.Items.Select(item => item.Id)));
    }

    /// <summary>Invalid or oversized filters are rejected without scanning or exposing private details.</summary>
    [Theory]
    [InlineData("before=-1")]
    [InlineData("level=Invalid")]
    [InlineData("before=invalid")]
    public async Task InvalidFiltersReturnBadRequest(string query)
    {
        await using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.Token("logs.read"));
        using var response = await client.GetAsync("/api/logs?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var oversized = await client.GetAsync("/api/logs?search=" + new string('x', 121));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
    }
}
