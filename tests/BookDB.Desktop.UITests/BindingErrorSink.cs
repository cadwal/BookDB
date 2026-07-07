using System.Collections.Generic;
using System.Text;
using Avalonia.Logging;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Avalonia log sink installed once on <see cref="Logger.Sink"/> for the test session. It records binding
/// warnings/errors into the currently-running test's collector (swapped per test by the harness) so a binding
/// failure while a window/pane/dialog is exercised fails that test. Only the binding area is enabled — other log
/// noise is dropped.
/// </summary>
internal sealed class BindingErrorSink : ILogSink
{
    /// <summary>The current test's collector; null between tests. Set/cleared on the single UI thread, so no locking.</summary>
    public List<string>? Collector { get; set; }

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && area == LogArea.Binding;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Capture(level, area, messageTemplate, null);

    public void Log<T0>(LogEventLevel level, string area, object? source, string messageTemplate, T0 p0) =>
        Capture(level, area, messageTemplate, [p0]);

    public void Log<T0, T1>(LogEventLevel level, string area, object? source, string messageTemplate, T0 p0, T1 p1) =>
        Capture(level, area, messageTemplate, [p0, p1]);

    public void Log<T0, T1, T2>(LogEventLevel level, string area, object? source, string messageTemplate, T0 p0, T1 p1, T2 p2) =>
        Capture(level, area, messageTemplate, [p0, p1, p2]);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, object?[] propertyValues) =>
        Capture(level, area, messageTemplate, propertyValues);

    private void Capture(LogEventLevel level, string area, string messageTemplate, object?[]? values)
    {
        if (Collector is null || !IsEnabled(level, area))
            return;
        Collector.Add(values is { Length: > 0 } ? Render(messageTemplate, values) : messageTemplate);
    }

    // Render an Avalonia message template ("{Name}" holes filled positionally) for a readable assertion message.
    private static string Render(string template, object?[] values)
    {
        var value = 0;
        var sb = new StringBuilder(template.Length);
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == '{' && i + 1 < template.Length && template[i + 1] != '{')
            {
                var end = template.IndexOf('}', i);
                if (end > 0)
                {
                    sb.Append(value < values.Length ? values[value++] : "?");
                    i = end;
                    continue;
                }
            }
            sb.Append(template[i]);
        }
        return sb.ToString();
    }
}
