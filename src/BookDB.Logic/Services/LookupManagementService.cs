using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Logic.Helpers;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed record CleanupProposal(
    int PersonId,
    string CurrentDisplayName,
    string CurrentSortName,
    string ProposedDisplayName,
    string SuggestedSortName);

public sealed record SplitFragment(string ProposedDisplayName, string SuggestedSortName);

public sealed record SplitProposal(
    int PersonId,
    string CurrentDisplayName,
    IReadOnlyList<SplitFragment> Fragments);

public sealed record PersonBioData(
    string? Bio,
    string? BirthDate,
    string? BirthPlace,
    string? DeathDate,
    string? DeathPlace,
    string? Website);

/// <summary>An ignored person-name cleanup proposal, joined with the person's current display name for the UI.</summary>
public sealed record CleanupIgnoreRow(
    int IgnoreId,
    int PersonId,
    string PersonDisplayName,
    string Kind,
    string ProposedContent,
    DateTime CreatedAt);

/// <summary>The proposal kinds an ignore can dismiss; stored verbatim in <c>PersonCleanupIgnore.Kind</c>.</summary>
public static class CleanupIgnoreKind
{
    public const string Rename = "Rename";
    public const string Split = "Split";
    public const string Duplicate = "Duplicate";
}

public sealed class LookupManagementService : ILookupManagementService
{
    private const string AuthorRoleCode = "Author";

    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly ILookupNameMatcher _nameMatcher;

    public LookupManagementService(IDbContextFactory<BookDbContext> factory, ILookupNameMatcher nameMatcher)
    {
        _factory = factory;
        _nameMatcher = nameMatcher;
    }

    public async Task<int> GetPublisherBookCountAsync(int publisherId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Books.CountAsync(b => b.PublisherId == publisherId, ct);
    }

    public async Task<int> GetSeriesBookCountAsync(int seriesId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Books.CountAsync(b => b.SeriesId == seriesId, ct);
    }

    public async Task<int> GetLocationBookCountAsync(int locationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Books.CountAsync(b => b.LocationId == locationId, ct);
    }

    public async Task<int> GetOwnerBookCountAsync(int ownerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Books.CountAsync(b => b.OwnerId == ownerId, ct);
    }

    public async Task<int> GetLanguageBookCountAsync(int languageId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Books.CountAsync(b => b.LanguageId == languageId, ct);
    }

    public async Task<int> GetCollectionBookCountAsync(int collectionId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Books.CountAsync(b => b.CollectionId == collectionId, ct);
    }

    public Task RenameCollectionAsync(int collectionId, string newName, CancellationToken ct = default)
        => RenameNamedEntityAsync<Collection>(collectionId, newName, "A Collection", ct);

    public Task DeleteCollectionAsync(int collectionId, CancellationToken ct = default)
        => DeleteNamedEntityAsync<Collection>(collectionId, b => b.CollectionId == collectionId, "Collection", ct);

    public async Task<int> AddCollectionAsync(string name, CancellationToken ct = default)
    {
        var trimmed = ValidateName(name);
        return await InTransactionAsync(async db =>
        {
            if (await db.Collections.AnyAsync(_nameMatcher.NameEquals<Collection>(trimmed), ct))
                throw new InvalidOperationException($"A Collection with the name '{trimmed}' already exists.");
            // Append at the end of the existing sort order.
            var maxSort = await db.Collections.MaxAsync(c => (int?)c.SortOrder, ct) ?? -1;
            var entity = new Collection { Name = trimmed, SortOrder = maxSort + 1 };
            db.Collections.Add(entity);
            await db.SaveChangesAsync(ct);
            return entity.CollectionId;
        }, ct);
    }

