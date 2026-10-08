using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Components;
using Runninghill.Clients;
using Runninghill.Contracts;

namespace Runninghill.Web.Components.Pages;

public partial class Home
{
    [Inject] private HttpClient Client { get; set; } = null!;
    [Inject] private ILogger<Home> Logger { get; set; } = null!;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<string> selectedTypes = new();
    private readonly Stack<long> previous = new();
    private readonly List<WordResponse> chosen = [];
    private WordResponse[] words = [];
    private SentenceResponse[] sentences = [];
    private string token = "", draftWord = "", draftType = "Noun", search = "";
    private string appliedSearch = "", appliedTypes = "";
    private string message = "Connect to load your collection.";
    private bool connected, busy, error;
    private long after, sentenceAfter;
    private long? editingId, pendingDelete, nextAfter, nextSentenceAfter;
    private Guid sentenceRequestId = Guid.NewGuid();
    private ElementReference wordInput;

    // Each screen has one operation in flight. This prevents double submissions and stale
    // responses replacing a newer search. A failure keeps all unsaved form values intact.
    private async Task RunAsync(Func<Task> action, string success)
    {
        if (busy) return;
        busy = true;
        error = false;
        message = "Working…";
        try { await action(); message = success; }
        catch (ClientFailure exception) { error = true; message = exception.Message; }
        catch (HttpRequestException exception) { error = true; message = ClientMessages.ForRequestFailure(exception); }
        catch (OperationCanceledException) { error = true; message = lifetime.IsCancellationRequested ? "Request cancelled." : ClientMessages.TimedOut; }
        catch (JsonException) { error = true; message = ClientMessages.InvalidReply; }
        catch (FormatException) { error = true; message = "Copy a valid access token and connect again."; }
        catch (Exception exception)
        {
            Logger.LogError("Collection screen error: {ErrorType}", exception.GetType().Name);
            error = true; message = ClientMessages.Unexpected;
        }
        finally { busy = false; }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, JsonTypeInfo<T> resultType, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        using var response = await Client.SendAsync(request, lifetime.Token);
        if (!response.IsSuccessStatusCode)
        {
            var detail = ClientMessages.ForStatus(response.StatusCode);
            if ((int)response.StatusCode is 400 or 404 or 409)
            {
                var problem = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.ApiProblem, lifetime.Token);
                detail = problem?.Detail ?? detail;
            }
            throw new ClientFailure(ClientMessages.WithReference(detail, ClientMessages.ReadReference(response)));
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
        try
        {
            var result = await response.Content.ReadFromJsonAsync(resultType, lifetime.Token) ?? throw new JsonException();
            if (result is WordPage { Items: null } or SentencePage { Items: null }) throw new JsonException();
            return result;
        }
        catch (JsonException)
        {
            throw new ClientFailure(ClientMessages.WithReference(ClientMessages.InvalidReply, ClientMessages.ReadReference(response)));
        }
    }

    private Task ConnectAsync() => RunAsync(async () =>
    {
        ApplyFilterValues();
        await LoadWordsAsync(0);
        connected = true;
        previous.Clear();
        await LoadHistoryAsync(0);
    }, "Your collection is up to date.");

    private async Task LoadWordsAsync(long cursor)
    {
        var page = await SendAsync(HttpMethod.Get,
            $"api/words?after={cursor}&search={Uri.EscapeDataString(appliedSearch)}&types={Uri.EscapeDataString(appliedTypes)}", ApiJsonContext.Default.WordPage);
        if (page.Items is null) throw new JsonException();
        words = page.Items; nextAfter = page.NextAfter; after = cursor; pendingDelete = null;
    }
    private async Task LoadHistoryAsync(long cursor)
    {
        var page = await SendAsync(HttpMethod.Get, $"api/sentences?after={cursor}", ApiJsonContext.Default.SentencePage);
        if (page.Items is null) throw new JsonException();
        sentences = page.Items; nextSentenceAfter = page.NextAfter; sentenceAfter = cursor;
    }
    private void ApplyFilterValues() { appliedSearch = search; appliedTypes = string.Join(',', selectedTypes); }
    private Task SearchAsync() => RunAsync(async () => { ApplyFilterValues(); await LoadWordsAsync(0); previous.Clear(); }, "Filters applied.");
    private Task ResetFiltersAsync()
    {
        search = "";
        selectedTypes.Clear();
        return SearchAsync();
    }
    private string SentenceHelp => !connected ? "Connect to your collection before saving a sentence."
        : busy ? "Please wait for the current request to finish."
        : chosen.Count == 0 ? "Add at least one word using Add to sentence in the word list to enable saving."
        : "Your sentence is ready to save. You can change the word order using the arrows above.";

