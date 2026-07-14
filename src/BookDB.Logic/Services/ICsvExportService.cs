using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;

namespace BookDB.Logic.Services;

public record CsvExportParameters(
    string OutputPath,
    IReadOnlyList<string> SelectedColumns,
    IReadOnlySet<int>? CollectionIds,
    IReadOnlyList<int>? SearchBookIds,
    Dictionary<string, HashSet<int>>? FacetFilters,
    string? SortColumn,
    bool SortAscending);

public interface ICsvExportService
{
    IReadOnlyList<string> AllColumnNames { get; }
    IReadOnlyList<string> DefaultColumnNames { get; }
    Task ExportAsync(CsvExportParameters parameters, CancellationToken ct = default, IProgress<ProgressUpdate<CsvExportProgressStep>>? progress = null);
}
