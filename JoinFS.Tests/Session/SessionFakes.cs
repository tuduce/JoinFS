using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>One message handed to the outbox.</summary>
    sealed record Sent(object Message, NodeId To, IPEndPoint EndPoint, bool Guaranteed, bool Broadcast, NodeId[] Recipients);

    /// <summary>Records every send instead of queuing it for a network thread.</summary>
    sealed class FakeOutbox : INetworkOutbox
    {
        public readonly List<Sent> Sent = [];

        public IEnumerable<T> Messages<T>() => Sent.Where(s => s.Message is T).Select(s => (T)s.Message);

        public IEnumerable<Sent> Of<T>() => Sent.Where(s => s.Message is T);

        public void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage =>
            Sent.Add(new Sent(message, default, null, meta.Guaranteed, false, recipients.ToArray()));

        public void SendTo<T>(NodeId recipient, in T message, bool guaranteed = false) where T : struct, IMessage =>
            Sent.Add(new Sent(message, recipient, null, guaranteed, false, [recipient]));

        public void SendToEndPoint<T>(IPEndPoint endPoint, in T message, bool guaranteed = false) where T : struct, IMessage =>
            Sent.Add(new Sent(message, default, endPoint, guaranteed, false, []));

        public void Broadcast<T>(in T message, bool guaranteed = false) where T : struct, IMessage =>
            Sent.Add(new Sent(message, default, null, guaranteed, true, []));

        public void SendObjectState<TPosition>(in IdentityUpdate identity, in TPosition position, ReadOnlySpan<NodeId> recipients) where TPosition : struct, IMessage
        {
            Sent.Add(new Sent(identity, default, null, false, false, recipients.ToArray()));
            Sent.Add(new Sent(position, default, null, false, false, recipients.ToArray()));
        }
    }

    sealed class FakeSession : ISessionState
    {
        public NetworkSnapshot Snapshot { get; set; } = NetworkSnapshot.Empty;
        public NodeId LocalId { get; set; } = new(0x01020304, 6112, 4);
        public bool Ready => LocalId.Valid();
        public bool Connected { get; set; } = true;
    }

    sealed class FakeLog : ISessionLog
    {
        public readonly List<string> Events = [];
        public readonly List<string> NetworkLines = [];
        public void Event(string text) => Events.Add(text);
        public void Network(string text) => NetworkLines.Add(text);
    }

    sealed class FakeProfile : ILocalProfile
    {
        public Guid Guid { get; set; } = new("11111111-2222-3333-4444-555555555555");
        public uint Uuid { get; set; } = 0x12345678;
        public string Nickname { get; set; } = "Me";
        public bool Hub { get; set; }
        public string HubName { get; set; } = "";
        public string HubAbout { get; set; } = "";
        public string HubVoip { get; set; } = "";
        public string HubEvent { get; set; } = "";
        public string HubDomain { get; set; } = "";
        public bool Atc { get; set; }
        public string AtcAirport { get; set; } = "";
        public int AtcLevel { get; set; } = 2;
        public int AtcFrequency { get; set; } = 22800;
        public int ActivityCircle { get; set; } = 40;
        public string MyIp { get; set; } = "";
        public bool MultipleObjects { get; set; }
        public bool ShareCockpitEveryone { get; set; }
        public bool LowBandwidth { get; set; }
        public bool ElevationCorrection { get; set; }
        public bool PublishWhazzup { get; set; }
        public string Password { get; set; } = "";
        public string StoragePath { get; set; } = Directory.CreateTempSubdirectory("joinfs-session-").FullName;
        public string DocumentsPath { get; set; } = Path.GetTempPath();
        public List<AddressBook.AddressBookEntry> AddressBook { get; set; } = [];
    }

    sealed class FakePolicy : IPeerPolicy
    {
        public readonly HashSet<NodeId> MultipleObjectNodes = [];
        public readonly HashSet<NodeId> SharingNodes = [];
        public readonly HashSet<IPAddress> IgnoredAddresses = [];
        public bool IgnoreNode(IPAddress address) => IgnoredAddresses.Contains(address);
        public bool IgnoreNode(ref Guid guid) => false;
        public bool ShareCockpit(NodeId nuid) => SharingNodes.Contains(nuid);
        public bool MultipleObjects(NodeId nuid) => MultipleObjectNodes.Contains(nuid);
    }

    sealed class FakeAtc : IAtcListener
    {
        public readonly List<string> Calls = [];
        public void AddAtc(string airport, int level, int frequency) => Calls.Add("add " + airport + " " + level + " " + frequency);
        public void RemoveAtc(string airport, int level, int frequency) => Calls.Add("remove " + airport + " " + level + " " + frequency);
    }

    sealed class FakeUi : ISessionUi
    {
        public int SessionRefreshes;
        public int HubRefreshes;
        public int CommsChanges;
        public bool CommsVisible { get; set; }
        public bool AddressBookVisible { get; set; }
        public bool ShowsGlobalUsers { get; set; }
        public readonly List<string> Messages = [];
        public void SessionChanged(int refreshes) => SessionRefreshes += refreshes;
        public void HubsChanged(int refreshes) => HubRefreshes += refreshes;
        public void CommsChanged() => CommsChanges++;
        public void ShowMessage(string message) => Messages.Add(message);
    }

    /// <summary>A simulator that records what the network asked of it.</summary>
    sealed class FakeSim : ISimSink, ISimView
    {
        public bool Available { get; set; } = true;
        public NodeId OwnAircraftOwner = new();
        public uint OwnAircraftNetId = 0;
        public bool HasOwnAircraft = true;
        public string CurrentMetar { get; set; }

        public readonly List<(NodeId Owner, IdentityUpdate Identity, bool User, string Nickname, PositionUpdate Position)> Aircraft = [];
        public readonly List<PositionUpdate> OwnAircraftPositions = [];
        public readonly List<(NodeId Owner, IdentityUpdate Identity, ObjectPositionUpdate Position)> Objects = [];
        public readonly List<(NodeId Owner, IdentityUpdate Identity)> IdentityChanges = [];
        public readonly List<(NodeId Owner, uint NetId, Dictionary<uint, int> Integers, Dictionary<uint, float> Floats, Dictionary<uint, string> String8s, bool Record)> Variables = [];
        public readonly List<(NodeId Owner, uint NetId, uint EventId, uint Data, bool FlightControls, bool Record)> Events = [];
        public readonly List<(NodeId Owner, uint NetId)> Removed = [];
        public readonly List<NodeId> RemovedOwners = [];
        public readonly List<FlightPlanUpdate> UserFlightPlans = [];
        public readonly List<(NodeId Owner, uint NetId, FlightPlanUpdate FlightPlan)> AircraftFlightPlans = [];
        public readonly List<string> Weather = [];
        public readonly List<(NodeId Node, ShareCockpitFlags Share)> Shares = [];
        public readonly List<NodeId> NicknameChanges = [];

        public bool TryGetOwnAircraft(out NodeId owner, out uint netId)
        {
            owner = OwnAircraftOwner;
            netId = OwnAircraftNetId;
            return Available && HasOwnAircraft;
        }

        public void ChangeIdentity(NodeId owner, in IdentityUpdate identity) => IdentityChanges.Add((owner, identity));
        public void UpdateAircraft(NodeId owner, in IdentityUpdate identity, bool user, string nickname, in PositionUpdate position) => Aircraft.Add((owner, identity, user, nickname, position));
        public void UpdateOwnAircraft(in PositionUpdate position) => OwnAircraftPositions.Add(position);
        public void UpdateObject(NodeId owner, in IdentityUpdate identity, in ObjectPositionUpdate position) => Objects.Add((owner, identity, position));
        public void UpdateVariables(NodeId owner, uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s, bool record) =>
            Variables.Add((owner, netId, integers, floats, string8s, record));
        public void ApplyEvent(NodeId owner, uint netId, uint eventId, uint data, bool flightControls, bool record) => Events.Add((owner, netId, eventId, data, flightControls, record));
        public void RemoveObject(NodeId owner, uint netId) => Removed.Add((owner, netId));
        public void RemoveObjects(NodeId owner) => RemovedOwners.Add(owner);
        public void UpdateUserFlightPlan(in FlightPlanUpdate flightPlan) => UserFlightPlans.Add(flightPlan);
        public void UpdateAircraftFlightPlan(NodeId owner, uint netId, in FlightPlanUpdate flightPlan) => AircraftFlightPlans.Add((owner, netId, flightPlan));
        public void SetWeather(string metar) => Weather.Add(metar);
        public void SetWeather(NodeId from, string metar) => Weather.Add(from + " " + metar);
        public void ShareCockpit(NodeId nuid, ShareCockpitFlags share) => Shares.Add((nuid, share));
        public void NicknameChanged(NodeId nuid) => NicknameChanges.Add(nuid);

        // ISimView
        public string SimulatorName { get; set; } = "TestSim";
        public bool SimulatorConnected { get; set; } = true;
        public string UserCallsign { get; set; } = "ME1";
        public Sim.Aircraft UserAircraft => null;
        public Sim.Aircraft FindUserAircraft(NodeId owner) => null;
        public void CountObjects(out ushort planes, out ushort helicopters, out ushort boats, out ushort vehicles) { planes = 1; helicopters = 2; boats = 3; vehicles = 4; }
        public bool TryGetAirport(string code, out Main.Airport airport) { airport = default; return false; }
    }

    /// <summary>The session's parts wired to fakes the way Network wires them to the app.</summary>
    sealed class SessionRig
    {
        public readonly FakeOutbox Outbox = new();
        public readonly FakeSession Session = new();
        public readonly FakeProfile Profile = new();
        public readonly FakeSim Sim = new();
        public readonly FakePolicy Policy = new();
        public readonly FakeAtc Atc = new();
        public readonly FakeUi Ui = new();
        public readonly ManualClock Clock = new();
        public readonly FakeLog Log = new();
        public readonly FakeEndPoints EndPoints = new();

        public readonly PeerTable Peers;
        public readonly SimIngest Ingest;
        public readonly HubDirectory Hubs;
        public readonly UserDirectory Users;
        public readonly HubHost Host;

        public SessionRig()
        {
            Clock.Advance(1000);
            Peers = new PeerTable(Outbox, Session, Profile, Sim, Sim, Policy, Atc, Clock, Log);
            Ingest = new SimIngest(Sim, Session, Profile, Policy, Peers, Outbox, Log);
            Hubs = new HubDirectory(Outbox, Session, Profile, Policy, EndPoints, Ui, Clock, Log);
            Users = new UserDirectory(Outbox, Session, Profile, Hubs, EndPoints, Ui, Clock, Log);
            Host = new HubHost(Outbox, Session, Profile, Sim, Peers, Hubs, Users, Clock, Log);
            Peers.HubAnnounced += Hubs.SubmitHub;
        }

        /// <summary>Metadata of a message from <paramref name="sender"/> at its default endpoint.</summary>
        public static MessageMeta From(NodeId sender) => new() { Sender = sender, EndPoint = sender.ToEndPoint(sender.port) };
    }

    sealed class FakeEndPoints : IEndPointResolver
    {
        public IPEndPoint MakeEndPoint(NodeId node, ushort port) => node.ToEndPoint(port);

        public bool MakeEndPoint(string addressText, ushort port, out IPEndPoint endPoint)
        {
            bool ok = IPAddress.TryParse(addressText, out IPAddress address);
            endPoint = ok ? new IPEndPoint(address, port) : new IPEndPoint(0, 0);
            return ok;
        }
    }
}
