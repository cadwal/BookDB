using BookDB.Logic.Services;

namespace BookDB.Companion.Host;

/// <summary>
/// The library-side services the host serves from, gathered so the desktop hands them over in one place.
/// Everything here is plain Logic: the host maps between these and the wire contract, and no ASP.NET type
/// ever reaches them.
/// </summary>
public sealed record CompanionLibrary(
    ICompanionPreviewService Preview,
    ICompanionBrowseService Browse,
    ICompanionIntakeService Intake,
    ICompanionQueueStatusService QueueStatus);
