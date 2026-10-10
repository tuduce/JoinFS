using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;

namespace JoinFS.UI.ViewModels;

/// <summary>The words one connector shows, per state (README "Connection state machines").</summary>
public sealed record ConnectionLabels(
    string Disconnected, string Connecting, string Connected,
    string ActionDisconnected, string ActionConnecting, string ActionConnected)
{
    // Properties, not fields: the words are looked up when asked for, so they follow the language.
    public static ConnectionLabels Simulator => new(Loc.T("Disconnected"), Loc.T("Connecting…"), Loc.T("Connected"), Loc.T("Connect"), Loc.T("Connecting…"), Loc.T("Disconnect"));
    public static ConnectionLabels Network => new(Loc.T("Disconnected"), Loc.T("Connecting…"), Loc.T("Connected"), Loc.T("Join"), Loc.T("Connecting…"), Loc.T("Disconnect"));
    public static ConnectionLabels FlightPlan => new(Loc.T("Not loaded"), Loc.T("Fetching…"), Loc.T("Loaded"), Loc.T("Fetch"), Loc.T("Fetching…"), Loc.T("Loaded"));
}

/// <summary>
/// One of the three independent <c>disconnected → connecting → connected</c> machines (Simulator, Network, Flight Plan).
/// Clicking while connecting does nothing (or, when the state is observed from the live app, gives up); clicking while connected goes
/// straight to disconnected.
/// </summary>
public sealed partial class ConnectionViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task> _connect;
    private readonly Func<Task> _disconnect;
    private readonly Func<Task>? _requestConnect;
    private readonly ConnectionLabels _labels;
    private readonly bool _observed;

    /// <param name="observed">
    /// True when the real state lives elsewhere (the live app): <see cref="ConnectAsync"/> and <see cref="DisconnectAsync"/> only ask for the
    /// change, and the state is whatever <see cref="Sync"/> was last given. False when this machine owns the state.
    /// </param>
    /// <param name="connect">The work of connecting. Throw <see cref="OperationCanceledException"/> to end back at disconnected.</param>
    /// <param name="disconnect">The work of disconnecting.</param>
    /// <param name="requestConnect">
    /// What a click does while disconnected, when that is more than connecting (the Network button asks for a hub password first).
    /// It is expected to call <see cref="ConnectAsync"/> itself when ready.
    /// </param>
    public ConnectionViewModel(ConnectionLabels labels, Func<CancellationToken, Task> connect, Func<Task>? disconnect = null, Func<Task>? requestConnect = null, bool observed = false)
    {
        _observed = observed;
        _labels = labels;
        _connect = connect;
        _disconnect = disconnect ?? (() => Task.CompletedTask);
        _requestConnect = requestConnect;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected), nameof(IsConnecting), nameof(IsConnected), nameof(StateLabel), nameof(ActionLabel))]
    private ConnectionState _state = ConnectionState.Disconnected;

    /// <summary>The tooltip that names the shortcut of this connector, or null when its shortcut is off.</summary>
    [ObservableProperty]
    private string? _shortcutHint;

    /// <summary>Why the last attempt failed, or null. The design has no error UI yet.</summary>
    [ObservableProperty]
    private string? _error;

    public bool IsDisconnected => State == ConnectionState.Disconnected;
    public bool IsConnecting => State == ConnectionState.Connecting;
    public bool IsConnected => State == ConnectionState.Connected;

    public string StateLabel => State switch
    {
        ConnectionState.Connecting => _labels.Connecting,
        ConnectionState.Connected => _labels.Connected,
        _ => _labels.Disconnected,
    };

    public string ActionLabel => State switch
    {
        ConnectionState.Connecting => _labels.ActionConnecting,
        ConnectionState.Connected => _labels.ActionConnected,
        _ => _labels.ActionDisconnected,
    };

    [RelayCommand]
    public async Task ToggleAsync()
    {
        switch (State)
        {
            case ConnectionState.Connecting:
                // On a machine that owns its state a click while connecting does nothing. A live connection can hang, so there a click gives up.
                if (_observed)
                    await DisconnectAsync();
                return;
            case ConnectionState.Connected:
                await DisconnectAsync();
                return;
            default:
                await (_requestConnect?.Invoke() ?? ConnectAsync());
                return;
        }
    }

    public async Task ConnectAsync()
    {
        if (State == ConnectionState.Connecting)
            return;

        Error = null;

        if (_observed)
        {
            // Ask, and leave the state to Sync: the request may be refused or late, and the sim may not answer at all.
            try
            {
                await _connect(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            return;
        }

        State = ConnectionState.Connecting;
        try
        {
            await _connect(CancellationToken.None);
            State = ConnectionState.Connected;
        }
        catch (OperationCanceledException)
        {
            State = ConnectionState.Disconnected;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            State = ConnectionState.Disconnected;
        }
    }

    public async Task DisconnectAsync()
    {
        if (!_observed)
            State = ConnectionState.Disconnected;
        await _disconnect();
    }

    /// <summary>Takes the real state, for a machine that is <c>observed</c>. Does nothing for one that owns its state.</summary>
    public void Sync(ConnectionState real)
    {
        if (_observed)
            State = real;
    }

    /// <summary>Records a change that happened elsewhere, e.g. a flight plan imported from its own tab.</summary>
    public void SetState(ConnectionState state) => State = state;
}
