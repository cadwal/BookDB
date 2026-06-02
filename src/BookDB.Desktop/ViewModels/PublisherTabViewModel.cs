using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

public partial class PublisherTabViewModel : LookupTabViewModel
{
    public PublisherTabViewModel(ILookupManagementService service, ILookupService lookupService, IWindowService windowService, IMessenger messenger)
        : base(service, lookupService, windowService, "Publisher", messenger) { }

    public override bool SupportsMerge => true;

    protected override async Task<IReadOnlyList<LookupEntryRow>> LoadRowsAsync()
    {
        var items = await LookupService.GetAllAsync<Publisher>();
        return items.Select(e => new LookupEntryRow(e.PublisherId, e.Name)).ToList();
    }

    protected override Task<int> GetUsageCountAsync(int id) => Service.GetPublisherBookCountAsync(id);
    protected override Task<int> AddEntryAsync(string name) => Service.AddPublisherAsync(name);
    protected override Task RenameEntryAsync(int id, string name) => Service.RenamePublisherAsync(id, name);
    protected override Task DeleteEntryAsync(int id) => Service.DeletePublisherAsync(id);

    protected override Task PerformMergeAsync(int sourceId, int targetId)
        => Service.MergePublishersAsync(sourceId, targetId);
}
