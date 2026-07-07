using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using BookDB.Data.DbContexts;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Desktop.Views.ImportStepViews;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Import wizard journey over a real (minimal) Readerware backup folder, with only the OS file picker faked:
/// file select gates Next on both a backup path and a collection (including the inline collection create and its
/// duplicate-name error), preview reads the parsed counts through the real parser, Back retains the choices, the
/// confirm step states the numbers, and the import itself runs against the temp database — new books land with
/// their contributor, cover and collection, the colliding ISBN is not imported twice, and the report shows it all.
/// The step panes are realized through the production <see cref="ViewLocator"/>, exactly as the app composes them.
/// </summary>
public sealed class ImportWizardFlowTests : HeadlessTest, IDisposable
{
    private const string AlphaIsbn = "9789100012345";
    private const string CollidingIsbn = "9789100000011";

    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task WholeJourney_PickPreviewConfirmImport_PersistsTheBackupAndReports()
    {
        var ct = TestContext.Current.CancellationToken;
        var backup = CreateBackupFolder();
        var picker = Substitute.For<IFilePickerService>();
        picker.PickFolderAsync(Arg.Any<string>()).Returns(backup);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(picker));
            await SeedData.AddBookAsync(host, "Already Here", CollidingIsbn, ct);
            var (vm, window, dialogResult) = await OpenAsync(host);

            // File select: only Cancel and a disabled Next are offered until path and collection are both chosen.
            AssertChrome(window, Resources.Import_StepTitle_FileSelect, step: 1);
            var next = window.ButtonFor(vm.NextCommand);
            Assert.True(window.ButtonFor(vm.CancelCommand).IsVisible);
            Assert.False(window.ButtonFor(vm.BackCommand).IsVisible);
            Assert.False(window.ButtonFor(vm.StartImportCommand).IsVisible);
            Assert.False(window.ButtonFor(vm.CloseCommand).IsVisible);
            Assert.False(next.IsEffectivelyEnabled);

            await Ui.ClickAsync(window.ButtonFor(vm.PickFolderCommand));
            var step1 = window.Find<ImportStep1View>();
            Assert.Equal(backup, step1.Find<TextBox>().Text); // the read-only path box shows the picked folder
            Assert.False(next.IsEffectivelyEnabled);          // still no collection

            // Creating a collection under an already-taken name fails inline and keeps the typed text.
            var nameBox = step1.Descendants<TextBox>()
                .Single(b => b.Watermark == Resources.Import_Step1_NewCollectionWatermark);
            var createButton = step1.ButtonFor(vm.CreateCollectionCommand);
            var takenName = vm.Step1.AvailableCollections[0].Name;
            window.TypeInto(nameBox, takenName);
            await Ui.ClickAsync(createButton);
            Assert.Contains(step1.Descendants<TextBlock>(),
                t => t.IsVisible && t.Text == Resources.Import_Step1_CollectionCreateFailed);
            Assert.Equal(takenName, nameBox.Text);

            // A fresh name succeeds: created, auto-selected, error cleared, input reset — and Next opens up.
            Ui.RetypeInto(window, nameBox, "Imported Books");
            await Ui.ClickAsync(createButton);
            Assert.Equal("Imported Books", vm.Step1.SelectedCollection?.Name);
            Assert.True(string.IsNullOrEmpty(nameBox.Text));
            Assert.False(createButton.IsEffectivelyEnabled);
            Assert.DoesNotContain(step1.Descendants<TextBlock>(),
                t => t.IsVisible && t.Text == Resources.Import_Step1_CollectionCreateFailed);

            // Preview: the counts come from the real parser reading the UTF-16BE backup files.
            await Ui.ClickAsync(next);
            AssertChrome(window, Resources.Import_StepTitle_Preview, step: 2);
            Assert.Equal(3, vm.Step2.TotalRecords);
            Assert.Equal(2, vm.Step2.RecordsWithIsbn);
            Assert.Equal(1, vm.Step2.RecordsWithoutIsbn);
            Assert.Equal(1, vm.Step2.DuplicateIsbnCount); // "Imported Beta" collides with the seeded ISBN
            Assert.Equal(1, vm.Step2.RecordsWithCovers);
            Assert.Equal(3, vm.Step2.SampleRows.Count);
            Assert.Equal("Jane Writer", vm.Step2.SampleRows[0].AuthorDisplay); // resolved via the CONTRIBUTOR file
            Assert.True(vm.Step2.SampleRows[0].HasCover);
            Assert.False(window.Find<ImportStep2View>().Find<Expander>().IsVisible); // clean backup → no notices

