using System.Collections.Generic;
using System.Linq;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using BookDB.Mobile.ViewModels;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The batch tray: what it lists, what it says when there is nothing in it, what removing a book
/// takes, and why the upload button is or is not pressable.</summary>
public class BatchTrayViewModelTests
{
    private static BatchTrayViewModel New(
        out FakeStagingStore store,
        out FakeConnectionService connection,
        out FakeNavigator navigator,
        out List<StagedItem> edited,
        out List<int> uploads)
    {
        store = new FakeStagingStore();
        connection = new FakeConnectionService();
        navigator = new FakeNavigator();
        var reopened = new List<StagedItem>();
        edited = reopened;
        var sent = new List<int>();
        uploads = sent;

        return new BatchTrayViewModel(
            store, connection, navigator,
            item =>
            {
                reopened.Add(item);
                return new StubPage();
            },
            () => sent.Add(1));
    }

    private static StagedBook Book(string id, string isbn, params ScanImageType[] types) => new()
    {
        ClientItemId = id,
        Isbn = isbn,
        Images = [.. types.Select(type => new StagedImage { Type = type, Jpeg = [1, 2, 3] })],
    };

    [Fact]
    public void AnEmptyTray_SaysSoAndHasNothingToSend()
    {
        var tray = New(out _, out _, out _, out _, out _);

        Assert.True(tray.IsEmpty);
        Assert.Empty(tray.Items);
        Assert.False(tray.UploadCommand.CanExecute(null));
        Assert.False(tray.ShowsOfflineReason);
    }

    [Fact]
    public void EachStagedBook_GetsARowWithItsNumberAndPhotoCount()
    {
        var tray = New(out var store, out _, out _, out _, out _);
        store.Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover, ScanImageType.BackCover));

        var row = Assert.Single(tray.Items);
        Assert.Contains("013593", row.IsbnTail);
        Assert.Equal(string.Format(Resources.Batch_PhotoCount, 2), row.PhotoCount);
        Assert.NotNull(row.Thumbnail);
    }

    [Fact]
    public void OnePhoto_IsNotCalledOnePhotos()
    {
        var tray = New(out var store, out _, out _, out _, out _);
        store.Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover));

        Assert.Equal(Resources.Batch_PhotoCountOne, Assert.Single(tray.Items).PhotoCount);
    }

    [Fact]
    public void AShortNumber_IsShownWhole()
    {
        var tray = New(out var store, out _, out _, out _, out _);
        store.Save(Book("aaa1", "1234"));

        Assert.Equal("1234", Assert.Single(tray.Items).IsbnTail);
    }

    [Fact]
    public void TheTray_FollowsTheStore()
    {
        var tray = New(out var store, out _, out _, out _, out _);

        store.Save(Book("aaa1", "1111111111111"));
        store.Save(Book("bbb2", "2222222222222"));

        Assert.Equal(2, tray.Items.Count);
        Assert.False(tray.IsEmpty);
        Assert.Contains("2", tray.CountLine);
    }

    [Fact]
    public void RemovingABook_AsksFirstAndOnlyThenDropsIt()
    {
        var tray = New(out var store, out _, out _, out _, out _);
        store.Save(Book("aaa1", "1111111111111"));
        var row = Assert.Single(tray.Items);

        row.RemoveCommand.Execute(null);
        Assert.True(row.IsConfirmingRemove);
        Assert.Empty(store.Removed);

        row.ConfirmRemoveCommand.Execute(null);
        Assert.Equal(["aaa1"], store.Removed);
        Assert.True(tray.IsEmpty);
    }

    [Fact]
    public void ChangingYourMindAboutRemoving_LeavesTheBookAlone()
    {
        var tray = New(out var store, out _, out _, out _, out _);
        store.Save(Book("aaa1", "1111111111111"));
        var row = Assert.Single(tray.Items);

        row.RemoveCommand.Execute(null);
        row.CancelRemoveCommand.Execute(null);

        Assert.False(row.IsConfirmingRemove);
        Assert.Empty(store.Removed);
        Assert.Single(tray.Items);
    }

    [Fact]
    public void EditingABook_OpensItAgain()
    {
        var tray = New(out var store, out _, out var navigator, out var edited, out _);
        store.Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover));

        Assert.Single(tray.Items).EditCommand.Execute(null);

        Assert.Equal("aaa1", Assert.Single(edited).ClientItemId);
        Assert.Single(navigator.Pushed);
    }

    [Fact]
    public void Offline_TheUploadIsHeldBackAndTheReasonIsShown()
    {
        var tray = New(out var store, out var connection, out _, out _, out _);
        store.Save(Book("aaa1", "1111111111111"));

        Assert.True(tray.IsOffline);
        Assert.True(tray.ShowsOfflineReason);
        Assert.False(tray.UploadCommand.CanExecute(null));

        connection.Report(ConnectionStatus.Connected);

        Assert.False(tray.ShowsOfflineReason);
        Assert.True(tray.UploadCommand.CanExecute(null));
    }

    /// <summary>The same disabled button, a different reason: waiting for the network cannot fix a removal.</summary>
    [Fact]
    public void RemovedOnTheComputer_TheTraySaysThatInsteadOfWaitingForTheNetwork()
    {
        var tray = New(out var store, out var connection, out _, out _, out _);
        store.Save(Book("aaa1", "1111111111111"));

        connection.Report(ConnectionStatus.Revoked);

        Assert.True(tray.ShowsRevokedReason);
        Assert.False(tray.ShowsOfflineReason);
        Assert.False(tray.UploadCommand.CanExecute(null));
    }

    /// <summary>An empty tray offline has nothing to explain — the reason belongs to a batch that is waiting.</summary>
    [Fact]
    public void AnEmptyTrayOffline_ExplainsNothing()
    {
        var tray = New(out _, out _, out _, out _, out _);

        Assert.True(tray.IsOffline);
        Assert.False(tray.ShowsOfflineReason);
    }

    [Fact]
    public void Connected_UploadingSendsWhatIsWaiting()
    {
        var tray = New(out var store, out var connection, out _, out _, out var uploads);
        store.Save(Book("aaa1", "1111111111111"));
        connection.Report(ConnectionStatus.Connected);

        tray.UploadCommand.Execute(null);

        Assert.Single(uploads);
    }

    [Fact]
    public void GoingBackToTheTray_StopsItListening()
    {
        var tray = New(out var store, out var connection, out _, out _, out _);

        tray.Dispose();
        store.Save(Book("aaa1", "1111111111111"));
        connection.Report(ConnectionStatus.Connected);

        Assert.Empty(tray.Items);
    }
}
