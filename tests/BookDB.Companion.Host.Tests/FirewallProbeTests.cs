using System;
using BookDB.Companion.Host;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// Reading ufw's own rules file. Only what ufw writes has to be understood; the point of the exercise is
/// that a rule which is plainly there is not reported as missing, because a hint telling someone to open a
/// port they have already opened is the kind of wrong that stops people reading hints.
/// </summary>
public sealed class FirewallProbeTests
{
    /// <summary>Written by `ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` and its udp twin —
    /// the same port number both times, since discovery listens on the companion port over UDP.</summary>
    private const string ScopedRules = """
        *filter
        :ufw-user-input - [0:0]
        ### RULES ###
        -A ufw-user-input -p tcp --dport 7443 -s 192.168.1.0/24 -j ACCEPT
        -A ufw-user-input -p udp --dport 7443 -s 192.168.1.0/24 -j ACCEPT
        ### END RULES ###
        """;

    /// <summary>The TCP half alone — the state 6g exists to catch, and what the protocol test needs.</summary>
    private const string TcpOnlyRules = "-A ufw-user-input -p tcp --dport 7443 -s 192.168.1.0/24 -j ACCEPT";

    /// <summary>
    /// The one test that runs the real detection rather than a stub: it asks the machine it is on and
    /// checks the answer is one that machine could give. On Linux that means actually spawning
    /// `systemctl is-active`, which is the only automated exercise the process helper gets — everything
    /// else about the probe is driven through stubs so it is deterministic off Linux.
    /// </summary>
    [Fact]
    public void TheDetectedFirewallIsTheOneThisPlatformCouldHave()
    {
        var probe = new SystemFirewallProbe();
        var kind = probe.Detect();

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(FirewallKind.WindowsPrompt, kind);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(FirewallKind.MacPrompt, kind);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.Contains(kind, new[] { FirewallKind.Ufw, FirewallKind.Firewalld, FirewallKind.None });
        }
        else
        {
            Assert.Equal(FirewallKind.None, kind);
        }

