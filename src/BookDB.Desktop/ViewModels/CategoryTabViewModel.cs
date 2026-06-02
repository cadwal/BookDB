using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

public partial class CategoryTabViewModel : LookupTabViewModel
{
    public override bool SupportsMerge => true;

    public CategoryTabViewModel(ILookupManagementService service, ILookupService lookupService, IWindowService windowService, IMessenger messenger)
        : base(service, lookupService, windowService, "Category", messenger) { }

    protected override Task PerformMergeAsync(int sourceId, int targetId)
        => Service.MergeCategoriesAsync(sourceId, targetId);

    protected override async Task<IReadOnlyList<LookupEntryRow>> LoadRowsAsync()
    {
        var items = await LookupService.GetAllAsync<Category>();
        return items.OrderBy(e => e.SortOrder).ThenBy(e => e.Name)
                    .Select(e => new LookupEntryRow(e.CategoryId, e.Name)).ToList();
    }

    protected override Task<int> GetUsageCountAsync(int id) => Service.GetCategoryBookCountAsync(id);
    protected override Task<int> AddEntryAsync(string name) => Service.AddCategoryAsync(name);
    protected override Task RenameEntryAsync(int id, string name) => Service.RenameCategoryAsync(id, name);
    protected override Task DeleteEntryAsync(int id) => Service.DeleteCategoryAsync(id);
}
