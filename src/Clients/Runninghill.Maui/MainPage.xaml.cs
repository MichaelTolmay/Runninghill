using static Runninghill.Contracts.AppText;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Runninghill.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Runninghill.Clients;
using Runninghill.Contracts;

namespace Runninghill.Maui;

// These are real MAUI controls (WinUI on Windows), not a browser embedded in a desktop shell.
/// <summary>
/// Displays the native word collection, sentence draft and saved history, and calls the shared HTTP
/// service.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly ILogger<MainPage> logger;
    private readonly RecentLogStore logStore;
    private bool showingLogs;
    private HttpClient? client;
    private CancellationTokenSource? activeRequest;
    private readonly HashSet<string> selectedTypes = new();
    // Keep page bookmarks, not copies of whole pages. A sentence draft is separate so
    // the user can pick words from several pages without losing the chosen order.
    private readonly Stack<long> previous = new();
    private readonly List<WordResponse> chosen = [];
    private readonly List<(Grid Row, View Actions)> wordLayouts = [];
    private WordResponse[] words = [];
    private SentenceResponse[] history = [];
    private Border? deleteConfirmation;
    private WorkspaceLayout layout;
    private bool connected;
    private long after, sentenceAfter;
    private long? nextAfter, nextSentenceAfter, editingId;
    // Remember a confirmed write separately from the following screen refresh.
    private string? confirmedChange;
    private Guid sentenceRequestId = Guid.NewGuid();
    private string search = "", types = "";

    /// <summary>
    /// Loads the native controls, creates the word-type checkboxes and sets up local connection presets
    /// in Debug builds.
    /// </summary>
    public MainPage(ILogger<MainPage> logger, RecentLogStore logStore)
    {
        InitializeComponent();
        this.logger = logger;
        this.logStore = logStore;
        OperationLog.Event(logger, "PageCreated");
        WordInput.TextChanged += (_, _) => OperationLog.Event(logger, "WordInputChanged", diagnostic: true);
        SearchInput.TextChanged += (_, _) => OperationLog.Event(logger, "SearchInputChanged", diagnostic: true);
        WordType.SelectedIndexChanged += (_, _) => OperationLog.Event(logger, "WordTypeChanged");
        ConfigureDesktop();
        WordType.ItemsSource = WordTypes.All.Select(T).ToArray(); WordType.SelectedIndex = 0;
        foreach (var type in WordTypes.All)
        {
            var check = new CheckBox().WithTheme("Color", "AccentText");
            SemanticProperties.SetDescription(check, F($"Filter {T(type)}"));
            check.CheckedChanged += (_, e) =>
            {
                OperationLog.Event(logger, "TypeSelectionChanged");
                if (e.Value) selectedTypes.Add(type); else selectedTypes.Remove(type);
                TypeFilterButton.Text = F($"Word types · {(selectedTypes.Count == 0 ? T("All") : selectedTypes.Count)} ▾");
            };
            TypeFilters.Add(new HorizontalStackLayout { Children = { check, new Label { Text = T(type), VerticalOptions = LayoutOptions.Center } } });
        }
        RenderWords(); RenderSentence();
#if DEBUG
        ServiceUrl.Text = OperatingSystem.IsAndroid() ? "http://10.0.2.2:5080/" : "http://localhost:5080/";
        LocalConnections.IsVisible = true;
        if (OperatingSystem.IsAndroid())
        {
            ConnectionHelp.Text = T("Emulator: use 10.0.2.2 to reach your computer. For a USB-connected phone, forward port 5080 with adb and choose USB device. Paste a fresh token from the website's service.");
            AddLocalConnection(T("Docker emulator"), "http://10.0.2.2:5080/");
            AddLocalConnection(T("Debug emulator"), "http://10.0.2.2:5180/");
            AddLocalConnection(T("USB device"), "http://localhost:5080/");
        }
        else
        {
            AddLocalConnection(T("Docker service"), "http://localhost:5080/");
            AddLocalConnection(T("IDE service"), "http://localhost:5180/");
        }
#endif
    }

    // Network events enter here. Only one request sequence may run at a time, so
    // double taps cannot submit two saves or let an old search replace a newer one.
    /// <summary>
    /// Runs one request sequence at a time, shows progress and turns failures into readable messages. A
    /// confirmed save remains reported even if its following refresh fails.
    /// </summary>
    private async Task RunAsync(Func<CancellationToken, Task> action, string success, [CallerMemberName] string operation = "")
    {
        if (activeRequest is not null) return;
        using var eventLog = new OperationLog(logger, operation);
        if (connected && client is not null && Uri.TryCreate(ServiceUrl.Text, UriKind.Absolute, out var changedAddress) && client.BaseAddress != changedAddress)
        {
            connected = false; RefreshButtons();
            eventLog.Complete("ReconnectRequired");
            StatusLabel.Text = T("The service address changed. Connect again before editing words.");
            return;
        }
        if (!Uri.TryCreate(ServiceUrl.Text, UriKind.Absolute, out var address) || !IsAllowedServiceAddress(address) || string.IsNullOrWhiteSpace(AccessToken.Text))
        { eventLog.Complete("InvalidConnectionSettings"); StatusLabel.Text = T("Enter an HTTPS service URL and an access token."); return; }
        using var cancellation = new CancellationTokenSource();
        activeRequest = cancellation;
        confirmedChange = null;
        try
        {
            if (client is not null && client.BaseAddress != address)
            {
                // IDs belong to a particular server. Never send an old selection to a new server.
                words = [];
                chosen.Clear();
                previous.Clear();
                history = []; HistoryRows.Clear();
                nextAfter = nextSentenceAfter = null;
                CancelEdit();
                RenderWords();
                RenderSentence();
            }
            Panels.IsEnabled = ConnectionFields.IsEnabled = LanguageChoice.IsEnabled = false;
            ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = true;
            Feedback.BackgroundColor = Colors.Transparent; Feedback.Padding = 0;
            StatusLabel.Text = T("Working…"); StatusLabel.WithTheme("TextColor", "BadgeText");
            // Reuse the connection pool until the user changes the service address.
            if (client?.BaseAddress != address)
            {
                client?.Dispose();
                client = new HttpClient { BaseAddress = address, Timeout = ClientMessages.RequestTimeout, MaxResponseContentBufferSize = 256 * 1024 };
            }
            await action(cancellation.Token); eventLog.Complete();
            StatusLabel.Text = success;
        }
        catch (RequestFailure exception) { ShowError(exception.Message); }
        catch (HttpRequestException exception) { ShowError(ClientMessages.ForRequestFailure(exception)); }
        catch (JsonException) { ShowError(ClientMessages.InvalidReply); }
        catch (FormatException) { ShowError(T("Copy a valid access token and connect again.")); }
        catch (OperationCanceledException) { eventLog.Complete(cancellation.IsCancellationRequested ? "Cancelled" : "TimedOut"); ShowError(cancellation.IsCancellationRequested ? T("Request cancelled.") : ClientMessages.TimedOut); }
        catch (Exception exception)
        {
            ReportUnexpected(exception);
        }
        finally
        {
            activeRequest = null;
            confirmedChange = null;
            Panels.IsEnabled = ConnectionFields.IsEnabled = LanguageChoice.IsEnabled = true;
            ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = false;
            RefreshButtons();
        }
    }

    /// <summary>
    /// Sends an authenticated request and reads the expected JSON reply. Invalid or failed replies
    /// become friendly errors, and cancelled replies do not update the page.
    /// </summary>
    private async Task<T> SendAsync<T>(HttpMethod method, string path, JsonTypeInfo<T> type, CancellationToken cancellation, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.AcceptLanguage.ParseAdd(Runninghill.Contracts.AppText.Language);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken.Text.Trim());
        using var response = await client!.SendAsync(request, cancellation);
        OperationLog.Response(logger, (int)response.StatusCode, ClientMessages.ReadReference(response));
        if (!response.IsSuccessStatusCode)
        {
            throw new RequestFailure(await ClientMessages.ReadErrorAsync(response, cancellation));
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
        try
        {
            var result = await response.Content.ReadFromJsonAsync(type, cancellation) ?? throw new JsonException();
            if (result is WordPage { Items: null } or SentencePage { Items: null }) throw new JsonException();
            // Some HTTP handlers finish buffering just as the page closes. Do not apply
            // that late reply to a disconnected screen.
            cancellation.ThrowIfCancellationRequested();
            return result;
        }
        catch (JsonException)
        {
            throw new RequestFailure(ClientMessages.WithReference(ClientMessages.InvalidReply, ClientMessages.ReadReference(response)));
        }
    }

    /// <summary>
    /// Loads the first word and history pages with the entered service address and token, then shows
    /// the connection result.
    /// </summary>
    private async void OnConnect(object? sender, EventArgs e) => await RunAsync(async ct =>
    {
        search = SearchInput.Text ?? ""; types = string.Join(',', selectedTypes);
        await LoadWordsAsync(0, ct); previous.Clear(); connected = true;
        ConnectionState.Text = T("Connected"); ConnectionFields.IsVisible = false;
        await LoadHistoryAsync(0, ct);
    }, T("Your collection is up to date."));

    /// <summary>
    /// Fetches one filtered word page after the supplied ID and redraws the list with its next-page
    /// bookmark.
    /// </summary>
    private async Task LoadWordsAsync(long cursor, CancellationToken cancellation)
    {
        var page = await SendAsync(HttpMethod.Get, $"api/words?after={cursor}&search={Uri.EscapeDataString(search)}&types={Uri.EscapeDataString(types)}", ApiJsonContext.Default.WordPage, cancellation);
        words = page.Items ?? throw new JsonException();
        nextAfter = page.NextAfter;
        after = cursor;
        RenderWords();
    }

    /// <summary>
    /// Fetches one page of saved sentences and displays their text and local creation times.
    /// </summary>
    private async Task LoadHistoryAsync(long cursor, CancellationToken cancellation)
    {
        var page = await SendAsync(HttpMethod.Get, $"api/sentences?after={cursor}", ApiJsonContext.Default.SentencePage, cancellation);
        if (page.Items is null) throw new JsonException();
        nextSentenceAfter = page.NextAfter;
        sentenceAfter = cursor;
        history = page.Items;
        RenderHistory();
    }

    /// <summary>Redraws saved content and dates locally when the language changes.</summary>
    private void RenderHistory()
    {
        HistoryRows.Clear();
        if (history.Length == 0) HistoryRows.Add(new Label { Text = T("Your saved sentences will appear here.") });
        foreach (var sentence in history)
        {
            HistoryRows.Add(new Label { Text = sentence.Text, LineBreakMode = LineBreakMode.CharacterWrap });
            HistoryRows.Add(new Label { Text = sentence.CreatedAt.ToLocalTime().ToString("dd MMM yyyy · HH:mm", Culture), FontSize = 11}.WithTheme("TextColor", "Muted"));
            HistoryRows.Add(new BoxView { HeightRequest = 1}.WithTheme("Color", "Line"));
        }
    }

    /// <summary>
    /// Creates or updates a word, updates any copies in the draft, and returns the list to its
    /// unfiltered first page.
    /// </summary>
    private async void OnSaveWord(object? sender, EventArgs e)
    {
        // Enter must obey the same connection guard as the disabled submit button.
        if (!connected) return;
        await RunAsync(async ct =>
        {
            var saved = await SendAsync(editingId is null ? HttpMethod.Post : HttpMethod.Put, editingId is null ? "api/words" : $"api/words/{editingId}", ApiJsonContext.Default.WordResponse, ct,
                JsonContent.Create(new SaveWordRequest(WordInput.Text ?? "", WordType.SelectedIndex >= 0 ? WordTypes.All[WordType.SelectedIndex] : ""), ApiJsonContext.Default.SaveWordRequest));
            confirmedChange = T("Word saved.");
            for (var i = 0; i < chosen.Count; i++) if (chosen[i].Id == saved.Id) chosen[i] = saved;
            CancelEdit(); RenderSentence();
            // Return to the full, paged collection rather than showing only the word just saved.
            search = ""; SearchInput.Text = search; types = ""; ClearTypes(); previous.Clear();
            await LoadWordsAsync(0, ct);
        }, T("Word saved. Your collection has been refreshed."));
    }

    /// <summary>
    /// Deletes a word after inline confirmation, removes it from the draft and refreshes the current
    /// page. Saved sentences remain unchanged.
    /// </summary>
    private async Task DeleteAsync(WordResponse word)
    {
        // Only the inline Confirm delete button calls this method. Keep word makes no request.
        await RunAsync(async ct =>
        {
            await SendAsync(HttpMethod.Delete, $"api/words/{word.Id}", ApiJsonContext.Default.WordResponse, ct);
            confirmedChange = T("Word deleted.");
            chosen.RemoveAll(item => item.Id == word.Id); sentenceRequestId = Guid.NewGuid(); RenderSentence();
            if (editingId == word.Id) CancelEdit();
            await LoadWordsAsync(after, ct);
        }, T("Word deleted. Saved sentences have not changed."));
    }

    /// <summary>
    /// Copies a selected word into the editor and scrolls to the input so the user can change it.
    /// </summary>
    private async Task EditAsync(WordResponse word)
    {
        OperationLog.Event(logger);
        editingId = word.Id; WordInput.Text = word.Word; WordType.SelectedIndex = Array.IndexOf(WordTypes.All.ToArray(), word.Type);
        WordFieldLabel.Text = T("Edit word"); SaveWordButton.Text = T("Save changes"); CancelEditButton.IsVisible = true;
        await PageScroll.ScrollToAsync(WordForm, ScrollToPosition.Center, false); WordInput.Focus();
    }

    /// <summary>
    /// Clears the word editor and restores add-word mode without changing saved data.
    /// </summary>
    private void CancelEdit() { OperationLog.Event(logger); editingId = null; WordInput.Text = ""; WordType.SelectedIndex = 0; WordFieldLabel.Text = T("Add a word"); SaveWordButton.Text = T("Add word"); CancelEditButton.IsVisible = false; }

    /// <summary>
    /// Handles Cancel edit by restoring the empty add-word form.
    /// </summary>
    private void OnCancelEdit(object? sender, EventArgs e) => CancelEdit();

    /// <summary>
    /// Closes the type selector and loads the first page using the current search and selected types.
    /// </summary>
    private async void OnSearch(object? sender, EventArgs e)
    {
        // Enter must obey the same connection guard as the disabled submit button.
        if (!connected) return;
        await RunAsync(async ct =>
        {
            TypeFilterPanel.IsVisible = false;
            search = SearchInput.Text?.Trim() ?? ""; types = string.Join(',', selectedTypes);
            await LoadWordsAsync(0, ct); previous.Clear();
        }, T("Filters applied."));
    }

    /// <summary>
    /// Clears the search and type selections, then reloads the unfiltered first page when connected and
    /// idle.
    /// </summary>
    private async void OnClearFilters(object? sender, EventArgs e)
    {
        if (!connected || activeRequest is not null) return;
        SearchInput.Text = "";
        ClearTypes();
        await RunAsync(async ct =>
        {
            TypeFilterPanel.IsVisible = false;
            search = types = "";
            await LoadWordsAsync(0, ct);
            previous.Clear();
        }, T("Filters cleared."));
    }

    /// <summary>
    /// Loads the next word page and keeps the old bookmark for Previous after the load succeeds.
    /// </summary>
    private async void OnNext(object? sender, EventArgs e) => await RunAsync(async ct => { var old = after; await LoadWordsAsync(nextAfter!.Value, ct); previous.Push(old); }, T("Next page loaded."));

    /// <summary>
    /// Loads the preceding word page and removes its bookmark only after the load succeeds.
    /// </summary>
    private async void OnPrevious(object? sender, EventArgs e) => await RunAsync(async ct => { await LoadWordsAsync(previous.Peek(), ct); previous.Pop(); }, T("Previous page loaded."));

    /// <summary>
    /// Reloads the newest saved sentences and returns history to its first page.
    /// </summary>
    private async void OnHistoryFirst(object? sender, EventArgs e) => await RunAsync(ct => LoadHistoryAsync(0, ct), T("Saved sentences refreshed."));

    /// <summary>
    /// Loads the next page of older saved sentences.
    /// </summary>
    private async void OnHistoryNext(object? sender, EventArgs e) => await RunAsync(ct => LoadHistoryAsync(nextSentenceAfter!.Value, ct), T("Next sentences loaded."));

    /// <summary>
    /// Saves the ordered draft with its retry ID, then clears it and reloads history after
    /// confirmation. An uncertain save keeps the draft and retry ID.
    /// </summary>
    private async void OnSaveSentence(object? sender, EventArgs e) => await RunAsync(async ct =>
    {
        await SendAsync(HttpMethod.Post, "api/sentences", ApiJsonContext.Default.SentenceResponse, ct,
            JsonContent.Create(new SaveSentenceRequest(chosen.Select(w => w.Id).ToArray(), sentenceRequestId), ApiJsonContext.Default.SaveSentenceRequest));
        confirmedChange = T("Sentence saved.");
        // Only reset after a confirmed save; an uncertain network retry reuses the request ID.
        chosen.Clear();
        sentenceRequestId = Guid.NewGuid();
        RenderSentence();
        await LoadHistoryAsync(0, ct);
    }, T("Sentence saved."));

    /// <summary>
    /// Empties the sentence draft and assigns a new request ID for the next intended save.
    /// </summary>
    private void OnClearSentence(object? sender, EventArgs e) { OperationLog.Event(logger); chosen.Clear(); sentenceRequestId = Guid.NewGuid(); RenderSentence(); }

    /// <summary>
    /// Shows or hides the service address and access-token fields.
    /// </summary>
    private void OnConnectionToggle(object? sender, EventArgs e) { OperationLog.Event(logger); ConnectionFields.IsVisible = !ConnectionFields.IsVisible; }

    /// <summary>
    /// Opens or closes the word-type selector without changing its selected checkboxes.
    /// </summary>
    private void OnTypeToggle(object? sender, EventArgs e) { OperationLog.Event(logger); TypeFilterPanel.IsVisible = !TypeFilterPanel.IsVisible; }

    /// <summary>
    /// Unchecks all word types; the user can apply that selection to show every type.
    /// </summary>
    private void OnClearTypes(object? sender, EventArgs e) => ClearTypes();

