using System;
using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Xaml.Interactivity;

namespace BookDB.Desktop.Behaviors;

public class CoverHoverPopupBehavior : Behavior<Control>
{
    public static readonly StyledProperty<string> ImagePropertyNameProperty =
        AvaloniaProperty.Register<CoverHoverPopupBehavior, string>(
            nameof(ImagePropertyName), "CoverBitmap");

    public string ImagePropertyName
    {
        get => GetValue(ImagePropertyNameProperty);
        set => SetValue(ImagePropertyNameProperty, value);
    }

    // Pointer placement opens the preview under the cursor. That is fine on a large target, but on a
    // small one the preview covers the cursor and — on X11/WSLg, where the popup is a separate window —
    // steals pointer-over from the target, flickering the tooltip open/closed. Side placements (Right,
    // Bottom, …) keep the preview off the cursor. Default stays Pointer to preserve existing callers.
    public static readonly StyledProperty<PlacementMode> PlacementProperty =
        AvaloniaProperty.Register<CoverHoverPopupBehavior, PlacementMode>(
            nameof(Placement), PlacementMode.Pointer);

    public PlacementMode Placement
    {
        get => GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    private INotifyPropertyChanged? _subscribedNotifier;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null) return;
        AssociatedObject.DataContextChanged += OnDataContextChanged;
        SubscribeToPropertyChanged();
        UpdateToolTip();
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.DataContextChanged -= OnDataContextChanged;
        UnsubscribeFromPropertyChanged();
        base.OnDetaching();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeFromPropertyChanged();
        SubscribeToPropertyChanged();
        UpdateToolTip();
    }

    private void SubscribeToPropertyChanged()
    {
        // For dot-notation paths, subscribe to the leaf object's PropertyChanged so we
        // react to property changes on a child ViewModel (e.g. ImageEditor.CoverBitmap).
        var notifier = ResolveLeafObject() as INotifyPropertyChanged
                       ?? AssociatedObject?.DataContext as INotifyPropertyChanged;
        if (notifier is not null)
        {
            notifier.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedNotifier = notifier;
        }
    }

    private void UnsubscribeFromPropertyChanged()
    {
        if (_subscribedNotifier is not null)
        {
            _subscribedNotifier.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedNotifier = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var leafName = LeafPropertyName();
        if (e.PropertyName == leafName || e.PropertyName == leafName + "SizeBytes")
            UpdateToolTip();
    }

    private void UpdateToolTip()
    {
        if (AssociatedObject is null) return;

        var bitmap = GetBitmap();
        if (bitmap is null)
        {
            ToolTip.SetIsOpen(AssociatedObject, false);
            ToolTip.SetTip(AssociatedObject, null);
            return;
        }

        var sizeBytes = GetSizeBytes();

        string dimensionText;
        if (sizeBytes.HasValue)
            dimensionText = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height} px · {sizeBytes.Value / 1024} KB";
        else
            dimensionText = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height} px · size unknown";

        var image = new Image
        {
            Source = bitmap,
            MaxWidth = 400,
            MaxHeight = 500,
            Stretch = Avalonia.Media.Stretch.Uniform,
        };

        var metadataText = new TextBlock
        {
            Text = dimensionText,
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.Gray,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var stack = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Children = { image, metadataText },
        };

        var border = new Border
        {
            Child = stack,
            Background = Avalonia.Media.Brushes.White,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
        };

        ToolTip.SetTip(AssociatedObject, border);
        ToolTip.SetShowDelay(AssociatedObject, 300);
        ToolTip.SetPlacement(AssociatedObject, Placement);
    }

    // Returns the last path segment (e.g. "CoverBitmap" from "ImageEditor.CoverBitmap").
    private string LeafPropertyName()
    {
        var parts = ImagePropertyName.Split('.');
        return parts[^1];
    }

    // Navigates all but the last segment, returning the object that owns the leaf property.
    private object? ResolveLeafObject()
    {
        var dc = AssociatedObject?.DataContext;
        if (dc is null) return null;

        var parts = ImagePropertyName.Split('.');
        if (parts.Length == 1) return dc;

        object? current = dc;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var prop = current?.GetType().GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance);
            current = prop?.GetValue(current);
            if (current is null) return null;
        }
        return current;
    }

    private Bitmap? GetBitmap()
    {
        var leafObj = ResolveLeafObject();
        if (leafObj is null) return null;

        var prop = leafObj.GetType().GetProperty(LeafPropertyName(), BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(leafObj) as Bitmap;
    }

    private long? GetSizeBytes()
    {
        var leafObj = ResolveLeafObject();
        if (leafObj is null) return null;

        var prop = leafObj.GetType().GetProperty(LeafPropertyName() + "SizeBytes", BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(leafObj) as long?;
    }
}
