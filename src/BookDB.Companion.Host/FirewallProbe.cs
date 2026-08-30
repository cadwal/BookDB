using System;
using System.Diagnostics;
using System.IO;

namespace BookDB.Companion.Host;

/// <summary>What stands between a device on the LAN and this desktop, as far as we can tell without root.</summary>
public enum FirewallKind
{
    /// <summary>Nothing worth warning about: no firewall found, or a platform that does not need the warning.</summary>
    None,

    /// <summary>Windows asks on the first bind. The user has only to answer it.</summary>
    WindowsPrompt,

    /// <summary>
    /// macOS asks too, but only when its firewall is on — and it is off by default, so the prompt may never
    /// come. Said the same way either way, since we cannot tell from here which case this machine is in.
    /// </summary>
    MacPrompt,

    /// <summary>ufw is running. Nothing will ask; the ports stay shut until the user opens them.</summary>
    Ufw,

    /// <summary>firewalld is running. Same silence, different command.</summary>
    Firewalld,
}

/// <summary>Whether the companion's port is already let through, where that can be read without root.</summary>
public enum FirewallPortState
{
    /// <summary>Not readable from here. The user gets the commands, phrased as something to try, not a verdict.</summary>
    Unknown,

    /// <summary>Both protocols are allowed. Saying "no device can connect" here would simply be wrong.</summary>
    Open,

    /// <summary>The rules are readable and the port is not in them.</summary>
    Closed,
}

/// <summary>
/// Which firewall, if any, is in front of the companion, and whether it is already letting the ports
/// through. Advisory only — a wrong answer costs a hint that does not apply, never a refusal to serve.
/// </summary>
public interface IFirewallProbe
{
    FirewallKind Detect();

    /// <summary>
    /// One port, but both protocols have to be allowed to count as open: TCP alone leaves pairing and
    /// browsing working while rediscovery is silently dead, which is the trap 6g exists to catch.
    /// </summary>
    FirewallPortState InspectPort(int port);
}

/// <summary>
/// Asks systemd which firewall unit is running. This must never be done by dialling our own LAN address
/// instead: traffic to a local address is routed over loopback, which every firewall accepts unconditionally,
/// so the probe would report an open port to a user whose phone cannot reach it.
/// </summary>
public sealed class SystemFirewallProbe : IFirewallProbe
{
    private FirewallKind? _cached;

    /// <summary>
    /// The rules as they read when this probe first needed them — ufw's file, or firewalld's port list. Read
    /// once and then asked about as often as the caller likes: the settings dialog re-reads the hint on every
    /// keystroke in the port box, and going back to the firewall for each one would mean a `firewall-cmd`
    /// process per digit typed, on the UI thread, each capped at two seconds.
    /// </summary>
    private readonly Func<FirewallKind, string?> _readRules;
    private string? _rules;
    private bool _rulesRead;

    public SystemFirewallProbe() => _readRules = ReadRules;

    /// <summary>
    /// Both halves stubbed, so the rule-reading behaviour can be driven on a machine that is not running the
    /// firewall in question — which, for a Linux-only path, is every machine the tests usually run on.
    /// </summary>
    internal SystemFirewallProbe(FirewallKind firewall, Func<FirewallKind, string?> readRules)
    {
        _cached = firewall;
        _readRules = readRules;
    }

    public FirewallKind Detect() => _cached ??= Probe();

    /// <summary>Where ufw keeps the rules the user has added. World-readable on some distributions and not
    /// on others, which is exactly why an unreadable file has to mean "unknown" rather than "closed".</summary>
    private const string UfwRulesPath = "/etc/ufw/user.rules";

    public FirewallPortState InspectPort(int port)
    {
        var firewall = Detect();
        if (firewall is not (FirewallKind.Ufw or FirewallKind.Firewalld))
        {
            return FirewallPortState.Unknown;
        }

        if (!_rulesRead)
        {
            _rules = _readRules(firewall);
            _rulesRead = true;
        }

        if (_rules is null)
        {
            return FirewallPortState.Unknown;
        }

        return firewall == FirewallKind.Ufw
            ? UfwPortState(_rules, port)
            : FirewalldPortState(_rules, port);
    }