            // Back returns to file select with everything retained; Next re-runs the preview.
            var back = window.ButtonFor(vm.BackCommand);
            Assert.True(back.IsVisible);
            await Ui.ClickAsync(back);
            AssertChrome(window, Resources.Import_StepTitle_FileSelect, step: 1);
            Assert.Equal(backup, vm.Step1.FilePath);
            Assert.Equal("Imported Books", vm.Step1.SelectedCollection?.Name);
            await Ui.ClickAsync(next);
            await Ui.ClickAsync(next);

            // Confirm: the summary states the preview numbers; Next gives way to the Import button.
            AssertChrome(window, Resources.Import_StepTitle_Confirm, step: 3);
            Assert.Contains(window.Find<ImportStep3View>().Descendants<TextBlock>(),
                t => t.IsVisible && t.Text == string.Format(Resources.Import_Step3_Summary, 3, 2, 1, 1, 1));
            Assert.False(next.IsVisible);
            var import = window.ButtonFor(vm.StartImportCommand);
            Assert.True(import.IsVisible);

            // Import for real (overwrite policy defaults to Skip, so the collision never asks).
            await Ui.ClickAsync(import);
            AssertChrome(window, Resources.Import_StepTitle_Complete, step: 5);
            await Ui.PumpUntil(() => vm.Step4.ProcessedCount == 3, ct);
            Assert.Equal(3, vm.Step4.TotalCount);

            // Report: two new books, the colliding one never re-imported, the ISBN-less one flagged.
            Assert.Equal(2, vm.Step5.Imported);
            Assert.Equal(1, vm.Step5.FlaggedNoIsbn);
            Assert.Equal(1, vm.Step5.Skipped + vm.Step5.Updated);
            Assert.False(vm.Step5.WasCancelled);
            Assert.Empty(vm.Step5.Errors!);
            // The cancelled banner toggles on its parent Border, so effective visibility is what the user sees.
            Assert.DoesNotContain(window.Find<ImportStep5View>().Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.Import_Step5_Cancelled);

            // The data actually landed: contributor, cover image and collection on the new book; one ISBN owner.
            var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
            await using (var db = await factory.CreateDbContextAsync(ct))
            {
                var alpha = await db.Books.SingleAsync(b => b.Title == "Imported Alpha", ct);
                Assert.Equal(AlphaIsbn, alpha.Isbn);
                Assert.Equal(vm.Step1.SelectedCollectionId, alpha.CollectionId);
                Assert.True(await db.BookContributors.AnyAsync(
                    bc => bc.BookId == alpha.BookId && bc.Person!.DisplayName == "Jane Writer", ct));
                Assert.Equal(1, await db.BookImages.CountAsync(i => i.BookId == alpha.BookId, ct));
                Assert.True(await db.Books.AnyAsync(b => b.Title == "No Isbn Gamma", ct));
                Assert.Equal("Already Here", (await db.Books.SingleAsync(b => b.Isbn == CollidingIsbn, ct)).Title);
            }

