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

    /// <summary>
    /// Validates the page bookmark and filters, then asks for one page plus a look-ahead row to detect
    /// more results without counting the collection.
    /// </summary>
    public Task<WordEntry[]> ListAsync(long after, string? search, string? types, CancellationToken cancellation)
    {
        ValidateCursor(after);
        search = search?.Trim() ?? "";
        if (search.Length > 80) throw new CollectionException(400, "Search using 80 characters or fewer.");
        // Reject oversized filters before allocating one string per comma. Nine type names
        // can each use at most 20 characters, plus the eight commas between them.
        if (types?.Length > 9 * 20 + 8)
            throw new CollectionException(400, "Choose one or more of the nine word types.");
        var selected = string.IsNullOrEmpty(types) ? [] : types.Split(',', 10);
        if (selected.Length > 9 || selected.Any(type => !Types.Contains(type)))
            throw new CollectionException(400, "Choose one or more of the nine word types.");
        // Fetch one extra row to tell the UI whether another page exists; no costly COUNT query.
        return repository.ListAsync(after, NormalizeText(search), selected, PageSize + 1, cancellation);
    }

    /// <summary>
    /// Returns a saved word or raises a friendly not-found error if it has been removed.
    /// </summary>
    public async Task<WordEntry> GetAsync(long id, CancellationToken cancellation) =>
        await repository.GetAsync(id, cancellation) ?? throw MissingWord();

    /// <summary>
    /// Validates and normalizes a new word before passing it to storage.
    /// </summary>
    public Task<WordEntry> CreateAsync(string? word, string? type, CancellationToken cancellation)
    {
        var clean = ValidateWord(word, type);
        return repository.CreateAsync(clean, type!, cancellation);
    }

    /// <summary>
    /// Validates a replacement spelling and type, then updates the word or reports that it no longer
    /// exists.
    /// </summary>
    public async Task<WordEntry> UpdateAsync(long id, string? word, string? type, CancellationToken cancellation)
    {
        var clean = ValidateWord(word, type);
        return await repository.UpdateAsync(id, clean, type!, cancellation) ?? throw MissingWord();
    }

    /// <summary>
    /// Deletes the requested word or reports a friendly not-found error.
    /// </summary>
    public async Task DeleteAsync(long id, CancellationToken cancellation)
    {
        if (!await repository.DeleteAsync(id, cancellation)) throw MissingWord();
    }

    /// <summary>
    /// Checks the ordered selection and retry ID before saving a sentence snapshot. Missing selected
    /// words produce guidance to rebuild the draft.
    /// </summary>
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

    /// <summary>
    /// Validates the history bookmark and requests a page plus one extra sentence to detect older
    /// results.
    /// </summary>
    public Task<SentenceEntry[]> ListSentencesAsync(long after, CancellationToken cancellation)
    {
        ValidateCursor(after);
        return repository.ListSentencesAsync(after, SentencePageSize + 1, cancellation);
    }

    /// <summary>
    /// Checks spelling length, allowed characters and the word type, then returns normalized text for
    /// storage.
    /// </summary>
    private static string ValidateWord(string? word, string? type)
    {
        word = word?.Trim();
        if (string.IsNullOrEmpty(word) || word.Length > 80 || !IsSingleWord(word))
            throw new CollectionException(400, "Enter one word, up to 80 characters. Use letters, apostrophes or hyphens; no spaces or numbers.");
        if (type is null || !Types.Contains(type))
            throw new CollectionException(400, "Choose a word type from the list.");
        return NormalizeText(word);
    }

    /// <summary>
    /// Combines equivalent Unicode spellings into one form and rejects unreadable text or results
    /// exceeding the storage limit.
    /// </summary>
    private static string NormalizeText(string text)
    {
        // The same visible letter can arrive as one character or a letter plus an accent.
        // Store one consistent form. A few characters expand, so check length again afterward.
        string normalized;
        try { normalized = text.Normalize(); }
        catch (ArgumentException)
        {
            throw new CollectionException(400, "Some text contains an unreadable character. Retype the word or search and try again.");
        }
        if (normalized.Length > 80)
            throw new CollectionException(400, "The word or search is too long after its accents are combined. Use a shorter word or search, up to 80 characters.");
        return normalized;
    }

    /// <summary>
    /// Accepts letters, combining accents, apostrophes and hyphens, and requires at least one letter.
    /// Unicode characters are read as complete runes.
    /// </summary>
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

    /// <summary>
    /// Rejects negative page bookmarks; zero represents the first page.
    /// </summary>
    private static void ValidateCursor(long after)
    {
        if (after < 0) throw new CollectionException(400, "The page position is invalid. Return to the first page.");
    }

    /// <summary>
    /// Creates the standard not-found error with guidance to refresh the collection.
    /// </summary>
    private static CollectionException MissingWord() => new(404, "This word no longer exists. Refresh your collection.");
}
