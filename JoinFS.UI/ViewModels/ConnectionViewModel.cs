using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;

namespace JoinFS.UI.ViewModels;

/// <summary>
/// The words one connector shows, per state (README "Connection state machines"), and what it says when something goes wrong.
/// <see cref="Failed"/>, <see cref="Lost"/> and <see cref="Slow"/> are whole sentences, each naming its subject; null says nothing.
/// </summary>
public sealed record ConnectionLabels(
    string Disconnected, string Connecting, string Connected,
    string ActionDisconnected, string ActionConnecting, string ActionConnected)
{
    /// <summary>What a click does while connecting, on a connection that can be given up.</summary>
    public string Cancel => Loc.T("Cancel");

    /// <summary>An attempt ended by itself, without the user asking.</summary>
    public string? Failed { get; init; }

    /// <summary>A connection that was up ended by itself.</summary>
    public string? Lost { get; init; }

    /// <summary>An attempt that has had no answer for <see cref="SlowAfter"/>: still going, but the user may want to give up.</summary>
    public string? Slow { get; init; }

    public TimeSpan SlowAfter { get; init; } = TimeSpan.FromSeconds(15);

    // Properties, not fields: the words are looked up when asked for, so they follow the language.
    public static ConnectionLabels Simulator => new(Loc.T("Disconnected"), Loc.T("Connecting…"), Loc.T("Connected"), Loc.T("Connect"), Loc.T("Connecting…"), Loc.T("Disconnect"))
    {
        Failed = Loc.T("Simulator not found. Start it, then click Simulator."),
        Lost = Loc.T("Simulator connection lost."),
    };

    public static ConnectionLabels Network => new(Loc.T("Disconnected"), Loc.T("Connecting…"), Loc.T("Connected"), Loc.T("Join"), Loc.T("Connecting…"), Loc.T("Disconnect"))
    {
        Failed = Loc.T("Could not join the hub. Check its address and your connection."),
        Lost = Loc.T("Network connection lost."),
        Slow = Loc.T("No answer from the hub yet. Click Network to cancel."),
    };

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
    private readonly Func<bool>? _failureIsExpected;
    private readonly TimeProvider _time;

    // What the strip says besides the state (see Detail). Only a machine that is observed can tell an end it did not ask for.
    private bool _endAsked;
    private string? _outcome;
    private bool _slow;
    private bool _dismissed;
    private DateTimeOffset _connectingSince;
    private string? _pending;
    private DateTimeOffset _pendingSince;

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
    /// <param name="failureIsExpected">
    /// Asked when an observed machine ends on its own: true when that is part of a conversation and not a failure (the Network button
    /// leaves a session that asks for a password, then joins again).
    /// </param>
    /// <param name="time">The clock for <see cref="ConnectionLabels.SlowAfter"/>; the system's unless a test gives its own.</param>
    public ConnectionViewModel(ConnectionLabels labels, Func<CancellationToken, Task> connect, Func<Task>? disconnect = null, Func<Task>? requestConnect = null, bool observed = false,
        Func<bool>? failureIsExpected = null, TimeProvider? time = null)
    {
        _failureIsExpected = failureIsExpected;
        _time = time ?? TimeProvider.System;
        _observed = observed;
        _labels = labels;
        _connect = connect;
        _disconnect = disconnect ?? (() => Task.CompletedTask);
        _requestConnect = requestConnect;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected), nameof(IsConnecting), nameof(IsConnected), nameof(StateLabel), nameof(ActionLabel), nameof(CanCancel))]
    private ConnectionState _state = ConnectionState.Disconnected;

    /// <summary>The tooltip that names the shortcut of this connector, or null when its shortcut is off.</summary>
    [ObservableProperty]
    private string? _shortcutHint;

    /// <summary>Why the last attempt failed, when connecting threw; or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(HasDetail), nameof(DetailIsProblem))]
    private string? _error;

    /// <summary>
    /// What the user should know besides the state label, as a whole sentence: why an attempt ended, that a connection was lost, or that
    /// an attempt is taking long. Null when there is nothing to say. It goes when the user tries again, when the connection comes up,
    /// or when it is dismissed.
    /// </summary>
    public string? Detail => _dismissed ? null : Error ?? _outcome ?? (_slow ? _labels.Slow : null);

    public bool HasDetail => Detail is not null;

    /// <summary>True for something that went wrong; false for "still trying".</summary>
    public bool DetailIsProblem => Detail is not null && (Error is not null || _outcome is not null);

    /// <summary>A click right now gives the attempt up (only a machine that is observed can).</summary>
    public bool CanCancel => IsConnecting && _observed;

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
        ConnectionState.Connecting => _observed ? _labels.Cancel : _labels.ActionConnecting,
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
        _endAsked = false;
        _pending = null;
        SetDetail(outcome: null, slow: false);

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
        // The user asked, so what follows is no failure, and an old one is answered.
        _endAsked = true;
        _pending = null;
        Error = null;
        SetDetail(outcome: null, slow: false);
        if (!_observed)
            State = ConnectionState.Disconnected;
        await _disconnect();
    }

    /// <summary>Takes the real state, for a machine that is <c>observed</c>. Does nothing for one that owns its state.</summary>
    public void Sync(ConnectionState real)
    {
        if (!_observed)
            return;

        ConnectionState before = State;
        State = real;
        if (real != before)
            Moved(before, real);

        DateTimeOffset now = _time.GetUtcNow();
        if (real == ConnectionState.Connecting && !_slow && _labels.Slow is not null && now - _connectingSince >= _labels.SlowAfter)
            SetDetail(_outcome, slow: true);
        else if (real == ConnectionState.Disconnected && _pending is not null && now - _pendingSince >= SettleAfter)
        {
            string outcome = _pending;
            _pending = null;
            SetDetail(outcome, slow: false);
        }
    }

    /// <summary>How long an end nobody asked for must last before it is told.</summary>
    private static readonly TimeSpan SettleAfter = TimeSpan.FromSeconds(1);

    // The real state changed: say what an end that nobody asked for means.
    private void Moved(ConnectionState from, ConnectionState to)
    {
        _pending = null;
        if (to != ConnectionState.Disconnected)
        {
            if (to == ConnectionState.Connecting)
                _connectingSince = _time.GetUtcNow();
            // a new attempt, or a connection that is up: whatever was said before is old
            _endAsked = false;
            Error = null;
            SetDetail(outcome: null, slow: false);
            return;
        }

        bool asked = _endAsked;
        _endAsked = false;
        SetDetail(outcome: null, slow: false);
        if (!asked && !(_failureIsExpected?.Invoke() ?? false))
        {
            // Said only if it lasts (see Sync): a retry passes through "disconnected" for a moment, and that is no failure.
            _pending = from == ConnectionState.Connecting ? _labels.Failed : from == ConnectionState.Connected ? _labels.Lost : null;
            _pendingSince = _time.GetUtcNow();
        }
    }

    private void SetDetail(string? outcome, bool slow)
    {
        _outcome = outcome;
        _slow = slow;
        _dismissed = false;
        RaiseDetailChanged();
    }

    /// <summary>The user has read <see cref="Detail"/>; hides it until something new happens.</summary>
    public void DismissDetail()
    {
        _dismissed = true;
        RaiseDetailChanged();
    }

    private void RaiseDetailChanged()
    {
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(DetailIsProblem));
    }

    /// <summary>Records a change that happened elsewhere, e.g. a flight plan imported from its own tab.</summary>
    public void SetState(ConnectionState state) => State = state;
}
