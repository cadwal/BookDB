using System.Collections.Generic;
using System.Threading.Tasks;

namespace BookDB.Models.Interfaces;

public interface IFilePickerService
{
    Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions);
    Task<string?> PickFolderAsync(string title);
    Task<string?> SaveFileAsync(string title, string suggestedName, IReadOnlyList<string> extensions);
}