            var close = window.ButtonFor(vm.CloseCommand);
            Assert.True(close.IsVisible);
            await Ui.ClickAsync(close);
            Assert.True(dialogResult());
        });
    }

    [Fact]
    public async Task CancelOnFileSelect_ClosesWithoutTouchingTheDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var backup = CreateBackupFolder();
        var picker = Substitute.For<IFilePickerService>();
        picker.PickFolderAsync(Arg.Any<string>()).Returns(backup);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(picker));
            var (vm, window, dialogResult) = await OpenAsync(host);

            await Ui.ClickAsync(window.ButtonFor(vm.PickFolderCommand));
            await Ui.ClickAsync(window.ButtonFor(vm.CancelCommand));
            Assert.False(dialogResult());

            var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            Assert.Equal(0, await db.Books.CountAsync(ct));
        });
    }

    [Fact]
    public async Task FailedPreview_BouncesBackToFileSelect_WithTheInlineError()
    {
        var picker = Substitute.For<IFilePickerService>();
        picker.PickFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.zip"));

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(picker));
            var (vm, window, _) = await OpenAsync(host);

            await Ui.ClickAsync(window.ButtonFor(vm.PickFileCommand)); // the zip browse button
            SelectCollection(window, vm);
            await Ui.ClickAsync(window.ButtonFor(vm.NextCommand));

            // The wizard returns to file select and shows what went wrong where the user can fix it.
            AssertChrome(window, Resources.Import_StepTitle_FileSelect, step: 1);
            Assert.False(vm.Step2.IsLoading);
            Assert.False(string.IsNullOrEmpty(vm.Step1.ErrorMessage));
            Assert.Contains(window.Find<ImportStep1View>().Descendants<TextBlock>(),
                t => t.IsVisible && t.Text == vm.Step1.ErrorMessage);
            window.Close();
        });
    }

    [Fact]
    public async Task BackupMissingTheCatalogMarker_SurfacesTheAnalysisNotice()
    {
        var backup = CreateBackupFolder(includeCatalogMarker: false);
        var picker = Substitute.For<IFilePickerService>();
        picker.PickFolderAsync(Arg.Any<string>()).Returns(backup);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(picker));
            var (vm, window, _) = await OpenAsync(host);

            await Ui.ClickAsync(window.ButtonFor(vm.PickFolderCommand));
            SelectCollection(window, vm);
            await Ui.ClickAsync(window.ButtonFor(vm.NextCommand));

            Assert.True(vm.Step2.HasWarnings);
            var expander = window.Find<ImportStep2View>().Find<Expander>();
            Assert.True(expander.IsVisible);
            Assert.Equal(string.Format(Resources.Import_Step2_WarningsHeader, 1), expander.Header);

            expander.IsExpanded = true;
            Ui.Pump();
            Assert.Contains(expander.Descendants<TextBlock>(),
                t => t.IsVisible && t.Text is not null && t.Text.Contains("DBCATALOG40"));
            window.Close();
        });
    }

    // ─── Wizard plumbing ─────────────────────────────────────────────────────

    /// <summary>Opens the wizard the way <c>WindowService.ShowImportWizardAsync</c> does; the production
    /// <see cref="ViewLocator"/> goes on the window since the headless app skips the app-level registration.</summary>
    private static async Task<(ImportWizardViewModel Vm, ImportWizardWindow Window, Func<bool?> DialogResult)>
        OpenAsync(TestHost host)
    {
        var vm = host.Resolve<ImportWizardViewModel>();
        await vm.InitializeAsync();
        var window = new ImportWizardWindow { DataContext = vm };
        window.DataTemplates.Add(new ViewLocator(host.Services));
        bool? result = null;
        vm.CloseDialog = r => { result = r; window.Close(); };
        window.Show();
        Ui.Pump();
        return (vm, window, () => result);
    }

    /// <summary>The window chrome shows the step's title and the "step n of 5" indicator.</summary>
    private static void AssertChrome(ImportWizardWindow window, string title, int step)
    {
        Assert.Contains(window.Descendants<TextBlock>(), t => t.IsVisible && t.Text == title);
        var indicator = string.Format(Resources.Import_StepIndicator, step, 5);
        Assert.Contains(window.Descendants<TextBlock>(), t => t.IsVisible && t.Text == indicator);
    }

    /// <summary>Picks the first available collection through the real ComboBox.</summary>
    private static void SelectCollection(ImportWizardWindow window, ImportWizardViewModel vm)
    {
        window.Find<ImportStep1View>().Find<ComboBox>().SelectedItem = vm.Step1.AvailableCollections[0];
        Ui.Pump();
    }

    // ─── Backup fixture ──────────────────────────────────────────────────────

    /// <summary>
    /// A minimal but real Readerware 4.x backup folder — UTF-16BE CSV files the production parser reads by header
    /// name: three books (one with an author and a cover, one whose ISBN collides with an existing book, one with
    /// no ISBN at all), the contributor lookup, and one valid JPEG cover.
    /// </summary>
    private string CreateBackupFolder(bool includeCatalogMarker = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bookdb_rw_backup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        WriteUtf16Be(dir, "READERWARE",
            "ROWKEY,TITLE,AUTHOR,ISBN",
            $"1,Imported Alpha,1,{AlphaIsbn}",
            $"2,Imported Beta,-1,{CollidingIsbn}",
            "3,No Isbn Gamma,-1,");
        WriteUtf16Be(dir, "CONTRIBUTOR",
            "ROWKEY,NAME,SORT_NAME",
            "1,Jane Writer,\"Writer, Jane\"");
        WriteUtf16Be(dir, "FULL_IMAGES",
            "ROW_ID,IMAGE_INDEX,IMAGE_DATA",
            "1,0,ffd8ffe000104a46494600010100000100010000"); // minimal JPEG: SOI + JFIF APP0
        if (includeCatalogMarker)
            File.WriteAllText(Path.Combine(dir, "DBCATALOG40"), "");
        return dir;
    }

    private static void WriteUtf16Be(string dir, string name, params string[] lines) =>
        File.WriteAllBytes(Path.Combine(dir, name), Encoding.BigEndianUnicode.GetBytes(string.Join("\r\n", lines)));
}
