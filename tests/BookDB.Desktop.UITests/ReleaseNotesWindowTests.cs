using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.VisualTree;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using ColorTextBlock.Avalonia;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// ReleaseNotesWindow: the notes markdown really renders (not just binds), the title carries the version,
/// and the single Close button answers both Esc and Enter. The window is deliberately absent from the
/// Window menu, so there is no open-windows registration to cover.
/// </summary>
public class ReleaseNotesWindowTests : HeadlessTest
{
    [Fact]
    public Task RendersMarkdown_TitlesWithVersion_AndEscCloses()
        => RunUi(() =>
        {
            var (vm, window, wasClosed) = Show();

            Assert.Equal(string.Format(Resources.ReleaseNotes_WindowTitle, "9.9.9"), window.Title);
            // The markdown viewer renders paragraphs as CTextBlock — assert the notes text is actually
            // realized in the visual tree, not merely bound.
            Assert.Contains(window.GetVisualDescendants().OfType<CTextBlock>(),
                t => t.Text?.Contains("brand-new smoke feature") == true);

            window.Press(PhysicalKey.Escape);
            Assert.True(wasClosed());
            return Task.CompletedTask;
        });

    [Fact]
    public Task EnterClosesToo()
        => RunUi(() =>
        {
            var (_, window, wasClosed) = Show();

            window.Press(PhysicalKey.Enter);
            Assert.True(wasClosed());
            return Task.CompletedTask;
        });

    private static (ReleaseNotesViewModel Vm, ReleaseNotesWindow Window, System.Func<bool> WasClosed) Show()
    {
        var vm = new ReleaseNotesViewModel("9.9.9", "### Added\n- A brand-new smoke feature");
        var window = new ReleaseNotesWindow { DataContext = vm };
        var closed = false;
        vm.CloseWindow = () => { closed = true; window.Close(); };
        window.Show();
        Ui.Pump();
        return (vm, window, () => closed);
    }
}
