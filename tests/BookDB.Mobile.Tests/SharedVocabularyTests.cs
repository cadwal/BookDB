using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>
/// The two apps describe the same library, so the words they use for the *things in it* must agree: a book
/// whose publisher is "Förlag" on the desktop cannot be "Utgivare" on the phone. The desktop's resx is the
/// older and shipped surface, so it owns the vocabulary and the phone follows it.
/// <para>
/// This covers domain terms only. Chrome — Cancel, Close, Back, Edit, Remove — is deliberately *not* shared:
/// the two apps are free to word their own buttons, and pinning them together would couple every screen of
/// one to the other for no reader's benefit.
/// </para>
/// </summary>
public class SharedVocabularyTests
{
    /// <summary>
    /// Mobile key → the desktop key that owns the term. Adding a phone string that names something in the
    /// library belongs here; if the desktop has no word for it yet, the phone is inventing one and the
    /// desktop should get it too.
    /// </summary>
    private static readonly (string Mobile, string Desktop)[] SharedTerms =
    [
        ("Detail_Field_Authors", "AddBook_Label_Authors"),
        ("Detail_Field_Series", "BookEditForm_Label_Series"),
        ("Detail_Field_Publisher", "BookEditForm_Label_Publisher"),
        ("Detail_Field_Published", "BookDetail_Label_Published"),
        ("Detail_Field_Format", "BookDetail_Label_Format"),
        ("Detail_Field_Language", "BookDetail_Label_Language"),
        ("Detail_Field_Pages", "BookDetail_Label_Pages"),
        ("Detail_Field_Isbn", "BookDetail_Label_Isbn"),
        ("Detail_Field_Collection", "AddBook_Label_Collection"),
        ("Detail_Field_Comments", "BookEditForm_Label_Comments"),
        ("Builder_SlotFrontCover", "BookImageType_Cover"),
        ("Builder_SlotBackCover", "BookImageType_BackCover"),
        ("Builder_SlotSpine", "BookImageType_Spine"),
        ("Builder_SlotDustJacket", "BookImageType_DustJacket"),
        ("Browse_Untitled", "MergeReview_Identity_Untitled"),

        // The seeded lookup words themselves. The phone keeps its own copy so it can say them in its own
        // language, which only works as long as the copy stays the desktop's word.
        ("Format_Audiobook", "Format_Audiobook"),
        ("Format_Comic", "Format_Comic"),
        ("Format_Ebook", "Format_Ebook"),
        ("Format_GraphicNovel", "Format_GraphicNovel"),
        ("Format_Hardcover", "Format_Hardcover"),
        ("Format_Magazine", "Format_Magazine"),
        ("Format_MassMarketPaperback", "Format_MassMarketPaperback"),
        ("Format_Paperback", "Format_Paperback"),
        ("Format_TradePaperback", "Format_TradePaperback"),
        ("Language_Danish", "Language_Danish"),
        ("Language_Finnish", "Language_Finnish"),
        ("Language_French", "Language_French"),
        ("Language_German", "Language_German"),
        ("Language_Italian", "Language_Italian"),
        ("Language_Japanese", "Language_Japanese"),
        ("Language_Norwegian", "Language_Norwegian"),
        ("Language_Spanish", "Language_Spanish"),
        ("Language_Swedish", "Language_Swedish"),
    ];

    [Theory]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("nl")]
    [InlineData("pt-BR")]
    [InlineData("pt-PT")]
    [InlineData("sv")]
    public void ThePhoneUsesTheDesktopsWordForEveryTermTheyShare(string locale)
    {
        var mobile = ReadValues(ResxPath("BookDB.Mobile", locale));
        var desktop = ReadValues(ResxPath("BookDB.Desktop", locale));

        var drifted = new List<string>();
        foreach (var (mobileKey, desktopKey) in SharedTerms)
        {
            Assert.True(mobile.ContainsKey(mobileKey), $"Resources.resx has no {mobileKey}");
            Assert.True(desktop.ContainsKey(desktopKey), $"the desktop resx has no {desktopKey}");

            if (Term(mobile[mobileKey]) != Term(desktop[desktopKey]))
            {
                drifted.Add($"{mobileKey} is \"{mobile[mobileKey]}\" but the desktop's {desktopKey} " +
                            $"is \"{desktop[desktopKey]}\"");
            }
        }

        Assert.True(drifted.Count == 0,
            $"The two apps disagree on library vocabulary in '{(locale.Length == 0 ? "en" : locale)}':" +
            Environment.NewLine + string.Join(Environment.NewLine, drifted));
    }

    /// <summary>
    /// The staged set of books is a batch on both ends, so the phone has to call it what the desktop calls
    /// it — the phone said "bunt" in Swedish and "stapel" in Dutch for a thing the desktop has always called
    /// a batch. Checked by containment rather than by pairing values, because the desktop only ever has the
    /// word inside a sentence: it has no label that is the bare noun.
    /// <para>
    /// Italian is absent deliberately: its desktop strings say "in blocco", an adverbial for doing something
    /// in bulk, and never name a batch as a noun. There is nothing for the phone to follow, so its "lotto"
    /// stands until the desktop grows a word of its own.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("nl")]
    [InlineData("pt-BR")]
    [InlineData("pt-PT")]
    [InlineData("sv")]
    public void ThePhoneCallsABatchWhatTheDesktopCallsIt(string locale)
    {
        var mobile = ReadValues(ResxPath("BookDB.Mobile", locale));
        var desktop = ReadValues(ResxPath("BookDB.Desktop", locale));

        string phone = Term(mobile["Batch_Title"]);
        string computer = desktop["BatchQueue_Idle"];

        Assert.True(
            computer.Contains(phone, StringComparison.OrdinalIgnoreCase),
            $"In '{(locale.Length == 0 ? "en" : locale)}' the phone calls a batch \"{phone}\", which is not " +
            $"the word the desktop uses in \"{computer}\".");
    }

    /// <summary>A term is the same term whether or not the desktop's form layout puts a colon after it —
    /// French writes that as " :", so the space goes with it.</summary>
    private static string Term(string value) => Regex.Replace(value, @"\s*:\s*$", string.Empty).Trim();

    /// <summary>Parsed as XML rather than matched with a pattern: every resx carries a commented-out schema
    /// example with `data` elements in it, and only a parser knows they are a comment.</summary>
    private static Dictionary<string, string> ReadValues(string path)
    {
        Assert.True(File.Exists(path), $"resx not found at: {path}");
        return XDocument.Load(path).Root!.Elements("data")
            .Where(e => e.Attribute("name") is not null)
            .ToDictionary(
                e => e.Attribute("name")!.Value,
                e => e.Element("value")?.Value ?? string.Empty);
    }

    private static string ResxPath(string project, string locale)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BookDB.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var name = locale.Length == 0 ? "Resources.resx" : $"Resources.{locale}.resx";
        return Path.Combine(dir.FullName, "src", project, "Localization", name);
    }
}
