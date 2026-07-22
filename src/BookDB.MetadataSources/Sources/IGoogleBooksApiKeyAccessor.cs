namespace BookDB.MetadataSources.Sources;

/// <summary>
/// Holds the user's optional Google Books API key so <see cref="GoogleBooksClient"/> can attach it
/// without depending on the settings layer, which lives above this project. The value is pushed in
/// from the Logic layer before each lookup; null means fall back to the shared anonymous quota
/// (which is exhausted often enough that a personal key is the reliable path).
/// </summary>
public interface IGoogleBooksApiKeyAccessor
{
    string? ApiKey { get; set; }
}

public sealed class GoogleBooksApiKeyAccessor : IGoogleBooksApiKeyAccessor
{
    public string? ApiKey { get; set; }
}