        // Whatever it found, the answer is stable: detection is asked for on every hint and must not go
        // back to the OS each time.
        Assert.Equal(kind, probe.Detect());
    }

    /// <summary>
    /// The settings dialog re-reads the hint on every keystroke in the port box, so a probe that went back
    /// to the firewall per port would spawn a `firewall-cmd` per digit typed, on the UI thread, each capped
    /// at two seconds. The rules are read once and then asked about as often as the caller likes.
    /// </summary>
    [Fact]
    public void TheRulesAreReadOnceHoweverManyPortsAreAskedAbout()
    {
        int reads = 0;
        var probe = new SystemFirewallProbe(FirewallKind.Firewalld, _ =>
        {
            reads++;
            return "7443/tcp 7443/udp";
        });

        for (int port = 7443; port < 7450; port++)
        {
            probe.InspectPort(port);
        }

        Assert.Equal(1, reads);
        Assert.Equal(FirewallPortState.Open, probe.InspectPort(7443));
        Assert.Equal(FirewallPortState.Closed, probe.InspectPort(9100));
    }

    /// <summary>Rules that cannot be read are not rules that say no — and asking again per port would be the
    /// same process storm by another route.</summary>
    [Fact]
    public void RulesThatCannotBeReadStayUnknownAndAreNotReReadPerPort()
    {
        int reads = 0;
        var probe = new SystemFirewallProbe(FirewallKind.Ufw, _ => { reads++; return null; });

        Assert.Equal(FirewallPortState.Unknown, probe.InspectPort(7443));
        Assert.Equal(FirewallPortState.Unknown, probe.InspectPort(9100));
        Assert.Equal(1, reads);
    }

    /// <summary>A machine with no firewall of ours in front of it is not asked for rules at all.</summary>
    [Fact]
    public void NoFirewallMeansNothingIsRead()
    {
        int reads = 0;
        var probe = new SystemFirewallProbe(FirewallKind.WindowsPrompt, _ => { reads++; return "anything"; });

        Assert.Equal(FirewallPortState.Unknown, probe.InspectPort(7443));
        Assert.Equal(0, reads);
    }

    /// <summary>Both protocols have to be allowed on the port: TCP alone is the state 6g exists to catch.</summary>
    [Fact]
    public void TheTcpHalfAloneIsNotOpen()
    {
        const string tcpOnly = "-A ufw-user-input -p tcp --dport 7443 -j ACCEPT";

        Assert.Equal(FirewallPortState.Closed, SystemFirewallProbe.UfwPortState(tcpOnly, 7443));
        Assert.Equal(
            FirewallPortState.Open,
            SystemFirewallProbe.UfwPortState(
                tcpOnly + "\n-A ufw-user-input -p udp --dport 7443 -j ACCEPT", 7443));
        Assert.Equal(FirewallPortState.Closed, SystemFirewallProbe.FirewalldPortState("7443/tcp", 7443));
        Assert.Equal(
            FirewallPortState.Open, SystemFirewallProbe.FirewalldPortState("7443/tcp 7443/udp", 7443));
    }

    [Fact]
    public void APortAllowedForTheLocalSubnetCounts()
    {
        Assert.True(SystemFirewallProbe.UfwAllows(ScopedRules, "tcp", 7443));
        Assert.True(SystemFirewallProbe.UfwAllows(ScopedRules, "udp", 7443));
    }

    [Fact]
    public void APortNobodyAllowedDoesNot()
    {
        Assert.False(SystemFirewallProbe.UfwAllows(ScopedRules, "tcp", 9100));
    }

    /// <summary>The protocol has to match: 7443/udp is not what an allow for 7443/tcp granted — which is the
    /// whole reason one port number still means two rules.</summary>
    [Fact]
    public void TheProtocolHasToMatch()
    {
        Assert.True(SystemFirewallProbe.UfwAllows(TcpOnlyRules, "tcp", 7443));
        Assert.False(SystemFirewallProbe.UfwAllows(TcpOnlyRules, "udp", 7443));
    }

    /// <summary>How ufw writes an application profile — KDE Connect ships one, and it is a range.</summary>
    [Fact]
    public void APortInsideAMultiportRangeCounts()
    {
        const string rules =
            "-A ufw-user-input -p udp -m multiport --dports 1714:1764 -j ACCEPT -m comment --comment 'dapp_KDE'";

        Assert.True(SystemFirewallProbe.UfwAllows(rules, "udp", 1714));
        Assert.True(SystemFirewallProbe.UfwAllows(rules, "udp", 1740));
        Assert.True(SystemFirewallProbe.UfwAllows(rules, "udp", 1764));
        Assert.False(SystemFirewallProbe.UfwAllows(rules, "udp", 1765));
    }

    [Fact]
    public void APortInAMultiportListCounts()
    {
        const string rules = "-A ufw-user-input -p tcp -m multiport --dports 80,443,7443 -j ACCEPT";

        Assert.True(SystemFirewallProbe.UfwAllows(rules, "tcp", 7443));
        Assert.False(SystemFirewallProbe.UfwAllows(rules, "tcp", 8443));
    }

    /// <summary>A deny is not an allow, however exactly the port matches.</summary>
    [Fact]
    public void ARuleThatIsNotAnAcceptDoesNotCount()
    {
        Assert.False(SystemFirewallProbe.UfwAllows(
            "-A ufw-user-input -p tcp --dport 7443 -j DROP", "tcp", 7443));
        Assert.False(SystemFirewallProbe.UfwAllows(
            "-A ufw-user-input -p tcp --dport 7443 -j REJECT", "tcp", 7443));
    }

    /// <summary>Only the user chain is ours to read; the before/after chains carry ufw's own plumbing.</summary>
    [Fact]
    public void AnAcceptOnAnotherChainDoesNotCount()
    {
        Assert.False(SystemFirewallProbe.UfwAllows(
            "-A ufw-before-input -p tcp --dport 7443 -j ACCEPT", "tcp", 7443));
    }

    /// <summary>7443 must not be read out of 17443 or 74430.</summary>
    [Fact]
    public void ALongerPortThatMerelyContainsOursDoesNot()
    {
        Assert.False(SystemFirewallProbe.UfwAllows(
            "-A ufw-user-input -p tcp --dport 17443 -j ACCEPT", "tcp", 7443));
        Assert.False(SystemFirewallProbe.UfwAllows(
            "-A ufw-user-input -p tcp --dport 74430 -j ACCEPT", "tcp", 7443));
    }
}
