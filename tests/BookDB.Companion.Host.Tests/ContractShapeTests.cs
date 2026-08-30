using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BookDB.Contracts;
using ProtoBuf.Grpc.Configuration;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// Freezes the parts of the contract that are invisible to the compiler: the enum numbers that go on the
/// wire, the declared contract version, and protobuf-net.Grpc's ability to bind every method of the
/// service interface — an unsupported signature otherwise fails only at runtime on a paired phone.
/// </summary>
public sealed class ContractShapeTests
{
    [Fact]
    public void ContractVersion_IsOne()
    {
        Assert.Equal(1, ContractVersion.Current);
    }

    [Fact]
    public void ScanImageTypeNumbersMatchTheDesktopImageTypeIds()
    {
        Assert.Equal(0, (int)ScanImageType.FrontCover);
        Assert.Equal(2, (int)ScanImageType.BackCover);
        Assert.Equal(3, (int)ScanImageType.Spine);
        Assert.Equal(4, (int)ScanImageType.DustJacket);
        Assert.False(Enum.IsDefined(typeof(ScanImageType), 1), "1 is the desktop's thumbnail type and is never uploaded");
    }

    [Fact]
    public void BatchItemStateNumbersAreFrozen()
    {
        Assert.Equal(0, (int)BatchItemState.Unknown);
        Assert.Equal(1, (int)BatchItemState.Received);
        Assert.Equal(2, (int)BatchItemState.Saved);
        Assert.Equal(3, (int)BatchItemState.AddedToExisting);
        Assert.Equal(4, (int)BatchItemState.Cataloguing);
        Assert.Equal(5, (int)BatchItemState.Done);
        Assert.Equal(6, (int)BatchItemState.NeedsReview);
        Assert.Equal(7, (int)BatchItemState.Failed);
        Assert.Equal(8, (int)BatchItemState.AlreadyOwned);
    }

    [Fact]
    public void BatchItemFailureNumbersAreFrozen()
    {
        Assert.Equal(0, (int)BatchItemFailure.None);
        Assert.Equal(1, (int)BatchItemFailure.Unknown);
        Assert.Equal(2, (int)BatchItemFailure.InvalidIsbn);
        Assert.Equal(3, (int)BatchItemFailure.ImageRejected);
        Assert.Equal(4, (int)BatchItemFailure.SaveFailed);
        Assert.Equal(5, (int)BatchItemFailure.CatalogueFailed);
    }

    [Fact]
    public void IsbnPreviewSourceNumbersAreFrozen()
    {
        Assert.Equal(0, (int)IsbnPreviewSource.None);
        Assert.Equal(1, (int)IsbnPreviewSource.Library);
        Assert.Equal(2, (int)IsbnPreviewSource.Lookup);
    }

    [Fact]
    public void ClaimOutcomeNumbersAreFrozen()
    {
        Assert.Equal(0, (int)ClaimOutcome.Unknown);
        Assert.Equal(1, (int)ClaimOutcome.Registered);
        Assert.Equal(2, (int)ClaimOutcome.AlreadyRegistered);
        Assert.Equal(3, (int)ClaimOutcome.UnknownOrExpired);
        Assert.Equal(4, (int)ClaimOutcome.AlreadyUsed);
        Assert.Equal(5, (int)ClaimOutcome.DeviceLimitReached);
    }

    [Fact]
    public void ScannerServiceIsARecognizedServiceContract()
    {
        Assert.True(ServiceBinder.Default.IsServiceContract(typeof(IBookScannerService), out var serviceName));
        Assert.False(string.IsNullOrWhiteSpace(serviceName));
    }

    [Fact]
    public void EveryScannerServiceMethodBindsAsAnOperation()
    {
        var methods = typeof(IBookScannerService).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(9, methods.Length);

        var names = new List<string>();
        foreach (var method in methods)
        {
            Assert.True(ServiceBinder.Default.IsOperationContract(method, out var operationName), method.Name);
            Assert.False(string.IsNullOrWhiteSpace(operationName), method.Name);
            names.Add(operationName!);
        }

        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
