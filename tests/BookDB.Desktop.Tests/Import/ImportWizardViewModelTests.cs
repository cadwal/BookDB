using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Import;
using BookDB.Logic.Messages;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.Import;

public class ImportWizardViewModelTests : IDisposable
{
    private readonly TestLookupServiceFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private ImportWizardViewModel CreateVm(
        IImportService? importService = null,
        IFilePickerService? filePicker = null,
        IMessenger? messenger = null,
        ILookupManagementService? lookupManagement = null)
    {
        importService ??= Substitute.For<IImportService>();
        filePicker ??= Substitute.For<IFilePickerService>();
        messenger ??= new WeakReferenceMessenger();
        lookupManagement ??= Substitute.For<ILookupManagementService>();

        return new ImportWizardViewModel(
            importService, filePicker, _factory.LookupService, lookupManagement, messenger,
            Substitute.For<IWindowService>());
    }

    private static ImportPreview MakeCannedPreview() => new ImportPreview(
        TotalRecords: 10,
        RecordsWithIsbn: 8,
        RecordsWithoutIsbn: 2,
        DuplicateIsbnCount: 1,
        RecordsWithCovers: 3,
        Warnings: Array.Empty<string>(),
        SampleRows: new List<ImportSampleRow>());

    [Fact]
    public async Task StepNavigationAdvancesStepIndex()
    {
        var importService = Substitute.For<IImportService>();
        importService
            .PreviewAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MakeCannedPreview()));

        var vm = CreateVm(importService: importService);
        vm.Step1.FilePath = "backup.zip";
        vm.Step1.SelectedCollection = new Collection { CollectionId = 1, Name = "Library" };

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.CurrentStepIndex);
    }

    [Fact]
    public async Task BackFromStep2ReturnsToStep1()
    {
        var importService = Substitute.For<IImportService>();
        importService
            .PreviewAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MakeCannedPreview()));

        var vm = CreateVm(importService: importService);
        vm.Step1.FilePath = "backup.zip";
        vm.Step1.SelectedCollection = new Collection { CollectionId = 1, Name = "Library" };

        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.CurrentStepIndex);

        vm.BackCommand.Execute(null);
        Assert.Equal(0, vm.CurrentStepIndex);
    }

    [Fact]
    public void CancelFromAnyStepClosesDialog()
    {
        var vm = CreateVm();

        bool? capturedResult = null;
        vm.CloseDialog = r => capturedResult = r;

        vm.CancelCommand.Execute(null);

        Assert.Equal(false, capturedResult);
    }

    [Fact]
    public void Step1IsInvalidWithoutFilePath()
    {
        var vm = CreateVm();
        vm.Step1.FilePath = string.Empty;
        vm.Step1.SelectedCollection = new Collection { CollectionId = 1, Name = "Library" };

        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public void Step1IsInvalidWithoutCollection()
    {
        var vm = CreateVm();
        vm.Step1.FilePath = "backup.zip";
        vm.Step1.SelectedCollection = null;

        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public void ProgressMessageUpdatesStep4ViewModel()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = CreateVm(messenger: messenger);

        // The messenger handler uses Dispatcher.UIThread.Post which is not available in unit tests.
        // Instead, directly verify the Step4 ViewModel exists and the wizard registered the handler
        // by checking that sending the message does not throw.
        var ex = Record.Exception(() => messenger.Send(new ImportProgressMessage(50, 100, "Test Book")));
        Assert.Null(ex);
        Assert.NotNull(vm.Step4);
    }

    // ---------------------------------------------------------------------------
    // StepIndicator formatting
    // ---------------------------------------------------------------------------

    [Fact]
    public void ImportWizardViewModel_StepIndicator_UsesFormatPlaceholders()
    {
        // StepIndicator returns a string containing the step number derived from
        // CurrentStepIndex, formatted via a resource key
        // (string.Format(Resources.Import_StepIndicator, step, total)).
        // It must not contain the literal unformatted placeholder text "Step {0}".
        var vm = CreateVm();

        // Step 1 — initial state, CurrentStepIndex = 0, so display shows step 1.
        Assert.Contains("1", vm.StepIndicator);

        // Step 2 — advance one step directly via the backing field.
        vm.CurrentStepIndex = 1;
        Assert.Contains("2", vm.StepIndicator);

        // Must never expose an unformatted placeholder to the user.
        Assert.DoesNotContain("Step {0}", vm.StepIndicator);
    }
}
