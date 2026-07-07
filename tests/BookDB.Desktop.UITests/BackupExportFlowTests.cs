using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Backup and CSV-export journeys. The backup format dialog offers both formats with the configured default
/// preselected, falls back to the CSV archive (with an always-visible note) on a backend without file backup,
/// changes the destination in-place via Browse, and gates Confirm on a destination being set. Confirming the
/// main-window backup command lands a real backup file in the chosen folder and remembers the folder; cancelling
/// writes nothing. The CSV column picker drives select/clear-all and per-column checkboxes, and the export
/// command writes a CSV whose header and rows follow the picked columns.
/// </summary>
public class BackupExportFlowTests : HeadlessTest
{
    [Fact]
    public async Task BackupDialog_PreselectsConfiguredFormat_BrowsesAndConfirms()
    {
        var picker = Substitute.For<IFilePickerService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var startFolder = Path.GetTempPath();
            var browsedFolder = Path.Combine(Path.GetTempPath(), "bookdb-backup-target");
            picker.PickFolderAsync(Arg.Any<string>()).Returns(browsedFolder);

            var (vm, dialog, closed) = Open(supportsFileBackup: true,
                configDefault: BackupFormatDialogViewModel.SqliteFormat, defaultFolder: startFolder, picker);

            // The configured SQLite default is preselected; the remote-fallback note stays hidden.
            var radios = dialog.Descendants<RadioButton>();
            Assert.True(radios[0].IsChecked);
            Assert.False(radios[1].IsChecked);
            Assert.DoesNotContain(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.BackupDialog_RemoteFallbackNote);

            // Browse swaps the destination in-place — no separate pop-up on Confirm.
            Assert.Equal(startFolder, dialog.Find<TextBox>().Text);
            await Ui.ClickAsync(dialog.ButtonFor(vm.BrowseCommand));
            Assert.Equal(browsedFolder, vm.DestinationFolder);
            Assert.Equal(browsedFolder, dialog.Find<TextBox>().Text);

            // Picking the CSV radio and confirming reports the CSV format.
            radios[1].IsChecked = true;
            Ui.Pump();
            Assert.True(vm.CsvSelected);
            await Ui.ClickAsync(dialog.ButtonFor(vm.ConfirmCommand));
            Assert.Equal(BackupFormatDialogViewModel.CsvFormat, vm.Result);
            Assert.True(closed());
            dialog.Close();
        });
    }

    [Fact]
    public async Task BackupDialog_RemoteBackend_FallsBackToCsvWithNote_AndGatesConfirmOnFolder()
    {
        var picker = Substitute.For<IFilePickerService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create();

            // No file backup: CSV is preselected regardless of the configured default, and the note is shown.
            var (vm, dialog, closed) = Open(supportsFileBackup: false,
                configDefault: BackupFormatDialogViewModel.SqliteFormat, defaultFolder: "", picker);
            Assert.True(vm.CsvSelected);
            Assert.False(vm.SqliteSelected);
            Assert.Contains(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.BackupDialog_RemoteFallbackNote);

            // No destination yet — Confirm is gated until Browse supplies one.
            var confirmButton = dialog.ButtonFor(vm.ConfirmCommand);
            Assert.False(confirmButton.IsEffectivelyEnabled);
            picker.PickFolderAsync(Arg.Any<string>()).Returns(Path.GetTempPath());
            await Ui.ClickAsync(dialog.ButtonFor(vm.BrowseCommand));
            Assert.True(confirmButton.IsEffectivelyEnabled);

            // Cancel closes without a result.
            await Ui.ClickAsync(dialog.ButtonFor(vm.CancelCommand));
            Assert.Null(vm.Result);
            Assert.False(closed());
            dialog.Close();
        });
    }

    [Fact]
    public async Task BackupCommand_LandsARealBackupFile_AndRemembersTheFolder()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            await SeedData.AddBookAsync(host, "Backup Subject", ct);
            var folder = Path.Combine(Path.GetTempPath(), $"bookdb_backup_{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                windowService.ShowBackupFormatDialogAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>())
                    .Returns((BackupFormatDialogViewModel.SqliteFormat, folder));

                var vm = host.Resolve<MainWindowViewModel>();
                await vm.BackupCommand.ExecuteAsync(null);

                // A real SQLite backup zip landed, and the folder is remembered for next time.
                var written = Assert.Single(Directory.GetFiles(folder));
                Assert.EndsWith(".zip", written);
                Assert.Equal(folder, await host.Resolve<ISettingsService>().GetAsync("LastBackupFolder", ct));

                // The zip holds a genuinely restorable library: a library.db that opens as a SQLite
                // database and still contains the book. (config.json inclusion is covered by the Logic
                // tests; the test host writes no config file, so the backup rightly omits it here.)
                using (var archive = System.IO.Compression.ZipFile.OpenRead(written))
                {
                    var dbEntry = archive.Entries.Single(e => e.Name == "library.db");
                    var extracted = Path.Combine(folder, "extracted.db");
                    dbEntry.ExtractToFile(extracted);
                    Assert.Equal("Backup Subject", await QuerySingleTitleAsync(extracted, ct));
                    File.Delete(extracted);
                }

                // The dialog was offered the backend capability and the configured default.
                await windowService.Received(1).ShowBackupFormatDialogAsync(true,
                    BackupFormatDialogViewModel.SqliteFormat, Arg.Any<string>());

                // The CSV-archive branch lands its own file, with the book's row in the per-table CSV.
                File.Delete(written);
                windowService.ShowBackupFormatDialogAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>())
                    .Returns((BackupFormatDialogViewModel.CsvFormat, folder));
                await vm.BackupCommand.ExecuteAsync(null);
                var csvArchive = Assert.Single(Directory.GetFiles(folder));
                Assert.EndsWith(".zip", csvArchive);
                using (var archive = System.IO.Compression.ZipFile.OpenRead(csvArchive))
                {
                    using var reader = new StreamReader(archive.Entries.Single(e => e.Name == "Books.csv").Open());
                    Assert.Contains("Backup Subject", await reader.ReadToEndAsync(ct));
                }
            }
            finally { Directory.Delete(folder, recursive: true); }
        });
    }

    [Fact]
    public async Task BackupCommand_Cancelled_WritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var folder = Path.Combine(Path.GetTempPath(), $"bookdb_backup_{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                windowService.ShowBackupFormatDialogAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>())
                    .Returns(((string, string)?)null);

                await host.Resolve<MainWindowViewModel>().BackupCommand.ExecuteAsync(null);

                Assert.Empty(Directory.GetFiles(folder));
                Assert.Null(await host.Resolve<ISettingsService>().GetAsync("LastBackupFolder", ct));
            }
            finally { Directory.Delete(folder, recursive: true); }
        });
    }

    [Fact]
    public async Task CsvColumnPicker_SelectsClearsAndTogglesColumns()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var exportService = host.Resolve<ICsvExportService>();
            var vm = host.Resolve<CsvColumnPickerViewModel>();
            vm.Initialize(exportService.AllColumnNames, exportService.DefaultColumnNames);
            System.Collections.Generic.IReadOnlyList<string>? picked = null;
            var closedTimes = 0;
            vm.CloseDialog = columns => { picked = columns; closedTimes++; };
            var dialog = new CsvColumnPickerDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            // One checkbox per exportable column, with the defaults preselected.
            var checkBoxes = dialog.Descendants<CheckBox>();
            Assert.Equal(exportService.AllColumnNames.Count, checkBoxes.Count);
            Assert.Equal(exportService.DefaultColumnNames.Count, checkBoxes.Count(c => c.IsChecked == true));

            // Clear all, then export: with nothing selected the dialog closes empty-handed.
            await Ui.ClickAsync(dialog.Descendants<Button>().First(b => ReferenceEquals(b.Command, vm.ClearAllCommand)));
            Assert.Equal(0, checkBoxes.Count(c => c.IsChecked == true));
            await Ui.ClickAsync(dialog.Descendants<Button>().First(b => ReferenceEquals(b.Command, vm.ExportCommand)));
            Assert.Equal(1, closedTimes);
            Assert.Null(picked);

            // Select all, untick one column through the view, and export the rest.
            await Ui.ClickAsync(dialog.Descendants<Button>().First(b => ReferenceEquals(b.Command, vm.SelectAllCommand)));
            Assert.Equal(checkBoxes.Count, checkBoxes.Count(c => c.IsChecked == true));
            var comments = vm.Columns.Single(c => c.Name == "Comments");
            checkBoxes.Single(c => Equals(c.Content, comments.Label)).IsChecked = false;
            Ui.Pump();
            Assert.False(comments.IsSelected);
            await Ui.ClickAsync(dialog.Descendants<Button>().First(b => ReferenceEquals(b.Command, vm.ExportCommand)));
            Assert.Equal(2, closedTimes);
            Assert.NotNull(picked);
            Assert.Equal(exportService.AllColumnNames.Count - 1, picked!.Count);
            Assert.DoesNotContain("Comments", picked);

            // Cancel always closes empty-handed.
            await Ui.ClickAsync(dialog.Descendants<Button>().First(b => ReferenceEquals(b.Command, vm.CancelCommand)));
            Assert.Equal(3, closedTimes);
            dialog.Close();
        });
    }

    [Fact]
    public async Task ExportCsvCommand_WritesTheFileWithThePickedColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();
        var picker = Substitute.For<IFilePickerService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s =>
            {
                s.AddSingleton(windowService);
                s.AddSingleton(picker);
            });
            await SeedData.AddBookAsync(host, "Dune", "9780441013593", ct);
            await SeedData.AddBookAsync(host, "Emma", "9780141439587", ct);

            var outputPath = Path.Combine(Path.GetTempPath(), $"bookdb_export_{Guid.NewGuid():N}.csv");
            try
            {
                windowService.ShowCsvColumnPickerAsync(
                        Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(),
                        Arg.Any<System.Collections.Generic.IReadOnlyList<string>>())
                    .Returns(new[] { "Title", "ISBN" });
                picker.SaveFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyList<string>>())
                    .Returns(outputPath);

                await host.Resolve<MainWindowViewModel>().ExportCsvCommand.ExecuteAsync(null);

                var lines = await File.ReadAllLinesAsync(outputPath, ct);
                Assert.Equal("Title,ISBN", lines[0]);
                Assert.Contains(lines, l => l.Contains("Dune") && l.Contains("9780441013593"));
                Assert.Contains(lines, l => l.Contains("Emma") && l.Contains("9780141439587"));
                Assert.Equal(3, lines.Length);
            }
            finally { File.Delete(outputPath); }
        });
    }

    [Fact]
    public async Task ExportCsvCommand_CancelledInThePicker_WritesNothing()
    {
        var windowService = Substitute.For<IWindowService>();
        var picker = Substitute.For<IFilePickerService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s =>
            {
                s.AddSingleton(windowService);
                s.AddSingleton(picker);
            });
            windowService.ShowCsvColumnPickerAsync(
                    Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(),
                    Arg.Any<System.Collections.Generic.IReadOnlyList<string>>())
                .Returns((System.Collections.Generic.IReadOnlyList<string>?)null);

            await host.Resolve<MainWindowViewModel>().ExportCsvCommand.ExecuteAsync(null);

            // Cancelling the column picker never reaches the save-file prompt.
            await picker.DidNotReceiveWithAnyArgs().SaveFileAsync(default!, default!, default!);
        });
    }

    // ─── Plumbing ────────────────────────────────────────────────────────────

    private static async Task<string?> QuerySingleTitleAsync(string dbPath, System.Threading.CancellationToken ct)
    {
        // Pooling off so the file handle is released the moment the connection closes (the caller deletes it).
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Title FROM Book"; // schema uses singular table names
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    /// <summary>Opens the dialog the way <c>WindowService.ShowBackupFormatDialogAsync</c> composes it.</summary>
    private static (BackupFormatDialogViewModel Vm, BackupFormatDialog Dialog, Func<bool?> Closed) Open(
        bool supportsFileBackup, string configDefault, string defaultFolder, IFilePickerService picker)
    {
        var vm = new BackupFormatDialogViewModel(supportsFileBackup, configDefault, defaultFolder, picker);
        bool? closed = null;
        vm.CloseDialog = accepted => closed = accepted;
        var dialog = new BackupFormatDialog { DataContext = vm };
        dialog.Show();
        Ui.Pump();
        return (vm, dialog, () => closed);
    }

}
