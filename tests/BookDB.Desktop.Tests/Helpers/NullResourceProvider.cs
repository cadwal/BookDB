using BookDB.Logic.Services;

namespace BookDB.Desktop.Tests.Helpers;

/// <summary>
/// Test stub for IResourceProvider — always returns null (Name fallback path).
/// </summary>
internal sealed class NullResourceProvider : IResourceProvider
{
    public string? GetString(string key) => null;
}
