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
            string? picked = Selected?.Name;
            (IReadOnlyList<AddressBookEntry> entries, string? storedSelection) = _store.Load();

            Entries.Clear();
            foreach (AddressBookEntry entry in entries)
                Entries.Add(new AddressBookRow(entry, Remove));
            Selected = Entries.FirstOrDefault(r => r.Name == picked) ?? Entries.FirstOrDefault(r => r.Name == storedSelection) ?? Entries.FirstOrDefault();
        }
        finally
        {
            _reloading = false;
        }
    }

    /// <summary>The hub the Network button joins. Null only when the book is empty.</summary>
    [ObservableProperty]
    private AddressBookRow? _selected;

    partial void OnSelectedChanged(AddressBookRow? value) => Persist();

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
