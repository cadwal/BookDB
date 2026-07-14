using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Thin helpers for driving and querying the headless visual tree, so flow tests read as user actions. Every
/// gesture flushes the dispatcher so the resulting bindings/commands have run before the next assertion.
/// </summary>
public static class Ui
{
    public static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>First descendant of type <typeparamref name="T"/> (optionally matching <paramref name="name"/>).</summary>
    public static T Find<T>(this Visual root, string? name = null) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => name is null || c.Name == name)
        ?? throw new InvalidOperationException(
            $"No {typeof(T).Name}{(name is null ? "" : $" named '{name}'")} in the visual tree.");

    /// <summary>All descendants of type <typeparamref name="T"/>, in visual-tree order.</summary>
    public static IReadOnlyList<T> Descendants<T>(this Visual root) where T : Control =>
        root.GetVisualDescendants().OfType<T>().ToList();

    /// <summary>The button whose Command is <paramref name="command"/> — identifies a button without its label.</summary>
    public static Button ButtonFor(this Visual root, System.Windows.Input.ICommand command) =>
        root.Descendants<Button>().FirstOrDefault(b => ReferenceEquals(b.Command, command))
        ?? throw new InvalidOperationException("No button bound to the given command.");

    /// <summary>Focus a text box and type into it via real headless key input.</summary>
    public static void TypeInto(this Window window, TextBox target, string text)
    {
        target.Focus();
        Pump();
        window.KeyTextInput(text);
        Pump();
    }

    /// <summary>Real input that replaces the field's current content: focus, select all, then type.</summary>
    public static void RetypeInto(this Window window, TextBox target, string text)
    {
        target.Focus();
        Pump();
        target.SelectAll();
        window.KeyTextInput(text);
        Pump();
    }

    /// <summary>Clicks a button the way a user can: asserts it is effectively enabled, then invokes its bound
    /// command with the button's own CommandParameter (awaited when async).</summary>
    public static async Task ClickAsync(this Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        if (button.Command is IAsyncRelayCommand asyncCommand)
            await asyncCommand.ExecuteAsync(button.CommandParameter);
        else
            button.Command!.Execute(button.CommandParameter);
        Pump();
    }

    public static void Press(this Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        Pump();
    }

    /// <summary>
    /// Real double-click at the centre of <paramref name="target"/>. The double-tap gesture window is
    /// wall-clock, so a stalled runner can split the two clicks past it and the gesture never fires —
    /// callers waiting on the gesture's effect should re-invoke this inside <see cref="PumpUntil"/> rather
    /// than bet on a single attempt.
    /// </summary>
    public static void DoubleClick(this Window window, Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Pump();
    }

    /// <summary>
    /// Pumps the dispatcher until <paramref name="condition"/> holds, letting real-time work (e.g. a debounced
    /// search timer plus its async reload) settle. Throws if it never converges within the timeout.
    /// </summary>
    public static async Task PumpUntil(Func<bool> condition, CancellationToken ct, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException($"Condition not met within {timeoutMs} ms.");
            Pump();
            await Task.Delay(25, ct);
        }
        Pump();
    }

    /// <summary>Show a control hosted in a window (the smoke/flow entry point) and flush the first layout pass.</summary>
    public static Window Host(this Control content)
    {
        var window = new Window { Content = content, Width = 800, Height = 600 };
        window.Show();
        Pump();
        return window;
    }
}
