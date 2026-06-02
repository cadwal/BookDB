using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Focuses the attached control whenever <see cref="Sequence"/> changes to a positive value.
/// Bind to a VM int counter that starts at 0 and increments each time focus is required.
///
/// Value 0 is treated as "no request" so the initial binding push is implicitly ignored —
/// using an _attached flag is unreliable because the no-op 0→0 initial change may not fire
/// OnPropertyChanged, causing the first real increment (0→1) to be swallowed by the skip.
/// </summary>
public class FocusWhenChangedBehavior : Behavior<Control>
{
    public static readonly StyledProperty<int> SequenceProperty =
        AvaloniaProperty.Register<FocusWhenChangedBehavior, int>(nameof(Sequence));

    public int Sequence
    {
        get => GetValue(SequenceProperty);
        set => SetValue(SequenceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SequenceProperty) return;
        if (Sequence <= 0) return; // 0 = no request (initial binding or reset)
        if (AssociatedObject is null) return;

        void OnLayoutUpdated(object? s, EventArgs e)
        {
            if (AssociatedObject is null) return;
            if (AssociatedObject.Bounds.Width == 0 && AssociatedObject.Bounds.Height == 0)
                return;
            AssociatedObject.LayoutUpdated -= OnLayoutUpdated;
            Dispatcher.UIThread.Post(() => AssociatedObject?.Focus(), DispatcherPriority.Input);
        }

        AssociatedObject.LayoutUpdated += OnLayoutUpdated;
    }
}
