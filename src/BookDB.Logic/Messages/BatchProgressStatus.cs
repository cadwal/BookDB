namespace BookDB.Logic.Messages;

public enum BatchProgressStatus
{
    None = 0,
    QueryingSources,
    ProcessingResults,
    FetchingCovers,
    Saving,
    Complete
}
