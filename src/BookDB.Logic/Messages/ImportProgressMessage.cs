namespace BookDB.Logic.Messages;

/// <summary>
/// Progress update sent by ImportService during an active import.
/// ImportStep4ViewModel subscribes to update the progress bar.
/// </summary>
public record ImportProgressMessage(int Processed, int Total, string CurrentTitle);
