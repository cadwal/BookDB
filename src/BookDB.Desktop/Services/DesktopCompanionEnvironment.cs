using System.IO;
using System.Reflection;
using BookDB.Companion.Host;
using BookDB.Contracts;
using BookDB.Models;

namespace BookDB.Desktop.Services;

/// <summary>
/// Answers a phone's handshake from the desktop's own state: the app version, a readable name for the open
/// library, and the capture settings the user last saved. Read per call, so a Save that changes the
/// capture settings or a library switch is reflected without restarting the host.
/// </summary>
public sealed class DesktopCompanionEnvironment : ICompanionEnvironment
{
    private readonly AppSettings _appSettings;
    private readonly ICompanionConfig _config;

    public DesktopCompanionEnvironment(AppSettings appSettings, ICompanionConfig config)
    {
        _appSettings = appSettings;
        _config = config;
    }

    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    public string LibraryName => _appSettings.Backend switch
    {
        // The SQLite file's own name is the closest thing to a library name the app has.
        DatabaseBackend.Sqlite when !string.IsNullOrEmpty(_appSettings.SqliteLibraryPath)
            => Path.GetFileNameWithoutExtension(_appSettings.SqliteLibraryPath!),
        _ => "BookDB",
    };

    public string InstanceId => _config.Current.InstanceId ?? "";

    public CaptureSettings Capture => new()
    {
        MaxLongEdgePx = _config.Current.MaxLongEdgePx,
        JpegQuality = _config.Current.JpegQuality,
    };
}
