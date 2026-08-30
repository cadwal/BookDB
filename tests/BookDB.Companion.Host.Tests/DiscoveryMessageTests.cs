using BookDB.Contracts;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The discovery wire format, which both ends parse by hand. Anything that is not exactly a BookDB probe or
/// offer has to be rejected rather than half-understood — the socket hears every broadcast on the LAN.
/// </summary>
public sealed class DiscoveryMessageTests
{
    [Fact]
    public void AProbeRoundTrips()
    {
        Assert.True(DiscoveryProbe.TryParse(new DiscoveryProbe("instance-1").Format(), out var parsed));
        Assert.Equal("instance-1", parsed.InstanceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("BOOKDB/1 PROBE")]
    [InlineData("BOOKDB/1 PROBE ")]
    [InlineData("BOOKDB/1 OFFER instance-1")]
    [InlineData("BOOKDB/2 PROBE instance-1")]
    [InlineData("BOOKDB/1 PROBE instance-1 extra")]
    public void AnythingThatIsNotAProbeIsRejected(string text)
    {
        Assert.False(DiscoveryProbe.TryParse(text, out _));
    }

    [Fact]
    public void AReplyRoundTrips()
    {
        Assert.True(DiscoveryReply.TryParse(new DiscoveryReply("instance-1", "192.168.1.20", 7443).Format(), out var parsed));
        Assert.Equal("instance-1", parsed.InstanceId);
        Assert.Equal("192.168.1.20", parsed.Host);
        Assert.Equal(7443, parsed.Port);
    }

    [Theory]
    [InlineData("BOOKDB/1 OFFER instance-1 192.168.1.20")]
    [InlineData("BOOKDB/1 OFFER instance-1 192.168.1.20 not-a-port")]
    [InlineData("BOOKDB/1 OFFER instance-1 192.168.1.20 0")]
    [InlineData("BOOKDB/1 OFFER instance-1 192.168.1.20 70000")]
    [InlineData("BOOKDB/1 PROBE instance-1 192.168.1.20 7443")]
    public void AnythingThatIsNotAnOfferIsRejected(string text)
    {
        Assert.False(DiscoveryReply.TryParse(text, out _));
    }
}
