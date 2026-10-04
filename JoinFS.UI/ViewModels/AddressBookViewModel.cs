using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels;

public sealed partial class AddressBookRow : ObservableObject
{
    private readonly Action<AddressBookRow> _remove;

    internal AddressBookRow(AddressBookEntry entry, Action<AddressBookRow> remove)
    {
        Entry = entry;
        _remove = remove;
    }

    public AddressBookEntry Entry { get; }
    public string Name => Entry.Name;
    public string Address => Entry.Address;
    public bool IsBuiltIn => Entry.BuiltIn;
    public bool CanRemove => !Entry.BuiltIn;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove() => _remove(this);
}

/// <summary>
/// The hubs offered by the picker in the strip, edited from Settings → Address Book.
/// The built-in Global entry cannot be removed.
/// </summary>
public sealed partial class AddressBookViewModel : ObservableObject
{
    private readonly IAddressBookStore _store;

    public AddressBookViewModel(IAddressBookStore store)
    {
        _store = store;
        (IReadOnlyList<AddressBookEntry> entries, string? selectedName) = store.Load();
        foreach (AddressBookEntry entry in entries)
            Entries.Add(new AddressBookRow(entry, Remove));
        _selected = Entries.FirstOrDefault(r => r.Name == selectedName) ?? Entries.FirstOrDefault();
    }

    public ObservableCollection<AddressBookRow> Entries { get; } = [];

    private bool _reloading;

    /// <summary>
    /// Reads the book from the store again, for when it was changed elsewhere (Save in the Session tab). Keeps the picked hub if it is still there.
    /// Does not write anything back.
    /// </summary>
    public void Reload()
    {
        _reloading = true;
        try
        {
            string? picked = EffectiveSelection?.Name;
            (IReadOnlyList<AddressBookEntry> entries, string? storedSelection) = _store.Load();

            Entries.Clear();
            foreach (AddressBookEntry entry in entries)
                Entries.Add(new AddressBookRow(entry, Remove));
            AddressBookRow? row = Entries.FirstOrDefault(r => r.Name == picked) ?? Entries.FirstOrDefault(r => r.Name == storedSelection) ?? Entries.FirstOrDefault();

            // While a joined hub is shown as the label, the real pick waits underneath it.
            if (TransientLabel is not null)
                _stashed = row;
            else
                Selected = row;
        }
        finally
        {
            _reloading = false;
        }
    }

    /// <summary>The hub the Network button joins. Null only when the book is empty.</summary>
    [ObservableProperty]
    private AddressBookRow? _selected;

    partial void OnSelectedChanged(AddressBookRow? value)
    {
        if (_swapping)
            return;

        // Picking from the list takes over from the label.
        if (value is not null && TransientLabel is not null)
        {
            _stashed = null;
            TransientLabel = null;
        }
        Persist();
    }

    // ---- the joined hub that is not in the book ----

    private AddressBookRow? _stashed;
    private bool _swapping;

    /// <summary>
    /// The name of the hub that was joined from the directory when it is not in the address book, shown as the picker's current text.
    /// It is not added to the list. Null when the picker shows its own pick.
    /// </summary>
    [ObservableProperty]
    private string? _transientLabel;

    /// <summary>What the Network button joins: the pick, or the pick that waits under the label.</summary>
    public AddressBookRow? EffectiveSelection => Selected ?? _stashed;

    /// <summary>Shows <paramref name="label"/> as the picker's text. The pick is kept and comes back with <see cref="ClearTransient"/>.</summary>
    public void ShowTransient(string label)
    {
        if (TransientLabel is null)
            _stashed = Selected;

        _swapping = true;
        try
        {
            Selected = null;
        }
        finally
        {
            _swapping = false;
        }
        TransientLabel = label;
    }

    /// <summary>Goes back to the pick. Does nothing when no label is showing.</summary>
    public void ClearTransient()
    {
        if (TransientLabel is null)
            return;

        AddressBookRow? stashed = _stashed;
        _stashed = null;
        TransientLabel = null;

        if (Selected is null && stashed is not null)
        {
            _swapping = true;
            try
            {
                Selected = stashed;
            }
            finally
            {
                _swapping = false;
            }
        }
    }

    // The two draft fields of the "add" row.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newName = "";

    [ObservableProperty]
    private string _newAddress = "";

    private bool CanAdd => !string.IsNullOrWhiteSpace(NewName);

    /// <summary>Appends the drafted entry, then clears the draft. Does nothing while the name is blank.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        Entries.Add(new AddressBookRow(new AddressBookEntry(NewName.Trim(), NewAddress.Trim()), Remove));
        NewName = "";
        NewAddress = "";
        Persist();
    }

    public bool Select(string name)
    {
        AddressBookRow? row = Entries.FirstOrDefault(r => r.Name == name);
        if (row is null)
            return false;
        Selected = row;
        return true;
    }

    private void Remove(AddressBookRow row)
    {
        if (!row.CanRemove)
            return;
        // The pick under a label may be the one removed; fall back to the first entry, as for a visible pick.
        if (_stashed == row)
            _stashed = null;
        bool wasSelected = Selected == row;
        Entries.Remove(row);
        if (wasSelected)
            Selected = Entries.FirstOrDefault();
        else
            Persist();
    }

    private void Persist()
    {
        if (!_reloading)
            Save();
    }

    private void Save() => _store.Save([.. Entries.Select(r => r.Entry)], Selected?.Name);
}
