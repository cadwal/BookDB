using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace BookDB.Help;

public static class HelpContentLoader
{
    private static readonly Assembly _helpAssembly = typeof(HelpContentLoader).Assembly;

    // topic: "shortcuts" | "glossary" | "import-guide" | "data-sources"
    public static async Task<string> LoadAsync(string topic, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var lang = culture.TwoLetterISOLanguageName;

        var stream = TryOpen($"BookDB.Help.Content.{topic}.{lang}.md");
        stream ??= TryOpen($"BookDB.Help.Content.{topic}.md");

        if (stream is null)
            return $"# Content not found\n\nResource `{topic}` is missing from the assembly.";

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static Stream? TryOpen(string resourceName) =>
        _helpAssembly.GetManifestResourceStream(resourceName);
}
