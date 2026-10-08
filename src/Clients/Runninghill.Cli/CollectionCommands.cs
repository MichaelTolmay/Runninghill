using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Runninghill.Clients;
using Runninghill.Contracts;

internal static class CollectionCommands
{
    public const string Help = """
        Runninghill word collection
          status
          words list [--search prefix] [--types Noun,Verb] [--after ID]
          words get ID
          words add WORD TYPE
          words update ID WORD TYPE
          words delete ID --yes
          sentences list [--after ID]
          sentences add ID [ID ...] [--request-id UUID]

        Types: Noun, Verb, Adjective, Adverb, Pronoun, Preposition,
               Interjection, Conjunction, Determiner.
        Sentence IDs are word IDs in the order you want; repeating an ID is allowed.
        Keep the printed request ID to retry an uncertain sentence save safely.
        Configure RUNNINGHILL_SERVICE_URL and RUNNINGHILL_ACCESS_TOKEN.
        """;

    public static async Task<int> ExecuteAsync(string[] args, HttpClient client, string token, CancellationToken cancellation)
    {
        try
        {
            if (args.Length < 2) throw new UsageFailure("Choose a collection command.");
            switch ((args[0], args[1]))
            {
                case ("words", "list"):
                {
                    var query = Options(args[2..], "search", "types", "after");
                    var page = await SendAsync(client, token, HttpMethod.Get, "api/words" + query, ApiJsonContext.Default.WordPage, cancellation);
                    if (page.Items is null) throw new JsonException();
                    foreach (var item in page.Items) WriteWord(item);
                    if (page.Items.Length == 0) Console.WriteLine("No words found. Add a word, or try another filter.");
                    if (page.NextAfter is not null) WriteWrapped($"More words: repeat this command with --after {page.NextAfter} and the same filters.");
                    break;
                }
                case ("words", "get") when args.Length == 3:
                    WriteWord(await SendAsync(client, token, HttpMethod.Get, $"api/words/{Id(args[2])}", ApiJsonContext.Default.WordResponse, cancellation));
                    break;
                case ("words", "add") when args.Length == 4:
                case ("words", "update") when args.Length == 5:
                {
                    var adding = args[1] == "add";
                    var start = adding ? 2 : 3;
                    var word = await SendAsync(client, token, adding ? HttpMethod.Post : HttpMethod.Put,
                        adding ? "api/words" : $"api/words/{Id(args[2])}", ApiJsonContext.Default.WordResponse, cancellation,
                        JsonContent.Create(new SaveWordRequest(args[start], args[start + 1]), ApiJsonContext.Default.SaveWordRequest));
                    Console.WriteLine("Word saved."); WriteWord(word);
                    break;
                }
                case ("words", "delete") when args.Length == 4 && args[3] == "--yes":
                    await SendAsync(client, token, HttpMethod.Delete, $"api/words/{Id(args[2])}", ApiJsonContext.Default.WordResponse, cancellation);
                    Console.WriteLine("Word deleted. Saved sentences are unchanged.");
                    break;
                case ("sentences", "list"):
                {
                    var page = await SendAsync(client, token, HttpMethod.Get, "api/sentences" + Options(args[2..], "after"), ApiJsonContext.Default.SentencePage, cancellation);
                    if (page.Items is null) throw new JsonException();
                    foreach (var sentence in page.Items) { WriteWrapped($"#{sentence.Id} · {sentence.CreatedAt:u}"); WriteWrapped(sentence.Text); Console.WriteLine(); }
                    if (page.Items.Length == 0) Console.WriteLine("No sentences yet. Choose word IDs and use sentences add.");
                    if (page.NextAfter is not null) WriteWrapped($"More sentences: sentences list --after {page.NextAfter}");
                    break;
                }
                case ("sentences", "add") when args.Length >= 3:
                {
                    var values = args[2..];
                    var requestId = Guid.NewGuid();
                    if (values.Length >= 2 && values[^2] == "--request-id")
                    {
                        if (!Guid.TryParse(values[^1], out requestId) || requestId == Guid.Empty)
                            throw new UsageFailure("Use a valid, non-empty UUID for --request-id.");
                        values = values[..^2];
                    }
                    if (values.Length is < 1 or > 50) throw new UsageFailure("Choose between 1 and 50 word IDs.");
                    var ids = values.Select(Id).ToArray();
                    Console.Error.WriteLine($"Sentence request ID: {requestId}");
                    var saved = await SendAsync(client, token, HttpMethod.Post, "api/sentences", ApiJsonContext.Default.SentenceResponse, cancellation,
                        JsonContent.Create(new SaveSentenceRequest(ids, requestId), ApiJsonContext.Default.SaveSentenceRequest));
                    WriteWrapped($"Sentence #{saved.Id} saved: {saved.Text}");
                    break;
                }
                default: throw new UsageFailure("Check the command and arguments. Deletion requires --yes.");
            }
            return 0;
        }
        catch (UsageFailure exception) { Console.Error.WriteLine(exception.Message); Console.Error.WriteLine(Help); return 2; }
        catch (RequestFailure exception) { WriteWrapped(exception.Message, error: true); return 1; }
    }

    private static async Task<T> SendAsync<T>(HttpClient client, string token, HttpMethod method, string path,
        JsonTypeInfo<T> type, CancellationToken cancellation, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellation);
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

    private static long Id(string value) => long.TryParse(value, out var id) && id > 0 ? id : throw new UsageFailure("Word and page IDs must be positive whole numbers.");
    private static string Options(string[] args, params string[] allowed)
    {
        var query = new List<string>();
        var seen = new HashSet<string>();
        for (var i = 0; i < args.Length; i += 2)
        {
            var key = args[i].StartsWith("--", StringComparison.Ordinal) ? args[i][2..] : "";
            if (!allowed.Contains(key) || i + 1 >= args.Length || !seen.Add(key)) throw new UsageFailure("Check the list options in the help below.");
            if (key == "after") _ = Id(args[i + 1]);
            query.Add(key + "=" + Uri.EscapeDataString(args[i + 1]));
        }
        return "?" + string.Join('&', query);
    }
    private static void WriteWord(WordResponse word) => WriteWrapped($"#{word.Id}  {word.Word}  [{word.Type}]");

    // Use stacked text instead of wide tables. Strip terminal control characters from all
    // server text and wrap to the current terminal width; redirected output stays plain.
    private static void WriteWrapped(string text, bool error = false)
    {
        text = string.Concat(text.Where(c => !char.IsControl(c)));
        var width = 80;
        try { if (!Console.IsOutputRedirected) width = Math.Max(20, Console.WindowWidth - 1); } catch (IOException) { }
        var output = error ? Console.Error : Console.Out;
        while (text.Length > width)
        {
            var end = text.LastIndexOf(' ', width - 1, width);
            if (end < 1) end = width;
            output.WriteLine(text[..end]); text = text[end..].TrimStart();
        }
        output.WriteLine(text);
    }
    private sealed class UsageFailure(string message) : Exception(message);
    private sealed class RequestFailure(string message) : Exception(message);
}
