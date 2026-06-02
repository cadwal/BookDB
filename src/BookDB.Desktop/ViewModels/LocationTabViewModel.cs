using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

public partial class LocationTabViewModel : LookupTabViewModel
{
    public LocationTabViewModel(ILookupManagementService service, ILookupService lookupService, IWindowService windowService, IMessenger messenger)
        : base(service, lookupService, windowService, "Location", messenger) { }

    public override bool SupportsMerge => true;

    protected override async Task<IReadOnlyList<LookupEntryRow>> LoadRowsAsync()
    {
        var items = await LookupService.GetAllAsync<Location>();
        return items.Select(e => new LookupEntryRow(e.LocationId, e.Name)).ToList();
    }

    protected override Task<int> GetUsageCountAsync(int id) => Service.GetLocationBookCountAsync(id);
    protected override Task<int> AddEntryAsync(string name) => Service.AddLocationAsync(name);
    protected override Task RenameEntryAsync(int id, string name) => Service.RenameLocationAsync(id, name);
    protected override Task DeleteEntryAsync(int id) => Service.DeleteLocationAsync(id);

    protected override Task PerformMergeAsync(int sourceId, int targetId)
        => Service.MergeLocationsAsync(sourceId, targetId);
}
