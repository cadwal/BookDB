using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

// ── Suggestion types ──────────────────────────────────────────────────────────

public interface IPersonSuggestion
{
    string DisplayText { get; }
    string ValueText { get; }
}

public sealed record ExistingPersonSuggestion(Person Person) : IPersonSuggestion
{
    public string DisplayText => Person.DisplayName;
    public string ValueText => Person.DisplayName;
}

public sealed record NewPersonSuggestion(string InputName) : IPersonSuggestion
{
    public string DisplayText => string.Format(Resources.PersonSuggestion_NewAuthor_Format, InputName);
    public string ValueText => InputName;
}

// ── Provider ──────────────────────────────────────────────────────────────────

/// <summary>
/// Shared person type-ahead with the lend flow's semantics (CheckOutDialogViewModel): filters an
/// in-memory snapshot loaded once at open — a per-keystroke remote query would complete after
/// later keystrokes and reset the box to the stale (shorter) search text, dropping characters on
/// a high-latency backend. Matches both name orders ("First Last" via DisplayName, "Last, First"
/// via SortName) case-insensitively and appends a localized "new author" row when no exact match
/// exists. The typed text stays the single source of truth; <see cref="Resolve"/> maps it back to
/// an existing person in memory, because a DB name match is collation-dependent.
/// </summary>
public sealed class PersonSuggestionProvider
{
    private IReadOnlyList<Person> _people = [];

    public PersonSuggestionProvider()
    {
        Populator = PopulateAsync;
    }

    /// <summary>AsyncPopulator for an AutoCompleteBox — suggestion-only, no SelectedItem binding.</summary>
    public Func<string, CancellationToken, Task<IEnumerable<object?>?>> Populator { get; }

    /// <summary>Installs the people snapshot the suggestions and resolution work against.</summary>
    public void LoadSnapshot(IReadOnlyList<Person> people) => _people = people;

    private Task<IEnumerable<object?>?> PopulateAsync(string text, CancellationToken ct)
    {
        if (text.Length < 1) return Task.FromResult<IEnumerable<object?>?>(null);

        var matches = _people
            .Where(p =>
                p.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                p.SortName.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.SortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.PersonId)
            .Take(20)
            .ToList();

        bool exactMatch = matches.Any(p => IsExactMatch(p, text));

        var suggestions = new List<IPersonSuggestion>();
        foreach (var p in matches)
            suggestions.Add(new ExistingPersonSuggestion(p));
        if (!exactMatch)
            suggestions.Add(new NewPersonSuggestion(text));

        return Task.FromResult<IEnumerable<object?>?>(suggestions.Cast<object?>());
    }

    /// <summary>
    /// Resolves typed text to an existing person by exact case-insensitive match on either name
    /// order, against the snapshot. Null means the name is new and should be created on save.
    /// </summary>
    public Person? Resolve(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;
        return _people.FirstOrDefault(p => IsExactMatch(p, trimmed));
    }

    private static bool IsExactMatch(Person person, string text) =>
        string.Equals(person.DisplayName.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(person.SortName.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase);
}

// ── Row editor ────────────────────────────────────────────────────────────────

/// <summary>
/// One editable person row for the PersonSuggestionBox control: the typed text plus its live
/// resolution against the shared provider, so consumers can show existing-vs-new per row and, on save,
/// persist <see cref="NameToPersist"/> — the resolved person's canonical name — so reuse works even when
/// the user typed a sort-name or a different case.
/// </summary>
public partial class PersonSuggestionRowViewModel : ObservableObject
{
    private readonly PersonSuggestionProvider _provider;

    public PersonSuggestionRowViewModel(PersonSuggestionProvider provider)
    {
        _provider = provider;
        Populator = provider.Populator;
    }

    public Func<string, CancellationToken, Task<IEnumerable<object?>?>> Populator { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolvedPerson))]
    [NotifyPropertyChangedFor(nameof(IsExistingPerson))]
    [NotifyPropertyChangedFor(nameof(IsNewPerson))]
    [NotifyPropertyChangedFor(nameof(NameToPersist))]
    private string _searchText = string.Empty;

    /// <summary>The existing person the typed text resolves to, or null to create new on save.</summary>
    public Person? ResolvedPerson => _provider.Resolve(SearchText);

    public bool IsExistingPerson => ResolvedPerson is not null;

    /// <summary>True when non-empty text does not match any existing person.</summary>
    public bool IsNewPerson => !string.IsNullOrWhiteSpace(SearchText) && ResolvedPerson is null;

    /// <summary>
    /// The name to save for this row. When the typed text resolves to an existing person, this is that
    /// person's canonical <see cref="Person.DisplayName"/> — because the save-time reuse matches on
    /// DisplayName only, so persisting the sort-name form the user may have typed (e.g. "Tolkien, J.R.R.")
    /// would miss the match and mint a duplicate. Otherwise it is the trimmed typed text (a new person).
    /// </summary>
    public string NameToPersist => ResolvedPerson?.DisplayName ?? SearchText.Trim();
}
