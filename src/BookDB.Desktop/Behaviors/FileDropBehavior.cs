using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace BookDB.Desktop.Behaviors;

public class FileDropBehavior : Behavior<Control>
{
    private static readonly string[] AllowedImageExtensions =
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

    public static readonly StyledProperty<ICommand?> DropCommandProperty =
        AvaloniaProperty.Register<FileDropBehavior, ICommand?>(nameof(DropCommand));

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
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles()?.ToList();
        if (files is null) return;
        var first = files.FirstOrDefault(f =>
            AllowedImageExtensions.Contains(
                Path.GetExtension(f.Name).ToLowerInvariant()));
        if (first is not null)
            DropCommand?.Execute(first.Path.LocalPath);
    }
}
