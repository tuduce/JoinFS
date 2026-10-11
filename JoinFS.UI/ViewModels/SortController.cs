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
/// The tables' shared sort rule: clicking the active column flips its direction, clicking another sorts it ascending, unless the column
/// was added with <c>highestFirst</c> (a count or a version, where the biggest is what one looks for): then it sorts descending first.
/// Strings compare like the prototype's <c>localeCompare</c>; numbers compare numerically; the sort is stable.
/// </summary>
public sealed class SortController<T>
{
    private readonly Action _changed;
    private readonly Dictionary<string, (SortColumn Column, Func<T, object> Selector, bool HighestFirst)> _columns = [];

    public SortController(Action changed, string? initialKey = null)
    {
        _changed = changed;
        Key = initialKey;
    }

    public string? Key { get; private set; }
    public bool Descending { get; private set; }

    /// <param name="highestFirst">The first click on this column sorts from the highest to the lowest.</param>
    public SortColumn Add(string key, string label, Func<T, object> selector, bool highestFirst = false)
    {
        SortColumn column = new(key, label, Toggle);
        _columns[key] = (column, selector, highestFirst);
        if (key == Key)
            Descending = highestFirst;
        column.SetIndicator(key == Key ? Descending : null);
        return column;
    }

    public void Toggle(string key)
    {
        Descending = Key == key ? !Descending : _columns.TryGetValue(key, out var clicked) && clicked.HighestFirst;
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

/// <summary>
/// A version such as "26.6.0" as something to sort: its numbers are compared one by one, so 26.10.0 comes after 26.9.0 and 26.6.0 after
/// 26.2.0, which comparing the text does not give. A text that starts with no number (empty, a dash) is below every version.
/// </summary>
public readonly record struct VersionKey : IComparable<VersionKey>, IComparable
{
    private readonly int[] _numbers;

    public VersionKey(string? text) => _numbers = Parse(text);

    public int CompareTo(VersionKey other)
    {
        int[] a = _numbers ?? [], b = other._numbers ?? [];
        // no version at all is below any version, even 0
        if (a.Length == 0 || b.Length == 0)
            return a.Length == b.Length ? 0 : a.Length == 0 ? -1 : 1;

        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y)
                return x.CompareTo(y);
        }
        return 0;
    }

    public int CompareTo(object? obj) => obj is VersionKey other ? CompareTo(other) : 1;

    public bool Equals(VersionKey other) => CompareTo(other) == 0;

    public override int GetHashCode() => (_numbers ?? []).Aggregate(17, (hash, n) => hash * 31 + n);

    private static int[] Parse(string? text)
    {
        List<int> numbers = [];
        foreach (string part in (text ?? "").Trim().Split('.'))
        {
            int digits = 0;
            while (digits < part.Length && char.IsAsciiDigit(part[digits]))
                digits++;
            // the first part must be a number, or there is no version; a later one that is not (a "-beta") ends it
            if (digits == 0 || !int.TryParse(part.AsSpan(0, digits), out int number))
                break;
            numbers.Add(number);
            if (digits < part.Length)
                break;
        }
        return [.. numbers];
    }
}
