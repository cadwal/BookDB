using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Restores a plain linear Tab cycle to a <see cref="TabControl"/>-hosted window.
///
/// Avalonia's <see cref="TabControl"/> keeps a per-content "last focused descendant" (its
/// <c>TabOnceActiveElement</c>); once focus has entered the selected tab's content and moved on,
/// the default Tab handler — and <see cref="KeyboardNavigationHandler.GetNext"/>, which it relies
/// on — wraps back into that remembered element instead of the window's genuine first control. So
/// forward Tab degrades into a two-element oscillation after one lap (the tab header and earlier
/// controls drop out). The behaviour is baked into the traversal and is not reachable through
/// <c>KeyboardNavigation.TabNavigation</c> at any level.
///
/// This behaviour tunnels the Tab key ahead of that default handling and walks a flat list of tab
/// stops it builds from the live visual tree, so the cycle is deterministic. A container marked
/// <c>TabNavigation=Once</c> (a radio group) counts as a single stop, matching how the rest of the
/// app treats it; read-only controls with <c>IsTabStop=False</c> are excluded. Only the Tab key is
/// intercepted — arrow-key and intra-group navigation are untouched.
/// </summary>
public class LinearTabOrderBehavior : Behavior<Window>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetaching()
    {
        AssociatedObject?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        base.OnDetaching();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;
        if ((e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None) return; // leave Ctrl+Tab etc. alone
        if (AssociatedObject is null) return;

        var focused = AssociatedObject.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null) return;

        var stops = BuildStops(AssociatedObject);
        if (stops.Count < 2) return;

        var current = stops.FindIndex(s => ReferenceEquals(s.Scope, focused) || s.Scope.IsVisualAncestorOf(focused));
        if (current < 0) return; // focus is somewhere we don't model — let the default handler run

        var forward = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var next = forward
            ? (current + 1) % stops.Count
            : (current - 1 + stops.Count) % stops.Count;

        stops[next].Target.Focus(NavigationMethod.Tab);
        e.Handled = true;
    }

    /// <summary>A single Tab stop: <see cref="Target"/> receives focus, <see cref="Scope"/> is the
    /// element (itself, or the enclosing <c>Once</c> container) that owns the currently focused control.</summary>
    private readonly record struct Stop(IInputElement Target, Visual Scope);

    private static List<Stop> BuildStops(Visual root)
    {
        var stops = new List<Stop>();
        Walk(root, stops);
        return stops;
    }

    private static void Walk(Visual node, List<Stop> stops)
    {
        foreach (var child in node.GetVisualChildren())
        {
            if (child is not Control c)
            {
                Walk(child, stops);
                continue;
            }

            if (!c.IsEffectivelyVisible)
                continue;

            if (KeyboardNavigation.GetTabNavigation(c) == KeyboardNavigationMode.Once)
            {
                var target = FirstFocusable(c);
                if (target is not null)
                    stops.Add(new Stop(target, c));
                continue; // the whole Once container is one stop — do not descend
            }

            if (IsTabStop(c))
            {
                stops.Add(new Stop(c, c));
                continue; // a focusable leaf is its own stop — its template parts are not stops
            }

            Walk(c, stops);
        }
    }

    private static bool IsTabStop(Control c) =>
        c is { Focusable: true, IsTabStop: true, IsEffectivelyEnabled: true } && c.IsEffectivelyVisible;

    private static IInputElement? FirstFocusable(Visual node)
    {
        foreach (var child in node.GetVisualChildren())
        {
            if (child is Control c)
            {
                if (!c.IsEffectivelyVisible) continue;
                if (IsTabStop(c)) return c;
            }
            if (FirstFocusable(child) is { } found) return found;
        }
        return null;
    }
}
