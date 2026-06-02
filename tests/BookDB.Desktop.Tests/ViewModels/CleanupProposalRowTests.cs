using System.Collections.Generic;
using System.ComponentModel;
using BookDB.Desktop.ViewModels;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public class CleanupProposalRowTests
{
    [Fact]
    public void IsSplitRow_WhenSplitGroupIdIsNull_ReturnsFalse()
    {
        var row = new CleanupProposalRow { PersonId = 1 };
        Assert.False(row.IsSplitRow);
    }

    [Fact]
    public void IsSplitRow_WhenSplitGroupIdIsSet_ReturnsTrue()
    {
        var row = new CleanupProposalRow { SplitGroupId = "split:42" };
        Assert.True(row.IsSplitRow);
    }

    [Fact]
    public void ProposedDisplayName_IsObservable_RaisesPropertyChanged()
    {
        var row = new CleanupProposalRow();
        var raised = new List<string?>();
        row.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        row.ProposedDisplayName = "New Name";

        Assert.Contains("ProposedDisplayName", raised);
    }

    [Fact]
    public void ApplyChecked_DefaultsToTrue()
    {
        var row = new CleanupProposalRow();
        Assert.True(row.ApplyChecked);
    }
}
