using System;
using System.Threading.Tasks;
using Avalonia.Input.Platform;

namespace BookDB.Desktop.Services;

public sealed class ClipboardService : IClipboardService
{
    private readonly Func<IClipboard?> _clipboardFactory;

    public ClipboardService(Func<IClipboard?> clipboardFactory)
    {
        _clipboardFactory = clipboardFactory;
    }

    public async Task SetTextAsync(string text)
    {
        var clipboard = _clipboardFactory();
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }
}
