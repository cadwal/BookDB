using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

public partial class OwnerTabViewModel : LookupTabViewModel
{
    public OwnerTabViewModel(ILookupManagementService service, ILookupService lookupService, IWindowService windowService, IMessenger messenger)
        : base(service, lookupService, windowService, "Owner", messenger) { }

    public override bool SupportsMerge => true;

    protected override async Task<IReadOnlyList<LookupEntryRow>> LoadRowsAsync()
    {
        var items = await LookupService.GetAllAsync<Owner>();
        return items.Select(e => new LookupEntryRow(e.OwnerId, e.Name)).ToList();
    }

    protected override Task<int> GetUsageCountAsync(int id) => Service.GetOwnerBookCountAsync(id);
    protected override Task<int> AddEntryAsync(string name) => Service.AddOwnerAsync(name);
    protected override Task RenameEntryAsync(int id, string name) => Service.RenameOwnerAsync(id, name);
    protected override Task DeleteEntryAsync(int id) => Service.DeleteOwnerAsync(id);

    protected override Task PerformMergeAsync(int sourceId, int targetId)
        => Service.MergeOwnersAsync(sourceId, targetId);
}
