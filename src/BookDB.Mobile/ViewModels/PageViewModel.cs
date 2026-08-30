namespace BookDB.Mobile.ViewModels;

/// <summary>A screen the shell can make current in its navigation frame.</summary>
public abstract class PageViewModel : ViewModelBase
{
    /// <summary>Shown in the shell's top bar for this page.</summary>
    public abstract string Title { get; }
}
