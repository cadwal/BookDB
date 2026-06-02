using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public sealed partial class CsvColumnPickerViewModel : ObservableObject
{
    public Action<IReadOnlyList<string>?>? CloseDialog { get; set; }

    public ObservableCollection<CsvColumnItem> Columns { get; } = [];

    public void Initialize(IReadOnlyList<string> allColumns, IReadOnlyList<string> defaultSelected)
    {
        Columns.Clear();
        foreach (var col in allColumns)
            Columns.Add(new CsvColumnItem(col, ColumnLabel(col), defaultSelected.Contains(col)));
    }

    // Display label for a column identifier. Reuses the already-localized Print_Column_* resources
    // (whose suffix matches the export column identifier); falls back to the raw identifier.
    private static string ColumnLabel(string columnName)
        => Localization.Resources.ResourceManager.GetString($"Print_Column_{columnName}") ?? columnName;

    [RelayCommand]
    private void Export()
    {
        // Return the column identifier (Name), not the localized Label — it becomes the CSV
        // header and the persisted selection, which must stay stable across UI languages.
        var selected = Columns.Where(c => c.IsSelected).Select(c => c.Name).ToList();
        CloseDialog?.Invoke(selected.Count > 0 ? selected : null);
    }

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(null);

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var column in Columns)
            column.IsSelected = true;
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var column in Columns)
            column.IsSelected = false;
    }
}

public sealed partial class CsvColumnItem : ObservableObject
{
    /// <summary>Stable column identifier — used as the CSV header and persisted selection.</summary>
    public string Name { get; }

    /// <summary>Localized text shown in the picker.</summary>
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    public CsvColumnItem(string name, string label, bool isSelected)
    {
        Name = name;
        Label = label;
        _isSelected = isSelected;
    }
}