    /// <summary>Null for anything we cannot read, which has to stay distinct from rules that are readable and
    /// do not mention our ports.</summary>
    private static string? ReadRules(FirewallKind firewall) => firewall switch
    {
        FirewallKind.Ufw => ReadUfwRules(),
        FirewallKind.Firewalld => RunForOutput("firewall-cmd", ["--list-ports"]),
        _ => null,
    };

    private static string? ReadUfwRules()
    {
        try
        {
            return File.ReadAllText(UfwRulesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
            return null;
        }
    }

    internal static FirewallPortState UfwPortState(string rules, int port) =>
        UfwAllows(rules, "tcp", port) && UfwAllows(rules, "udp", port)
            ? FirewallPortState.Open
            : FirewallPortState.Closed;

    /// <summary>
    /// An accept rule on the user chain for this protocol and port. Only the shapes ufw itself writes are
    /// understood — a single --dport, or a multiport list or range — so anything hand-edited beyond that
    /// reads as not allowed, which costs advice the user can ignore rather than a silent failure.
    /// </summary>
    internal static bool UfwAllows(string rules, string protocol, int port)
    {
        foreach (string line in rules.Split('\n'))
        {
            if (!line.Contains("-A ufw-user-input", StringComparison.Ordinal)
                || !line.Contains("-j ACCEPT", StringComparison.Ordinal)
                || !line.Contains($"-p {protocol}", StringComparison.Ordinal))
            {
                continue;
            }

            if (MentionsPort(line, "--dport ", port) || MentionsPort(line, "--dports ", port))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MentionsPort(string line, string flag, int port)
    {
        int at = line.IndexOf(flag, StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        string spec = line[(at + flag.Length)..].Split(' ')[0];
        foreach (string part in spec.Split(','))
        {
            string[] bounds = part.Split(':');
            if (bounds.Length == 1 && int.TryParse(bounds[0], out int single) && single == port)
            {
                return true;
            }

            if (bounds.Length == 2
                && int.TryParse(bounds[0], out int low) && int.TryParse(bounds[1], out int high)
                && port >= low && port <= high)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// firewalld answers --list-ports to an unprivileged caller. A port opened through a custom service
    /// definition rather than directly would not appear and would read as closed; that costs advice, and
    /// BookDB's ports are not part of any shipped service.
    /// </summary>
    internal static FirewallPortState FirewalldPortState(string listed, int port) =>
        listed.Contains($"{port}/tcp", StringComparison.Ordinal)
            && listed.Contains($"{port}/udp", StringComparison.Ordinal)
                ? FirewallPortState.Open
                : FirewallPortState.Closed;

    private static FirewallKind Probe()
    {
        if (OperatingSystem.IsWindows())
        {
            return FirewallKind.WindowsPrompt;
        }

        if (OperatingSystem.IsMacOS())
        {
            return FirewallKind.MacPrompt;
        }

        if (!OperatingSystem.IsLinux())
        {
            return FirewallKind.None;
        }

        if (IsUnitActive("ufw"))
        {
            return FirewallKind.Ufw;
        }

        return IsUnitActive("firewalld") ? FirewallKind.Firewalld : FirewallKind.None;
    }

    /// <summary>
    /// False for every failure alike — no systemd, no systemctl on PATH, a unit that does not exist. Each one
    /// means we cannot show a firewall the user has, which leaves them exactly where they are today.
    /// </summary>
    private static bool IsUnitActive(string unit)
    {
        // "active" alone: "inactive" and "failed" both contain it as a substring.
        string? state = RunForOutput("systemctl", ["is-active", unit]);
        return state?.Trim().Equals("active", StringComparison.Ordinal) ?? false;
    }

    /// <summary>
    /// Standard output, or null when the command could not be run at all. A non-zero exit is not a failure
    /// here: `systemctl is-active` exits 3 for an inactive unit and still says so on stdout.
    /// </summary>
    private static string? RunForOutput(string command, string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(command, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return null;
            }

            // Both pipes are drained, not just the one we want: a command that fills the error pipe while
            // nobody is reading it blocks on the write and never exits. What it wrote there is of no use.
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            // Bounded because this runs while the settings dialog is being built.
            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException
                                              or System.ComponentModel.Win32Exception)
                {
                    // It exited on its own between the wait and here, or the OS will not let us; either way
                    // there is no answer to give.
                }

                return null;
            }

            return output.Result;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                       or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
