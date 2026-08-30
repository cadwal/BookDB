namespace BookDB.Mobile.ViewModels;

/// <summary>How a page asks the shell to move elsewhere. The shell keeps the back stack, and it is the shell
/// that builds the pages a hub destination leads to.</summary>
public interface INavigator
{
    void NavigateTo(PageViewModel page);

    void NavigateTo(HubDestination destination);

    /// <summary>Swap the current page for another without deepening the back stack — how the scan wizard
    /// loops from one book to the next without piling up a screen per book.</summary>
    void Replace(PageViewModel page);

    /// <summary>Land on the hub with nothing behind it — after pairing.</summary>
    void ShowHub();

    /// <summary>Land on the pairing page with nothing behind it — after unpairing.</summary>
    void ShowPairing();
}
