using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Serilog;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using BookDB.Desktop.Services;
using BookDB.Logic.Import;

namespace BookDB.Desktop.Helpers;

public static class AppDialogs
{
    public static Window BuildShutdownWarningDialog(
        string confirmButtonText,
        string cancelButtonText)
    {
        var dialog = new Window
        {
            Title = Localization.Resources.BatchQueue_ShutdownWarning_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 360
        };

        var confirmBtn = new Button
        {
            Content = confirmButtonText,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        confirmBtn.Click += (_, _) => dialog.Close(true);

        var cancelBtn = new Button
        {
            Content = cancelButtonText,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsCancel = true   // Esc keeps the app running (never the destructive close)
        };
        cancelBtn.Click += (_, _) => dialog.Close(false);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(confirmBtn);
        buttonRow.Children.Add(cancelBtn);

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = Localization.Resources.BatchQueue_ShutdownWarning_Body,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(buttonRow);

        dialog.Content = root;
        return dialog;
    }

    public static Window BuildUnsavedChangesDialog(string bookTitle)
    {
        var dialog = new Window
        {
            Title = Localization.Resources.UnsavedChanges_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 380
        };

        var bodyText = string.Format(Localization.Resources.UnsavedChanges_Body, bookTitle);

        var saveBtn = new Button
        {
            Content = Localization.Resources.Common_Save,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsDefault = true
        };
        saveBtn.Classes.Add("accent");
        saveBtn.Click += (_, _) => dialog.Close(UnsavedChangesResult.Save);

        var discardBtn = new Button
        {
            Content = Localization.Resources.Common_Discard,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        discardBtn.Click += (_, _) => dialog.Close(UnsavedChangesResult.Discard);

        var keepBtn = new Button
        {
            Content = Localization.Resources.UnsavedChanges_Cancel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsCancel = true
        };
        keepBtn.Click += (_, _) => dialog.Close(UnsavedChangesResult.KeepEditing);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(saveBtn);
        buttonRow.Children.Add(discardBtn);
        buttonRow.Children.Add(keepBtn);

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = bodyText,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(buttonRow);

        dialog.Content = root;
        return dialog;
    }

    public enum BackupConflictChoice { Overwrite, AddSuffix, Cancel }

    private static Window? MainWindowOwner() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt ? lt.MainWindow : null;

    public static Window BuildInfoDialog(string message)
    {
        var dialog = new Window
        {
            Title = Localization.Resources.AppDialog_Info_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 320
        };
        var okBtn = new Button
        {
            Content = Localization.Resources.Common_OK,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            IsDefault = true,
            IsCancel = true
        };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) => dialog.Close();
        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(okBtn);
        dialog.Content = root;
        return dialog;
    }

    public static void ShowInfoDialog(string message)
    {
        var dialog = BuildInfoDialog(message);
        var owner = MainWindowOwner();
        if (owner != null)
            _ = dialog.ShowDialog(owner);
        else
            Log.Warning("ShowInfoDialog called with no main window owner — dialog may not appear");
    }

    public static (Window dialog, Task<bool?> result) BuildConfirmDialog(string title, string body)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 360
        };
        // Captures the answer for the ownerless (startup) path, where the dialog is shown non-modally.
        var choice = new TaskCompletionSource<bool?>();
        var yesBtn = new Button { Content = Localization.Resources.AppDialog_Yes_Button, IsDefault = true };
        yesBtn.Classes.Add("accent");
        yesBtn.Click += (_, _) => { choice.TrySetResult(true); dialog.Close(true); };
        var noBtn = new Button { Content = Localization.Resources.AppDialog_No_Button, IsCancel = true };
        noBtn.Click += (_, _) => { choice.TrySetResult(false); dialog.Close(false); };
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        btnRow.Children.Add(yesBtn);
        btnRow.Children.Add(noBtn);
        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(btnRow);
        dialog.Content = root;
        dialog.Closed += (_, _) => choice.TrySetResult(null); // closed via window chrome — treat as no choice
        return (dialog, choice.Task);
    }

    public static async Task<bool?> ShowConfirmDialogAsync(string title, string body)
    {
        var (dialog, result) = BuildConfirmDialog(title, body);
        var owner = MainWindowOwner();
        if (owner == null)
        {
            // No main window yet (startup outage recovery): show non-modally but still await the user's choice,
            // so a restart confirmation actually waits for an answer instead of returning immediately.
            dialog.Show();
            return await result;
        }
        return await dialog.ShowDialog<bool?>(owner);
    }

    /// <summary>
    /// Mid-session write-failure modal: the connection dropped while saving. Retry re-attempts the write; Discard
    /// abandons the unsaved changes. Closing the window defaults to Retry, so closing never silently drops work.
    /// </summary>
    public static (Window dialog, Task<WriteFailureChoice> result) BuildWriteFailureDialog(string message)
    {
        var tcs = new TaskCompletionSource<WriteFailureChoice>();
        var dialog = new Window
        {
            Title = Localization.Resources.WriteFailure_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 380
        };
        var retryBtn = new Button { Content = Localization.Resources.WriteFailure_Retry_Button, IsDefault = true };
        retryBtn.Classes.Add("accent");
        retryBtn.Click += (_, _) => { tcs.TrySetResult(WriteFailureChoice.Retry); dialog.Close(); };
        var discardBtn = new Button { Content = Localization.Resources.WriteFailure_Discard_Button };
        discardBtn.Click += (_, _) => { tcs.TrySetResult(WriteFailureChoice.Discard); dialog.Close(); };
        dialog.Closing += (_, _) => tcs.TrySetResult(WriteFailureChoice.Retry);
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        btnRow.Children.Add(retryBtn);
        btnRow.Children.Add(discardBtn);
        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
        root.Children.Add(btnRow);
        dialog.Content = root;
        return (dialog, tcs.Task);
    }

    public static async Task<WriteFailureChoice> ShowWriteFailureDialogAsync(string message)
    {
        var (dialog, result) = BuildWriteFailureDialog(message);
        var owner = MainWindowOwner();
        if (owner == null)
            dialog.Show();
        else
            _ = dialog.ShowDialog(owner);
        return await result;
    }

    /// <summary>
    /// Escalation modal: the database has been unreachable past the retry window. Quit shuts the app down;
    /// closing defaults to "keep waiting" so an accidental close never loses the session.
    /// </summary>
    public static (Window dialog, Task<bool> result) BuildConnectionLostEscalationDialog()
    {
        var tcs = new TaskCompletionSource<bool>();
        var dialog = new Window
        {
            Title = Localization.Resources.ConnectionLost_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 380
        };
        // Keep-waiting is the safe, recommended action: it is the accent/default and sits on the left like
        // every other dialog's primary button. Quit (destructive) is the plain button on the right.
        var waitBtn = new Button { Content = Localization.Resources.ConnectionLost_KeepWaiting_Button, IsDefault = true };
        waitBtn.Classes.Add("accent");
        waitBtn.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        var quitBtn = new Button { Content = Localization.Resources.ConnectionLost_Quit_Button };
        quitBtn.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        dialog.Closing += (_, _) => tcs.TrySetResult(false);
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        btnRow.Children.Add(waitBtn);
        btnRow.Children.Add(quitBtn);
        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = Localization.Resources.ConnectionLost_Body,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(btnRow);
        dialog.Content = root;
        return (dialog, tcs.Task);
    }

    public static async Task<bool> ShowConnectionLostEscalationDialogAsync()
    {
        var (dialog, result) = BuildConnectionLostEscalationDialog();
        var owner = MainWindowOwner();
        if (owner == null)
            dialog.Show();
        else
            _ = dialog.ShowDialog(owner);
        return await result;
    }

    public enum RestoreTargetChoice { Current, Archived, Cancel }

    /// <summary>
    /// When a CSV backup names the database it came from, asks whether to restore into that database or the
    /// current one. Closing the window defaults to <see cref="RestoreTargetChoice.Cancel"/>.
    /// </summary>
    public static async Task<RestoreTargetChoice> ShowRestoreTargetDialogAsync(string archivedServerDescription)
    {
        var tcs = new TaskCompletionSource<RestoreTargetChoice>();
        var dialog = new Window
        {
            Title = Localization.Resources.RestoreTarget_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 420
        };
        Button Make(string content, RestoreTargetChoice result, bool accent = false, bool isDefault = false, bool isCancel = false)
        {
            var btn = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
            if (accent) btn.Classes.Add("accent");
            btn.Click += (_, _) => { tcs.TrySetResult(result); dialog.Close(); };
            return btn;
        }
        dialog.Closing += (_, _) => tcs.TrySetResult(RestoreTargetChoice.Cancel);

        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(new TextBlock
        {
            Text = string.Format(Localization.Resources.RestoreTarget_Body, archivedServerDescription),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440
        });
        root.Children.Add(Make(Localization.Resources.RestoreTarget_Archived, RestoreTargetChoice.Archived, accent: true, isDefault: true));
        root.Children.Add(Make(Localization.Resources.RestoreTarget_Current, RestoreTargetChoice.Current));
        root.Children.Add(Make(Localization.Resources.RestoreTarget_Cancel, RestoreTargetChoice.Cancel, isCancel: true));
        dialog.Content = root;

        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            owner = lt.MainWindow;
        if (owner != null)
            _ = dialog.ShowDialog(owner);
        else
            dialog.Show();
        return await tcs.Task;
    }

    /// <summary>
    /// Duplicate-ISBN prompt with per-item and "apply to all" choices plus cancel. Closing the window
    /// defaults to <see cref="ImportDuplicateResolution.Skip"/> (a safe non-destructive default).
    /// </summary>
    public static async Task<ImportDuplicateResolution> ShowDuplicateResolutionDialogAsync(string title, string body)
    {
        var tcs = new TaskCompletionSource<ImportDuplicateResolution>();
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 380
        };

        Button Make(string content, ImportDuplicateResolution result, bool accent = false)
        {
            var btn = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (accent) btn.Classes.Add("accent");
            btn.Click += (_, _) => { tcs.TrySetResult(result); dialog.Close(); };
            return btn;
        }
        dialog.Closing += (_, _) => tcs.TrySetResult(ImportDuplicateResolution.Skip);

        // 2x2 grid of per-item / apply-to-all choices, with Cancel on its own row.
        var grid = new Grid
        {
            Margin = new Thickness(0, 16, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        void Place(Control c, int row, int col)
        {
            c.Margin = new Thickness(col == 0 ? 0 : 4, row == 0 ? 0 : 8, col == 0 ? 4 : 0, 0);
            Grid.SetRow(c, row); Grid.SetColumn(c, col);
            grid.Children.Add(c);
        }
        Place(Make(Localization.Resources.Import_Duplicate_Overwrite, ImportDuplicateResolution.Overwrite, accent: true), 0, 0);
        Place(Make(Localization.Resources.Import_Duplicate_OverwriteAll, ImportDuplicateResolution.OverwriteAll), 0, 1);
        Place(Make(Localization.Resources.Import_Duplicate_Skip, ImportDuplicateResolution.Skip), 1, 0);
        Place(Make(Localization.Resources.Import_Duplicate_SkipAll, ImportDuplicateResolution.SkipAll), 1, 1);
        var cancel = Make(Localization.Resources.Import_Duplicate_CancelImport, ImportDuplicateResolution.CancelImport);
        cancel.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(cancel, 2); Grid.SetColumnSpan(cancel, 2);
        grid.Children.Add(cancel);

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
        root.Children.Add(grid);
        dialog.Content = root;

        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            owner = lt.MainWindow;
        if (owner != null)
            _ = dialog.ShowDialog(owner);
        else
            dialog.Show();
        return await tcs.Task;
    }

    public static async Task<BackupConflictChoice> ShowBackupConflictDialogAsync(
        string existingPath, Func<string, string> getCandidatePath)
    {
        var dir = Path.GetDirectoryName(existingPath)!;
        var nameNoExt = Path.GetFileNameWithoutExtension(existingPath);
        var ext = Path.GetExtension(existingPath);
        var suffixName = $"{nameNoExt}-1{ext}";
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{nameNoExt}-{i}{ext}");
            if (!File.Exists(candidate)) { suffixName = $"{nameNoExt}-{i}{ext}"; break; }
        }

        var tcs = new TaskCompletionSource<BackupConflictChoice>();
        var dialog = new Window
        {
            Title = Localization.Resources.AppDialog_BackupConflict_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 360
        };
        var overwriteBtn = new Button { Content = Localization.Resources.AppDialog_BackupConflict_Overwrite, Margin = new Thickness(0, 0, 8, 0) };
        overwriteBtn.Click += (_, _) => { tcs.TrySetResult(BackupConflictChoice.Overwrite); dialog.Close(); };
        var suffixBtn = new Button { Content = string.Format(Localization.Resources.AppDialog_BackupConflict_SaveAs, suffixName), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        suffixBtn.Classes.Add("accent");
        suffixBtn.Click += (_, _) => { tcs.TrySetResult(BackupConflictChoice.AddSuffix); dialog.Close(); };
        var cancelBtn = new Button { Content = Localization.Resources.Common_Cancel, IsCancel = true };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(BackupConflictChoice.Cancel); dialog.Close(); };
        dialog.Closing += (_, _) => tcs.TrySetResult(BackupConflictChoice.Cancel);
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        btnRow.Children.Add(suffixBtn);
        btnRow.Children.Add(overwriteBtn);
        btnRow.Children.Add(cancelBtn);
        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = string.Format(Localization.Resources.AppDialog_BackupConflict_Body, Path.GetFileName(existingPath)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(btnRow);
        dialog.Content = root;
        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            owner = lt.MainWindow;
        if (owner != null)
            _ = dialog.ShowDialog(owner);
        else
            dialog.Show();
        return await tcs.Task;
    }

    public static (Window window, IProgress<string> progress) ShowProgressWindow(string header, Window? owner = null)
    {
        var statusBlock = new TextBlock
        {
            Text = Localization.Resources.AppDialog_Progress_PleaseWait,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            Foreground = Palette.Brush("BrushTextSecondary", Brushes.Gray)
        };
        var root = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Spacing = 8
        };
        root.Children.Add(new TextBlock
        {
            Text = header,
            FontWeight = FontWeight.SemiBold
        });
        root.Children.Add(statusBlock);
        var window = new Window
        {
            Title = Localization.Resources.AppDialog_Progress_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            MinWidth = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            // Explicit background so the window never flashes empty/transparent before its first paint — visible
            // on a near-instant SQLite backup that opens and closes the window in one frame.
            Background = Palette.Brush("BrushBackground", Brushes.White)
        };
        window.Content = root;
        // Default to the main window as owner so the progress window centres on it. Shown modally (ShowDialog,
        // not awaited) so it blocks interaction with the main window while the long operation runs and can never
        // fall behind it; the caller closes the returned window when the work finishes. Falls back to a plain
        // Show() only when there is no owner (e.g. before the main window exists).
        if (owner == null && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            owner = lt.MainWindow;
        if (owner != null)
            _ = window.ShowDialog(owner);
        else
            window.Show();
        var prog = new Progress<string>(msg => statusBlock.Text = msg);
        return (window, prog);
    }

    /// <summary>
    /// Small borderless status window shown while a backup runs during shutdown. Caller closes
    /// the returned window when the work completes and reports the backup's detailed step text
    /// through the returned progress (the backup reports no percentages, so the bar is indeterminate).
    /// Shown standalone (no owner): by the time shutdown runs the main window has already closed,
    /// so parenting to it would throw. Showing is best-effort — it must never block or skip the backup.
    /// </summary>
    public static (Window window, IProgress<string> progress) ShowBackupProgressWindow()
    {
        var statusBlock = new TextBlock
        {
            Text = Localization.Resources.AppDialog_Progress_PleaseWait,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            Foreground = Palette.Brush("BrushTextSecondary", Brushes.Gray)
        };

        var root = new StackPanel
        {
            Margin = new Thickness(28, 22, 28, 22),
            Spacing = 12
        };
        root.Children.Add(new TextBlock
        {
            Text = Localization.Resources.Shutdown_BackupInProgress,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320
        });
        root.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Height = 6
        });
        root.Children.Add(statusBlock);

        var window = new Window
        {
            Title = Localization.Resources.AppDialog_Progress_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            MinWidth = 320,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new Border
            {
                Background = Palette.Brush("BrushBackground", Brushes.White),
                BorderBrush = Palette.Brush("BrushPrimaryBlue", Brushes.RoyalBlue),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = root
            }
        };

        try
        {
            window.Show();
        }
        catch (Exception ex)
        {
            // Best-effort: a failed status window must never abort the backup itself.
            Log.Warning("Could not show backup status window — continuing without it: {Error}", ex.Message);
        }

        var progress = new Progress<string>(msg => statusBlock.Text = msg);
        return (window, progress);
    }

    public static Window BuildAboutDialog()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var versionText = version != null
            ? $"Version {version.Major}.{version.Minor}.{version.Build}"
            : "Version unknown";
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
            ?? Localization.Resources.About_Copyright;

        var dialog = new Window
        {
            Title = Localization.Resources.About_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(32),
            MinWidth = 300
        };

        var okBtn = new Button
        {
            Content = Localization.Resources.Common_OK,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsDefault = true,
            IsCancel = true
        };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) => dialog.Close();

        var root = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        root.Children.Add(new TextBlock
        {
            Text = Localization.Resources.About_AppName,
            FontWeight = FontWeight.Bold,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        root.Children.Add(new TextBlock
        {
            Text = versionText,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        root.Children.Add(new TextBlock
        {
            Text = copyright,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Palette.Brush("BrushTextTertiary", Brushes.Gray)
        });
        root.Children.Add(new Separator { Margin = new Thickness(0, 8) });
        root.Children.Add(okBtn);

        dialog.Content = root;
        return dialog;
    }

    public static async Task ShowAboutDialogAsync()
    {
        var dialog = BuildAboutDialog();
        var owner = MainWindowOwner();
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();
    }

    public static Window BuildDeleteConfirmationDialog(string message)
    {
        var dialog = new Window
        {
            Title = Localization.Resources.Delete_Dialog_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 320
        };

        var deleteBtn = new Button
        {
            Content = Localization.Resources.Delete_Confirm_Button,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Palette.Brush("BrushError", Brushes.Red),
            Foreground = Palette.Brush("BrushBadgeText", Brushes.White)
        };
        deleteBtn.Click += (_, _) => dialog.Close(true);

        var cancelBtn = new Button
        {
            Content = Localization.Resources.Delete_Cancel_Button,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsCancel = true
        };
        cancelBtn.Click += (_, _) => dialog.Close(false);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(deleteBtn);
        buttonRow.Children.Add(cancelBtn);

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(buttonRow);

        dialog.Content = root;
        return dialog;
    }

    public static Window BuildDuplicateIsbnDialog(string isbn, string existingTitle)
    {
        var dialog = new Window
        {
            Title = Localization.Resources.DuplicateIsbn_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 380
        };

        var updateBtn = new Button
        {
            Content = Localization.Resources.DuplicateIsbn_UpdateExisting,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        updateBtn.Click += (_, _) => dialog.Close(DuplicateIsbnResult.UpdateExisting);

        var addBtn = new Button
        {
            Content = Localization.Resources.DuplicateIsbn_AddAsNew,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        addBtn.Click += (_, _) => dialog.Close(DuplicateIsbnResult.AddAsNew);

        var cancelBtn = new Button
        {
            Content = Localization.Resources.Common_Cancel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsCancel = true
        };
        cancelBtn.Click += (_, _) => dialog.Close(DuplicateIsbnResult.Cancel);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(updateBtn);
        buttonRow.Children.Add(addBtn);
        buttonRow.Children.Add(cancelBtn);

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = string.Format(Localization.Resources.DuplicateIsbn_Body, isbn, existingTitle),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(buttonRow);

        dialog.Content = root;
        return dialog;
    }

    public static Window BuildIsbnPromptDialog()
    {
        var dialog = new Window
        {
            Title = Localization.Resources.Recatalog_NoIsbn_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 340
        };

        var input = new TextBox
        {
            Watermark = Localization.Resources.Recatalog_NoIsbn_Watermark,
            Width = 280,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var okBtn = new Button
        {
            Content = Localization.Resources.Recatalog_NoIsbn_LookUp,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) =>
        {
            var isbn = input.Text?.Trim();
            dialog.Close(string.IsNullOrEmpty(isbn) ? null : isbn);
        };

        var cancelBtn = new Button
        {
            Content = Localization.Resources.Common_Cancel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsCancel = true
        };
        cancelBtn.Click += (_, _) => dialog.Close(null);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(okBtn);
        buttonRow.Children.Add(cancelBtn);

        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(new TextBlock
        {
            Text = Localization.Resources.Recatalog_NoIsbn_Body,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340
        });
        root.Children.Add(input);
        root.Children.Add(buttonRow);

        dialog.Content = root;
        return dialog;
    }
}
