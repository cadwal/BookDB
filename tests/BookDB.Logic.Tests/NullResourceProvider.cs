using BookDB.Logic.Services;

namespace BookDB.Logic.Tests;

/// <summary>
/// Test stub for IResourceProvider — always returns null (Name fallback path).
/// </summary>
internal sealed class NullResourceProvider : IResourceProvider
{
    public string? GetString(string key) => null;
}
