using System.Linq;
using System.Net;
using BookDB.Companion.Host;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The addresses offered in the pairing code. This reads the machine's real adapters, so it asserts the
/// shape of the answer rather than particular addresses.
/// </summary>
public sealed class LocalAddressesTests
{
    [Fact]
    public void EveryCandidateIsARoutableIpv4Address()
    {
        var candidates = LocalAddresses.Candidates();

        Assert.All(candidates, address =>
        {
            Assert.True(IPAddress.TryParse(address, out var parsed), address);
            Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, parsed!.AddressFamily);
            Assert.False(IPAddress.IsLoopback(parsed), "loopback cannot be reached from another device");
        });
    }

    [Fact]
    public void TheSameAddressIsNeverOfferedTwice()
    {
        var candidates = LocalAddresses.Candidates();

        Assert.Equal(candidates.Count, candidates.Distinct().Count());
    }

    [Fact]
    public void TheBestAddressIsTheFirstCandidateOrNothingAtAll()
    {
        var candidates = LocalAddresses.Candidates();

        Assert.Equal(candidates.FirstOrDefault(), LocalAddresses.Best());
    }

    /// <summary>The source network of a firewall rule: every host bit cleared, or the rule names one host
    /// and lets nothing else in.</summary>
    [Theory]
    [InlineData("192.168.1.163", "255.255.255.0", "192.168.1.0")]
    [InlineData("10.4.9.17", "255.255.0.0", "10.4.0.0")]
    [InlineData("172.16.34.200", "255.255.255.192", "172.16.34.192")]
    public void TheNetworkAddressClearsTheHostBits(string address, string mask, string expected)
    {
        Assert.Equal(
            IPAddress.Parse(expected),
            LocalAddresses.NetworkAddress(IPAddress.Parse(address), IPAddress.Parse(mask)));
    }

    [Theory]
    [InlineData("255.255.255.0", 24)]
    [InlineData("255.255.0.0", 16)]
    [InlineData("255.255.255.192", 26)]
    [InlineData("255.255.255.255", 32)]
    public void ThePrefixIsHowManyBitsTheMaskSets(string mask, int expected)
    {
        Assert.Equal(expected, LocalAddresses.PrefixLength(IPAddress.Parse(mask)));
    }

    /// <summary>
    /// Reads the machine's own adapters, so it asserts the shape: a subnet that parses, that the best
    /// address actually falls inside, and that is written with its host bits cleared. Null is a legitimate
    /// answer on a machine with no usable address — and on one, the companion has nothing to serve on.
    /// </summary>
    [Fact]
    public void TheLocalSubnetContainsTheAddressThePairingCodeWouldName()
    {
        string? subnet = LocalAddresses.LocalSubnet();
        if (subnet is null)
        {
            Assert.Null(LocalAddresses.Best());
            return;
        }

        string[] parts = subnet.Split('/');
        Assert.Equal(2, parts.Length);

        var network = IPAddress.Parse(parts[0]);
        int prefix = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(prefix, 1, 32);

        var mask = MaskFor(prefix);
        Assert.Equal(network, LocalAddresses.NetworkAddress(network, mask));
        Assert.Equal(
            network, LocalAddresses.NetworkAddress(IPAddress.Parse(LocalAddresses.Best()!), mask));
    }

    private static IPAddress MaskFor(int prefix)
    {
        byte[] bytes = new byte[4];
        for (int bit = 0; bit < prefix; bit++)
        {
            bytes[bit / 8] |= (byte)(1 << (7 - (bit % 8)));
        }

        return new IPAddress(bytes);
    }
}
