using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Mobile.Services;

public interface IPairingCoordinator
{
    Task<PairingOutcome> PairAsync(string scannedCode, string deviceName, CancellationToken ct = default);
}
