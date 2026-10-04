using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;

namespace JoinFS.UI.ViewModels;

/// <summary>What a tab or an overlay may ask of the app shell. Keeps the tabs from knowing about each other.</summary>
public interface IShell
{
    /// <summary>Opens <paramref name="tab"/>. Like every sidebar command, it also expands the window.</summary>
    void GoTo(TabId tab);

    /// <summary>Shows <paramref name="overlay"/> over the whole window, replacing any other.</summary>
    void ShowOverlay(OverlayViewModel overlay);

    /// <summary>
    /// Joins <paramref name="hub"/>, asking for its password first when it needs one.
    /// Leaves the current network first if connected.
    /// </summary>
    Task JoinAsync(AddressBookEntry hub);

    /// <summary>Leaves the current network and starts a new mesh. Returns its code.</summary>
    Task<string?> CreateMeshAsync();
}

/// <summary>
/// A modal card. The shell shows one at a time; <see cref="CloseRequested"/> asks it to go away.
/// Which view draws it is decided by the data templates in App.axaml.
/// </summary>
public abstract partial class OverlayViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>False for the first-run card, which has no ✕ and must be completed.</summary>
    public virtual bool IsDismissable => true;

    public event EventHandler? CloseRequested;

    /// <summary>Called as the overlay closes, whichever way it was closed (✕, Cancel, Escape or its own OK).</summary>
    protected virtual void OnClosing()
    {
    }

    [RelayCommand]
    public void Close()
    {
        OnClosing();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>One sidebar row.</summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    private readonly IShell _shell;

    public NavItemViewModel(IShell shell, TabId tab, string label, string iconKey)
    {
        _shell = shell;
        Tab = tab;
        Label = label;
        IconKey = iconKey;
    }

    public TabId Tab { get; }
    public string Label { get; }

    /// <summary>Resource key of the <c>IconData</c> in Styles/Icons.axaml.</summary>
    public string IconKey { get; }

    [ObservableProperty]
    private bool _isActive;

    /// <summary>The red unread dot. Only the Chat row ever sets it.</summary>
    [ObservableProperty]
    private bool _showBadge;

    [RelayCommand]
    private void Select() => _shell.GoTo(Tab);
}

/// <summary>One link in an expanded row's Actions block (Aircraft, Objects). A command that cannot run shows as a disabled link.</summary>
public sealed partial class ActionLink(string label, IRelayCommand command) : ObservableObject
{
    [ObservableProperty]
    private string _label = label;

    public IRelayCommand Command { get; } = command;
}
