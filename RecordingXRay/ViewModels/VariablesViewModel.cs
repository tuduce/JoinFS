using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecordingXRay.ViewModels;

/// <param name="Number">The value as a number for sorting, or null for text values.</param>
public sealed record VariableRow(uint Id, string Name, string Value, double? Number)
{
    public string IdText => Id.ToString(CultureInfo.InvariantCulture);
}

public enum VariableSort
{
    Id,
    Name,
    Value,
}

/// <summary>The Id | Name | Value table for an Integer / Float / String8 variables frame.</summary>
public sealed partial class VariablesViewModel : ObservableObject
{
    private readonly IReadOnlyList<VariableRow> all;

    public VariablesViewModel(IReadOnlyList<VariableRow> rows)
    {
        all = rows;
        View = Build();
    }

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdHeader), nameof(NameHeader), nameof(ValueHeader))]
    private VariableSort sort = VariableSort.Id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdHeader), nameof(NameHeader), nameof(ValueHeader))]
    private bool descending;

    /// <summary>The rows after filtering and sorting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private IReadOnlyList<VariableRow> view;

    public string CountText => View.Count == all.Count
        ? $"{all.Count.ToString("N0", CultureInfo.InvariantCulture)} variables"
        : $"{View.Count.ToString("N0", CultureInfo.InvariantCulture)} of {all.Count.ToString("N0", CultureInfo.InvariantCulture)} variables";

    public string IdHeader => Header("ID", VariableSort.Id);

    public string NameHeader => Header("NAME", VariableSort.Name);

    public string ValueHeader => Header("VALUE", VariableSort.Value);

    partial void OnFilterTextChanged(string value) => View = Build();

    partial void OnSortChanged(VariableSort value) => View = Build();

    partial void OnDescendingChanged(bool value) => View = Build();

    [RelayCommand]
    private void SortBy(VariableSort column)
    {
        if (Sort == column)
        {
            Descending = !Descending;
        }
        else
        {
            Sort = column;
            Descending = false;
        }
    }

    private string Header(string text, VariableSort column) =>
        Sort == column ? text + (Descending ? " ▼" : " ▲") : text;

    private IReadOnlyList<VariableRow> Build()
    {
        string text = FilterText.Trim();
        IEnumerable<VariableRow> rows = text.Length == 0
            ? all
            : all.Where(row =>
                row.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || row.IdText.Contains(text, StringComparison.Ordinal)
                || row.Value.Contains(text, StringComparison.OrdinalIgnoreCase));

        IOrderedEnumerable<VariableRow> ordered = Sort switch
        {
            VariableSort.Name => Order(rows, row => row.Name, StringComparer.OrdinalIgnoreCase),
            VariableSort.Value => rows.OrderBy(row => row.Number is null).ThenBy(row => row.Number).ThenBy(row => row.Value, StringComparer.OrdinalIgnoreCase),
            _ => Order(rows, row => row.Id, Comparer<uint>.Default),
        };

        if (Sort == VariableSort.Value && Descending)
        {
            ordered = rows.OrderBy(row => row.Number is null).ThenByDescending(row => row.Number).ThenByDescending(row => row.Value, StringComparer.OrdinalIgnoreCase);
        }

        return ordered.ToList();

        IOrderedEnumerable<VariableRow> Order<TKey>(IEnumerable<VariableRow> source, Func<VariableRow, TKey> key, IComparer<TKey> comparer) =>
            Descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);
    }
}
