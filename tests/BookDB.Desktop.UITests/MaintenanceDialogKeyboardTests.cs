using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookDB.Desktop.UITests;

public class MaintenanceDialogKeyboardTests : HeadlessTest
{
    [Fact]
    public async Task Esc_DoesNothingWhileRunning_ThenClosesOnceIdleAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var fake = new FakeMaintenanceService();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(services => services.AddSingleton<IDatabaseMaintenanceService>(fake));
            var vm = host.Resolve<MaintenanceViewModel>();
            var closed = false;
            vm.CloseDialog = () => closed = true;
            var dialog = new MaintenanceDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            // There is no Cancel action for a running check — Close is simply disabled until it ends, and Esc
            // must respect the same gate rather than reach a hidden/disabled button.
            vm.RunCheckCommand.Execute(null);
            await Ui.PumpUntil(() => vm.IsRunning, ct);
            Assert.False(dialog.ButtonFor(vm.CloseCommand).IsEffectivelyEnabled);
            dialog.Press(PhysicalKey.Escape);
            Assert.False(closed);
            Assert.True(vm.IsRunning);

            fake.Complete(new MaintenanceCheckResult(MaintenanceCheckStatus.Ok, [], []));
            await Ui.PumpUntil(() => !vm.IsRunning, ct);

            dialog.Press(PhysicalKey.Escape);
            Assert.True(closed);
        });
    }

    /// <summary>Blocks the integrity check mid-flight until the test releases it via <see cref="Complete"/>.</summary>
    private sealed class FakeMaintenanceService : IDatabaseMaintenanceService
    {
        private readonly TaskCompletionSource<MaintenanceCheckResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(MaintenanceCheckResult result) => _completion.TrySetResult(result);

        public Task<MaintenanceCheckResult> CheckIntegrityAsync(
            CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null)
            => _completion.Task;

        public Task<MaintenanceRepairResult> OptimizeAndRepairAsync(
            CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null,
            IProgress<string>? safetyBackupReport = null)
            => throw new NotSupportedException("Not exercised by this test.");
    }
}
