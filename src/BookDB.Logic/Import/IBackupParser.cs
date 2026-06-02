using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Import;

/// <summary>Interface for backup parsers — allows mocking in tests.</summary>
public interface IBackupParser
{
    Task<ParsedBackup> ParseAsync(string path, CancellationToken ct = default);
}
