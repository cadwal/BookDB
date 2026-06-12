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
        string confirmButtonText = "Close Application",
        string cancelButtonText = "Keep Running")
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
            HorizontalAlignment = HorizontalAlignment.Stretch
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
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
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
            HorizontalAlignment = HorizontalAlignment.Stretch
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

    public static void ShowInfoDialog(string message)
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
            Margin = new Thickness(0, 16, 0, 0)
        };
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
        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            owner = lt.MainWindow;
        if (owner != null)
            _ = dialog.ShowDialog(owner);
        else
            Log.Warning("ShowInfoDialog called with no main window owner — dialog may not appear");
    }

    public static async Task<bool?> ShowConfirmDialogAsync(string title, string body)
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
        var yesBtn = new Button { Content = Localization.Resources.AppDialog_Yes_Button };
        yesBtn.Classes.Add("accent");
        yesBtn.Click += (_, _) => dialog.Close(true);
        var noBtn = new Button { Content = Localization.Resources.AppDialog_No_Button };
        noBtn.Click += (_, _) => dialog.Close(false);
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
        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            owner = lt.MainWindow;
        if (owner == null)
        {
            dialog.Show();
            return null;
        }
        return await dialog.ShowDialog<bool?>(owner);
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
        var suffixBtn = new Button { Content = string.Format(Localization.Resources.AppDialog_BackupConflict_SaveAs, suffixName), Margin = new Thickness(0, 0, 8, 0) };
        suffixBtn.Classes.Add("accent");
        suffixBtn.Click += (_, _) => { tcs.TrySetResult(BackupConflictChoice.AddSuffix); dialog.Close(); };
        var cancelBtn = new Button { Content = Localization.Resources.Common_Cancel };
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
            ShowInTaskbar = false
        };
        window.Content = root;
        if (owner != null)
            window.Show(owner);
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

    public static async Task ShowAboutDialogAsync()
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
            HorizontalAlignment = HorizontalAlignment.Center
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

        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            owner = lt.MainWindow;
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();
    }
}