    public async Task MergeCollectionsAsync(int sourceId, int targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) return;
        await InTransactionAsync(async db =>
        {
            // Move the source's books to the target, then delete the source.
            // CategoryCollection rows for the source are removed by ON DELETE CASCADE.
            await db.Books
                .Where(b => b.CollectionId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.CollectionId, (int?)targetId), ct);
            await db.Collections.Where(c => c.CollectionId == sourceId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task ReorderCollectionsAsync(IReadOnlyList<int> orderedCollectionIds, CancellationToken ct = default)
    {
        await InTransactionAsync(async db =>
        {
            for (var i = 0; i < orderedCollectionIds.Count; i++)
            {
                var id = orderedCollectionIds[i];
                var sortOrder = i;
                await db.Collections
                    .Where(c => c.CollectionId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.SortOrder, sortOrder), ct);
            }
        }, ct);
    }

    public async Task<int> GetPersonBookContributionCountAsync(int personId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BookContributors.CountAsync(bc => bc.PersonId == personId, ct);
    }

    public async Task<bool> PersonHasAuthorRoleAsync(int personId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BookContributors
            .Where(bc => bc.PersonId == personId)
            .Join(db.ContributorRoles,
                bc => bc.ContributorRoleId,
                cr => cr.ContributorRoleId,
                (bc, cr) => cr.Code)
            .AnyAsync(code => code == AuthorRoleCode, ct);
    }

    public async Task<PersonBioData?> GetPersonBioAsync(int personId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.People
            .Where(p => p.PersonId == personId)
            .Select(p => new PersonBioData(
                p.Bio, p.BirthDate, p.BirthPlace,
                p.DeathDate, p.DeathPlace, p.Website))
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpdatePersonBioAsync(int personId, string? bio, string? birthDate, string? birthPlace,
        string? deathDate, string? deathPlace, string? website, CancellationToken ct = default)
    {
        await InTransactionAsync(async db =>
        {
            var count = await db.People
                .Where(p => p.PersonId == personId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Bio, bio == null ? null : bio.Trim())
                    .SetProperty(p => p.BirthDate, birthDate == null ? null : birthDate.Trim())
                    .SetProperty(p => p.BirthPlace, birthPlace == null ? null : birthPlace.Trim())
                    .SetProperty(p => p.DeathDate, deathDate == null ? null : deathDate.Trim())
                    .SetProperty(p => p.DeathPlace, deathPlace == null ? null : deathPlace.Trim())
                    .SetProperty(p => p.Website, website == null ? null : website.Trim()),
                ct);
            if (count == 0)
                throw new InvalidOperationException($"Person {personId} not found.");
        }, ct);
    }

    public Task RenamePublisherAsync(int publisherId, string newName, CancellationToken ct = default)
        => RenameNamedEntityAsync<Publisher>(publisherId, newName, "A Publisher", ct);

    public Task RenameSeriesAsync(int seriesId, string newName, CancellationToken ct = default)
        => RenameNamedEntityAsync<Series>(seriesId, newName, "A Series", ct);

    public Task RenameLocationAsync(int locationId, string newName, CancellationToken ct = default)
        => RenameNamedEntityAsync<Location>(locationId, newName, "A Location", ct);

    public Task RenameOwnerAsync(int ownerId, string newName, CancellationToken ct = default)
        => RenameNamedEntityAsync<Owner>(ownerId, newName, "An Owner", ct);

    public Task RenameLanguageAsync(int languageId, string newName, CancellationToken ct = default)
        => RenameNamedEntityAsync<Language>(languageId, newName, "A Language", ct);

    public async Task UpdatePersonAsync(int personId, string displayName, string sortName, CancellationToken ct = default)
    {
        var trimmedDisplay = ValidateName(displayName);
        var trimmedSort = string.IsNullOrWhiteSpace(sortName) ? trimmedDisplay : sortName.Trim();
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.People
            .Where(p => p.PersonId == personId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DisplayName, trimmedDisplay)
                .SetProperty(p => p.SortName, trimmedSort), ct);
    }

