using Runninghill.Application;
using Runninghill.Contracts;

namespace Runninghill.Service;

// HTTP only translates messages. Validation and sentence rules belong to WordCollection.
/// <summary>
/// Maps authenticated HTTP collection routes to application operations and public JSON messages.
/// </summary>
public static class WordEndpoints
{
    /// <summary>
    /// Registers word CRUD and sentence routes with their permissions, timeouts and request limits.
    /// Page replies hide the extra look-ahead row.
    /// </summary>
    public static void MapWordEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.RequireRateLimiting("api").WithRequestTimeout(TimeSpan.FromSeconds(10));
        api.MapGet("/statistics", async (WordCollection collection, HttpContext context, CancellationToken cancellation) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var counts = await collection.CountAsync(cancellation);
            return new CollectionStatistics(counts.Words, counts.Sentences, DateTimeOffset.UtcNow);
        }).RequireAuthorization("words.read", "sentences.read");
        api.MapGet("/words", async (long? after, string? search, string? types, WordCollection collection, CancellationToken cancellation) =>
        {
            var rows = await collection.ListAsync(after ?? 0, search, types, cancellation);
            // Hide the extra look-ahead row. The last visible ID becomes the next page's
            // bookmark; using the hidden row's ID would accidentally skip that word.
            return new WordPage(rows.Take(WordCollection.PageSize).Select(ToResponse).ToArray(),
                rows.Length > WordCollection.PageSize ? rows[WordCollection.PageSize - 1].Id : null);
        }).RequireAuthorization("words.read");
        api.MapGet("/words/{id:long}", async (long id, WordCollection collection, CancellationToken cancellation) =>
            ToResponse(await collection.GetAsync(id, cancellation))).RequireAuthorization("words.read");
        api.MapPost("/words", async (SaveWordRequest request, WordCollection collection, CancellationToken cancellation) =>
        {
            var word = ToResponse(await collection.CreateAsync(request.Word, request.Type, cancellation));
            return TypedResults.Created($"/api/words/{word.Id}", word);
        }).RequireAuthorization("words.write");
        api.MapPut("/words/{id:long}", async (long id, SaveWordRequest request, WordCollection collection, CancellationToken cancellation) =>
            ToResponse(await collection.UpdateAsync(id, request.Word, request.Type, cancellation))).RequireAuthorization("words.write");
        api.MapDelete("/words/{id:long}", async (long id, WordCollection collection, CancellationToken cancellation) =>
        {
            await collection.DeleteAsync(id, cancellation);
            return TypedResults.NoContent();
        }).RequireAuthorization("words.write");
        api.MapGet("/sentences", async (long? after, WordCollection collection, CancellationToken cancellation) =>
        {
            var rows = await collection.ListSentencesAsync(after ?? 0, cancellation);
            return new SentencePage(rows.Take(WordCollection.SentencePageSize).Select(ToResponse).ToArray(),
                rows.Length > WordCollection.SentencePageSize ? rows[WordCollection.SentencePageSize - 1].Id : null);
        }).RequireAuthorization("sentences.read");
        api.MapPost("/sentences", async (SaveSentenceRequest request, WordCollection collection, CancellationToken cancellation) =>
            TypedResults.Ok(ToResponse(await collection.SaveSentenceAsync(request.WordIds, request.RequestId, cancellation))))
            .RequireAuthorization("sentences.write");
    }

    /// <summary>
    /// Converts an application result to its small public response without database-specific fields.
    /// </summary>
    private static WordResponse ToResponse(WordEntry word) => new(word.Id, word.Word, word.Type);

    /// <summary>
    /// Converts an application result to its small public response without database-specific fields.
    /// </summary>
    private static SentenceResponse ToResponse(SentenceEntry sentence) => new(sentence.Id, sentence.Text, sentence.CreatedAt);
}
