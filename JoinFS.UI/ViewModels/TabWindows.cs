using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;

namespace JoinFS.UI.ViewModels;

/// <summary>
/// A tab shown in a window of its own, apart from the main window, which can be moved and sized on its own. The main window shows an
/// <see cref="UndockedTabViewModel"/> in its place. Closing the window puts the tab back. Made by <see cref="MainViewModel.Undock"/>.
/// </summary>
public sealed partial class TabWindowViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    internal TabWindowViewModel(MainViewModel main, TabId tab, string label, object content)
    {
        _main = main;
        Tab = tab;
        Label = label;
        Content = content;
        Placeholder = new UndockedTabViewModel(this);
    }

    public TabId Tab { get; }

    /// <summary>The tab's name in the sidebar.</summary>
    public string Label { get; }

    public string Title => Label + " - " + _main.SessionLabel;

    /// <summary>The tab's view model, the same one the main window would show.</summary>
    public object Content { get; }

    /// <summary>What the main window shows while the tab is here.</summary>
    public UndockedTabViewModel Placeholder { get; }

    public bool AlwaysOnTop => _main.Settings.UserInterface.AlwaysOnTop;

    /// <summary>The card this tab opened (Substitute, Explain Match, a password), drawn over this window rather than the main one.</summary>
    public OverlayViewModel? Overlay => _main.OverlayTab == Tab ? _main.Overlay : null;

    public bool IsOverlayOpen => Overlay is not null;

    /// <summary>The window should come to the front (the tab was picked in the sidebar, or Show window was clicked).</summary>
    public event EventHandler? ActivateRequested;

    [RelayCommand]
    private void Activate() => ActivateRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Puts the tab back in the main window and shows it there.</summary>
    [RelayCommand]
    private void Dock() => _main.Dock(Tab, show: true);

    /// <summary>The user closed the window: the tab goes back into the main window, which stays as it is.</summary>
    public void OnClosed() => _main.Dock(Tab, show: false);

    internal void RaiseOverlayChanged()
    {
        OnPropertyChanged(nameof(Overlay));
        OnPropertyChanged(nameof(IsOverlayOpen));
    }

    internal void RaiseAlwaysOnTopChanged() => OnPropertyChanged(nameof(AlwaysOnTop));
}

/// <summary>What the main window shows in place of a tab that is open in a window of its own.</summary>
public sealed class UndockedTabViewModel(TabWindowViewModel window)
{
    public string Message => Loc.F("{0} is open in a separate window.", window.Label);

    public IRelayCommand ShowCommand => window.ActivateCommand;

    public IRelayCommand DockCommand => window.DockCommand;
}

/// <summary>
/// The shell as one tab sees it. A card the tab opens goes over the window that shows the tab: its own window while it is undocked,
/// the main window otherwise.
/// </summary>
internal sealed class TabShell(MainViewModel main, TabId tab) : IShell
{
    public void GoTo(TabId target) => main.GoTo(target);

    public void ShowOverlay(OverlayViewModel overlay) => main.ShowOverlay(overlay, from: tab);

    public Task JoinAsync(AddressBookEntry hub) => main.JoinAsync(hub, from: tab);

    public string? JoinedHubName => main.JoinedHubName;

    public bool OnOwnMesh => main.OnOwnMesh;

    public Task LeaveAsync() => main.LeaveAsync();

    public Task<string?> CreateMeshAsync() => main.CreateMeshAsync();
}