    public Task<int> AddPublisherAsync(string name, CancellationToken ct = default)
        => AddNamedEntityAsync<Publisher>(name, n => new Publisher { Name = n }, "A Publisher", ct);

    public Task<int> AddSeriesAsync(string name, CancellationToken ct = default)
        => AddNamedEntityAsync<Series>(name, n => new Series { Name = n }, "A Series", ct);

    public Task<int> AddLocationAsync(string name, CancellationToken ct = default)
        => AddNamedEntityAsync<Location>(name, n => new Location { Name = n }, "A Location", ct);

    public Task<int> AddOwnerAsync(string name, CancellationToken ct = default)
        => AddNamedEntityAsync<Owner>(name, n => new Owner { Name = n }, "An Owner", ct);

    public Task<int> AddLanguageAsync(string name, CancellationToken ct = default)
        => AddNamedEntityAsync<Language>(name, n => new Language { Name = n }, "A Language", ct);

    public async Task<int> AddPersonAsync(string displayName, string? sortName = null, CancellationToken ct = default)
    {
        var trimmedDisplay = ValidateName(displayName);
        var trimmedSort = string.IsNullOrWhiteSpace(sortName) ? PersonNameHelper.DeriveSortName(trimmedDisplay) : sortName.Trim();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = new Person { DisplayName = trimmedDisplay, SortName = trimmedSort };
        db.People.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.PersonId;
    }

    public Task DeletePublisherAsync(int publisherId, CancellationToken ct = default)
        => DeleteNamedEntityAsync<Publisher>(publisherId, b => b.PublisherId == publisherId, "Publisher", ct);

    public Task DeleteSeriesAsync(int seriesId, CancellationToken ct = default)
        => DeleteNamedEntityAsync<Series>(seriesId, b => b.SeriesId == seriesId, "Series", ct);

    public Task DeleteLocationAsync(int locationId, CancellationToken ct = default)
        => DeleteNamedEntityAsync<Location>(locationId, b => b.LocationId == locationId, "Location", ct);

    public Task DeleteOwnerAsync(int ownerId, CancellationToken ct = default)
        => DeleteNamedEntityAsync<Owner>(ownerId, b => b.OwnerId == ownerId, "Owner", ct);

    public Task DeleteLanguageAsync(int languageId, CancellationToken ct = default)
        => DeleteNamedEntityAsync<Language>(languageId, b => b.LanguageId == languageId, "Language", ct);

