using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using BookDB.Mobile.ViewModels;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The sending screen: a row per book from the moment it starts, each row following what the
/// computer says, and a way back for a run that did not get everything across.</summary>
public class UploadViewModelTests
{
    private sealed class FakeUploadService : IUploadService
    {
        public UploadResult Result { get; set; } = UploadResult.Completed;

        public List<BatchItemStatus> Report { get; } = [];

        /// <summary>Books the run "confirms" — dropped from staging the way the real service does.</summary>
        public IStagingStore? Staging { get; set; }

        public List<string> Confirm { get; } = [];

        public int Runs { get; private set; }

        public Task<UploadResult> RunAsync(Action<BatchItemStatus> onStatus, CancellationToken ct = default)
        {
            Runs++;

            foreach (var status in Report)
                onStatus(status);

            foreach (var id in Confirm)
                Staging?.Remove(id);

            return Task.FromResult(Result);
        }
    }

    private static UploadViewModel New(
        out FakeStagingStore staging,
        out FakeUploadService upload,
        out FakeNavigator navigator)
    {
        staging = new FakeStagingStore();
        upload = new FakeUploadService { Staging = staging };
        navigator = new FakeNavigator();
        return new UploadViewModel(staging, upload, navigator);
    }

    private static StagedBook Book(string id, string isbn) =>
        new() { ClientItemId = id, Isbn = isbn, Images = [] };

    [Fact]
    public async Task TheWholeBatch_HasARowBeforeAnythingIsHeardBack()
    {
        var vm = New(out var staging, out _, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        staging.Save(Book("bbb2", "9789100120115"));

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Rows.Count);
        Assert.Contains("013593", vm.Rows[0].IsbnTail);
    }

    [Fact]
    public void ARowStartsOutWaiting()
    {
        var row = new UploadRowViewModel(new StagedItem
        {
            ClientItemId = "aaa1",
            Isbn = "9780441013593",
            ImageTypes = [],
            StagedAt = DateTimeOffset.UtcNow,
        });

        Assert.Equal(Resources.Upload_State_Waiting, row.StateText);
        Assert.False(row.HasFailed);
    }

    [Fact]
    public async Task EachRow_FollowsWhatTheComputerSaysAboutIt()
    {
        var vm = New(out var staging, out var upload, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        upload.Report.Add(new BatchItemStatus { ClientItemId = "aaa1", State = BatchItemState.Done, Title = "Dune" });

        await vm.RunCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Rows);
        Assert.Contains("Dune", row.StateText);
        Assert.Equal(BatchItemState.Done, row.State);
    }

    [Fact]
    public async Task AFailedBook_SaysWhyAndCarriesTheComputersOwnWording()
    {
        var vm = New(out var staging, out var upload, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        upload.Report.Add(new BatchItemStatus
        {
            ClientItemId = "aaa1",
            State = BatchItemState.Failed,
            Failure = BatchItemFailure.SaveFailed,
            FailureDetail = "disk full",
        });

        await vm.RunCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Rows);
        Assert.True(row.HasFailed);
        Assert.Equal(Resources.Upload_Failure_SaveFailed, row.StateText);
        Assert.Equal("disk full", row.Detail);
    }

    [Fact]
    public async Task AStatusForABookThatIsNotOnScreen_IsIgnored()
    {
        var vm = New(out var staging, out var upload, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        upload.Report.Add(new BatchItemStatus { ClientItemId = "somethingelse", State = BatchItemState.Done });

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Upload_State_Waiting, Assert.Single(vm.Rows).StateText);
    }

    [Fact]
    public async Task AFinishedRun_SaysSoAndHasNothingToRetry()
    {
        var vm = New(out var staging, out var upload, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        upload.Confirm.Add("aaa1");

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Upload_Completed, vm.Summary);
        Assert.False(vm.CanRetry);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task AnInterruptedRun_OffersToTryAgain()
    {
        var vm = New(out var staging, out var upload, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        upload.Result = UploadResult.Interrupted;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Upload_Interrupted, vm.Summary);
        Assert.True(vm.CanRetry);
    }

    [Fact]
    public async Task Offline_SaysNothingWasSentAndOffersToTryAgain()
    {
        var vm = New(out var staging, out var upload, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        upload.Result = UploadResult.Offline;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Upload_Offline, vm.Summary);
        Assert.True(vm.CanRetry);
    }

    /// <summary>Nothing to try again with: only pairing again changes a refusal.</summary>
    [Fact]
    public async Task Refused_SaysTheDeviceWasRemovedAndDoesNotOfferAnotherTry()
    {
        var vm = New(out var staging, out var upload, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        upload.Result = UploadResult.Refused;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Status_RevokedExplanation, vm.Summary);
        Assert.False(vm.CanRetry);
    }

    /// <summary>An interruption that nevertheless got everything across leaves nothing to retry.</summary>
    [Fact]
    public async Task AnInterruptionWithAnEmptyTray_HasNothingToTryAgain()
    {
        var vm = New(out var staging, out var upload, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        upload.Result = UploadResult.Interrupted;
        upload.Confirm.Add("aaa1");

        await vm.RunCommand.ExecuteAsync(null);

        Assert.False(vm.CanRetry);
    }

    [Fact]
    public async Task TryingAgain_StartsFromWhatIsLeft()
    {
        var vm = New(out var staging, out var upload, out _);
        staging.Save(Book("aaa1", "1111111111111"));
        staging.Save(Book("bbb2", "2222222222222"));
        upload.Result = UploadResult.Interrupted;
        upload.Confirm.Add("aaa1");
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Rows.Count);

        upload.Confirm.Clear();
        upload.Result = UploadResult.Completed;
        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(2, upload.Runs);
        Assert.Contains("222222", Assert.Single(vm.Rows).IsbnTail);
    }

    [Fact]
    public async Task Finishing_GoesBackToTheHub()
    {
        var vm = New(out var staging, out _, out var navigator);
        staging.Save(Book("aaa1", "9780441013593"));
        await vm.RunCommand.ExecuteAsync(null);

        vm.FinishCommand.Execute(null);

        Assert.Equal(1, navigator.HubCount);
    }
}
