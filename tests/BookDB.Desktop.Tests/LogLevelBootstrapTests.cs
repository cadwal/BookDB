using BookDB.Desktop;
using BookDB.Models;
using Serilog.Events;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class LogLevelBootstrapTests
{
    [Fact]
    public void ApplyLogLevelBootstrap_Verbose_ReturnsDebugLevel()
        => Assert.Equal(
            LogEventLevel.Debug,
            AppHost.ApplyLogLevelBootstrap(new BootstrapConfig { LogLevel = "Verbose" }).MinimumLevel);

    [Fact]
    public void ApplyLogLevelBootstrap_Normal_ReturnsWarningLevel()
        => Assert.Equal(
            LogEventLevel.Warning,
            AppHost.ApplyLogLevelBootstrap(new BootstrapConfig { LogLevel = "Normal" }).MinimumLevel);

    [Fact]
    public void ApplyLogLevelBootstrap_NullLevel_ReturnsWarningLevel()
        => Assert.Equal(
            LogEventLevel.Warning,
            AppHost.ApplyLogLevelBootstrap(new BootstrapConfig { LogLevel = null }).MinimumLevel);

    [Fact]
    public void ApplyLogLevelBootstrap_InvalidValue_ReturnsWarningLevel()
        => Assert.Equal(
            LogEventLevel.Warning,
            AppHost.ApplyLogLevelBootstrap(new BootstrapConfig { LogLevel = "DROP TABLE Settings;--" }).MinimumLevel);
}
