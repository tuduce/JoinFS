using JoinFS.Net;
using System;
using System.Collections.Generic;
using System.Net;

namespace JoinFS
{
    // The narrow views of the application that the network session's parts (JoinFS/Session) work
    // through. MainSessionHost implements the app-facing ones over Main; tests implement them with
    // small fakes. See docs/reference/joinfs-architecture.md §7.

    /// <summary>Monitor output (Main.MonitorEvent / Main.MonitorNetwork).</summary>
    public interface ISessionLog
    {
        /// <summary>Always shown.</summary>
        void Event(string text);

        /// <summary>Shown when network monitoring is on.</summary>
        void Network(string text);
    }

    /// <summary>The session's current state, as the parts need it.</summary>
    public interface ISessionState
    {
        NetworkSnapshot Snapshot { get; }

        /// <summary>This node's id (invalid until the public address is known).</summary>
        NodeId LocalId { get; }

        /// <summary>This node's public address is known, so it can take part in a session.</summary>
        bool Ready { get; }

        /// <summary>In a session.</summary>
        bool Connected { get; }
    }

    /// <summary>Turning node ids and address text into endpoints.</summary>
    public interface IEndPointResolver
    {
        /// <summary>Endpoint to reach <paramref name="node"/> on <paramref name="port"/> (LAN address when it shares our NAT).</summary>
        IPEndPoint MakeEndPoint(NodeId node, ushort port);

        /// <summary>"address[:port]" (IP or DNS name) to an endpoint.</summary>
        bool MakeEndPoint(string addressText, ushort port, out IPEndPoint endPoint);
    }

    /// <summary>Who the local user is, and the settings the session uses.</summary>
    public interface ILocalProfile
    {
        Guid Guid { get; }
        uint Uuid { get; }
        string Nickname { get; }
        bool Hub { get; }
        string HubName { get; }
        string HubAbout { get; }
        string HubVoip { get; }
        string HubEvent { get; }
        string HubDomain { get; }
        bool Atc { get; }
        string AtcAirport { get; }
        int AtcLevel { get; }
        int AtcFrequency { get; }
        int ActivityCircle { get; }
        /// <summary>Last known public IP (text).</summary>
        string MyIp { get; }
        /// <summary>Accept additional (non-user) objects from every node.</summary>
        bool MultipleObjects { get; }
        bool ShareCockpitEveryone { get; }
        bool LowBandwidth { get; }
        bool ElevationCorrection { get; }
        /// <summary>Whazzup publishing of global (all hubs') users.</summary>
        bool PublishWhazzup { get; }
        string Password { get; }
        string StoragePath { get; }
        string DocumentsPath { get; }
        List<AddressBook.AddressBookEntry> AddressBook { get; }
    }

    /// <summary>Per-node choices the user made (Log): ignoring, cockpit sharing, extra objects.</summary>
    public interface IPeerPolicy
    {
        bool IgnoreNode(IPAddress address);
        bool IgnoreNode(ref Guid guid);
        bool ShareCockpit(NodeId nuid);
        bool MultipleObjects(NodeId nuid);
    }

    /// <summary>Told when a node starts or stops controlling (Euroscope).</summary>
    public interface IAtcListener
    {
        void AddAtc(string airport, int level, int frequency);
        void RemoveAtc(string airport, int level, int frequency);
    }

    /// <summary>
    /// What network data does to the simulator and the recorder. Every method is a no-op when no
    /// simulator is running.
    /// </summary>
    public interface ISimSink
    {
        /// <summary>A simulator is running.</summary>
        bool Available { get; }

        /// <summary>The user's aircraft (for shared-cockpit messages), if there is one.</summary>
        bool TryGetOwnAircraft(out NodeId owner, out uint netId);

        /// <summary>The weather observation to answer requests with (null when there is none).</summary>
        string CurrentMetar { get; }

        /// <summary>An existing object's identity changed: update it (and respawn it if its model changed).</summary>
        void ChangeIdentity(NodeId owner, in IdentityUpdate identity);

        /// <summary>Create or move a remote aircraft (and record it if it's being recorded).</summary>
        void UpdateAircraft(NodeId owner, in IdentityUpdate identity, bool user, string nickname, in PositionUpdate position);

        /// <summary>Move our own aircraft (shared cockpit: someone else has the flight controls).</summary>
        void UpdateOwnAircraft(in PositionUpdate position);

        /// <summary>Create or move a remote non-aircraft object (and record it if it's being recorded).</summary>
        void UpdateObject(NodeId owner, in IdentityUpdate identity, in ObjectPositionUpdate position);

        void UpdateVariables(NodeId owner, uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s, bool record);

        void ApplyEvent(NodeId owner, uint netId, uint eventId, uint data, bool flightControls, bool record);

        void RemoveObject(NodeId owner, uint netId);

        void RemoveObjects(NodeId owner);

        void UpdateUserFlightPlan(in FlightPlanUpdate flightPlan);

        void UpdateAircraftFlightPlan(NodeId owner, uint netId, in FlightPlanUpdate flightPlan);

        void SetWeather(string metar);

        void SetWeather(NodeId from, string metar);

        /// <summary>What a node shares of its cockpit with us.</summary>
        void ShareCockpit(NodeId nuid, ShareCockpitFlags share);

        /// <summary>A node's nickname changed (ATC ids are derived from it).</summary>
        void NicknameChanged(NodeId nuid);
    }

    /// <summary>Read-only simulator state the directory and peer parts report.</summary>
    public interface ISimView
    {
        bool Available { get; }
        /// <summary>Null when no simulator is running.</summary>
        string SimulatorName { get; }
        bool SimulatorConnected { get; }
        /// <summary>The user's flight plan callsign ("" when no simulator is running).</summary>
        string UserCallsign { get; }
        /// <summary>The user's own aircraft (null if none).</summary>
        Sim.Aircraft UserAircraft { get; }
        /// <summary>The aircraft a remote node's user is flying (null if none).</summary>
        Sim.Aircraft FindUserAircraft(NodeId owner);
        /// <summary>Network and broadcast objects, by type.</summary>
        void CountObjects(out ushort planes, out ushort helicopters, out ushort boats, out ushort vehicles);
        bool TryGetAirport(string code, out Main.Airport airport);
    }

    /// <summary>The windows the session keeps up to date (none in CONSOLE builds).</summary>
    public interface ISessionUi
    {
        /// <summary>The session changed: refresh the aircraft, objects and users lists.</summary>
        void SessionChanged(int refreshes);
        void HubsChanged(int refreshes);
        /// <summary>The comms window is open.</summary>
        bool CommsVisible { get; }
        void CommsChanged();
        bool AddressBookVisible { get; }
        /// <summary>A window shows users from every hub (aircraft list with global aircraft, ATC list).</summary>
        bool ShowsGlobalUsers { get; }
        void ShowMessage(string message);
    }
}
