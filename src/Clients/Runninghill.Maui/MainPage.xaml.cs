using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Runninghill.Clients;
using Runninghill.Contracts;

namespace Runninghill.Maui;

// These are real MAUI controls (WinUI on Windows), not a browser embedded in a desktop shell.
public partial class MainPage : ContentPage
{
    private readonly ILogger<MainPage> logger;
    private HttpClient? client;
    private CancellationTokenSource? activeRequest;
    private readonly HashSet<string> selectedTypes = new();
    private readonly Stack<long> previous = new();
    private readonly List<WordResponse> chosen = [];
    private readonly List<(Grid Row, View Actions)> wordLayouts = [];
    private WordResponse[] words = [];
    private bool connected;
    private long after, sentenceAfter;
    private long? nextAfter, nextSentenceAfter, editingId;
    private Guid sentenceRequestId = Guid.NewGuid();
    private string search = "", types = "";

    public MainPage(ILogger<MainPage> logger)
    {
        InitializeComponent();
        this.logger = logger;
        WordType.ItemsSource = WordTypes.All.ToArray(); WordType.SelectedIndex = 0;
        foreach (var type in WordTypes.All)
        {
            var check = new CheckBox { Color = Color.FromArgb("#2868B1") };
            SemanticProperties.SetDescription(check, $"Filter {type}");
            check.CheckedChanged += (_, e) =>
            {
                if (e.Value) selectedTypes.Add(type); else selectedTypes.Remove(type);
                TypeFilterButton.Text = $"Word types · {(selectedTypes.Count == 0 ? "All" : selectedTypes.Count)} ▾";
            };
            TypeFilters.Add(new HorizontalStackLayout { Children = { check, new Label { Text = type, VerticalOptions = LayoutOptions.Center } } });
        }
        RenderWords();
#if DEBUG
        ServiceUrl.Text = OperatingSystem.IsAndroid() ? "http://10.0.2.2:5180/" : "http://localhost:5180/";
#endif
    }

