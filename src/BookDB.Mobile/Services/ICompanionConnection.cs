using System;
using BookDB.Contracts;

namespace BookDB.Mobile.Services;

/// <summary>An open, authenticated channel to a desktop library. Disposing it closes the underlying gRPC
/// channel and releases the client certificate.</summary>
public interface ICompanionConnection : IDisposable
{
    IBookScannerService Service { get; }
}
