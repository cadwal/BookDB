using System;
using System.IO;
using BookDB.Desktop.Helpers;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class ReaderwareInstallLocatorTests
{
    private const string WindowsFallback = @"C:\Program Files\Readerware 4";

    [Fact]
    public void ResolveWindows_ReturnsProbedPath_WhenProbeFindsAnInstall()
    {
        var found = ResolveWindows(() => @"D:\Apps\Readerware 4");

        Assert.Equal(@"D:\Apps\Readerware 4", found);
    }

    [Fact]
    public void ResolveWindows_FallsBackToDefault_WhenProbeFindsNothing()
    {
        Assert.Equal(WindowsFallback, ResolveWindows(() => null));
    }

    // The real regression: a framework-dependent build has no Microsoft.Win32.Registry assembly, so the probe
    // throws FileNotFoundException on first use. The guard must swallow it and return the fallback rather than
    // letting the exception be cached by the DefaultToolPath Lazy and break the Settings window for the session.
    [Fact]
    public void ResolveWindows_FallsBackToDefault_WhenProbeThrowsMissingAssembly()
    {
        var fallback = ResolveWindows(
            () => throw new FileNotFoundException("Could not load Microsoft.Win32.Registry"));

        Assert.Equal(WindowsFallback, fallback);
    }

    private static string ResolveWindows(Func<string?> probe) =>
        ReaderwareInstallLocator.ResolveWindows(probe);
}
