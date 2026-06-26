using BookDB.Models;

namespace BookDB.Desktop.Localization;

/// <summary>
/// Maps the migration enums emitted by the migration layer to localized strings. Phases are always mapped;
/// per-table progress is logged for the user-meaningful tables only (with cover images as an explicit row) —
/// <see cref="TryDescribe"/> returns false for the auxiliary lookup/join tables, which copy without their own line.
/// </summary>
public static class MigrationText
{
    public static string Describe(MigrationPhase phase) => phase switch
    {
        MigrationPhase.Clearing   => Resources.MoveLibrary_Phase_Preparing,
        MigrationPhase.Copying    => Resources.MoveLibrary_Phase_Copying,
        MigrationPhase.Finalizing => Resources.MoveLibrary_Phase_Finalizing,
        MigrationPhase.Verifying  => Resources.MoveLibrary_Phase_Verifying,
        _ => string.Empty,
    };

    public static bool TryDescribe(MigrationTable table, out string label)
    {
        label = table switch
        {
            MigrationTable.Book      => Resources.MoveLibrary_Table_Books,
            MigrationTable.BookImage => Resources.MoveLibrary_Table_CoverImages,
            MigrationTable.Loan      => Resources.MoveLibrary_Table_Loans,
            MigrationTable.Borrower  => Resources.MoveLibrary_Table_Borrowers,
            MigrationTable.Person    => Resources.MoveLibrary_Table_People,
            MigrationTable.Publisher => Resources.MoveLibrary_Table_Publishers,
            _ => string.Empty,
        };
        return label.Length > 0;
    }
}
