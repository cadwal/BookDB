using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BookDB.Models.Interfaces;

namespace BookDB.Desktop.Services;

public sealed class FilePickerService : IFilePickerService
{
    private readonly Func<TopLevel> _topLevelFactory;

    public FilePickerService(Func<TopLevel> topLevelFactory)
    {
        _topLevelFactory = topLevelFactory;
    }

    public async Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions)
    {
        var patterns = extensions
            .Select(e => e.StartsWith("*.") ? e : e.StartsWith(".") ? $"*{e}" : $"*.{e}")
            .ToArray();

        var files = await _topLevelFactory().StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Files") { Patterns = patterns }
                }
            });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _topLevelFactory().StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, IReadOnlyList<string> extensions)
    {
        var patterns = extensions
            .Select(e => e.StartsWith("*.") ? e : $"*.{e}")
            .ToArray();

        var file = await _topLevelFactory().StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Files") { Patterns = patterns }
                }
            });

        return file?.Path.LocalPath;
    }
}
