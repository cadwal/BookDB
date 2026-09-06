using System.Linq;
using System.Threading.Tasks;
using Avalonia.VisualTree;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using ColorTextBlock.Avalonia;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Release notes are the only markdown BookDB shows that has ever contained a link, so the viewer's link
/// handling had never run in the app before 4.0.1 put the call for Play testers in front of users. A link
/// the viewer failed to recognise would still read correctly in the window and simply do nothing when
/// clicked — the failure is silent, and invisible to every other test. Markdown.Avalonia renders through
/// <c>CTextBlock</c>, where a recognised link becomes a <c>CHyperlink</c> carrying the command that opens it.
/// </summary>
public class ReleaseNotesLinkTests : HeadlessTest
{
    private const string Url = "https://github.com/cadwal/BookDB/issues/1";

    [Fact]
    public Task AUrlInTheNotes_BecomesAHyperlinkThatCanBeFollowed() => RunUi(() =>
    {
        var window = new ReleaseNotesWindow
        {
            DataContext = new ReleaseNotesViewModel("4.0.1", $"How to volunteer: [{Url}]({Url})"),
        };
        window.Show();
        Ui.Pump();

        var link = window.GetVisualDescendants()
            .OfType<CTextBlock>()
            .SelectMany(block => block.Content)
            .OfType<CHyperlink>()
            .Single();

        Assert.NotNull(link.Command);
        Assert.Equal(Url, link.CommandParameter);

        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>
    /// The link text is the URL itself, so a viewer that cannot follow it can still read and type it. That
    /// only holds while the text survives rendering intact.
    /// </summary>
    [Fact]
    public Task TheLinkShowsTheUrlItself() => RunUi(() =>
    {
        var window = new ReleaseNotesWindow
        {
            DataContext = new ReleaseNotesViewModel("4.0.1", $"How to volunteer: [{Url}]({Url})"),
        };
        window.Show();
        Ui.Pump();

        var rendered = window.GetVisualDescendants()
            .OfType<CTextBlock>()
            .Select(block => block.Text);

        Assert.Contains(rendered, text => text is not null && text.Contains(Url));

        window.Close();
        return Task.CompletedTask;
    });
}
