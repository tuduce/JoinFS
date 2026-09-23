using JoinFS.Net;
using System.Collections.Generic;

namespace JoinFS
{
    /// <summary>
    /// Applies what other nodes send about their objects to the simulator (through
    /// <see cref="ISimSink"/>): identity, positions, variables, events, removals, flight plans and
    /// weather, whichever protocol they arrived over.
    ///
    /// Owns the identity cache: some protocols send identity with every position, others only when
    /// it changes. Either way positions find it here, and an object is only updated (or respawned)
    /// when its identity really changed.
    /// </summary>
    public sealed class SimIngest : IMessageHandler
    {
        readonly ISimSink sim;
        readonly ISessionState session;
        readonly ILocalProfile profile;
        readonly IPeerPolicy policy;
        readonly PeerTable peers;
        readonly INetworkOutbox outbox;
        readonly ISessionLog log;

        /// <summary>Latest identity of every remote object, keyed by (owner, object id).</summary>
        readonly Dictionary<(NodeId Owner, uint ObjectId), IdentityUpdate> identities = [];

        public SimIngest(ISimSink sim, ISessionState session, ILocalProfile profile, IPeerPolicy policy, PeerTable peers, INetworkOutbox outbox, ISessionLog log)
        {
            this.sim = sim;
            this.session = session;
            this.profile = profile;
            this.policy = policy;
            this.peers = peers;
            this.outbox = outbox;
            this.log = log;
        }

        /// <summary>Number of cached identities (diagnostics and tests).</summary>
        public int IdentityCount => identities.Count;

        /// <summary>A node left: forget its objects' identities and remove its objects.</summary>
        public void OnPeerLeft(NodeId nuid)
        {
            List<(NodeId, uint)> doomed = null;
            foreach (var key in identities.Keys)
            {
                if (key.Owner == nuid) (doomed ??= []).Add(key);
            }
            if (doomed != null) foreach (var key in doomed) identities.Remove(key);
            sim.RemoveObjects(nuid);
        }

        public void Handle(in MessageMeta meta, in IdentityUpdate identity)
        {
            var key = (meta.Sender, identity.ObjectId);
            bool known = identities.TryGetValue(key, out IdentityUpdate previous);
            identities[key] = identity;
            // new objects are created from it by their first position
            if (known && !previous.SameAs(identity) && sim.Available)
            {
                sim.ChangeIdentity(meta.Sender, identity);
            }
        }

        public void Handle(in MessageMeta meta, in PositionUpdate update)
        {
            NodeId nuid = meta.Sender;
            bool user = (update.StateFlags & PositionStateFlags.UserControlled) != 0;

            // shared cockpit: the sender is flying our aircraft
            if (update.ObjectId == uint.MaxValue)
            {
                if (peers.shareFlightControls == nuid)
                {
                    sim.UpdateOwnAircraft(update);
                }
                return;
            }

            // additional (non-user) objects only if allowed from this node
            if (!user && !AllowsMultipleObjects(nuid))
            {
                return;
            }

            if (!identities.TryGetValue((nuid, update.ObjectId), out IdentityUpdate identity))
            {
                log.Network("Dropped position for " + nuid + "/" + update.ObjectId + " - identity not known yet");
                return;
            }

            sim.UpdateAircraft(nuid, identity, user, user ? peers.GetNodeName(nuid) : "", update);
        }

        public void Handle(in MessageMeta meta, in ObjectPositionUpdate update)
        {
            NodeId nuid = meta.Sender;
            if (!AllowsMultipleObjects(nuid))
            {
                return;
            }
            if (identities.TryGetValue((nuid, update.ObjectId), out IdentityUpdate identity))
            {
                sim.UpdateObject(nuid, identity, update);
            }
        }

        bool AllowsMultipleObjects(NodeId nuid) => policy.MultipleObjects(nuid) || profile.MultipleObjects;

        public void Handle(in MessageMeta meta, in VariableSyncUpdate sync)
        {
            if (!sim.Available || sync.Entries == null || sync.Entries.Count == 0)
            {
                return;
            }
            NodeId owner = meta.Sender;
            uint netId = sync.ObjectId;
            bool sharedCockpit = netId == uint.MaxValue;
            // shared cockpit: the sender's variables for our own aircraft
            if (sharedCockpit && !sim.TryGetOwnAircraft(out owner, out netId))
            {
                return;
            }
            SimMessageMapper.SplitVariables(sync.Entries, out var integers, out var floats, out var string8s);
            sim.UpdateVariables(owner, netId, integers, floats, string8s, record: !sharedCockpit);
        }

        public void Handle(in MessageMeta meta, in EventUpdate update)
        {
            if (!session.Connected)
            {
                return;
            }
            NodeId nuid = meta.Sender;
            if (update.ObjectId == uint.MaxValue)
            {
                // shared cockpit: an event on our own aircraft
                if (sim.TryGetOwnAircraft(out NodeId owner, out uint netId))
                {
                    bool flightControls = policy.ShareCockpit(nuid) && nuid == peers.shareFlightControls;
                    sim.ApplyEvent(owner, netId, update.EventId, update.Data, flightControls, record: false);
                }
            }
            else
            {
                sim.ApplyEvent(nuid, update.ObjectId, update.EventId, update.Data, flightControls: true, record: true);
            }
        }

        public void Handle(in MessageMeta meta, in RemoveObject message)
        {
            identities.Remove((meta.Sender, message.ObjectId));
            sim.RemoveObject(meta.Sender, message.ObjectId);
        }

        public void Handle(in MessageMeta meta, in FlightPlanUpdate update)
        {
            if (!sim.Available)
            {
                return;
            }
            // Our own flight plan coming back. Neither send path should deliver a node's own
            // broadcast to itself, but the legacy receiver handled it, so this does too.
            if (session.LocalId.Equals(update.Owner))
            {
                sim.UpdateUserFlightPlan(update);
                log.Event("Flight Plan Update");
            }
            else
            {
                sim.UpdateAircraftFlightPlan(update.Owner, update.ObjectId, update);
            }
        }

        public void Handle(in MessageMeta meta, in WeatherRequest request)
        {
            string metar = sim.CurrentMetar;
            if (metar != null)
            {
                outbox.SendTo(meta.Sender, new WeatherReply { Metar = metar }, guaranteed: true);
            }
        }

        public void Handle(in MessageMeta meta, in WeatherReply reply)
        {
            if (reply.Metar.Length > 0)
            {
                sim.SetWeather(reply.Metar);
            }
        }

        public void Handle(in MessageMeta meta, in WeatherUpdate update)
        {
            if (update.Metar.Length > 0)
            {
                sim.SetWeather(meta.Sender, update.Metar);
            }
        }
    }
}
