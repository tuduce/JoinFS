namespace JoinFS.UI.Models;

public enum ConnectionState { Disconnected, Connecting, Connected }

public enum HubStatus { Online, Password, Offline, Global }

/// <summary>The sidebar tabs, in sidebar order. The README's ids are home|network|session|aircraft|objects|models|flightplan|recorder|chat|log|settings.</summary>
public enum TabId { Home, Network, Session, Aircraft, Objects, Models, FlightPlan, Recorder, Chat, Monitor, Settings }

/// <param name="Id">Names the hub to the service. Opaque to the UI.</param>
/// <param name="Ignored">The user has ignored this hub.</param>
/// <param name="Saved">The hub is in the address book.</param>
/// <param name="CanJoin">An online hub with an address. This node's own hub (in hub mode) has none.</param>
/// <param name="CanIgnore">This node's own hub cannot be ignored.</param>
public sealed record HubInfo(
    string Id, string Name, HubStatus Status, int Users, int Aircraft, string Version,
    string About, string Voice, string NextEvent, string Address,
    bool Ignored = false, bool Saved = false, bool CanJoin = true, bool CanIgnore = true);

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

/// <param name="Original">The model that is replaced.</param>
/// <param name="Substitute">The model that stands in for it, as shown.</param>
/// <param name="IsDefault">One of the built-in "default by kind" rules: it can be edited but not removed.</param>
public sealed record ModelRule(string Original, string Substitute, bool IsDefault);

/// <summary>
/// The model a Substitute action is about, as its owner has it. Where it was seen decides what is changed: the match of an aircraft
/// or object of the network, or the masquerade of your own aircraft, which is what the others see of it.
/// </summary>
/// <param name="Model">The model title.</param>
/// <param name="Livery">The livery (FS2024). Empty when not known; the service then takes the one of the current match.</param>
/// <param name="TypeRole">The kind of aircraft, as the matching numbers it. Zero when not known; the service then looks it up.</param>
/// <param name="IsMasquerade">Changing it changes what others see of your own aircraft, not what you see of theirs.</param>
public sealed record ModelTarget(string Model, string Livery = "", int TypeRole = 0, bool IsMasquerade = false);

/// <summary>The height adjustment of a model: a vertical offset, kept per model and applied to every aircraft that shows it.</summary>
/// <param name="Model">The model the adjustment is kept for, as it is named.</param>
/// <param name="Centimetres">Zero means off.</param>
public sealed record HeightAdjustment(string Model, int Centimetres);

/// <summary>A model picked by its type and variation. Together they name one model.</summary>
public sealed record ModelChoice(string Type, string Variation);

public sealed record VariableAssignment(string Model, IReadOnlyList<string> Files)
{
    public string FilesText => string.Join(", ", Files);
}

public sealed record ChatMessage(string From, string Text);

public sealed record RecordedAircraft(string Callsign, string Model);

/// <param name="Matched">The matched value, followed by "(+N)" when the attribute added to the score.</param>
/// <param name="Decisive">This attribute decided the match; the row is highlighted.</param>
public sealed record ExplainRow(string Attribute, string Requested, string Matched, bool Decisive = false);

/// <summary>
/// How the model of an aircraft was chosen: what was asked for against what was matched, and the steps tried. Plain text, ready to show.
/// </summary>
/// <param name="Callsign">The aircraft.</param>
/// <param name="Outcome">The result in one line, or that there is no match yet.</param>
/// <param name="Note">A warning about the matched model (its ICAO type was guessed or corrected), or null.</param>
/// <param name="Steps">What each tier tried, in order, and the other candidates considered.</param>
/// <param name="Source">Where the list of known models comes from.</param>
/// <param name="Report">All of it as a Markdown report: what "Copy to clipboard" copies and the debug bundle holds.</param>
public sealed record MatchExplanation(
    string Callsign, string Outcome, string? Note, IReadOnlyList<ExplainRow> Rows, IReadOnlyList<string> Steps, string Source, string Report);

public sealed record FlightPlanData(
    string Callsign, string Type, string Rules, string From, string To, string Altitude, string Route, string Remarks);

public sealed record UpdateInfo(string Version, string Url);
