using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

public interface ILookupManagementService
{
    Task<int> GetPublisherBookCountAsync(int publisherId, CancellationToken ct = default);

    Task<int> GetSeriesBookCountAsync(int seriesId, CancellationToken ct = default);

    Task<int> GetLocationBookCountAsync(int locationId, CancellationToken ct = default);

    Task<int> GetOwnerBookCountAsync(int ownerId, CancellationToken ct = default);

    Task<int> GetLanguageBookCountAsync(int languageId, CancellationToken ct = default);

    Task<int> GetPersonBookContributionCountAsync(int personId, CancellationToken ct = default);

    Task<bool> PersonHasAuthorRoleAsync(int personId, CancellationToken ct = default);

    Task<PersonBioData?> GetPersonBioAsync(int personId, CancellationToken ct = default);

    Task UpdatePersonBioAsync(int personId, string? bio, string? birthDate, string? birthPlace,
        string? deathDate, string? deathPlace, string? website, CancellationToken ct = default);

    Task RenamePublisherAsync(int publisherId, string newName, CancellationToken ct = default);

    Task RenameSeriesAsync(int seriesId, string newName, CancellationToken ct = default);

    Task RenameLocationAsync(int locationId, string newName, CancellationToken ct = default);

    Task RenameOwnerAsync(int ownerId, string newName, CancellationToken ct = default);

    Task RenameLanguageAsync(int languageId, string newName, CancellationToken ct = default);

    Task UpdatePersonAsync(int personId, string displayName, string sortName, CancellationToken ct = default);

    Task<int> AddPublisherAsync(string name, CancellationToken ct = default);

    Task<int> AddSeriesAsync(string name, CancellationToken ct = default);

    Task<int> AddLocationAsync(string name, CancellationToken ct = default);

    Task<int> AddOwnerAsync(string name, CancellationToken ct = default);

    Task<int> AddLanguageAsync(string name, CancellationToken ct = default);

    Task<int> AddPersonAsync(string displayName, string? sortName = null, CancellationToken ct = default);

    Task DeletePublisherAsync(int publisherId, CancellationToken ct = default);

    Task DeleteSeriesAsync(int seriesId, CancellationToken ct = default);

    Task DeleteLocationAsync(int locationId, CancellationToken ct = default);

    Task DeleteOwnerAsync(int ownerId, CancellationToken ct = default);

    Task DeleteLanguageAsync(int languageId, CancellationToken ct = default);

    Task DeletePersonAsync(int personId, CancellationToken ct = default);

    Task MergePublishersAsync(int sourceId, int targetId, CancellationToken ct = default);

    Task MergeSeriesAsync(int sourceId, int targetId, CancellationToken ct = default);

    Task MergePersonsAsync(int sourcePersonId, int targetPersonId, CancellationToken ct = default);

    Task MergeLocationsAsync(int sourceId, int targetId, CancellationToken ct = default);

    Task MergeOwnersAsync(int sourceId, int targetId, CancellationToken ct = default);

    Task MergeLanguagesAsync(int sourceId, int targetId, CancellationToken ct = default);

    Task<(IReadOnlyList<CleanupProposal> Renames, IReadOnlyList<SplitProposal> Splits, int IgnoredCount)>
        ScanPersonNameCleanupAsync(CancellationToken ct = default);

    Task ApplyPersonNameCleanupAsync(IReadOnlyList<CleanupProposal> proposals, CancellationToken ct = default);

    Task ApplySplitProposalAsync(IReadOnlyList<SplitProposal> proposals, CancellationToken ct = default);

    /// <summary>Persists an ignore for a rename proposal (person + proposed content). Idempotent.</summary>
    Task AddCleanupIgnoreAsync(CleanupProposal proposal, CancellationToken ct = default);

    /// <summary>Persists an ignore for a whole split proposal (person + the ordered fragment set). Idempotent.</summary>
    Task AddCleanupIgnoreAsync(SplitProposal proposal, CancellationToken ct = default);

    /// <summary>Persists an ignore for a suspected-duplicate pair, so it stops being suggested. Idempotent.</summary>
    Task AddDuplicateIgnoreAsync(int personIdA, string nameA, int personIdB, string nameB, CancellationToken ct = default);

    /// <summary>The persisted duplicate ignores as (anchor person id, the other person's name) for scan filtering.</summary>
    Task<IReadOnlySet<(int AnchorPersonId, string Fingerprint)>> GetDuplicateIgnoresAsync(CancellationToken ct = default);

    Task RemoveCleanupIgnoreAsync(int ignoreId, CancellationToken ct = default);

    Task<IReadOnlyList<CleanupIgnoreRow>> GetCleanupIgnoresAsync(CancellationToken ct = default);

    Task<int> GetCategoryBookCountAsync(int categoryId, CancellationToken ct = default);
    Task<int> AddCategoryAsync(string name, CancellationToken ct = default);
    Task RenameCategoryAsync(int categoryId, string newName, CancellationToken ct = default);
    Task DeleteCategoryAsync(int categoryId, CancellationToken ct = default);
    Task MergeCategoriesAsync(int sourceId, int targetId, CancellationToken ct = default);

    Task<int> GetCollectionBookCountAsync(int collectionId, CancellationToken ct = default);
    Task<int> AddCollectionAsync(string name, CancellationToken ct = default);
    Task RenameCollectionAsync(int collectionId, string newName, CancellationToken ct = default);
    Task DeleteCollectionAsync(int collectionId, CancellationToken ct = default);
    Task MergeCollectionsAsync(int sourceId, int targetId, CancellationToken ct = default);
    Task ReorderCollectionsAsync(IReadOnlyList<int> orderedCollectionIds, CancellationToken ct = default);

    Task<int> GetPurchasePlaceBookCountAsync(int purchasePlaceId, CancellationToken ct = default);
    Task<int> AddPurchasePlaceAsync(string name, CancellationToken ct = default);
    Task RenamePurchasePlaceAsync(int purchasePlaceId, string newName, CancellationToken ct = default);
    Task DeletePurchasePlaceAsync(int purchasePlaceId, CancellationToken ct = default);
    Task MergePurchasePlacesAsync(int sourceId, int targetId, CancellationToken ct = default);
}
