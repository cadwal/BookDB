using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Pins the dialog keyboard matrix across the app's XAML dialogs: every dialog that offers an
/// unambiguous non-destructive dismiss carries it on <c>IsCancel</c> (Esc), and Enter's
/// <c>IsDefault</c> sits only on a safe primary — never on a destructive action or where a
/// multiline input would compete for the key. The sweep guards both directions: a lost Esc
/// carrier, or a stray Enter default added to a dialog that must not have one, fails here.
/// </summary>
public class DialogDefaultButtonSweepTests : HeadlessTest
{
    [Theory]
    // Dialog view                       Esc    Enter
    [InlineData(typeof(CsvColumnPickerDialog),   true,  true)]  // export is selection-only, safe on Enter
    [InlineData(typeof(LookupWizardDialog),      true,  false)] // multiline ISBN box competes for Enter
    [InlineData(typeof(MergeTargetPickerDialog), true,  false)] // merge is not an Enter action
    [InlineData(typeof(MergeReviewDialog),       true,  false)]
    [InlineData(typeof(BulkEditDialog),          true,  false)] // bulk write — no Enter default
    [InlineData(typeof(CheckOutDialog),          true,  true)]
    [InlineData(typeof(AddBookDialog),           true,  true)]
    [InlineData(typeof(AdvancedSearchDialog),    true,  true)]
    [InlineData(typeof(BackupFormatDialog),      true,  true)]
    [InlineData(typeof(ConnectDialog),           false, true)]  // startup choice: Retry/Quit primary, no plain dismiss
    [InlineData(typeof(StartupFailureDialog),    false, true)]
    public async Task Dialog_CarriesExpectedEscAndEnterButtons(Type viewType, bool expectEsc, bool expectEnter)
    {
        await RunUi(() =>
        {
            var window = (Window)Activator.CreateInstance(viewType)!;
            var buttons = window.GetLogicalDescendants().OfType<Button>().ToList();

            var escCount = buttons.Count(b => b.IsCancel);
            var enterCount = buttons.Count(b => b.IsDefault);

            if (expectEsc)
                Assert.True(escCount == 1, $"{viewType.Name}: expected exactly one Esc (IsCancel) button, found {escCount}.");
            else
                Assert.True(escCount == 0, $"{viewType.Name}: expected no Esc (IsCancel) button, found {escCount}.");

            if (expectEnter)
                Assert.True(enterCount == 1, $"{viewType.Name}: expected exactly one Enter (IsDefault) button, found {enterCount}.");
            else
                Assert.True(enterCount == 0, $"{viewType.Name}: expected no Enter (IsDefault) button, found {enterCount}.");

            return Task.CompletedTask;
        });
    }
}
