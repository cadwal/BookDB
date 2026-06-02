namespace BookDB.Logic.Services;

/// <summary>
/// Provides localised string lookup by resource key.
/// Abstracts the ResourceManager to keep BookDB.Logic independent of BookDB.Desktop.
/// </summary>
public interface IResourceProvider
{
    /// <summary>Returns the localised string for <paramref name="key"/>, or null if not found.</summary>
    string? GetString(string key);
}
