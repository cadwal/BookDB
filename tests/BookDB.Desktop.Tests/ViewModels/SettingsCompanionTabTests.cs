using BookDB.Companion.Host;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The Companion tab's status line. Assertions compare against the resource strings, not literals, so they
/// hold whatever culture the machine runs (the test machine is Swedish).
/// </summary>
public sealed class SettingsCompanionTabTests
{
    [Fact]
    public void AStoppedHostShowsTheOffText()
    {
        Assert.Equal(
            Resources.Settings_Companion_StatusOff,
            SettingsCompanionTabViewModel.DescribeStatus(CompanionHostStatus.Stopped));
    }

    [Fact]
    public void ARunningHostShowsItsPort()
    {
        var text = SettingsCompanionTabViewModel.DescribeStatus(
            new CompanionHostStatus(CompanionHostState.Running, Endpoint: "7443"));

        Assert.Equal(string.Format(Resources.Settings_Companion_StatusRunning, "7443"), text);
        Assert.Contains("7443", text);
    }

    [Fact]
    public void AFailedHostShowsTheReason()
    {
        var text = SettingsCompanionTabViewModel.DescribeStatus(
            new CompanionHostStatus(CompanionHostState.Failed, Error: "port in use"));

        Assert.Equal(string.Format(Resources.Settings_Companion_StatusFailed, "port in use"), text);
        Assert.Contains("port in use", text);
    }

    [Fact]
    public void AMachineWithNoFirewallIsToldNothing()
    {
        Assert.Null(SettingsCompanionTabViewModel.DescribeFirewallHint(
            FirewallKind.None, FirewallPortState.Unknown, 7443, "192.168.1.0/24"));
    }

    [Fact]
    public void WindowsIsToldAboutThePromptItIsAboutToShow()
    {
        Assert.Equal(
            Resources.Settings_Companion_FirewallHint,
            SettingsCompanionTabViewModel.DescribeFirewallHint(
                FirewallKind.WindowsPrompt, FirewallPortState.Unknown, 7443, "192.168.1.0/24"));
    }

    [Fact]
    public void MacIsToldAboutAPromptThatMayNeverCome()
    {
        Assert.Equal(
            Resources.Settings_Companion_FirewallHintMac,
            SettingsCompanionTabViewModel.DescribeFirewallHint(
                FirewallKind.MacPrompt, FirewallPortState.Unknown, 7443, "192.168.1.0/24"));
    }

    /// <summary>
    /// UDP is the half a user forgets, and forgetting it fails silently much later — so both Linux hints
    /// have to ask for it as well as TCP, on the one port.
    /// </summary>
    [Theory]
    [InlineData(FirewallKind.Ufw)]
    [InlineData(FirewallKind.Firewalld)]
    public void ALinuxFirewallIsToldBothProtocols(FirewallKind firewall)
    {
        string? text = SettingsCompanionTabViewModel.DescribeFirewallHint(
            firewall, FirewallPortState.Closed, 7443, "192.168.1.0/24");

        Assert.NotNull(text);
        Assert.Contains("7443", text);
        Assert.Contains("tcp", text);
        Assert.Contains("udp", text);
    }

    /// <summary>The hint names one port and only that port: nothing derived from it, in either command.</summary>
    [Fact]
    public void ChangingThePortRewritesEveryPortInTheCommand()
    {
        string? text = SettingsCompanionTabViewModel.DescribeFirewallHint(
            FirewallKind.Ufw, FirewallPortState.Closed, 9100, "192.168.1.0/24");

        Assert.NotNull(text);
        Assert.Contains("9100", text);
        Assert.DoesNotContain("9101", text);
        Assert.DoesNotContain("7443", text);
    }

    /// <summary>
    /// The rule the hint hands over is scoped to the network the phone is on, not opened to everyone — the
    /// same command the Help topic gives, and the reason the wording can say "for your local network".
    /// </summary>
    [Theory]
    [InlineData(FirewallPortState.Closed)]
    [InlineData(FirewallPortState.Unknown)]
    public void TheUfwCommandIsScopedToTheNetworkTheDeviceIsOn(FirewallPortState ports)
    {
        string? text = SettingsCompanionTabViewModel.DescribeFirewallHint(
            FirewallKind.Ufw, ports, 7443, "10.4.0.0/16");

        Assert.NotNull(text);
        Assert.Contains("from 10.4.0.0/16 to any port 7443 proto tcp", text);
        Assert.Contains("from 10.4.0.0/16 to any port 7443 proto udp", text);
        Assert.DoesNotContain("allow 7443/tcp", text);
    }

    /// <summary>A machine we cannot read a subnet from still gets a command it can copy and edit, rather
    /// than one with a hole in the middle of it.</summary>
    [Fact]
    public void AnUnknownNetworkStillLeavesACopyableCommand()
    {
        string? text = SettingsCompanionTabViewModel.DescribeFirewallHint(
            FirewallKind.Ufw, FirewallPortState.Closed, 7443, subnet: null);

        Assert.NotNull(text);
        Assert.Contains("from 192.168.1.0/24 to any port 7443 proto tcp", text);
    }

    /// <summary>
    /// The complaint this exists to answer: rules already in place, and the hint still insisting nothing can
    /// connect until you write them. Whatever the open wording says, it must not be the closed wording.
    /// </summary>
    [Theory]
    [InlineData(FirewallKind.Ufw)]
    [InlineData(FirewallKind.Firewalld)]
    public void PortsAlreadyOpenAreNotToldToOpenThem(FirewallKind firewall)
    {
        string? text = SettingsCompanionTabViewModel.DescribeFirewallHint(
            firewall, FirewallPortState.Open, 7443, "192.168.1.0/24");

        Assert.NotNull(text);
        Assert.DoesNotContain("sudo", text);
        Assert.Contains("7443", text);
        // Named in every locale's wording, so this holds on the Swedish machine too.
        Assert.Contains("TCP", text);
        Assert.Contains("UDP", text);
        Assert.NotEqual(
            SettingsCompanionTabViewModel.DescribeFirewallHint(firewall, FirewallPortState.Closed, 7443, "192.168.1.0/24"),
            text);
    }

    /// <summary>
    /// Rules we cannot read still get the commands — they are the useful part — but as something to try
    /// rather than a verdict, so the wording has to differ from the one we are sure about.
    /// </summary>
    [Theory]
    [InlineData(FirewallKind.Ufw)]
    [InlineData(FirewallKind.Firewalld)]
    public void RulesWeCannotReadAreNotStatedAsFact(FirewallKind firewall)
    {
        string? unknown = SettingsCompanionTabViewModel.DescribeFirewallHint(
            firewall, FirewallPortState.Unknown, 7443, "192.168.1.0/24");

        Assert.NotNull(unknown);
        Assert.Contains("sudo", unknown);
        Assert.NotEqual(
            SettingsCompanionTabViewModel.DescribeFirewallHint(firewall, FirewallPortState.Closed, 7443, "192.168.1.0/24"),
            unknown);
    }
}
