using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Behavior that handles drag-drop of text or CSV files onto any <see cref="Control"/>.
/// Extension filtering is driven by <see cref="AllowedExtensions"/> (comma-separated, e.g. ".txt,.csv").
/// When a valid file is dropped, <see cref="DropCommand"/> is executed with the file's local path as the parameter.
/// </summary>
public class TextFileDropBehavior : Behavior<Control>
{
    public static readonly StyledProperty<string> AllowedExtensionsProperty =
        AvaloniaProperty.Register<TextFileDropBehavior, string>(nameof(AllowedExtensions), defaultValue: string.Empty);

    public string AllowedExtensions
    {
        get => GetValue(AllowedExtensionsProperty);
        set => SetValue(AllowedExtensionsProperty, value);
    }

    public static readonly StyledProperty<ICommand?> DropCommandProperty =
        AvaloniaProperty.Register<TextFileDropBehavior, ICommand?>(nameof(DropCommand));

    public ICommand? DropCommand
    {
        get => GetValue(DropCommandProperty);
        set => SetValue(DropCommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null) return;
        DragDrop.SetAllowDrop(AssociatedObject, true);
        AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrop);
        }
        base.OnDetaching();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool isValidFile = false;
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.ToList();
            if (files != null && files.Count > 0)
            {
                isValidFile = IsAllowedExtension(files[0].Name);
            }
            else
            {
                // Fallback: platform doesn't expose file name during hover
                isValidFile = true;
            }
        }

        e.DragEffects = isValidFile ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var files = e.DataTransfer.TryGetFiles()?.ToList();
        if (files == null || files.Count == 0) return;

        foreach (var file in files)
        {
            if (IsAllowedExtension(file.Name))
            {
                DropCommand?.Execute(file.Path.LocalPath);
            }
        }
    }

    private bool IsAllowedExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var allowed = AllowedExtensions
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant());
        return allowed.Contains(ext);
    }
}
