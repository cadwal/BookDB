namespace BookDB.Mobile.ViewModels;

/// <summary>Where a journey started from the hub leads. Every destination has a screen behind it, so the
/// shell's job is only to decide which one and when.</summary>
public enum HubDestination
{
    Scan,
    Browse,
    Settings,
    Batch,
}
