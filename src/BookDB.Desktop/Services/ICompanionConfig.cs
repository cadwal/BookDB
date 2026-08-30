using BookDB.Models;

namespace BookDB.Desktop.Services;

/// <summary>
/// The companion block of the bootstrap config, held in memory so the running host and the handshake read
/// the current values without touching disk on every call. <see cref="CompanionHostManager"/> owns the
/// single instance and updates it when the user saves Settings.
/// </summary>
public interface ICompanionConfig
{
    CompanionOptions Current { get; }
}
