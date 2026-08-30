using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BookDB.Mobile.ViewModels;

namespace BookDB.Mobile.UITests;

/// <summary>
/// Shows a screen in a window of a given size and lets a test read the visual tree. The sizes are the point:
/// the phone runs on everything from a small handset held upright to a tablet on its side, and several of
/// these tests are about what the layout does between those.
/// </summary>
public static class Phone
{
    /// <summary>A small handset upright — the width every screen was drawn at.</summary>
    public static readonly Size Portrait = new(400, 800);

    /// <summary>
    /// The same handset on its side with the on-screen keyboard up — which is the state it is in whenever
    /// someone is typing. This is the height that catches a screen built to be centred and nothing else.
    /// </summary>
    public static readonly Size Landscape = new(800, 220);

    /// <summary>A tablet, where a screen must not simply stretch.</summary>
    public static readonly Size Tablet = new(1280, 800);

    public static Window Show(Control content) => Show(content, Portrait);

    public static Window Show(Control content, Size size)
    {
        var window = new Window { Content = content, Width = size.Width, Height = size.Height };
        window.Show();
        Pump();

        // A second pass: the first arranges the frame, and the pages that measure themselves against it
        // (the capped content column, anything inside a ScrollViewer) settle on the next one.
        window.Measure(size);
        window.Arrange(new Rect(size));
        Pump();

        return window;
    }

    public static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>First descendant of type <typeparamref name="T"/> (optionally matching <paramref name="name"/>).</summary>
    public static T Find<T>(this Visual root, string? name = null) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => name is null || c.Name == name)
        ?? throw new InvalidOperationException(
            $"No {typeof(T).Name}{(name is null ? "" : $" named '{name}'")} in the visual tree.");

    /// <summary>All descendants of type <typeparamref name="T"/>, in visual-tree order.</summary>
    public static IReadOnlyList<T> Descendants<T>(this Visual root) where T : Control =>
        root.GetVisualDescendants().OfType<T>().ToList();

    /// <summary>The visible text blocks whose text is <paramref name="text"/> — how a test asks whether the
    /// screen is actually saying something, rather than merely holding a flag that says it would.</summary>
    public static IReadOnlyList<TextBlock> Showing(this Visual root, string text) =>
        root.Descendants<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Text == text)
            .ToList();

    /// <summary>The shell's navigation frame: the control actually holding the current page. Found by its
    /// content rather than by type, since the shell view is itself a ContentControl.</summary>
    public static ContentControl PageFrame(this Visual root, MainViewModel shell) =>
        root.Descendants<ContentControl>().FirstOrDefault(c => ReferenceEquals(c.Content, shell.CurrentPage))
        ?? throw new InvalidOperationException("The shell is not showing its current page.");

    /// <summary>The button whose Command is <paramref name="command"/> — identifies a button without its label.</summary>
    public static Button ButtonFor(this Visual root, System.Windows.Input.ICommand command) =>
        root.Descendants<Button>().FirstOrDefault(button => ReferenceEquals(button.Command, command))
        ?? throw new InvalidOperationException("No button bound to the given command.");

    /// <summary>
    /// Scrolls to the bottom of whatever <paramref name="target"/> sits in, the way a thumb would, and fails
    /// if it sits in nothing that scrolls. Deliberately the control's own enclosing scroller rather than the
    /// first one on screen: a TextBox brings its own, and finding that one would let a screen with no scroller
    /// of its own pass this as though it had one.
    /// </summary>
    public static void ScrollToBottom(this Window window, Control target)
    {
        var scroller = target.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()
            ?? throw new InvalidOperationException("The target is not inside anything that scrolls.");

        scroller.Offset = scroller.Offset.WithY(scroller.Extent.Height - scroller.Viewport.Height);
        Pump();
    }

    /// <summary>Whether <paramref name="target"/> lies inside the window, top to bottom — a control that is in
    /// the tree but below the bottom edge is not on screen.</summary>
    public static bool IsOnScreen(this Window window, Control target)
    {
        var top = target.TranslatePoint(default, window)?.Y;
        return top is { } y && y >= 0 && y + target.Bounds.Height <= window.Height;
    }
}
