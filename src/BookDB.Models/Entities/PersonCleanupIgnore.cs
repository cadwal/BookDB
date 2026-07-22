using System;

namespace BookDB.Models.Entities;

/// <summary>
/// A dismissed person-name cleanup proposal. Keyed by the proposal's content, not just the
/// person: the scan skips a proposal only while it re-derives exactly the same suggestion, so
/// changed person data surfaces a fresh proposal despite an old ignore. Rows are removed with
/// their person via the database cascade.
/// </summary>
public class PersonCleanupIgnore
{
    public int PersonCleanupIgnoreId { get; set; }
    public int PersonId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ProposedContent { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
