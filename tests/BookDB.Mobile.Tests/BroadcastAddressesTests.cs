using System.Net;
using BookDB.Mobile.Services;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>Where a discovery probe is aimed. The subnet maths is what decides whether a probe reaches a
/// computer on the same Wi-Fi, since the all-ones broadcast is routinely dropped there.</summary>
public class BroadcastAddressesTests
{
    [Theory]
    [InlineData("192.168.1.9", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.0.5.17", "255.255.0.0", "10.0.255.255")]
    [InlineData("172.16.4.3", "255.240.0.0", "172.31.255.255")]
    public void TheDirectedBroadcast_SetsTheHostBitsOfTheSubnet(string address, string mask, string expected)
    {
        var broadcast = BroadcastAddresses.DirectedBroadcast(IPAddress.Parse(address), IPAddress.Parse(mask));

        Assert.Equal(IPAddress.Parse(expected), broadcast);
    }

    [Fact]
    public void TheAllOnesBroadcast_IsAlwaysProbedEvenWhenNoInterfaceQualifies()
    {
        Assert.Contains(IPAddress.Broadcast, BroadcastAddresses.Local());
    }
}
