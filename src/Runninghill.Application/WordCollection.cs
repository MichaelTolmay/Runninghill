using System.Globalization;
using System.Text;

namespace Runninghill.Application;

/// <summary>All collection rules live here, so every transport follows the same rules.</summary>
public sealed class WordCollection(IWordRepository repository)
{
    public const int PageSize = 50;
    public const int SentencePageSize = 10;
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    { "Noun", "Verb", "Adjective", "Adverb", "Pronoun", "Preposition", "Interjection", "Conjunction", "Determiner" };

    public Task<WordEntry[]> ListAsync(long after, string? search, string? types, CancellationToken cancellation)
    {
        ValidateCursor(after);
        search = search?.Trim() ?? "";
        if (search.Length > 80) throw new CollectionException(400, "Search using 80 characters or fewer.");
        var selected = string.IsNullOrEmpty(types) ? [] : types.Split(',');
        if (selected.Length > 9 || selected.Any(type => !Types.Contains(type)))
            throw new CollectionException(400, "Choose one or more of the nine word types.");
        // Fetch one extra row to tell the UI whether another page exists; no costly COUNT query.
        return repository.ListAsync(after, search.Normalize(), selected, PageSize + 1, cancellation);
    }

    public async Task<WordEntry> GetAsync(long id, CancellationToken cancellation) =>
        await repository.GetAsync(id, cancellation) ?? throw MissingWord();

    public Task<WordEntry> CreateAsync(string? word, string? type, CancellationToken cancellation)
    {
        var clean = ValidateWord(word, type);
        return repository.CreateAsync(clean, type!, cancellation);
    }

    public async Task<WordEntry> UpdateAsync(long id, string? word, string? type, CancellationToken cancellation)
    {
        var clean = ValidateWord(word, type);
        return await repository.UpdateAsync(id, clean, type!, cancellation) ?? throw MissingWord();
    }

    public async Task DeleteAsync(long id, CancellationToken cancellation)
    {
        if (!await repository.DeleteAsync(id, cancellation)) throw MissingWord();
    }

    public async Task<SentenceEntry> SaveSentenceAsync(long[]? ids, Guid requestId, CancellationToken cancellation)
    {
        if (ids is null || ids.Length is < 1 or > 50 || ids.Any(id => id <= 0))
            throw new CollectionException(400, "Choose between 1 and 50 saved words for your sentence.");
        if (requestId == Guid.Empty)
            throw new CollectionException(400, "A sentence request ID is required. Refresh the app and try again.");
        // Repeated word IDs are intentional: a sentence can use the same word more than once.
        return await repository.SaveSentenceAsync(ids, requestId, cancellation) ??
            throw new CollectionException(409, "A selected word was deleted. Refresh your collection and rebuild the sentence.");
    }

    public Task<SentenceEntry[]> ListSentencesAsync(long after, CancellationToken cancellation)
    {
        ValidateCursor(after);
        return repository.ListSentencesAsync(after, SentencePageSize + 1, cancellation);
    }

    private static string ValidateWord(string? word, string? type)
    {
        word = word?.Trim();
        if (string.IsNullOrEmpty(word) || word.Length > 80 || !IsSingleWord(word))
            throw new CollectionException(400, "Enter one word, up to 80 characters. Use letters, apostrophes or hyphens; no spaces or numbers.");
        if (type is null || !Types.Contains(type))
            throw new CollectionException(400, "Choose a word type from the list.");
        return word.Normalize();
    }

    private static bool IsSingleWord(string word)
    {
        var containsLetter = false;
        // A Unicode rune is one complete character, even when UTF-16 uses two code units.
        // Combining accents belong to the preceding letter; spaces and control codes do not.
        foreach (var character in word.EnumerateRunes())
        {
            if (Rune.IsLetter(character)) { containsLetter = true; continue; }
            if (character.Value is '-' or '\'' or '’') continue;
            if (Rune.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) continue;
            return false;
        }
        return containsLetter;
    }

    private static void ValidateCursor(long after)
    {
        if (after < 0) throw new CollectionException(400, "The page position is invalid. Return to the first page.");
    }
    private static CollectionException MissingWord() => new(404, "This word no longer exists. Refresh your collection.");
}
