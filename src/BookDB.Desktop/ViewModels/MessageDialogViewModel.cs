using System;
using System.Collections.Generic;
using System.Linq;
using BookDB.Desktop.Services;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public sealed class MessageDialogButtonViewModel
{
    public string Text { get; }
    public bool IsDefault { get; }
    public bool IsCancel { get; }
    public bool IsAccent { get; }
    public bool IsDanger { get; }
    public IRelayCommand Command { get; }

    public MessageDialogButtonViewModel(DialogButton button, Action choose)
    {
        Text = button.Text;
        IsDefault = button.IsDefault;
        IsCancel = button.IsCancel;
        IsAccent = button.Role == DialogButtonRole.Primary;
        IsDanger = button.Role == DialogButtonRole.Danger;
        Command = new RelayCommand(choose);
    }
}

public sealed class MessageDialogViewModel
{
    public string Title { get; }
    public string Body { get; }
    public double MinWidth { get; }
    public IReadOnlyList<MessageDialogButtonViewModel> Buttons { get; }

    // Set by the show path before the dialog opens; receives the clicked button's result.
    public Action<object?>? CloseDialog { get; set; }

    public MessageDialogViewModel(MessageDialogSpec spec)
    {
        Title = spec.Title;
        Body = spec.Body;
        MinWidth = spec.MinWidth;
        Buttons = spec.Buttons
            .Select(b => new MessageDialogButtonViewModel(b, () => CloseDialog?.Invoke(b.Result)))
            .ToList();
    }
}