    // All async event handlers enter here. Errors never escape an async-void UI event.
    private async Task RunAsync(Func<CancellationToken, Task> action, string success)
    {
        if (activeRequest is not null) return;
        if (connected && client is not null && Uri.TryCreate(ServiceUrl.Text, UriKind.Absolute, out var changedAddress) && client.BaseAddress != changedAddress)
        {
            connected = false; RefreshButtons();
            StatusLabel.Text = "The service address changed. Connect again before editing words.";
            return;
        }
        if (!Uri.TryCreate(ServiceUrl.Text, UriKind.Absolute, out var address) || !IsAllowedServiceAddress(address) || string.IsNullOrWhiteSpace(AccessToken.Text))
        { StatusLabel.Text = "Enter an HTTPS service URL and an access token."; return; }
        using var cancellation = new CancellationTokenSource();
        activeRequest = cancellation;
        if (client is not null && client.BaseAddress != address)
        {
            // IDs belong to a particular server. Never send an old selection to a new server.
            words = []; chosen.Clear(); previous.Clear(); HistoryRows.Clear();
            nextAfter = nextSentenceAfter = null; CancelEdit(); RenderWords(); RenderSentence();
        }
        Panels.IsEnabled = ConnectionFields.IsEnabled = false;
        ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = true;
        StatusLabel.Text = "Working…"; StatusLabel.TextColor = Color.FromArgb("#315C8C");
        try
        {
            // Reuse the connection pool until the user changes the service address.
            if (client?.BaseAddress != address)
            {
                client?.Dispose();
                client = new HttpClient { BaseAddress = address, Timeout = ClientMessages.RequestTimeout, MaxResponseContentBufferSize = 256 * 1024 };
            }
            await action(cancellation.Token);
            StatusLabel.Text = success;
        }
        catch (RequestFailure exception) { ShowError(exception.Message); }
        catch (HttpRequestException exception) { ShowError(ClientMessages.ForRequestFailure(exception)); }
        catch (JsonException) { ShowError(ClientMessages.InvalidReply); }
        catch (FormatException) { ShowError("Copy a valid access token and connect again."); }
        catch (OperationCanceledException) { ShowError(cancellation.IsCancellationRequested ? "Request cancelled." : ClientMessages.TimedOut); }
        catch (Exception exception)
        {
            logger.LogError("Collection screen error: {ErrorType}", exception.GetType().Name);
            ShowError(ClientMessages.Unexpected);
        }
        finally
        {
            activeRequest = null;
            Panels.IsEnabled = ConnectionFields.IsEnabled = true;
            ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = false;
            RefreshButtons();
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, JsonTypeInfo<T> type, CancellationToken cancellation, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken.Text.Trim());
        using var response = await client!.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            var detail = ClientMessages.ForStatus(response.StatusCode);
            if ((int)response.StatusCode is 400 or 404 or 409)
                detail = (await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.ApiProblem, cancellation))?.Detail ?? detail;
            throw new RequestFailure(ClientMessages.WithReference(detail, ClientMessages.ReadReference(response)));
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
        return await response.Content.ReadFromJsonAsync(type, cancellation) ?? throw new JsonException();
    }

    private async void OnConnect(object? sender, EventArgs e) => await RunAsync(async ct =>
    {
        search = SearchInput.Text ?? ""; types = string.Join(',', selectedTypes);
        await LoadWordsAsync(0, ct); previous.Clear(); connected = true;
        ConnectionState.Text = "Connected"; ConnectionFields.IsVisible = false;
        await LoadHistoryAsync(0, ct);
    }, "Your collection is up to date.");

    private async Task LoadWordsAsync(long cursor, CancellationToken cancellation)
    {
        var page = await SendAsync(HttpMethod.Get, $"api/words?after={cursor}&search={Uri.EscapeDataString(search)}&types={Uri.EscapeDataString(types)}", ApiJsonContext.Default.WordPage, cancellation);
        words = page.Items ?? throw new JsonException(); nextAfter = page.NextAfter; after = cursor;
        RenderWords();
    }
    private async Task LoadHistoryAsync(long cursor, CancellationToken cancellation)
    {
        var page = await SendAsync(HttpMethod.Get, $"api/sentences?after={cursor}", ApiJsonContext.Default.SentencePage, cancellation);
        if (page.Items is null) throw new JsonException();
        nextSentenceAfter = page.NextAfter; sentenceAfter = cursor; HistoryRows.Clear();
        if (page.Items.Length == 0) HistoryRows.Add(new Label { Text = "Your saved sentences will appear here." });
        foreach (var sentence in page.Items)
        {
            HistoryRows.Add(new Label { Text = sentence.Text, LineBreakMode = LineBreakMode.CharacterWrap });
            HistoryRows.Add(new Label { Text = sentence.CreatedAt.ToLocalTime().ToString("dd MMM yyyy · HH:mm"), FontSize = 11, TextColor = Color.FromArgb("#5E6979") });
            HistoryRows.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E2E8F0") });
        }
    }
    private async void OnSaveWord(object? sender, EventArgs e) => await RunAsync(async ct =>
    {
        var saved = await SendAsync(editingId is null ? HttpMethod.Post : HttpMethod.Put, editingId is null ? "api/words" : $"api/words/{editingId}", ApiJsonContext.Default.WordResponse, ct,
            JsonContent.Create(new SaveWordRequest(WordInput.Text ?? "", WordType.SelectedItem as string ?? ""), ApiJsonContext.Default.SaveWordRequest));
        for (var i = 0; i < chosen.Count; i++) if (chosen[i].Id == saved.Id) chosen[i] = saved;
        CancelEdit(); RenderSentence();
        // Return to the full, paged collection rather than showing only the word just saved.
        search = ""; SearchInput.Text = search; types = ""; ClearTypes(); previous.Clear();
        await LoadWordsAsync(0, ct);
    }, "Word saved. Your collection has been refreshed.");

    private async Task DeleteAsync(WordResponse word)
    {
        // Name the exact word before the destructive action. A cancelled dialog makes no request.
        if (!await DisplayAlertAsync("Delete word?", $"Delete “{word.Word}”? Saved sentences will stay unchanged.", "Delete word", "Keep word")) return;
        await RunAsync(async ct =>
        {
            await SendAsync(HttpMethod.Delete, $"api/words/{word.Id}", ApiJsonContext.Default.WordResponse, ct);
            chosen.RemoveAll(item => item.Id == word.Id); sentenceRequestId = Guid.NewGuid(); RenderSentence();
            if (editingId == word.Id) CancelEdit();
            await LoadWordsAsync(after, ct);
        }, "Word deleted. Saved sentences have not changed.");
    }
    private async Task EditAsync(WordResponse word)
    {
        editingId = word.Id; WordInput.Text = word.Word; WordType.SelectedItem = word.Type;
        WordFieldLabel.Text = "Edit word"; SaveWordButton.Text = "Save changes"; CancelEditButton.IsVisible = true;
        await PageScroll.ScrollToAsync(WordForm, ScrollToPosition.Center, false); WordInput.Focus();
    }
    private void CancelEdit() { editingId = null; WordInput.Text = ""; WordType.SelectedIndex = 0; WordFieldLabel.Text = "Add a word"; SaveWordButton.Text = "Add word"; CancelEditButton.IsVisible = false; }
    private void OnCancelEdit(object? sender, EventArgs e) => CancelEdit();
    private async void OnSearch(object? sender, EventArgs e) => await RunAsync(async ct =>
    {
        TypeFilters.IsVisible = false;
        search = SearchInput.Text?.Trim() ?? ""; types = string.Join(',', selectedTypes);
        await LoadWordsAsync(0, ct); previous.Clear();
    }, "Filters applied.");
    private async void OnNext(object? sender, EventArgs e) => await RunAsync(async ct => { var old = after; await LoadWordsAsync(nextAfter!.Value, ct); previous.Push(old); }, "Next page loaded.");
    private async void OnPrevious(object? sender, EventArgs e) => await RunAsync(async ct => { await LoadWordsAsync(previous.Peek(), ct); previous.Pop(); }, "Previous page loaded.");
    private async void OnHistoryFirst(object? sender, EventArgs e) => await RunAsync(ct => LoadHistoryAsync(0, ct), "Saved sentences refreshed.");
    private async void OnHistoryNext(object? sender, EventArgs e) => await RunAsync(ct => LoadHistoryAsync(nextSentenceAfter!.Value, ct), "Next sentences loaded.");
    private async void OnSaveSentence(object? sender, EventArgs e) => await RunAsync(async ct =>
    {
        await SendAsync(HttpMethod.Post, "api/sentences", ApiJsonContext.Default.SentenceResponse, ct,
            JsonContent.Create(new SaveSentenceRequest(chosen.Select(w => w.Id).ToArray(), sentenceRequestId), ApiJsonContext.Default.SaveSentenceRequest));
        // Only reset after a confirmed save; an uncertain network retry reuses the request ID.
        chosen.Clear(); sentenceRequestId = Guid.NewGuid(); RenderSentence(); await LoadHistoryAsync(0, ct);
    }, "Sentence saved.");

    private void OnClearSentence(object? sender, EventArgs e) { chosen.Clear(); sentenceRequestId = Guid.NewGuid(); RenderSentence(); }
    private void OnConnectionToggle(object? sender, EventArgs e) => ConnectionFields.IsVisible = !ConnectionFields.IsVisible;
    private void OnTypeToggle(object? sender, EventArgs e) => TypeFilters.IsVisible = !TypeFilters.IsVisible;
    private void ClearTypes()
    {
        foreach (HorizontalStackLayout row in TypeFilters.Children) ((CheckBox)row.Children[0]).IsChecked = false;
        selectedTypes.Clear();
    }
    private void OnDisconnect(object? sender, EventArgs e)
    {
        if (activeRequest is not null) return;
        AccessToken.Text = ""; connected = false; words = []; chosen.Clear(); previous.Clear();
        nextAfter = nextSentenceAfter = null; after = sentenceAfter = 0; sentenceRequestId = Guid.NewGuid();
        HistoryRows.Clear(); CancelEdit(); RenderWords(); RenderSentence(); RefreshButtons();
        ConnectionState.Text = "Not connected"; ConnectionFields.IsVisible = true;
        StatusLabel.Text = "Disconnected. Your saved words are safe in the database.";
    }

    private void RenderWords()
    {
        WordRows.Clear(); wordLayouts.Clear(); WordCount.Text = $"{words.Length} on this page";
        if (words.Length == 0) WordRows.Add(new Label { Text = "Your collection starts here. Add a word above, or try a different search or type.", Margin = new Thickness(0, 24), HorizontalTextAlignment = TextAlignment.Center });
        // A page has at most 50 words. Never create controls for the entire database.
        foreach (var word in words)
        {
            var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) }, Padding = new Thickness(0, 14), RowSpacing = 8 };
            // Match the web row: spelling and type share a line, then wrap when space runs out.
            var spelling = new Label { Text = word.Word, FontAttributes = FontAttributes.Bold, FontSize = 16, LineBreakMode = LineBreakMode.CharacterWrap, Margin = new Thickness(0, 0, 10, 0) };
            FlexLayout.SetShrink(spelling, 1);
            var description = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center, Children = { spelling, Tag(word.Type, word.Type) } };
            // Let action buttons wrap on narrow screens instead of clipping their labels.
            var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            actions.Add(ActionButton("+ Add to sentence", () => { if (chosen.Count < 50) { chosen.Add(word); sentenceRequestId = Guid.NewGuid(); RenderSentence(); StatusLabel.Text = $"Added {word.Word} to your sentence."; } return Task.CompletedTask; }, $"Add {word.Word} to sentence"));
            actions.Add(ActionButton("Edit", () => EditAsync(word), $"Edit {word.Word}"));
            actions.Add(ActionButton("Delete", () => DeleteAsync(word), $"Delete {word.Word}", true));
            row.Add(description); row.Add(actions, 1); wordLayouts.Add((row, actions));
            WordRows.Add(row); WordRows.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E2E8F0") });
        }
        AdaptWordLayout();
    }
    private void RenderSentence()
    {
        SentenceRows.Clear(); SentenceCount.Text = $"{chosen.Count} / 50 words";
        SentencePreview.Text = chosen.Count == 0 ? "Add words from your collection to see your sentence here." : string.Join(' ', chosen.Select(w => w.Word));
        for (var i = 0; i < chosen.Count; i++)
        {
            var index = i;
            var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
            row.Add(Tag($"{i + 1}. {chosen[i].Word}", chosen[i].Type));
            var actions = new HorizontalStackLayout();
            var earlier = ActionButton("←", () => { Move(index, -1); return Task.CompletedTask; }, $"Move word {i + 1} earlier"); earlier.IsEnabled = i > 0;
            var later = ActionButton("→", () => { Move(index, 1); return Task.CompletedTask; }, $"Move word {i + 1} later"); later.IsEnabled = i < chosen.Count - 1;
            actions.Add(earlier); actions.Add(later);
            actions.Add(ActionButton("×", () => { chosen.RemoveAt(index); sentenceRequestId = Guid.NewGuid(); RenderSentence(); return Task.CompletedTask; }, $"Remove word {i + 1}"));
            row.Add(actions, 1); SentenceRows.Add(row);
        }
        RefreshButtons();
    }
    private void Move(int index, int delta) { (chosen[index], chosen[index + delta]) = (chosen[index + delta], chosen[index]); sentenceRequestId = Guid.NewGuid(); RenderSentence(); }
    private Button ActionButton(string text, Func<Task> action, string description, bool danger = false)
    {
        var button = new Button { Text = text, BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb(danger ? "#B42337" : "#2868B1"), Padding = new Thickness(8), FontSize = 13 };
        SemanticProperties.SetDescription(button, description);
        button.Clicked += async (_, _) =>
        {
            if (activeRequest is not null) return;
            try { await action(); }
            catch (Exception exception) { logger.LogError("Collection interaction error: {ErrorType}", exception.GetType().Name); ShowError(ClientMessages.Unexpected); }
        };
        return button;
    }
    private static Border Tag(string text, string type)
    {
        var (background, foreground) = type switch
        {
            "Noun" or "Pronoun" => ("#E8EFFF", "#3856A6"), "Verb" or "Adverb" => ("#E1F4EB", "#236448"),
            "Adjective" or "Determiner" => ("#F0E8FA", "#704292"), "Preposition" or "Conjunction" => ("#FFF0DB", "#8B5516"), _ => ("#FBE6EC", "#9B3853")
        };
        return new Border { BackgroundColor = Color.FromArgb(background), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 }, Padding = new Thickness(10, 5), HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center,
            Content = new Label { Text = text, TextColor = Color.FromArgb(foreground), FontSize = 12, LineBreakMode = LineBreakMode.CharacterWrap } };
    }
    private void RefreshButtons()
    {
        SaveWordButton.IsEnabled = ApplyFiltersButton.IsEnabled = connected;
        SaveSentenceButton.IsEnabled = connected && chosen.Count > 0;
        SentenceHelp.Text = !connected ? "Connect to your collection before saving a sentence."
            : chosen.Count == 0 ? "Add at least one word using Add to sentence in the word list to enable saving."
            : "Your sentence is ready to save. Use the arrows to change the word order.";
        PreviousButton.IsEnabled = previous.Count > 0; NextButton.IsEnabled = nextAfter is not null;
        HistoryFirstButton.IsEnabled = connected && sentenceAfter != 0; HistoryNextButton.IsEnabled = nextSentenceAfter is not null;
        PageLabel.Text = $"Page {previous.Count + 1}";
        foreach (var (_, actions) in wordLayouts)
            ((Button)((FlexLayout)actions).Children[0]).IsEnabled = chosen.Count < 50;
    }
    private void ShowError(string message) { StatusLabel.Text = message; StatusLabel.TextColor = Color.FromArgb("#B42337"); }
    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        if (Panels is null) return;
        var narrow = Width <= 820;
        Workspace.Padding = Width <= 560 ? new Thickness(14, 18) : new Thickness(40, 24);
        Panels.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : 1.7, GridUnitType.Star);
        Panels.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : GridLength.Star;
        Grid.SetColumn(SentenceColumn, narrow ? 0 : 1); Grid.SetRow(SentenceColumn, narrow ? 1 : 0);
    }
    private void OnWordPanelChanged(object? sender, EventArgs e) => AdaptWordLayout();
    private void AdaptWordLayout()
    {
        var narrow = WordPanel.Width < 620;
        WordForm.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(150);
        WordForm.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(TypeField, narrow ? 0 : 1); Grid.SetRow(TypeField, narrow ? 1 : 0);
        Grid.SetColumn(SaveWordButton, narrow ? 0 : 2); Grid.SetRow(SaveWordButton, narrow ? 2 : 0);
        foreach (var (row, actions) in wordLayouts)
        {
            Grid.SetColumn(actions, narrow ? 0 : 1); Grid.SetRow(actions, narrow ? 1 : 0);
            row.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : GridLength.Auto;
        }
    }
    private static bool IsAllowedServiceAddress(Uri address)
    {
        if (address.Scheme == "https") return true;
#if DEBUG
        return address.Scheme == "http" && (address.IsLoopback || (OperatingSystem.IsAndroid() && address.Host == "10.0.2.2"));
#else
        return false;
#endif
    }
    protected override void OnDisappearing()
    {
        activeRequest?.Cancel(); AccessToken.Text = ""; connected = false;
        ConnectionState.Text = "Not connected"; ConnectionFields.IsVisible = true; RefreshButtons();
        base.OnDisappearing();
    }
    private sealed class RequestFailure(string message) : Exception(message);
}
