namespace BookDB.Companion.Host;

public interface IPairingService
{
    /// <summary>Mints a device certificate and the offer that carries it; the QR is built from the result.</summary>
    PendingPairing BeginPairing();

    /// <summary>
    /// Whether a certificate may open a connection at all: either a registered device, or a live offer on
    /// its way to being claimed. Everything else is refused before it reaches a service method.
    /// </summary>
    bool IsAcceptableForConnection(string thumbprint);

    PairingClaimOutcome Claim(string thumbprint, string deviceName);

    /// <summary>
    /// Forgets an offer. A code is a secret on a screen, so putting the screen away has to take the code with
    /// it: without this, a code that was photographed stays claimable for the rest of its two minutes with
    /// nothing on the computer showing that it is still live. A device that already claimed the offer is
    /// unaffected — it is registered, and registration is what it connects on from then on.
    /// </summary>
    void Withdraw(string thumbprint);
}
