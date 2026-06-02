using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

public partial class PurchasePlaceTabViewModel : LookupTabViewModel
{
    public PurchasePlaceTabViewModel(ILookupManagementService service, ILookupService lookupService, IWindowService windowService, IMessenger messenger)
        : base(service, lookupService, windowService, "PurchasePlace", messenger) { }

    public override bool SupportsMerge => true;

    protected override async Task<IReadOnlyList<LookupEntryRow>> LoadRowsAsync()
    {
        var items = await LookupService.GetAllAsync<PurchasePlace>();
        return items.Select(e => new LookupEntryRow(e.PurchasePlaceId, e.Name)).ToList();
    }

    protected override Task<int> GetUsageCountAsync(int id) => Service.GetPurchasePlaceBookCountAsync(id);
    protected override Task<int> AddEntryAsync(string name) => Service.AddPurchasePlaceAsync(name);
    protected override Task RenameEntryAsync(int id, string name) => Service.RenamePurchasePlaceAsync(id, name);
    protected override Task DeleteEntryAsync(int id) => Service.DeletePurchasePlaceAsync(id);

    protected override Task PerformMergeAsync(int sourceId, int targetId)
        => Service.MergePurchasePlacesAsync(sourceId, targetId);
}
