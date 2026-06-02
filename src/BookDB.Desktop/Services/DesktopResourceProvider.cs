using BookDB.Desktop.Localization;
using BookDB.Logic.Services;

namespace BookDB.Desktop.Services;

/// <summary>
/// Desktop implementation of IResourceProvider that delegates to the
/// generated Resources.ResourceManager. Keeps BookDB.Logic free of any
/// reference to BookDB.Desktop.
/// </summary>
public sealed class DesktopResourceProvider : IResourceProvider
{
    public string? GetString(string key)
        => Resources.ResourceManager.GetString(key);
}
