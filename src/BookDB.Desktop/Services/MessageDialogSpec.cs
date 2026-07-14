using System.Collections.Generic;

namespace BookDB.Desktop.Services;

public enum DialogButtonRole { Primary, Secondary, Danger }

public sealed record DialogButton(
    string Text,
    object? Result,
    DialogButtonRole Role = DialogButtonRole.Secondary,
    bool IsDefault = false,
    bool IsCancel = false);

/// <summary>
/// Declarative spec for the one message-dialog shape (title + wrapping body + right-aligned
/// button row). <paramref name="SafeCloseResult"/> is mandatory: it is what closing the window
/// by any path other than a button (title-bar X, programmatic close) resolves to, so no caller
/// ever awaits a dialog that vanished without an answer.
/// </summary>
public sealed record MessageDialogSpec(
    string Title,
    string Body,
    IReadOnlyList<DialogButton> Buttons,
    object? SafeCloseResult,
    double MinWidth = 320);
