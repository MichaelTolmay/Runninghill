using static Runninghill.Contracts.AppText;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Runninghill.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Components;
using Runninghill.Clients;
using Runninghill.Contracts;

namespace Runninghill.Web.Components.Pages;

/// <summary>
/// Manages the browser word collection, sentence draft and history using the authenticated HTTP
/// service.
/// </summary>
public partial class Home
{
    [Inject] private HttpClient Client { get; set; } = null!;
    [Inject] private ILogger<Home> Logger { get; set; } = null!;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<string> selectedTypes = new();
    // A page bookmark is only an ID. Keep the sentence draft separately so paging and
    // filtering do not throw away words already selected for the sentence.
    private readonly Stack<long> previous = new();
    private readonly List<WordResponse> chosen = [];
    private WordResponse[] words = [];
    private SentenceResponse[] sentences = [];
    private string token = "", draftWord = "", draftType = "Noun", search = "";
    private string appliedSearch = "", appliedTypes = "";
    private string message = T("Connect to load your collection.");
    private bool connected, busy, error, showLogs;
    private string activeTheme = "Runninghill_light";

    /// <summary>Opens diagnostics without disposing the collection state or its current connection.</summary>
    private void OpenLogs() { OperationLog.Event(Logger); showLogs = true; }

    /// <summary>Returns to the same collection, keeping drafts, filters and the connection intact.</summary>
    private void CloseLogs() { showLogs = false; OperationLog.Event(Logger); }
    private long after, sentenceAfter;
    private long? editingId, pendingDelete, nextAfter, nextSentenceAfter;
    // A receipt is set only after the server confirms a write, before any refresh starts.
    private string? confirmedChange;
    private Guid sentenceRequestId = Guid.NewGuid();
    private ElementReference wordInput;

    // Each screen has one operation in flight. This prevents double submissions and stale
    // responses replacing a newer search. A failure keeps all unsaved form values intact.
    /// <summary>
    /// Runs one screen operation at a time and converts failures into safe messages while preserving
    /// unsaved input and confirmed-write notices.
    /// </summary>
    private async Task RunAsync(Func<Task> action, string success, [CallerMemberName] string operation = "")
    {
        if (busy) return;
        using var eventLog = new OperationLog(Logger, operation);
        busy = true;
        confirmedChange = null;
        error = false;
        message = T("Working…");
        try { await action(); eventLog.Complete(); message = success; }
        catch (ClientFailure exception) { SetError(exception.Message); }
        catch (HttpRequestException exception) { SetError(ClientMessages.ForRequestFailure(exception)); }
        catch (OperationCanceledException) { eventLog.Complete(lifetime.IsCancellationRequested ? "Cancelled" : "TimedOut"); SetError(lifetime.IsCancellationRequested ? T("Request cancelled.") : ClientMessages.TimedOut); }
        catch (JsonException) { SetError(ClientMessages.InvalidReply); }
        catch (FormatException) { SetError(T("Copy a valid access token and connect again.")); }
        catch (Exception exception)
        {
            // A local reference lets support match the screen to a log entry. Avoid logging
            // exception messages, which can contain a token or private server response.
            var reference = Guid.NewGuid().ToString("N");
            Logger.LogError("Collection screen error. Reference: {Reference}; type: {ErrorType}; stack: {StackTrace}",
                reference, exception.GetType().Name, exception.StackTrace);
            SetError(ClientMessages.WithReference(ClientMessages.ForUnexpected(exception), reference));
        }
        finally { busy = false; confirmedChange = null; }
    }

    /// <summary>
    /// Marks the screen message as an error and preserves any earlier confirmation that a write
    /// succeeded.
    /// </summary>
    private void SetError(string detail)
    {
        error = true;
        message = ClientMessages.AfterConfirmedChange(confirmedChange, detail);
    }

