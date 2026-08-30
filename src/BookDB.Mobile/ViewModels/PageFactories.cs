using System;

namespace BookDB.Mobile.ViewModels;

/// <summary>How the shell builds each page it can land on. The pages need collaborators the shell has no
/// business knowing about, so the composition root supplies them and the shell only decides when.</summary>
public sealed class PageFactories
{
    public required Func<INavigator, PageViewModel> Pairing { get; init; }

    public required Func<INavigator, PageViewModel> Hub { get; init; }

    public required Func<INavigator, PageViewModel> Settings { get; init; }

    public required Func<INavigator, PageViewModel> Browse { get; init; }

    public required Func<INavigator, PageViewModel> Scan { get; init; }

    public required Func<INavigator, PageViewModel> Batch { get; init; }
}