    private Task NextAsync() => RunAsync(async () => { var old = after; await LoadWordsAsync(nextAfter!.Value); previous.Push(old); }, "Next page loaded.");
    private Task PreviousAsync() => RunAsync(async () => { await LoadWordsAsync(previous.Peek()); previous.Pop(); }, "Previous page loaded.");
    private Task RefreshHistoryAsync() => RunAsync(() => LoadHistoryAsync(0), "Saved sentences refreshed.");
    private Task NextHistoryAsync() => RunAsync(() => LoadHistoryAsync(nextSentenceAfter!.Value), "Next sentences loaded.");
    private void ToggleType(string type) { if (!selectedTypes.Add(type)) selectedTypes.Remove(type); }

    private Task SaveWordAsync() => RunAsync(async () =>
    {
        var saved = await SendAsync(editingId is null ? HttpMethod.Post : HttpMethod.Put,
            editingId is null ? "api/words" : $"api/words/{editingId}", ApiJsonContext.Default.WordResponse,
            JsonContent.Create(new SaveWordRequest(draftWord, draftType), ApiJsonContext.Default.SaveWordRequest));
        for (var i = 0; i < chosen.Count; i++) if (chosen[i].Id == saved.Id) chosen[i] = saved;
        CancelEdit();
        // Show the collection again after saving, rather than searching for only the saved word.
        // Large collections still use Next/Previous; never download every word at once.
        search = ""; selectedTypes.Clear(); previous.Clear(); ApplyFilterValues();
        await LoadWordsAsync(0);
    }, "Word saved. Your collection has been refreshed.");

    private async Task EditAsync(WordResponse word)
    {
        editingId = word.Id; draftWord = word.Word; draftType = word.Type;
        await wordInput.FocusAsync();
    }
    private void CancelEdit() { editingId = null; draftWord = ""; draftType = "Noun"; }
    private Task DeleteAsync(WordResponse word) => RunAsync(async () =>
    {
        await SendAsync(HttpMethod.Delete, $"api/words/{word.Id}", ApiJsonContext.Default.WordResponse);
        chosen.RemoveAll(item => item.Id == word.Id); sentenceRequestId = Guid.NewGuid();
        if (editingId == word.Id) CancelEdit();
        await LoadWordsAsync(after);
    }, "Word deleted. Saved sentences have not changed.");

    private void AddToSentence(WordResponse word)
    {
        if (busy || chosen.Count >= 50) return;
        chosen.Add(word); sentenceRequestId = Guid.NewGuid();
        message = $"Added {word.Word} to your sentence."; error = false;
    }
    private void Move(int index, int change)
    {
        (chosen[index], chosen[index + change]) = (chosen[index + change], chosen[index]);
        sentenceRequestId = Guid.NewGuid();
    }
    private void Remove(int index) { chosen.RemoveAt(index); sentenceRequestId = Guid.NewGuid(); }
    private void ClearSentence() { chosen.Clear(); sentenceRequestId = Guid.NewGuid(); }
    private Task SaveSentenceAsync() => RunAsync(async () =>
    {
        await SendAsync(HttpMethod.Post, "api/sentences", ApiJsonContext.Default.SentenceResponse,
            JsonContent.Create(new SaveSentenceRequest(chosen.Select(w => w.Id).ToArray(), sentenceRequestId), ApiJsonContext.Default.SaveSentenceRequest));
        // Keep the same request ID on failure. Retrying an uncertain save cannot create a duplicate.
        ClearSentence();
        await LoadHistoryAsync(0);
    }, "Sentence saved.");

    private void Disconnect()
    {
        token = ""; connected = false; words = []; sentences = []; previous.Clear();
        nextAfter = nextSentenceAfter = null; pendingDelete = null; after = sentenceAfter = 0;
        CancelEdit(); ClearSentence(); message = "Disconnected. Your saved words are safe in the database.";
    }
    private static string TypeClass(string type) => type.ToLowerInvariant();
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
    private sealed class ClientFailure(string message) : Exception(message);
}
