using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed class LookupService : ILookupService, ISettingsService
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly IResourceProvider _resourceProvider;

    public LookupService(IDbContextFactory<BookDbContext> factory, IResourceProvider resourceProvider)
    {
        _factory = factory;
        _resourceProvider = resourceProvider;
    }

    public async Task<IReadOnlyList<T>> GetAllAsync<T>(
        CancellationToken cancellationToken = default) where T : class
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Set<T>().ToListAsync(cancellationToken);
    }

    public async Task<T> AddAsync<T>(T entity, CancellationToken cancellationToken = default)
        where T : class
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        dbContext.Set<T>().Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task UpdateAsync<T>(T entity, CancellationToken cancellationToken = default)
        where T : class
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        dbContext.Set<T>().Update(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync<T>(T entity, CancellationToken cancellationToken = default)
        where T : class
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        dbContext.Set<T>().Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ContributorRole>> GetContributorRolesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Set<ContributorRole>()
            .OrderBy(r => r.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<ContributorRole?> GetContributorRoleByCodeAsync(
        string code, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Set<ContributorRole>()
            .FirstOrDefaultAsync(r => r.Code == code, cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetCollectionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Set<Collection>()
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Set<Category>()
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> GetCategoriesForCollectionAsync(
        int collectionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Set<CategoryCollection>()
            .Where(cc => cc.CollectionId == collectionId)
            .Select(cc => cc.Category!)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);
    }

    private async Task<string?> GetSettingAsync(
        string key, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        var setting = await dbContext.Set<Settings>()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        return setting?.Value;
    }

    private async Task SetSettingAsync(
        string key, string? value, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        var setting = await dbContext.Set<Settings>()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting is null)
        {
            dbContext.Set<Settings>().Add(new Settings { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
            dbContext.Set<Settings>().Update(setting);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => GetSettingAsync(key, ct);

    public Task SetAsync(string key, string? value, CancellationToken ct = default)
        => SetSettingAsync(key, value, ct);

    public string GetDisplayName(string name, string? resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey)) return name;
        return _resourceProvider.GetString(resourceKey) ?? name;
    }
}
