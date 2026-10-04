namespace JoinFS.UI.Models;

public enum ConnectionState { Disconnected, Connecting, Connected }

public enum HubStatus { Online, Password, Offline, Global }

/// <summary>The sidebar tabs, in sidebar order. The README's ids are home|network|session|aircraft|objects|models|flightplan|recorder|chat|log|settings.</summary>
public enum TabId { Home, Network, Session, Aircraft, Objects, Models, FlightPlan, Recorder, Chat, Monitor, Settings }

public sealed record HubInfo(
    string Name, HubStatus Status, int Users, int Aircraft, string Version,
    string About, string Voice, string NextEvent, string Address);

/// <summary>A hub the user can pick from the strip. <see cref="BuiltIn"/> entries (the Global directory) cannot be removed.</summary>
public sealed record AddressBookEntry(string Name, string Address, bool BuiltIn = false, bool RequiresPassword = false);

/// <summary>One row of the Session table: a user connected to the network, or this node itself.</summary>
/// <param name="Id">Names the user to the service, for the settings and actions of the row. Opaque to the UI.</param>
/// <param name="Connected">"Yes" (direct), "Route" (through another user) or "No".</param>
/// <param name="Protocol">How the link is spoken: "JFP2", "Legacy" or "Pending". Empty for this node, which has no link.</param>
public sealed record PeerInfo(
    string Id, string Nick, string Callsign, string Connected, int LatencyMs, int Aircraft, int Objects,
    string Simulator, string Version, string Protocol, int Port, bool IsMe = false)
{
    public const string LegacyProtocol = "Legacy";

    public bool IsLegacy => Protocol == LegacyProtocol;
}

/// <summary>What the user has set for one other user (the old PermissionsForm, plus Save and Ignore).</summary>
public sealed record PeerSettings(bool CockpitEntry, bool HandOverControls, bool MultipleObjects, bool IsSaved, bool IsIgnored);

/// <summary>How far an aircraft got in the simulator: shows in the distance column.</summary>
public enum AircraftLinkState { Pending, Created, Failed }

/// <summary>What can be done with one aircraft right now. The old context menu enabled each item only in certain cases.</summary>
[Flags]
public enum AircraftActions
{
    None = 0,
    Record = 1,
    Ignore = 2,
    Follow = 4,
    EnterCockpit = 8,
    Track = 16,
    CopyWeather = 32,
    Substitute = 64,
    ExplainMatch = 128,
    FlightPlan = 256,
    Variables = 512,
    AdjustHeight = 1024,
    All = Record | Ignore | Follow | EnterCockpit | Track | CopyWeather | Substitute | ExplainMatch | FlightPlan | Variables | AdjustHeight,
}

/// <summary>One row of the Aircraft table. Values that may not be known (no user aircraft to measure from, no position yet) are null.</summary>
/// <param name="Id">Names the aircraft to the service. Opaque to the UI.</param>
/// <param name="Owner">The pilot, marked "(R)" for a recorded aircraft and "(A)" for an AI aircraft.</param>
/// <param name="Model">The model shown, marked "(S)" substituted, "(A)" automatic, "(D)" default or "(AI)".</param>
public sealed record AircraftInfo(
    string Id, string Callsign, string Owner,
    double? DistanceNm, int? Heading, int? AltitudeFt, double SpeedKnots, string Model,
    int? Bearing, string Squawk, string Com1, string Com2, string Simulator, string OriginalModel, string FlightPlan, string Remarks,
    AircraftLinkState Link, bool Recording, bool Ignored, bool Tracked, AircraftActions Can);

/// <summary>One row of the Objects table: a scenery or shared object, or (grouped by model) all the objects of one owner and model.</summary>
/// <param name="Id">Names the row to the service. Opaque to the UI.</param>
/// <param name="Model">The model as shown, marked "(S)", "(A)" or "(D)" for objects of the network.</param>
/// <param name="OriginalModel">The model the owner has, which the broadcast and ignore settings are kept by.</param>
/// <param name="Count">How many objects the row stands for: 1, unless grouped by model.</param>
/// <param name="Broadcast">Broadcast: this object, or for a group whether its model is.</param>
/// <param name="ModelBroadcast">Every object of this model is broadcast.</param>
/// <param name="CanBroadcast">Only your own objects can be broadcast, not ones already on the network.</param>
/// <param name="CanIgnore">Only objects of the network can be ignored.</param>
public sealed record ObjectInfo(
    string Id, string Owner, string Model, string OriginalModel, int Count, int? Bearing, double? DistanceNm,
    bool Broadcast, bool IgnoreOwner, bool IgnoreModel, bool ModelBroadcast, bool CanBroadcast, bool CanIgnore, bool CanSubstitute);

public sealed record ModelRule(string Original, string Substitute, bool IsDefault);

public sealed record VariableAssignment(string Model, IReadOnlyList<string> Files)
{
    public string FilesText => string.Join(", ", Files);
}

public sealed record ChatMessage(string From, string Text);

public sealed record RecordedAircraft(string Callsign, string Model);

public sealed record ExplainRow(string Attribute, string Requested, string Matched);

public sealed record FlightPlanData(
    string Callsign, string Type, string Rules, string From, string To, string Altitude, string Route, string Remarks);

public sealed record UpdateInfo(string Version, string Url);
