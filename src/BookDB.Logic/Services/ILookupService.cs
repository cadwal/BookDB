using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Entities;

namespace BookDB.Logic.Services;

public interface ILookupService
{
    Task<T> AddAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;
    Task DeleteAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;
    Task UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;

    Task<IReadOnlyList<T>> GetAllAsync<T>(CancellationToken cancellationToken = default) where T : class;

    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesForCollectionAsync(int collectionId, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<Collection>> GetCollectionsAsync(CancellationToken cancellationToken = default);
    
    Task<ContributorRole?> GetContributorRoleByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContributorRole>> GetContributorRolesAsync(CancellationToken cancellationToken = default);
}
