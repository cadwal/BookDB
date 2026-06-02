using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

public partial class LanguageTabViewModel : LookupTabViewModel
{
    public LanguageTabViewModel(ILookupManagementService service, ILookupService lookupService, IWindowService windowService, IMessenger messenger)
        : base(service, lookupService, windowService, "Language", messenger) { }

    public override bool SupportsMerge => true;

    protected override async Task<IReadOnlyList<LookupEntryRow>> LoadRowsAsync()
    {
        var items = await LookupService.GetAllAsync<Language>();
        return items.Select(e => new LookupEntryRow(e.LanguageId, e.Name)).ToList();
    }

    protected override Task<int> GetUsageCountAsync(int id) => Service.GetLanguageBookCountAsync(id);
    protected override Task<int> AddEntryAsync(string name) => Service.AddLanguageAsync(name);
    protected override Task RenameEntryAsync(int id, string name) => Service.RenameLanguageAsync(id, name);
    protected override Task DeleteEntryAsync(int id) => Service.DeleteLanguageAsync(id);

    protected override Task PerformMergeAsync(int sourceId, int targetId)
        => Service.MergeLanguagesAsync(sourceId, targetId);
}
