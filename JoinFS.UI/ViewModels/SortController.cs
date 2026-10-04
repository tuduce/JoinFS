using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JoinFS.UI.ViewModels;

/// <summary>A sortable column header: shows its label, plus ↑ or ↓ while it is the active sort.</summary>
public sealed partial class SortColumn : ObservableObject
{
    private readonly Action<string> _toggle;

    internal SortColumn(string key, string label, Action<string> toggle)
    {
        Key = key;
        Label = label;
        _toggle = toggle;
        _header = label;
    }

    public string Key { get; }
    public string Label { get; }

    [ObservableProperty]
    private string _header;

    [RelayCommand]
    private void Sort() => _toggle(Key);

    internal void SetIndicator(bool? descending) =>
        Header = descending switch { null => Label, false => Label + " ↑", true => Label + " ↓" };
}

/// <summary>
/// The tables' shared sort rule: clicking the active column flips its direction, clicking another sorts it ascending.
/// Strings compare like the prototype's <c>localeCompare</c>; numbers compare numerically; the sort is stable.
/// </summary>
public sealed class SortController<T>
{
    private readonly Action _changed;
    private readonly Dictionary<string, (SortColumn Column, Func<T, object> Selector)> _columns = [];

    public SortController(Action changed, string? initialKey = null)
    {
        _changed = changed;
        Key = initialKey;
    }

    public string? Key { get; private set; }
    public bool Descending { get; private set; }

    public SortColumn Add(string key, string label, Func<T, object> selector)
    {
        SortColumn column = new(key, label, Toggle);
        _columns[key] = (column, selector);
        column.SetIndicator(key == Key ? false : null);
        return column;
    }

    public void Toggle(string key)
    {
        Descending = Key == key && !Descending;
        Key = key;
        foreach (var (columnKey, entry) in _columns)
            entry.Column.SetIndicator(columnKey == Key ? Descending : null);
        _changed();
    }

    public IEnumerable<T> Apply(IEnumerable<T> items)
    {
        if (Key is null || !_columns.TryGetValue(Key, out var entry))
            return items;

        Func<T, object> selector = entry.Selector;
        return Descending
            ? items.OrderByDescending(selector, SortValueComparer.Instance)
            : items.OrderBy(selector, SortValueComparer.Instance);
    }
}

internal sealed class SortValueComparer : IComparer<object>
{
    public static readonly SortValueComparer Instance = new();

    public int Compare(object? x, object? y) => (x, y) switch
    {
        (string a, string b) => string.Compare(a, b, CultureInfo.CurrentCulture, CompareOptions.None),
        (IComparable a, _) when y is not null && a.GetType() == y.GetType() => a.CompareTo(y),
        _ => string.Compare(x?.ToString(), y?.ToString(), CultureInfo.CurrentCulture, CompareOptions.None),
    };
}