    public async Task DeletePersonAsync(int personId, CancellationToken ct = default)
    {
        await InTransactionAsync(async dbContext =>
        {
            var count = await dbContext.BookContributors.CountAsync(bc => bc.PersonId == personId, ct);
            if (count > 0)
                throw new InvalidOperationException($"Cannot delete Person — used in {count} books.");
            await dbContext.People.Where(p => p.PersonId == personId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task MergePublishersAsync(int sourceId, int targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) return;
        await InTransactionAsync(async db =>
        {
            await db.Books
                .Where(b => b.PublisherId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.PublisherId, (int?)targetId), ct);
            await db.Publishers.Where(p => p.PublisherId == sourceId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task MergeSeriesAsync(int sourceId, int targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) return;
        await InTransactionAsync(async db =>
        {
            await db.Books
                .Where(b => b.SeriesId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.SeriesId, (int?)targetId), ct);
            await db.Series.Where(p => p.SeriesId == sourceId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task MergeLocationsAsync(int sourceId, int targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) return;
        await InTransactionAsync(async db =>
        {
            await db.Books
                .Where(b => b.LocationId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.LocationId, (int?)targetId), ct);
            await db.Locations.Where(l => l.LocationId == sourceId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task MergeOwnersAsync(int sourceId, int targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) return;
        await InTransactionAsync(async db =>
        {
            await db.Books
                .Where(b => b.OwnerId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.OwnerId, (int?)targetId), ct);
            await db.Owners.Where(o => o.OwnerId == sourceId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task MergeLanguagesAsync(int sourceId, int targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) return;
        await InTransactionAsync(async db =>
        {
            await db.Books
                .Where(b => b.LanguageId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.LanguageId, (int?)targetId), ct);
            await db.Languages.Where(l => l.LanguageId == sourceId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task MergePersonsAsync(int sourcePersonId, int targetPersonId, CancellationToken ct = default)
    {
        if (sourcePersonId == targetPersonId) return;
        await InTransactionAsync(async db =>
        {
            // 1. Repoint source contributor rows to target.
            await db.BookContributors
                .Where(bc => bc.PersonId == sourcePersonId)
                .ExecuteUpdateAsync(s => s.SetProperty(bc => bc.PersonId, targetPersonId), ct);

            // 2. Deduplicate: after repoint, the target may now have multiple rows with the same
            //    (BookId, ContributorRoleId). Keep the row with the smallest BookContributorId;
            //    delete the rest. V001 schema has no unique index, so we handle it ourselves.
            var targetRows = await db.BookContributors
                .Where(bc => bc.PersonId == targetPersonId)
                .ToListAsync(ct);
            var duplicateIds = targetRows
                .GroupBy(bc => new { bc.BookId, bc.ContributorRoleId })
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.OrderBy(bc => bc.BookContributorId).Skip(1))
                .Select(bc => bc.BookContributorId)
                .ToList();
            if (duplicateIds.Count > 0)
            {
                await db.BookContributors
                    .Where(bc => duplicateIds.Contains(bc.BookContributorId))
                    .ExecuteDeleteAsync(ct);
            }

            // 3. Delete the source person row.
            await db.People.Where(p => p.PersonId == sourcePersonId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task<(IReadOnlyList<CleanupProposal> Renames, IReadOnlyList<SplitProposal> Splits, int IgnoredCount)>
        ScanPersonNameCleanupAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var persons = await db.People
            .AsNoTracking()
            .Select(p => new { p.PersonId, p.DisplayName, p.SortName })
            .ToListAsync(ct);

        // The fingerprint (person + kind + proposed content) is re-derived on every scan, so a dismissed proposal
        // is dropped only while it re-derives identically — changed person data yields a new fingerprint and
        // resurfaces. Ordinal comparison mirrors the derivation below.
        var ignores = (await db.PersonCleanupIgnores
                .Select(i => new { i.PersonId, i.Kind, i.ProposedContent })
                .ToListAsync(ct))
            .Select(i => (i.PersonId, i.Kind, i.ProposedContent))
            .ToHashSet();

        var proposals = new List<CleanupProposal>();
        var splits = new List<SplitProposal>();
        var ignoredCount = 0;
        foreach (var p in persons)
        {
            // Check squish first — if it's a squished entry, it becomes a SplitProposal, not a rename
            var fragments = PersonNameHelper.SplitSquished(p.DisplayName);
            if (fragments.Count > 1)
            {
                var splitFragments = fragments
                    .Select(f =>
                    {
                        var cleaned = PersonNameHelper.DeriveDisplayName(f);
                        var sort = PersonNameHelper.DeriveSortName(cleaned);
                        return new SplitFragment(cleaned, sort);
                    })
                    .Where(fr => !string.IsNullOrWhiteSpace(fr.ProposedDisplayName))
                    .ToList();
                if (splitFragments.Count < 2) continue; // not a real split if only one valid fragment remains
                if (ignores.Contains((p.PersonId, CleanupIgnoreKind.Split, SplitFingerprint(splitFragments))))
                {
                    ignoredCount++;
                    continue;
                }
                splits.Add(new SplitProposal(p.PersonId, p.DisplayName, splitFragments));
                continue; // Skip rename check for squished entries
            }

            // Normal rename check
            var proposedDisplay = PersonNameHelper.DeriveDisplayName(p.DisplayName);
            var proposedSort = PersonNameHelper.DeriveSortName(proposedDisplay);
            var displayChanged = !string.Equals(proposedDisplay, p.DisplayName, StringComparison.Ordinal);
            var sortChanged = !string.Equals(proposedSort, p.SortName, StringComparison.Ordinal);
            if (!displayChanged && !sortChanged) continue;
            if (ignores.Contains((p.PersonId, CleanupIgnoreKind.Rename, RenameFingerprint(p.SortName, proposedDisplay, proposedSort))))
            {
                ignoredCount++;
                continue;
            }
            proposals.Add(new CleanupProposal(p.PersonId, p.DisplayName, p.SortName, proposedDisplay, proposedSort));
        }
        return (proposals, splits, ignoredCount);
    }

    // Canonical, human-readable fingerprints — stored verbatim so the ignored-list UI can show them without
    // re-deriving. Ordinal-sensitive, matching the scan's comparison. The rename fingerprint includes the
    // person's current sort name (not just the proposed values) — DeriveSortName is a pure function of the
    // display name, so without it, editing only the sort name would re-derive the identical fingerprint and
    // the ignore would never lift.
    private static string RenameFingerprint(string currentSortName, string proposedDisplay, string suggestedSort) =>
        $"{currentSortName}|{proposedDisplay}|{suggestedSort}";

    private static string SplitFingerprint(IEnumerable<SplitFragment> fragments) =>
        string.Join(";", fragments.Select(f => $"{f.ProposedDisplayName}|{f.SuggestedSortName}"));

    public async Task AddCleanupIgnoreAsync(CleanupProposal proposal, CancellationToken ct = default)
    {
        var content = RenameFingerprint(proposal.CurrentSortName, proposal.ProposedDisplayName, proposal.SuggestedSortName);
        await AddCleanupIgnoreAsync(proposal.PersonId, CleanupIgnoreKind.Rename, content, ct);
    }

    public async Task AddCleanupIgnoreAsync(SplitProposal proposal, CancellationToken ct = default)
    {
        var content = SplitFingerprint(proposal.Fragments);
        await AddCleanupIgnoreAsync(proposal.PersonId, CleanupIgnoreKind.Split, content, ct);
    }

    public async Task AddDuplicateIgnoreAsync(
        int personIdA, string nameA, int personIdB, string nameB, CancellationToken ct = default)
    {
        // Anchor on the lower id; the fingerprint carries the OTHER person's id AND current name. The id keeps
        // two distinct people who share a display name from aliasing onto one ignore (name alone can't tell them
        // apart); the name keeps the "resurface on rename" behaviour — renaming either changes the fingerprint.
        var (anchorId, otherId, otherName) = personIdA <= personIdB
            ? (personIdA, personIdB, nameB)
            : (personIdB, personIdA, nameA);
        await AddCleanupIgnoreAsync(anchorId, CleanupIgnoreKind.Duplicate, DuplicateFingerprint(otherId, otherName), ct);
    }

    /// <summary>
    /// Duplicate-ignore fingerprint: <c>"{otherPersonId}|{otherName}"</c>. Kept next to its parse in the scan
    /// (<c>PersonTabViewModel.ScanPairs</c>, which also still matches the pre-3.1 bare-name form) so the two
    /// never drift.
    /// </summary>
    public static string DuplicateFingerprint(int otherPersonId, string otherName) => $"{otherPersonId}|{otherName}";

    public async Task<IReadOnlySet<(int AnchorPersonId, string Fingerprint)>> GetDuplicateIgnoresAsync(
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.PersonCleanupIgnores
            .Where(i => i.Kind == CleanupIgnoreKind.Duplicate)
            .Select(i => new { i.PersonId, i.ProposedContent })
            .ToListAsync(ct);
        return rows.Select(r => (r.PersonId, r.ProposedContent)).ToHashSet();
    }

    private async Task AddCleanupIgnoreAsync(int personId, string kind, string content, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Idempotent: the (PersonId, Kind, ProposedContent) unique index would otherwise reject a repeat ignore.
        var exists = await db.PersonCleanupIgnores.AnyAsync(
            i => i.PersonId == personId && i.Kind == kind && i.ProposedContent == content, ct);
        if (exists) return;
        db.PersonCleanupIgnores.Add(new PersonCleanupIgnore
        {
            PersonId = personId,
            Kind = kind,
            ProposedContent = content,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveCleanupIgnoreAsync(int ignoreId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.PersonCleanupIgnores.Where(i => i.PersonCleanupIgnoreId == ignoreId).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<CleanupIgnoreRow>> GetCleanupIgnoresAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Materialise the join as an anonymous row, then build the record in memory — projecting the record
        // inside the query and ordering over it is not translatable.
        var rows = await db.PersonCleanupIgnores
            .Join(db.People, i => i.PersonId, p => p.PersonId,
                (i, p) => new
                {
                    i.PersonCleanupIgnoreId, i.PersonId, p.DisplayName, i.Kind, i.ProposedContent, i.CreatedAt,
                })
            .ToListAsync(ct);
        return rows
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new CleanupIgnoreRow(
                r.PersonCleanupIgnoreId, r.PersonId, r.DisplayName, r.Kind, r.ProposedContent, r.CreatedAt))
            .ToList();
    }

    public async Task ApplyPersonNameCleanupAsync(IReadOnlyList<CleanupProposal> proposals, CancellationToken ct = default)
    {
        if (proposals is null || proposals.Count == 0) return;
        await InTransactionAsync(async db =>
        {
            foreach (var proposal in proposals)
            {
                await db.People
                    .Where(p => p.PersonId == proposal.PersonId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.DisplayName, proposal.ProposedDisplayName)
                        .SetProperty(p => p.SortName, proposal.SuggestedSortName), ct);
            }
        }, ct);
    }

    /// <summary>
    /// For each SplitProposal: creates N new Person records (one per accepted fragment),
    /// repoints all BookContributor rows from the original PersonId to each new Person,
    /// then deletes the original Person.
    /// </summary>
    public async Task ApplySplitProposalAsync(IReadOnlyList<SplitProposal> proposals, CancellationToken ct = default)
    {
        if (proposals is null || proposals.Count == 0) return;
        foreach (var proposal in proposals)
        {
            await InTransactionAsync(async db =>
            {
                // Load existing BookContributor rows for this person before any changes
                var existingContributors = await db.BookContributors
                    .Where(bc => bc.PersonId == proposal.PersonId)
                    .ToListAsync(ct);

                // Create N new Person records (one per accepted fragment)
                var newPersonIds = new List<int>(proposal.Fragments.Count);
                foreach (var fragment in proposal.Fragments)
                {
                    if (string.IsNullOrWhiteSpace(fragment.ProposedDisplayName)) continue; // defensive guard
                    var existing = await db.People
                        .FirstOrDefaultAsync(p => p.DisplayName == fragment.ProposedDisplayName, ct);
                    var newPerson = existing ?? new Person
                    {
                        DisplayName = fragment.ProposedDisplayName,
                        SortName = fragment.SuggestedSortName
                    };
                    if (existing is null)
                    {
                        db.People.Add(newPerson);
                        await db.SaveChangesAsync(ct); // flush to get PersonId
                    }
                    newPersonIds.Add(newPerson.PersonId);
                }

                // For each new person, add BookContributor rows mirroring the original person's rows
                // (all fragments get the same book links as the original).
                // Skip pairs that already exist to avoid silent duplicates when a fragment name
                // matches an existing Person who already has rows for these books.
                foreach (var newPersonId in newPersonIds)
                {
                    var alreadyLinked = (await db.BookContributors
                        .Where(bc => bc.PersonId == newPersonId)
                        .Select(bc => new { bc.BookId, bc.ContributorRoleId })
                        .ToListAsync(ct))
                        .Select(bc => (bc.BookId, bc.ContributorRoleId))
                        .ToHashSet();

                    foreach (var bc in existingContributors)
                    {
                        if (alreadyLinked.Contains((bc.BookId, bc.ContributorRoleId))) continue;
                        db.BookContributors.Add(new BookContributor
                        {
                            BookId = bc.BookId,
                            PersonId = newPersonId,
                            ContributorRoleId = bc.ContributorRoleId,
                            SortOrder = bc.SortOrder
                        });
                    }
                }
                await db.SaveChangesAsync(ct);

                // Delete the original BookContributor rows and the original Person
                await db.BookContributors
                    .Where(bc => bc.PersonId == proposal.PersonId)
                    .ExecuteDeleteAsync(ct);
                await db.People
                    .Where(p => p.PersonId == proposal.PersonId)
                    .ExecuteDeleteAsync(ct);
            }, ct);
        }
    }

    public async Task<int> GetCategoryBookCountAsync(int categoryId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BookCategories.CountAsync(bc => bc.CategoryId == categoryId, ct);
    }

    public Task<int> AddCategoryAsync(string name, CancellationToken ct = default)
        => AddNamedEntityAsync<Category>(name, n => new Category { Name = n, SortOrder = 0 }, "A Category", ct);

    public Task RenameCategoryAsync(int categoryId, string newName, CancellationToken ct = default)
        => RenameNamedEntityAsync<Category>(categoryId, newName, "A Category", ct);

    public async Task DeleteCategoryAsync(int categoryId, CancellationToken ct = default)
    {
        await InTransactionAsync(async db =>
        {
            var count = await db.BookCategories.CountAsync(bc => bc.CategoryId == categoryId, ct);
            if (count > 0)
                throw new InvalidOperationException($"Cannot delete Category — used by {count} books.");
            await db.Categories.Where(c => c.CategoryId == categoryId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task MergeCategoriesAsync(int sourceId, int targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) return;
        await InTransactionAsync(async db =>
        {
            // Books already assigned to target — don't duplicate
            var alreadyTargeted = await db.BookCategories
                .Where(bc => bc.CategoryId == targetId)
                .Select(bc => bc.BookId)
                .ToListAsync(ct);
            // Move source rows that don't already have the target
            await db.BookCategories
                .Where(bc => bc.CategoryId == sourceId && !alreadyTargeted.Contains(bc.BookId))
                .ExecuteUpdateAsync(s => s.SetProperty(bc => bc.CategoryId, targetId), ct);
            // Delete any remaining source rows (duplicates)
            await db.BookCategories.Where(bc => bc.CategoryId == sourceId).ExecuteDeleteAsync(ct);
            await db.Categories.Where(c => c.CategoryId == sourceId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    public async Task<int> GetPurchasePlaceBookCountAsync(int purchasePlaceId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Books.CountAsync(b => b.PurchasePlaceId == purchasePlaceId, ct);
    }

    public Task<int> AddPurchasePlaceAsync(string name, CancellationToken ct = default)
        => AddNamedEntityAsync<PurchasePlace>(name, n => new PurchasePlace { Name = n }, "A PurchasePlace", ct);

    public Task RenamePurchasePlaceAsync(int purchasePlaceId, string newName, CancellationToken ct = default)
        => RenameNamedEntityAsync<PurchasePlace>(purchasePlaceId, newName, "A PurchasePlace", ct);

    public Task DeletePurchasePlaceAsync(int purchasePlaceId, CancellationToken ct = default)
        => DeleteNamedEntityAsync<PurchasePlace>(purchasePlaceId, b => b.PurchasePlaceId == purchasePlaceId, "Purchase Place", ct);

    public async Task MergePurchasePlacesAsync(int sourceId, int targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) return;
        await InTransactionAsync(async db =>
        {
            await db.Books
                .Where(b => b.PurchasePlaceId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.PurchasePlaceId, (int?)targetId), ct);
            await db.PurchasePlaces.Where(p => p.PurchasePlaceId == sourceId).ExecuteDeleteAsync(ct);
        }, ct);
    }

    private async Task RenameNamedEntityAsync<T>(
        int id,
        string newName,
        string entityLabel,
        CancellationToken ct)
        where T : class, INamedLookup
    {
        var trimmed = ValidateName(newName);
        await InTransactionAsync(async dbContext =>
        {
            var keyName = GetPrimaryKeyPropertyName<T>(dbContext);
            var duplicate = await dbContext.Set<T>()
                .Where(e => EF.Property<int>(e, keyName) != id)
                .Where(_nameMatcher.NameEquals<T>(trimmed))
                .AnyAsync(ct);
            if (duplicate)
                throw new InvalidOperationException($"{entityLabel} with the name '{trimmed}' already exists.");
            await dbContext.Set<T>()
                .Where(e => EF.Property<int>(e, keyName) == id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Name, trimmed), ct);
        }, ct);
    }

    private async Task<int> AddNamedEntityAsync<T>(
        string name,
        Func<string, T> factory,
        string entityLabel,
        CancellationToken ct)
        where T : class, INamedLookup
    {
        var trimmed = ValidateName(name);
        return await InTransactionAsync(async dbContext =>
        {
            if (await dbContext.Set<T>().AnyAsync(_nameMatcher.NameEquals<T>(trimmed), ct))
                throw new InvalidOperationException($"{entityLabel} with the name '{trimmed}' already exists.");
            var entity = factory(trimmed);
            dbContext.Set<T>().Add(entity);
            await dbContext.SaveChangesAsync(ct);
            return entity.Id;
        }, ct);
    }

    private async Task DeleteNamedEntityAsync<T>(
        int id,
        Expression<Func<Book, bool>> bookUsagePredicate,
        string entityLabel,
        CancellationToken ct)
        where T : class, INamedLookup
    {
        await InTransactionAsync(async dbContext =>
        {
            var count = await dbContext.Books.CountAsync(bookUsagePredicate, ct);
            if (count > 0)
                throw new InvalidOperationException($"Cannot delete {entityLabel} — used by {count} books.");
            var keyName = GetPrimaryKeyPropertyName<T>(dbContext);
            await dbContext.Set<T>()
                .Where(e => EF.Property<int>(e, keyName) == id)
                .ExecuteDeleteAsync(ct);
        }, ct);
    }

    // Runs a unit of work inside a transaction the Postgres retrying execution strategy can re-run as a
    // whole — a free-standing BeginTransaction throws under that strategy. On a transient failure the
    // delegate re-executes from scratch (the rolled-back transaction left the database untouched), so the
    // change tracker is cleared each attempt. A non-transient throw (e.g. a duplicate-name conflict) is not
    // retried and propagates with the transaction rolled back.
    private async Task<TResult> InTransactionAsync<TResult>(Func<BookDbContext, Task<TResult>> work, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work(db);
            await tx.CommitAsync(ct);
            return result;
        });
    }

    private Task InTransactionAsync(Func<BookDbContext, Task> work, CancellationToken ct) =>
        InTransactionAsync(async db => { await work(db); return 0; }, ct);

    private static string ValidateName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Name cannot be empty.", nameof(raw));
        return raw.Trim();
    }

    private static string GetPrimaryKeyPropertyName<T>(BookDbContext dbContext)
        where T : class
    {
        var entityType = dbContext.Model.FindEntityType(typeof(T));
        if (entityType is null)
            throw new InvalidOperationException($"Entity type '{typeof(T).Name}' is not part of the EF model.");

        var key = entityType.FindPrimaryKey();
        if (key is null || key.Properties.Count != 1)
            throw new InvalidOperationException($"Entity type '{typeof(T).Name}' must have a single primary key.");

        return key.Properties[0].Name;
    }
}
