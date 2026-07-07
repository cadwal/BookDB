using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Bulk-edit journey through the real dialog: for a multi-book selection, drive the field and value ComboBoxes and
/// Apply, once per every editable field, asserting the chosen value lands on every selected book. A second test
/// cancels and asserts nothing changed.
/// </summary>
public class BulkEditFlowTests : HeadlessTest
{
    [Fact]
    public async Task ApplyingEachField_UpdatesEverySelectedBook()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await FacetSample.SeedAsync(host, ct); // three books plus an A/B pair of every lookup

            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            var ids = list.Books.Select(b => b.BookId).ToList();
            Assert.Equal(3, ids.Count);

            var vm = host.Resolve<BulkEditViewModel>();
            await vm.InitializeAsync(ids);
            var dialog = new BulkEditDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            var service = host.Resolve<IBookService>();
            var fieldCombo = dialog.Descendants<ComboBox>().First();
            var valueCombo = dialog.Descendants<ComboBox>().Last();

            var checks = new (BulkEditField Field, Func<Book, int?> Get)[]
            {
                (BulkEditField.Status,   b => b.StatusId),
                (BulkEditField.Location, b => b.LocationId),
                (BulkEditField.Rating,   b => b.RatingId),
                (BulkEditField.Format,   b => b.FormatId),
                (BulkEditField.Language, b => b.LanguageId),
                (BulkEditField.Owner,    b => b.OwnerId),
            };

            foreach (var (field, get) in checks)
            {
                var option = vm.EditableFields.Single(o => o.Field == field);
                fieldCombo.SelectedItem = option;
                Ui.Pump(); // let the field-change handler clear the stale values before we wait for the new ones
                await Ui.PumpUntil(() => vm.Values.Count > 0, ct);

                var chosen = vm.Values.First();
                valueCombo.SelectedItem = chosen;
                Ui.Pump();

                var applyButton = dialog.ButtonFor(vm.ApplyCommand);
                Assert.True(applyButton.IsEnabled);
                await ((IAsyncRelayCommand)applyButton.Command!).ExecuteAsync(null);

                foreach (var id in ids)
                {
                    var book = await service.GetBookByIdAsync(id, ct);
                    Assert.Equal(chosen.Id, get(book!));
                }
            }

            dialog.Close();
        });
    }

    [Fact]
    public async Task CancellingBulkEdit_LeavesEveryBookUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await FacetSample.SeedAsync(host, ct);

            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            var ids = list.Books.Select(b => b.BookId).ToList();
            var service = host.Resolve<IBookService>();
            var originalStatuses = new Dictionary<int, int?>();
            foreach (var id in ids)
                originalStatuses[id] = (await service.GetBookByIdAsync(id, ct))!.StatusId;

            var vm = host.Resolve<BulkEditViewModel>();
            await vm.InitializeAsync(ids);
            bool? closed = null;
            vm.CloseDialog = r => closed = r;
            var dialog = new BulkEditDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            var fieldCombo = dialog.Descendants<ComboBox>().First();
            fieldCombo.SelectedItem = vm.EditableFields.Single(o => o.Field == BulkEditField.Status);
            Ui.Pump();
            await Ui.PumpUntil(() => vm.Values.Count > 0, ct);
            // Pick a value different from any book's current status so a stray apply would be detectable.
            dialog.Descendants<ComboBox>().Last().SelectedItem = vm.Values.Last();
            Ui.Pump();

            var cancelButton = dialog.ButtonFor(vm.CancelCommand);
            cancelButton.Command!.Execute(null);
            Ui.Pump();

            Assert.False(closed);
            foreach (var id in ids)
                Assert.Equal(originalStatuses[id], (await service.GetBookByIdAsync(id, ct))!.StatusId);
            dialog.Close();
        });
    }
}
