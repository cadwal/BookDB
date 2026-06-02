using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BookDB.Desktop.Tests;

/// <summary>
/// Static-analysis tests that scan source files for logging severity contract violations:
///  - No Log.Warning(ex,...) or _logger.LogWarning(ex,...) calls in src/.
///  - BookListViewModel thumbnail/tooltip hot-path catches use Log.Debug.
///  - Resources.resx and Resources.sv.resx each contain 6 Settings_Advanced_Logging_* keys.
/// </summary>
public sealed class LogSeverityContractTests
{
    // Resolve repo root by walking up from the test assembly directory until BookDB.slnx is found.
    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BookDB.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repo root (BookDB.slnx not found walking up from " + AppContext.BaseDirectory + ")");
    }

    private static string SrcPath() => Path.Combine(GetRepoRoot(), "src");

    private static IEnumerable<string> EnumerateCsFiles(string root)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories);

    [Fact]
    public void NoLogWarningWithExceptionArgument_InSrcDirectory()
    {
        // No Log.Warning(ex, ...) calls anywhere in src/
        var violations = new List<string>();
        foreach (var file in EnumerateCsFiles(SrcPath()))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("Log.Warning(ex,") || line.Contains("Log.Warning(ex ,"))
                    violations.Add($"{file}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Log.Warning(ex,...) found in src/:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void NoLoggerLogWarningWithExceptionArgument_InSrcDirectory()
    {
        // No _logger.LogWarning(ex, ...) calls anywhere in src/ (Logic layer)
        var violations = new List<string>();
        foreach (var file in EnumerateCsFiles(SrcPath()))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("LogWarning(ex,") || line.Contains("LogWarning(ex ,"))
                    violations.Add($"{file}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "_logger.LogWarning(ex,...) found in src/:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BookListViewModel_ThumbnailAndTooltipCatches_UseLogDebug()
    {
        // Per-thumbnail and per-tooltip catches in BookListViewModel use Log.Debug
        var bookListVmPath = Path.Combine(
            SrcPath(), "BookDB.Desktop", "ViewModels", "BookListViewModel.cs");
        Assert.True(File.Exists(bookListVmPath),
            $"BookListViewModel.cs not found at: {bookListVmPath}");

        var content = File.ReadAllText(bookListVmPath);

        Assert.Contains(
            "Log.Debug(ex, \"Failed to load thumbnail",
            content);

        Assert.Contains(
            "Log.Debug(ex, \"Failed to load tooltip image",
            content);
    }

    [Fact]
    public void ResourcesResx_ContainsSixSettingsAdvancedLoggingKeys()
    {
        // Resources.resx contains 6 Settings_Advanced_Logging_* keys
        var resxPath = Path.Combine(
            SrcPath(), "BookDB.Desktop", "Localization", "Resources.resx");
        Assert.True(File.Exists(resxPath),
            $"Resources.resx not found at: {resxPath}");

        var content = File.ReadAllText(resxPath);
        var count = CountOccurrences(content, "Settings_Advanced_Logging");

        Assert.True(count >= 6,
            $"Resources.resx contains {count} 'Settings_Advanced_Logging' occurrence(s), expected >= 6.");
    }

    [Fact]
    public void ResourcesSvResx_ContainsSixSettingsAdvancedLoggingKeys()
    {
        // Resources.sv.resx contains 6 Settings_Advanced_Logging_* keys
        var resxPath = Path.Combine(
            SrcPath(), "BookDB.Desktop", "Localization", "Resources.sv.resx");
        Assert.True(File.Exists(resxPath),
            $"Resources.sv.resx not found at: {resxPath}");

        var content = File.ReadAllText(resxPath);
        var count = CountOccurrences(content, "Settings_Advanced_Logging");

        Assert.True(count >= 6,
            $"Resources.sv.resx contains {count} 'Settings_Advanced_Logging' occurrence(s), expected >= 6.");
    }

    private static int CountOccurrences(string source, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
