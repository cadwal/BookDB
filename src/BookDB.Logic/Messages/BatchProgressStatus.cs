namespace BookDB.Logic.Messages;

public enum BatchProgressStatus
{
    None = 0,
    QueryingSources,
    ProcessingResults,
    Saving,
    Complete
}
