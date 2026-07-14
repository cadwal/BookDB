using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Import;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookDB.Desktop.UITests;

public class ReaderwareDbImportDialogKeyboardTests : HeadlessTest
{
    [Fact]
    public async Task Esc_AbortsARunningConversion_ThenClosesOnceItEnds()
    {
        var ct = TestContext.Current.CancellationToken;
        var fake = new FakeReaderwareDbExportService();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(services => services.AddSingleton<IReaderwareDbExportService>(fake));
            var vm = host.Resolve<ReaderwareDbImportViewModel>();
            await vm.InitializeAsync(ct);
            vm.DatabasePath = "C:/fake/rw4";
            string? closedWith = "unset";
            vm.CloseDialog = r => closedWith = r;
            var dialog = new ReaderwareDbImportDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            // Start a conversion the way a click does — fire the command without awaiting its completion,
            // since the fake service blocks mid-flight until this test lets it proceed.
            Assert.True(dialog.ButtonFor(vm.ConvertCommand).IsEffectivelyEnabled);
            vm.ConvertCommand.Execute(null);
            await Ui.PumpUntil(() => vm.IsRunning, ct);
            await fake.Started;

            // Esc while running hits the visible Cancel — it aborts the conversion, not the dialog.
            dialog.Press(PhysicalKey.Escape);
            await Ui.PumpUntil(() => !vm.IsRunning, ct);
            Assert.Equal("unset", closedWith);
            Assert.False(vm.IsComplete);

            // Start again and let it finish; Esc now hits Close (never Continue) and discards the result.
            fake.Reset();
            vm.ConvertCommand.Execute(null);
            await Ui.PumpUntil(() => vm.IsRunning, ct);
            await fake.Started;
            fake.Complete(new ReaderwareExportResult
            {
                Failure = ReaderwareExportFailure.None,
                OutputDirectory = "C:/fake/out",
                ExportedTables = ["READERWARE"],
            });
            await Ui.PumpUntil(() => vm.IsComplete, ct);

            dialog.Press(PhysicalKey.Escape);
            Assert.Null(closedWith);
        });
    }

    /// <summary>Blocks mid-export until the test releases it via <see cref="Complete"/> or cancellation.</summary>
    private sealed class FakeReaderwareDbExportService : IReaderwareDbExportService
    {
        private TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<ReaderwareExportResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Reset()
        {
            _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = new TaskCompletionSource<ReaderwareExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Complete(ReaderwareExportResult result) => _completion.TrySetResult(result);

        public async Task<ReaderwareExportResult> ExportAsync(
            string rw4Dir, string outputDir, string toolBinPath, IProgress<string>? log = null, CancellationToken ct = default)
        {
            _started.TrySetResult();
            using var registration = ct.Register(() =>
                _completion.TrySetResult(new ReaderwareExportResult { Failure = ReaderwareExportFailure.Cancelled }));
            return await _completion.Task;
        }
    }
}