    /// <summary>
    /// Sends one authenticated request using the page lifetime token and generated JSON metadata.
    /// Service errors retain a safe explanation and support reference.
    /// </summary>
    private async Task<T> SendAsync<T>(HttpMethod method, string path, JsonTypeInfo<T> resultType, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.AcceptLanguage.ParseAdd(Runninghill.Contracts.AppText.Language);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        using var response = await Client.SendAsync(request, lifetime.Token);
        OperationLog.Response(Logger, (int)response.StatusCode, ClientMessages.ReadReference(response));
        if (!response.IsSuccessStatusCode)
        {
            throw new ClientFailure(await ClientMessages.ReadErrorAsync(response, lifetime.Token));
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

    /// <summary>
    /// Applies the current filters and loads the first word and history pages with the entered access
    /// token.
    /// </summary>
    private Task ConnectAsync() => RunAsync(async () =>
    {
        // Browser-native validation follows the browser's language. Our feedback follows
        // the language chosen inside the app and is announced by the existing live region.
        if (string.IsNullOrWhiteSpace(token)) throw new ClientFailure(T("Copy a valid access token and connect again."));
        ApplyFilterValues();
        await LoadWordsAsync(0);
        connected = true;
        previous.Clear();
        await LoadHistoryAsync(0);
    }, T("Your collection is up to date."));

    /// <summary>
    /// Loads a filtered word page after the supplied bookmark and clears any pending delete
    /// confirmation.
    /// </summary>
    private async Task LoadWordsAsync(long cursor)
    {
        var page = await SendAsync(HttpMethod.Get,
            $"api/words?after={cursor}&search={Uri.EscapeDataString(appliedSearch)}&types={Uri.EscapeDataString(appliedTypes)}", ApiJsonContext.Default.WordPage);
        if (page.Items is null) throw new JsonException();
        words = page.Items;
        nextAfter = page.NextAfter;
        after = cursor;
        pendingDelete = null;
    }

    /// <summary>
    /// Loads a page of saved sentences and remembers the bookmark for older entries.
    /// </summary>
    private async Task LoadHistoryAsync(long cursor)
    {
        var page = await SendAsync(HttpMethod.Get, $"api/sentences?after={cursor}", ApiJsonContext.Default.SentencePage);
        if (page.Items is null) throw new JsonException();
        sentences = page.Items;
        nextSentenceAfter = page.NextAfter;
        sentenceAfter = cursor;
    }

    /// <summary>
    /// Copies the draft search and checkbox selections into the filters used for subsequent page
    /// requests.
    /// </summary>
    private void ApplyFilterValues() { appliedSearch = search; appliedTypes = string.Join(',', selectedTypes); }

    /// <summary>
    /// Applies the current filter inputs and returns the collection to its first page.
    /// </summary>
    private Task SearchAsync() => RunAsync(async () => { ApplyFilterValues(); await LoadWordsAsync(0); previous.Clear(); }, T("Filters applied."));

    /// <summary>
    /// Clears the search and selected types, then reloads the unfiltered first page.
    /// </summary>
    private Task ResetFiltersAsync()
    {
        search = "";
        selectedTypes.Clear();
        return SearchAsync();
    }
    private string SentenceHelp => !connected ? T("Connect to your collection before saving a sentence.")
        : busy ? T("Please wait for the current request to finish.")
        : chosen.Count == 0 ? T("Add at least one word using Add to sentence in the word list to enable saving.")
        : T("Your sentence is ready to save. You can change the word order using the arrows above.");

    /// <summary>
    /// Loads the next word page and records the previous bookmark only after a successful response.
    /// </summary>
    private Task NextAsync() => RunAsync(async () => { var old = after; await LoadWordsAsync(nextAfter!.Value); previous.Push(old); }, T("Next page loaded."));

    /// <summary>
    /// Loads the preceding word page before removing its stored bookmark.
    /// </summary>
    private Task PreviousAsync() => RunAsync(async () => { await LoadWordsAsync(previous.Peek()); previous.Pop(); }, T("Previous page loaded."));

    /// <summary>
    /// Reloads the newest saved sentences from the first history page.
    /// </summary>
    private Task RefreshHistoryAsync() => RunAsync(() => LoadHistoryAsync(0), T("Saved sentences refreshed."));

    /// <summary>
    /// Loads the next page of older saved sentences.
    /// </summary>
    private Task NextHistoryAsync() => RunAsync(() => LoadHistoryAsync(nextSentenceAfter!.Value), T("Next sentences loaded."));

    /// <summary>
    /// Adds or removes a type in the draft filter selection without sending a request.
    /// </summary>
    private void ToggleType(string type) { OperationLog.Event(Logger); if (!selectedTypes.Add(type)) selectedTypes.Remove(type); }

    /// <summary>
    /// Creates or updates a word, refreshes copies in the sentence draft and reloads the unfiltered
    /// first page.
    /// </summary>
    private Task SaveWordAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(draftWord))
            throw new ClientFailure(T("Enter one word, up to 80 characters. Use letters, apostrophes or hyphens; no spaces or numbers."));
        var saved = await SendAsync(editingId is null ? HttpMethod.Post : HttpMethod.Put,
            editingId is null ? "api/words" : $"api/words/{editingId}", ApiJsonContext.Default.WordResponse,
            JsonContent.Create(new SaveWordRequest(draftWord, draftType), ApiJsonContext.Default.SaveWordRequest));
        confirmedChange = T("Word saved.");
        for (var i = 0; i < chosen.Count; i++) if (chosen[i].Id == saved.Id) chosen[i] = saved;
        CancelEdit();
        // Show the collection again after saving, rather than searching for only the saved word.
        // Large collections still use Next/Previous; never download every word at once.
        search = ""; selectedTypes.Clear(); previous.Clear(); ApplyFilterValues();
        await LoadWordsAsync(0);
    }, T("Word saved. Your collection has been refreshed."));

    /// <summary>
    /// Copies a word into the editor and attempts to focus the input; focus failure leaves the editable
    /// values intact.
    /// </summary>
    private async Task EditAsync(WordResponse word)
    {
        OperationLog.Event(Logger);
        editingId = word.Id; draftWord = word.Word; draftType = word.Type;
        try { await wordInput.FocusAsync(); }
        catch (Microsoft.JSInterop.JSException)
        {
            SetError(T("The word is ready to edit. Tap the word field to continue."));
        }
    }

    /// <summary>
    /// Resets the editor to an empty new noun without changing saved data.
    /// </summary>
    private void CancelEdit() { OperationLog.Event(Logger); editingId = null; draftWord = ""; draftType = "Noun"; }

    /// <summary>
    /// Deletes the confirmed word, removes it from the draft and refreshes the list while leaving saved
    /// sentences intact.
    /// </summary>
    private Task DeleteAsync(WordResponse word) => RunAsync(async () =>
    {
        await SendAsync(HttpMethod.Delete, $"api/words/{word.Id}", ApiJsonContext.Default.WordResponse);
        confirmedChange = T("Word deleted.");
        chosen.RemoveAll(item => item.Id == word.Id);
        sentenceRequestId = Guid.NewGuid();
        if (editingId == word.Id) CancelEdit();
        await LoadWordsAsync(after);
    }, T("Word deleted. Saved sentences have not changed."));

    /// <summary>
    /// Appends a word to an idle draft of fewer than 50 words and assigns the changed draft a fresh
    /// request ID.
    /// </summary>
    private void AddToSentence(WordResponse word)
    {
        OperationLog.Event(Logger);
        if (busy || chosen.Count >= 50) return;
        chosen.Add(word); sentenceRequestId = Guid.NewGuid();
        message = F($"Added {word.Word} to your sentence."); error = false;
    }

    /// <summary>
    /// Swaps neighbouring draft words in place and gives the changed order a fresh request ID.
    /// </summary>
    private void Move(int index, int change)
    {
        OperationLog.Event(Logger);
        // Swap neighbors in place; there is no need to copy or rebuild the whole draft.
        (chosen[index], chosen[index + change]) = (chosen[index + change], chosen[index]);
        sentenceRequestId = Guid.NewGuid();
    }

    /// <summary>
    /// Removes the chosen draft position and resets the request ID for the changed sentence.
    /// </summary>
    private void Remove(int index) { OperationLog.Event(Logger); chosen.RemoveAt(index); sentenceRequestId = Guid.NewGuid(); }

    /// <summary>
    /// Empties the draft and prepares a new request ID without changing saved history.
    /// </summary>
    private void ClearSentence() { OperationLog.Event(Logger); chosen.Clear(); sentenceRequestId = Guid.NewGuid(); }

    /// <summary>
    /// Saves the ordered draft and clears it only after confirmation, then reloads history. Failed or
    /// uncertain saves retain the same request ID for retry.
    /// </summary>
    private Task SaveSentenceAsync() => RunAsync(async () =>
    {
        await SendAsync(HttpMethod.Post, "api/sentences", ApiJsonContext.Default.SentenceResponse,
            JsonContent.Create(new SaveSentenceRequest(chosen.Select(w => w.Id).ToArray(), sentenceRequestId), ApiJsonContext.Default.SaveSentenceRequest));
        confirmedChange = T("Sentence saved.");
        // Keep the same request ID on failure. Retrying an uncertain save cannot create a duplicate.
        ClearSentence();
        await LoadHistoryAsync(0);
    }, T("Sentence saved."));

    /// <summary>
    /// Clears the token, displayed records, bookmarks and drafts without deleting stored words or
    /// sentences.
    /// </summary>
    private void Disconnect()
    {
        OperationLog.Event(Logger);
        token = ""; connected = false; words = []; sentences = []; previous.Clear();
        nextAfter = nextSentenceAfter = null; pendingDelete = null; after = sentenceAfter = 0;
        CancelEdit(); ClearSentence(); message = T("Disconnected. Your saved words are safe in the database.");
    }

    /// <summary>Records a request to show or dismiss a delete prompt without exposing the word.</summary>
    private void ConfirmDelete(long? id) { OperationLog.Event(Logger); pendingDelete = id; }

    /// <summary>Clears the type checkboxes and records the local action.</summary>
    private void ClearTypes() { OperationLog.Event(Logger); selectedTypes.Clear(); }

    /// <summary>Records the page becoming available without logging the stored token or drafts.</summary>
    protected override void OnInitialized() { AppText.LanguageChanged += RefreshLanguage; OperationLog.Event(Logger, "PageCreated"); }

    /// <summary>
    /// Converts a word type to the lowercase CSS class used for its colour badge.
    /// </summary>
    private static string TypeClass(string type) => type.ToLowerInvariant();

    /// <summary>Refreshes translated labels while retaining the draft, filters, and connection.</summary>
    private void RefreshLanguage() => _ = InvokeAsync(() =>
    {
        message = T("Language changed."); error = false; StateHasChanged();
    });

    /// <summary>
    /// Cancels pending page requests and releases the lifetime cancellation source.
    /// </summary>
    public void Dispose() { AppText.LanguageChanged -= RefreshLanguage; OperationLog.Event(Logger); lifetime.Cancel(); lifetime.Dispose(); }

    /// <summary>
    /// Carries a service error or unreadable-reply message that can be shown safely in the browser.
    /// </summary>
    private sealed class ClientFailure(string message) : Exception(message);
}
