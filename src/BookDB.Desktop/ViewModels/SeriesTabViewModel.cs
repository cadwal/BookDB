using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

public partial class SeriesTabViewModel : LookupTabViewModel
{
    public SeriesTabViewModel(ILookupManagementService service, ILookupService lookupService, IWindowService windowService, IMessenger messenger)
        : base(service, lookupService, windowService, "Series", messenger) { }

    public override bool SupportsMerge => true;

    protected override async Task<IReadOnlyList<LookupEntryRow>> LoadRowsAsync()
    {
        var items = await LookupService.GetAllAsync<Series>();
        return items.Select(e => new LookupEntryRow(e.SeriesId, e.Name)).ToList();
    }

    protected override Task<int> GetUsageCountAsync(int id) => Service.GetSeriesBookCountAsync(id);
    protected override Task<int> AddEntryAsync(string name) => Service.AddSeriesAsync(name);
    protected override Task RenameEntryAsync(int id, string name) => Service.RenameSeriesAsync(id, name);
    protected override Task DeleteEntryAsync(int id) => Service.DeleteSeriesAsync(id);

    protected override Task PerformMergeAsync(int sourceId, int targetId)
        => Service.MergeSeriesAsync(sourceId, targetId);
}
