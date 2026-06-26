using System.Globalization;
using System.Threading;
using BookDB.Desktop;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class CultureBootstrapTests
{
    [Fact]
    public void ApplyCultureBootstrap_WithStoredLanguage_SetsCultureToStoredValue()
    {
        var originalCulture = Thread.CurrentThread.CurrentUICulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        try
        {
            AppHost.ApplyCultureBootstrap(new BootstrapConfig { Language = "sv" });

            Assert.Equal("sv", Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalCulture;
            Thread.CurrentThread.CurrentCulture   = originalCulture;
            // ApplyCultureBootstrap mutates the process-wide default cultures; restore for test isolation.
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.DefaultThreadCurrentCulture   = originalDefaultCulture;
        }
    }

    [Fact]
    public void ApplyCultureBootstrap_NoStoredLanguage_NoSatelliteForProbedCulture_FallsBackToEnglish()
    {
        // First-run probe — no stored language; probed OS culture ("ja") has no satellite → falls back to "en".
        var originalCulture = Thread.CurrentThread.CurrentUICulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ja");

            AppHost.ApplyCultureBootstrap(new BootstrapConfig { Language = null });

            Assert.Equal("en", Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName);
            Assert.Equal("en", Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalCulture;
            Thread.CurrentThread.CurrentCulture   = originalCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.DefaultThreadCurrentCulture   = originalDefaultCulture;
        }
    }

    [Fact]
    public void ApplyCultureBootstrap_InvalidStoredCultureCode_DoesNotThrow()
    {
        var originalCulture = Thread.CurrentThread.CurrentUICulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        try
        {
            var ex = Record.Exception(() =>
                AppHost.ApplyCultureBootstrap(new BootstrapConfig { Language = "zzz-invalid-9999" }));

            Assert.Null(ex);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalCulture;
            Thread.CurrentThread.CurrentCulture   = originalCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.DefaultThreadCurrentCulture   = originalDefaultCulture;
        }
    }
}
