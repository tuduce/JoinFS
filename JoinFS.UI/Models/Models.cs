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

public sealed record PeerInfo(
    string Nick, string Callsign, bool Connected, int LatencyMs, int Aircraft, string Simulator, string Protocol)
{
    public const string LegacyProtocol = "Legacy";

    public bool IsLegacy => Protocol == LegacyProtocol;

    // The README derives both from the protocol until real session data is wired in.
    public string Version => IsLegacy ? "18.2.4" : "26.4.0";
    public int Port => 6809 + Nick.Length % 40;
}

public sealed record AircraftInfo(
    string Callsign, string Owner, double DistanceNm, int Heading, int AltitudeFt, int GroundSpeed, string Model,
    string Squawk, string Com1, string Com2, string Simulator, string OriginalModel, string FlightPlan, string Remarks);

public sealed record ObjectInfo(
    string Owner, string Model, int Count, int Bearing, double DistanceNm,
    bool Broadcast, bool IgnoreOwner, bool IgnoreModel);

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
