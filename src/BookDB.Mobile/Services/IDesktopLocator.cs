using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;

namespace BookDB.Mobile.Services;

/// <summary>Finds the computer this device is paired with on the local network, by the instance id the
/// handshake reported. Used only when the address on file no longer answers.</summary>
public interface IDesktopLocator
{
    /// <summary>Returns the answering computer's address, or null when nothing answered in time.</summary>
    /// <param name="port">The companion port, probed over UDP — see <see cref="DiscoveryProtocol"/>.</param>
    Task<DiscoveryReply?> LocateAsync(string instanceId, int port, CancellationToken ct = default);
}
