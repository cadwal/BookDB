namespace BookDB.Models;

public sealed class AppSettings
{
    public string ActiveLibraryPath { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
}