#if DEBUG
    /// <summary>
    /// Adds a Debug-only button that fills in a local service address without connecting automatically.
    /// </summary>
    private void AddLocalConnection(string label, string address)
    {
        var button = new Button { Text = label, Style = (Style)Resources["Secondary"], Margin = new Thickness(0, 0, 8, 8) };
        button.Clicked += (_, _) => { OperationLog.Event(logger, "LocalConnectionSelected"); ServiceUrl.Text = address; };
        LocalConnections.Add(button);
    }
#endif

    // Navigation only scrolls the existing native page; it does not reload data or lose a draft.
    /// <summary>
    /// Scrolls to the word collection without reloading data or discarding the draft.
    /// </summary>
    private async void OnWordsSection(object? sender, EventArgs e) => await ScrollToSectionAsync(WordPanel);

    /// <summary>
    /// Scrolls to the sentence builder without changing its chosen words.
    /// </summary>
    private async void OnSentenceSection(object? sender, EventArgs e) => await ScrollToSectionAsync(SentencePanel);

    /// <summary>
    /// Scrolls to the saved-sentence history already shown on the page.
    /// </summary>
    private async void OnHistorySection(object? sender, EventArgs e) => await ScrollToSectionAsync(HistoryPanel);

    /// <summary>
    /// Moves the page to a section and shows a fallback message if native scrolling fails.
    /// </summary>
    private async Task ScrollToSectionAsync(View section)
    {
        OperationLog.Event(logger, "SectionNavigation");
        try { await PageScroll.ScrollToAsync(section, ScrollToPosition.Start, false); }
        catch (Exception exception)
        {
            logger.LogWarning("Section navigation failed: {ErrorType}", exception.GetType().Name);
            ShowError(T("Could not jump to that section. You can still scroll to it."));
        }
    }

    /// <summary>
    /// Closes an open type selector or cancels an edit before allowing normal Back navigation.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        OperationLog.Event(logger);
        // Android Back dismisses an open filter before leaving the screen.
        if (TypeFilterPanel.IsVisible) { TypeFilterPanel.IsVisible = false; return true; }
        if (editingId is not null) { CancelEdit(); return true; }
        return base.OnBackButtonPressed();
    }

    /// <summary>
    /// Clears both the native checkboxes and the set of selected word types.
    /// </summary>
    private void ClearTypes()
    {
        OperationLog.Event(logger);
        foreach (HorizontalStackLayout row in TypeFilters.Children) ((CheckBox)row.Children[0]).IsChecked = false;
        selectedTypes.Clear();
    }

    /// <summary>
    /// Clears the in-memory token, displayed data and drafts when idle. It does not delete anything
    /// from the service.
    /// </summary>
    private void OnDisconnect(object? sender, EventArgs e)
    {
        OperationLog.Event(logger);
        if (activeRequest is not null) return;
        AccessToken.Text = ""; connected = false; words = []; chosen.Clear(); previous.Clear();
        nextAfter = nextSentenceAfter = null; after = sentenceAfter = 0; sentenceRequestId = Guid.NewGuid();
        history = []; HistoryRows.Clear(); CancelEdit(); RenderWords(); RenderSentence(); RefreshButtons();
        ConnectionState.Text = T("Not connected"); ConnectionFields.IsVisible = true;
        StatusLabel.Text = T("Disconnected. Your saved words are safe in the database.");
    }

    /// <summary>
    /// Builds native rows for the current word page, including editing and sentence actions.
    /// Delete-confirmation controls are created only when requested.
    /// </summary>
    private void RenderWords()
    {
        deleteConfirmation = null;
        WordRows.Clear(); wordLayouts.Clear(); WordCount.Text = F($"{words.Length} on this page");
        if (words.Length == 0) WordRows.Add(new Label { Text = T("Your collection starts here. Add a word above, or try a different search or type."), Margin = new Thickness(0, 24), HorizontalTextAlignment = TextAlignment.Center });
        // A page has at most 50 words. Never create controls for the entire database.
        foreach (var word in words)
        {
            var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, Padding = new Thickness(0, 14), RowSpacing = 8 };
            // Match the web row: spelling and type share a line, then wrap when space runs out.
            var spelling = new Label { Text = word.Word, FontAttributes = FontAttributes.Bold, FontSize = 16, LineBreakMode = LineBreakMode.CharacterWrap, Margin = new Thickness(0, 0, 10, 0) };
            FlexLayout.SetShrink(spelling, 1);
            var description = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center, Children = { spelling, Tag(T(word.Type), word.Type) } };
            // Let action buttons wrap on narrow screens instead of clipping their labels.
            var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            var add = ActionButton(T("+ Add to sentence"), () => { if (chosen.Count < 50) { chosen.Add(word); sentenceRequestId = Guid.NewGuid(); RenderSentence(); StatusLabel.Text = F($"Added {word.Word} to your sentence."); } return Task.CompletedTask; }, F($"Add {word.Word} to sentence"));
            add.Style = (Style)Resources["Secondary"];
            add.WithTheme("BackgroundColor", "Surface");
            add.BorderWidth = 1;
            add.WithTheme("BorderColor", "SecondaryBorder");
            actions.Add(add);
            actions.Add(ActionButton(T("Edit"), () => EditAsync(word), F($"Edit {word.Word}")));
            Border? confirmation = null;
            actions.Add(ActionButton(T("Delete"), () =>
            {
                if (deleteConfirmation is not null) deleteConfirmation.IsVisible = false;
                // Most rows are never deleted. Create confirmation controls only on demand.
                if (confirmation is null)
                {
                    confirmation = CreateDeleteConfirmation(word);
                    row.Add(confirmation, 0, 2);
                    Grid.SetColumnSpan(confirmation, 2);
                }
                deleteConfirmation = confirmation;
                confirmation.IsVisible = true;
                return Task.CompletedTask;
            }, F($"Delete {word.Word}"), true));
            row.Add(description); row.Add(actions, 1); wordLayouts.Add((row, actions));
            WordRows.Add(row); WordRows.Add(new BoxView { HeightRequest = 1}.WithTheme("Color", "Line"));
        }
        AdaptWordLayout();
    }

    /// <summary>
    /// Creates an inline prompt naming the word, with separate confirm and keep actions.
    /// </summary>
    private Border CreateDeleteConfirmation(WordResponse word)
    {
        var box = new Border
        {
            IsVisible = false, StrokeThickness = 0,            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }, Padding = 12
        }.WithTheme("BackgroundColor", "ErrorBackground");
        var confirm = ActionButton(T("Confirm delete"), () => DeleteAsync(word), F($"Confirm deletion of {word.Word}"));
        confirm.Style = (Style)Resources["DangerButton"];
        confirm.WithTheme("BackgroundColor", "Danger");
        confirm.WithTheme("TextColor", "OnAccent");
        var keep = ActionButton(T("Keep word"), () => { box.IsVisible = false; return Task.CompletedTask; }, F($"Keep {word.Word}"));
        keep.Style = (Style)Resources["Secondary"];
        box.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = F($"Delete “{word.Word}”? Saved sentences will stay unchanged."), LineBreakMode = LineBreakMode.CharacterWrap },
                new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { confirm, keep } }
            }
        };
        return box;
    }

    /// <summary>
    /// Redraws the sentence preview, ordered word controls and enabled actions from the current draft.
    /// </summary>
    private void RenderSentence()
    {
        SentenceRows.Clear(); SentenceShortcut.Text = chosen.Count == 0 ? T("Sentence") : F($"Sentence ({chosen.Count})"); SentenceCount.Text = F($"{chosen.Count} / 50 words");
        SentencePreview.FontSize = chosen.Count == 0 ? 14 : 19;
        SentencePreview.WithTheme("TextColor", chosen.Count == 0 ? "Muted" : "PreviewText");
        SentencePreview.Text = chosen.Count == 0 ? T("Add words from your collection to see your sentence here.") : string.Join(' ', chosen.Select(w => w.Word));
        for (var i = 0; i < chosen.Count; i++)
        {
            var index = i;
            var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
            row.Add(Tag($"{i + 1}. {chosen[i].Word}", chosen[i].Type));
            var actions = new HorizontalStackLayout();
            var earlier = ActionButton("←", () => { Move(index, -1); return Task.CompletedTask; }, F($"Move word {i + 1} earlier")); earlier.IsEnabled = i > 0;
            var later = ActionButton("→", () => { Move(index, 1); return Task.CompletedTask; }, F($"Move word {i + 1} later")); later.IsEnabled = i < chosen.Count - 1;
            actions.Add(earlier); actions.Add(later);
            actions.Add(ActionButton("×", () => { chosen.RemoveAt(index); sentenceRequestId = Guid.NewGuid(); RenderSentence(); return Task.CompletedTask; }, F($"Remove word {i + 1}")));
            row.Add(actions, 1); SentenceRows.Add(row);
        }
        RefreshButtons();
    }

    /// <summary>
    /// Swaps a chosen word with its neighbour and gives the changed draft a fresh request ID.
    /// </summary>
    private void Move(int index, int delta) { OperationLog.Event(logger); (chosen[index], chosen[index + delta]) = (chosen[index + delta], chosen[index]); sentenceRequestId = Guid.NewGuid(); RenderSentence(); }

    /// <summary>
    /// Creates an accessible row action that ignores clicks while a request is running and reports
    /// unexpected handler failures.
    /// </summary>
    private Button ActionButton(string text, Func<Task> action, string description, bool danger = false)
    {
        var button = new Button { Text = text, BackgroundColor = Colors.Transparent, Padding = new Thickness(8), FontSize = 13 }.WithTheme("TextColor", danger ? "DangerText" : "AccentText");
        SemanticProperties.SetDescription(button, description);
        button.Clicked += async (_, _) =>
        {
            if (activeRequest is not null) return;
            OperationLog.Event(logger, "RowAction:" + text);
            try { await action(); }
            catch (Exception exception) { ReportUnexpected(exception); }
        };
        return button;
    }

    /// <summary>
    /// Creates a wrapping label with the background and text colours assigned to its word type.
    /// </summary>
    private static Border Tag(string text, string type)
    {
        var (background, foreground) = type switch
        {
            "Noun" or "Pronoun" => ("NounBackground", "NounText"), "Verb" or "Adverb" => ("VerbBackground", "VerbText"),
            "Adjective" or "Determiner" => ("AdjectiveBackground", "AdjectiveText"), "Preposition" or "Conjunction" => ("PrepositionBackground", "PrepositionText"), _ => ("InterjectionBackground", "InterjectionText")
        };
        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Padding = new Thickness(10, 5), HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center,
            Content = new Label { Text = text, FontSize = 12, LineBreakMode = LineBreakMode.CharacterWrap }
                .WithTheme("TextColor", foreground)
        }.WithTheme("BackgroundColor", background);
    }

    /// <summary>
    /// Updates action availability, page labels and sentence guidance to match the current connection,
    /// selection and bookmarks.
    /// </summary>
    private void RefreshButtons()
    {
        SaveWordButton.IsEnabled = ApplyFiltersButton.IsEnabled = ApplyTypesButton.IsEnabled = ClearFiltersButton.IsEnabled = RefreshHistoryButton.IsEnabled = connected;
        ClearSentenceButton.IsEnabled = chosen.Count > 0;
        SaveSentenceButton.IsEnabled = connected && chosen.Count > 0;
        SentenceHelp.Text = !connected ? T("Connect to your collection before saving a sentence.")
            : chosen.Count == 0 ? T("Add at least one word using Add to sentence in the word list to enable saving.")
            : T("Your sentence is ready to save. Use the arrows to change the word order.");
        PreviousButton.IsEnabled = previous.Count > 0; NextButton.IsEnabled = nextAfter is not null;
        HistoryFirstButton.IsEnabled = connected && sentenceAfter != 0; HistoryNextButton.IsEnabled = nextSentenceAfter is not null;
        PageLabel.Text = F($"Page {previous.Count + 1}");
        foreach (var (_, actions) in wordLayouts)
            ((Button)((FlexLayout)actions).Children[0]).IsEnabled = chosen.Count < 50;
    }

    /// <summary>
    /// Displays and announces an error, preserving any confirmation that a write already succeeded.
    /// </summary>
    private void ShowError(string message)
    {
        message = ClientMessages.AfterConfirmedChange(confirmedChange, message);
        StatusLabel.Text = message; StatusLabel.WithTheme("TextColor", "ErrorText");
        Feedback.WithTheme("BackgroundColor", "ErrorBackground"); Feedback.Padding = 12;
        SemanticScreenReader.Default.Announce(message);
    }

    /// <summary>
    /// Logs a local support reference, exception type and stack, then shows a safe message without
    /// exposing private exception text.
    /// </summary>
    private void ReportUnexpected(Exception exception)
    {
        // Log where the failure happened without logging private response text or tokens.
        var reference = Guid.NewGuid().ToString("N");
        logger.LogError("Collection screen error. Reference: {Reference}; type: {ErrorType}; stack: {StackTrace}",
            reference, exception.GetType().Name, exception.StackTrace);
        ShowError(ClientMessages.WithReference(ClientMessages.ForUnexpected(exception), reference));
    }

    /// <summary>
    /// Repositions existing panels and connection fields using the website breakpoints when the page
    /// width changes.
    /// </summary>
    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        if (Panels is null || Width <= 0) return;
        layout = new WorkspaceLayout(Width);
        Workspace.WidthRequest = layout.ContentWidth;
        AppearanceToolbar.WidthRequest = Math.Min(560, layout.ContentWidth - 2 * layout.OuterPadding);
        // Keep the branding within the available width while reserving a 2:1 box on every device.
        ThemeIcon.WidthRequest = Math.Min(256, Math.Max(0, layout.ContentWidth - 2 * layout.OuterPadding));
        ThemeIcon.HeightRequest = ThemeIcon.WidthRequest / 2;
        // Mobile apps always centre the logo, including tablets and landscape.
        // Desktop windows follow the website's phone-width breakpoint.
        ThemeIcon.HorizontalOptions = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || layout.Phone
            ? LayoutOptions.Center : LayoutOptions.Start;
        Workspace.Padding = new Thickness(layout.OuterPadding, layout.Phone ? 18 : 24);
        HeroTitle.FontSize = layout.HeadingSize;
        WordPanel.Padding = SentencePanel.Padding = HistoryPanel.Padding = layout.PanelPadding;
        ConnectionPanel.Padding = new Thickness(layout.Phone ? 18 : 20, 14);
        IntroSentenceButton.IsVisible = !layout.Stacked;
        // Windows follows the website; the phone clients retain their section shortcuts.
        SectionNavigation.IsVisible = !OperatingSystem.IsWindows() && layout.Stacked;
        Panels.ColumnSpacing = layout.Stacked ? 0 : 24;
        Panels.ColumnDefinitions[1].Width = new GridLength(layout.SidebarWidth);
        Grid.SetColumn(SentenceColumn, layout.Stacked ? 0 : 1);
        Grid.SetRow(SentenceColumn, layout.Stacked ? 1 : 0);

        // At tablet widths the website puts the builder and history alongside one another.
        SentenceColumn.ColumnSpacing = layout.SplitSentencePanels ? 24 : 0;
        SentenceColumn.ColumnDefinitions[1].Width = layout.SplitSentencePanels ? GridLength.Star : new GridLength(0);
        Grid.SetColumn(HistoryPanel, layout.SplitSentencePanels ? 1 : 0);
        Grid.SetRow(HistoryPanel, layout.SplitSentencePanels ? 0 : 1);

        ConnectionFields.ColumnDefinitions[1].Width = layout.Stacked ? new GridLength(0) : GridLength.Star;
        ConnectionFields.ColumnDefinitions[2].Width = layout.Stacked ? new GridLength(0) : GridLength.Auto;
        ConnectionFields.ColumnSpacing = layout.Stacked ? 0 : 12;
        Grid.SetColumn(TokenField, layout.Stacked ? 0 : 1);
        Grid.SetRow(TokenField, layout.Stacked ? 1 : 0);
        Grid.SetColumn(ConnectionActions, layout.Stacked ? 0 : 2);
        Grid.SetRow(ConnectionActions, layout.Stacked ? 2 : 0);
        Grid.SetRow(ConnectionHelp, layout.Stacked ? 3 : 1);
        Grid.SetRow(LocalConnections, layout.Stacked ? 4 : 2);
        AdaptWordLayout();
    }

    /// <summary>
    /// Rechecks form and row wrapping after the word panel receives a new size.
    /// </summary>
    private void OnWordPanelChanged(object? sender, EventArgs e) => AdaptWordLayout();

    /// <summary>
    /// Stacks or separates word fields, filters and action buttons to fit the available card width.
    /// </summary>
    private void AdaptWordLayout()
    {
        if (WordPanel.Width <= 0) return;
        var innerWidth = WordPanel.Width - 2 * layout.PanelPadding;
        var stackForm = layout.Phone || innerWidth < 480;
        WordForm.ColumnSpacing = stackForm ? 0 : 12;
        WordForm.ColumnDefinitions[1].Width = stackForm ? new GridLength(0) : new GridLength(150);
        WordForm.ColumnDefinitions[2].Width = stackForm ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(TypeField, stackForm ? 0 : 1);
        Grid.SetRow(TypeField, stackForm ? 1 : 0);
        Grid.SetColumn(SaveWordButton, stackForm ? 0 : 2);
        Grid.SetRow(SaveWordButton, stackForm ? 2 : 0);
        var stackFilters = layout.Phone || innerWidth < 500;
        FilterForm.ColumnSpacing = stackFilters ? 0 : 12;
        FilterForm.ColumnDefinitions[1].Width = stackFilters ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(FilterActions, stackFilters ? 0 : 1);
        Grid.SetRow(FilterActions, stackFilters ? 1 : 0);
        foreach (var (row, actions) in wordLayouts)
        {
            var stackActions = layout.StackActions || innerWidth < 520;
            Grid.SetColumn(actions, stackActions ? 0 : 1);
            Grid.SetRow(actions, stackActions ? 1 : 0);
            row.ColumnDefinitions[1].Width = stackActions ? new GridLength(0) : GridLength.Auto;
        }
    }

    // Windows adds Escape-key support through its native WinUI root. Other platforms
    // omit this partial method entirely, so they carry no Windows dependencies.
    /// <summary>
    /// Provides a platform hook for Windows keyboard behaviour; other targets omit this partial method.
    /// </summary>
    partial void ConfigureDesktop();

    /// <summary>
    /// Allows HTTPS service addresses. Debug also permits local HTTP connections and the Android
    /// emulator host address.
    /// </summary>
    private static bool IsAllowedServiceAddress(Uri address)
    {
        if (address.Scheme == "https") return true;
#if DEBUG
        return address.Scheme == "http" && (address.IsLoopback || (OperatingSystem.IsAndroid() && address.Host == "10.0.2.2"));
#else
        return false;
#endif
    }

    /// <summary>Opens native diagnostics while keeping the collection and unfinished sentence in memory.</summary>
    private async void OnOpenLogs(object? sender, EventArgs e)
    {
        if (showingLogs || activeRequest is not null) return;
        showingLogs = true;
        try
        {
            var address = Uri.TryCreate(ServiceUrl.Text, UriKind.Absolute, out var candidate) && IsAllowedServiceAddress(candidate) ? candidate : null;
            await Navigation.PushModalAsync(new LogsPage(logStore, logger, address, AccessToken.Text ?? ""));
        }
        catch (Exception exception)
        {
            showingLogs = false;
            ReportUnexpected(exception);
        }
    }

    /// <summary>Translates controls created in code, keeping IDs, typed words, and the draft unchanged.</summary>
    private void RefreshLanguage()
    {
        OperationLog.Event(logger, "LanguageChanged");
        var selected = WordType.SelectedIndex;
        WordType.ItemsSource = WordTypes.All.Select(T).ToArray();
        WordType.SelectedIndex = selected;
        for (var i = 0; i < TypeFilters.Children.Count; i++)
        {
            var row = (HorizontalStackLayout)TypeFilters.Children[i];
            ((Label)row.Children[1]).Text = T(WordTypes.All[i]);
            SemanticProperties.SetDescription((CheckBox)row.Children[0], F($"Filter {T(WordTypes.All[i])}"));
        }
        TypeFilterButton.Text = F($"Word types · {(selectedTypes.Count == 0 ? T("All") : selectedTypes.Count)} ▾");
        WordFieldLabel.Text = editingId is null ? T("Add a word") : T("Edit word");
        SaveWordButton.Text = editingId is null ? T("Add word") : T("Save changes");
        ConnectionState.Text = connected ? T("Connected") : T("Not connected");
        StatusLabel.Text = T("Language changed.");
        if (Window is { } window) window.Title = T("Word collection · Runninghill");
#if DEBUG
        LocalConnections.Clear();
        if (OperatingSystem.IsAndroid())
        {
            ConnectionHelp.Text = T("Emulator: use 10.0.2.2 to reach your computer. For a USB-connected phone, forward port 5080 with adb and choose USB device. Paste a fresh token from the website's service.");
            AddLocalConnection(T("Docker emulator"), "http://10.0.2.2:5080/");
            AddLocalConnection(T("Debug emulator"), "http://10.0.2.2:5180/");
            AddLocalConnection(T("USB device"), "http://localhost:5080/");
        }
        else
        {
            AddLocalConnection(T("Docker service"), "http://localhost:5080/");
            AddLocalConnection(T("IDE service"), "http://localhost:5180/");
        }
#endif
        RenderWords(); RenderSentence(); RenderHistory(); RefreshButtons();
    }

    /// <summary>Restores normal page departure handling after returning from diagnostics.</summary>
    protected override void OnAppearing()
    {
        showingLogs = false;
        LanguageChanged += RefreshLanguage;
        base.OnAppearing();
    }

    /// <summary>
    /// Cancels pending work and clears the access token when the native page leaves view.
    /// </summary>
    protected override void OnDisappearing()
    {
        LanguageChanged -= RefreshLanguage;
        OperationLog.Event(logger);
        if (showingLogs) { base.OnDisappearing(); return; }
        activeRequest?.Cancel(); AccessToken.Text = ""; connected = false;
        ConnectionState.Text = T("Not connected"); ConnectionFields.IsVisible = true; RefreshButtons();
        base.OnDisappearing();
    }

    /// <summary>
    /// Carries a service or reply-format error that is safe to display on the native page.
    /// </summary>
    private sealed class RequestFailure(string message) : Exception(message);
}
