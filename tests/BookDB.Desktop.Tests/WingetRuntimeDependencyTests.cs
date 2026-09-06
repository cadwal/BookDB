using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace BookDB.Desktop.Tests;

/// <summary>
/// The desktop app binds more than one shared framework: the companion host runs Kestrel, so
/// <c>Microsoft.AspNetCore.App</c> rides along with the base runtime through a transitive
/// <c>FrameworkReference</c>. The framework-dependent zip is what winget installs, and its manifest must
/// name a package for every framework the app binds. 4.0.0 shipped naming only the base runtime; the
/// omission surfaced as the runtime's own "install this too" prompt on first launch rather than as a build
/// or test failure, because adding a <c>FrameworkReference</c> anywhere in the reference graph is otherwise
/// invisible to this suite.
/// </summary>
public class WingetRuntimeDependencyTests
{
    // A framework can only be declared to winget if we know which package carries it. The major version is
    // appended from the app's own target framework, so a TFM bump has to move the dependency ids with it.
    private static readonly Dictionary<string, string> WingetPackageFor = new(StringComparer.Ordinal)
    {
        ["Microsoft.NETCore.App"] = "Microsoft.DotNet.Runtime",
        ["Microsoft.AspNetCore.App"] = "Microsoft.DotNet.AspNetCore",
        ["Microsoft.WindowsDesktop.App"] = "Microsoft.DotNet.DesktopRuntime",
    };

    [Fact]
    public void DesktopApp_BindsTheBaseRuntimeAndAspNetCore()
    {
        Assert.Equal(
            new[] { "Microsoft.AspNetCore.App", "Microsoft.NETCore.App" },
            BoundFrameworks().OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void EveryBoundFramework_HasAWingetPackageThatCarriesIt()
    {
        foreach (var framework in BoundFrameworks())
        {
            Assert.True(
                WingetPackageFor.ContainsKey(framework),
                $"No winget package is known for the shared framework '{framework}'. Name the package here " +
                "and add it to Dependencies.PackageDependencies in the winget installer manifest.");
        }
    }

    [Fact]
    public void WingetManifest_DeclaresAPackageForEveryFrameworkTheAppBinds()
    {
        var manifest = InstallerManifestForCurrentVersion();

        // Absent in two normal cases: the public repo, which does not export winget/, and the window
        // between the version bump and the reference manifest being committed after the winget PR.
        Assert.SkipUnless(
            manifest is not null,
            "No winget installer manifest for the current version is present in this checkout.");

        var declared = DeclaredPackageIdentifiers(File.ReadAllText(manifest!));
        foreach (var framework in BoundFrameworks())
            Assert.Contains($"{WingetPackageFor[framework]}.{RuntimeMajorVersion()}", declared);
    }

    private static IReadOnlyList<string> BoundFrameworks()
    {
        var options = RuntimeOptions();

        // Two frameworks are listed under "frameworks"; drop to one and the SDK emits a single "framework"
        // object instead. A self-contained publish names the same set "includedFrameworks".
        if (options.TryGetProperty("frameworks", out var many))
            return many.EnumerateArray().Select(NameOf).ToList();
        if (options.TryGetProperty("framework", out var one))
            return new[] { NameOf(one) };
        return options.GetProperty("includedFrameworks").EnumerateArray().Select(NameOf).ToList();
    }

    private static string NameOf(JsonElement framework) => framework.GetProperty("name").GetString()!;

    private static string RuntimeMajorVersion()
    {
        var tfm = RuntimeOptions().GetProperty("tfm").GetString()!;
        return tfm["net".Length..].Split('.')[0];
    }

    private static JsonElement RuntimeOptions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "BookDB.Desktop.runtimeconfig.json");
        Assert.True(File.Exists(path), $"Desktop runtimeconfig not found at: {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("runtimeOptions").Clone();
    }

    private static string? InstallerManifestForCurrentVersion()
    {
        var root = RepositoryRoot();
        if (root is null)
            return null;

        var version = typeof(global::BookDB.Desktop.Services.ReleaseNotesService)
            .Assembly.GetName().Version!;
        var path = Path.Combine(
            root, "winget", "cadwal.BookDB",
            $"{version.Major}.{version.Minor}.{version.Build}", "cadwal.BookDB.installer.yaml");
        return File.Exists(path) ? path : null;
    }

    private static string? RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BookDB.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    // Only dependency entries are list items; the manifest's own PackageIdentifier has no leading dash.
    private static IReadOnlyList<string> DeclaredPackageIdentifiers(string manifest)
        => Regex.Matches(manifest, @"^\s*-\s*PackageIdentifier:\s*(?<id>\S+)\s*$", RegexOptions.Multiline)
            .Select(match => match.Groups["id"].Value)
            .ToList();
}
