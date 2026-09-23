using JoinFS.Net;
using System;
using System.Collections.Generic;

namespace JoinFS
{
    /// <summary>
    /// Sends the simulator's state to the session as canonical messages: positions (with identity),
    /// variables, events, removals, flight plans and weather. Sim decides who is due what and when
    /// (its rate policy); this only builds the messages and hands them to the network thread.
    /// </summary>
    public sealed class SimSender
    {
        readonly INetworkOutbox outbox;
        readonly ISessionState session;
        readonly ILocalProfile profile;

        public SimSender(INetworkOutbox outbox, ISessionState session, ILocalProfile profile)
        {
            this.outbox = outbox;
            this.session = session;
            this.profile = profile;
        }

        /// <summary>
        /// Send an aircraft's position (with its identity) to <paramref name="recipients"/>.
        /// <paramref name="sharedCockpit"/>: this is our own aircraft flying someone else's (the
        /// recipient's) aircraft, sent under the shared-cockpit object id.
        /// </summary>
        public void SendAircraftPosition(Sim.Aircraft aircraft, ref Sim.AircraftPosition position, double netTime, ReadOnlySpan<NodeId> recipients, bool sharedCockpit = false)
        {
            if (recipients.IsEmpty) return;
            uint objectId = sharedCockpit ? uint.MaxValue : aircraft.netId;
            outbox.SendObjectState(SimMessageMapper.BuildIdentity(aircraft, objectId),
                SimMessageMapper.ToPositionUpdate(objectId, aircraft, ref position, netTime, profile.ElevationCorrection), recipients);
        }

        /// <summary>Send a non-aircraft object's position (with its identity) to <paramref name="recipients"/>.</summary>
        public void SendObjectPosition(Sim.Obj obj, ref Sim.ObjectPositionVelocity pv, ReadOnlySpan<NodeId> recipients)
        {
            if (recipients.IsEmpty) return;
            outbox.SendObjectState(SimMessageMapper.BuildIdentity(obj, obj.netId),
                SimMessageMapper.ToObjectPositionUpdate(obj, ref pv, profile.ElevationCorrection), recipients);
        }

        /// <summary>Send a simulator event on one of our objects.</summary>
        public void SendEvent(uint netId, uint eventId, uint data, ReadOnlySpan<NodeId> recipients)
        {
            if (recipients.IsEmpty) return;
            outbox.Send(new MessageMeta { Guaranteed = true }, new EventUpdate { ObjectId = netId, EventId = eventId, Data = data }, recipients);
        }

        public void BroadcastEvent(uint netId, uint eventId, uint data) =>
            outbox.Broadcast(new EventUpdate { ObjectId = netId, EventId = eventId, Data = data }, guaranteed: true);

        /// <summary>Send an object's simulator variables (all three kinds in one canonical message).</summary>
        public void SendVariables(uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s, ReadOnlySpan<NodeId> recipients)
        {
            if (recipients.IsEmpty) return;
            VariableSyncUpdate update = BuildVariables(netId, integers, floats, string8s);
            if (update.Entries.Count > 0) outbox.Send(new MessageMeta(), update, recipients);
        }

        public void BroadcastVariables(uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s)
        {
            VariableSyncUpdate update = BuildVariables(netId, integers, floats, string8s);
            if (update.Entries.Count > 0) outbox.Broadcast(update);
        }

        // our own objects (and objects we re-broadcast) are ours as far as receivers are concerned
        VariableSyncUpdate BuildVariables(uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s) =>
            new() { Owner = session.LocalId, ObjectId = netId, Entries = SimMessageMapper.ToVariableEntries(integers, floats, string8s) };

        /// <summary>We stopped showing one of our objects.</summary>
        public void SendRemoveObjectMessage(uint netId) => outbox.Broadcast(new RemoveObject { ObjectId = netId }, guaranteed: true);

        /// <summary>Send one of our aircraft's flight plan to everyone (fills in a missing airline from the callsign).</summary>
        public void BroadcastFlightPlanUpdate(uint netId, Sim.FlightPlan flightPlan)
        {
            if (flightPlan.icaoAirline.Length == 0)
            {
                flightPlan.icaoAirline = Sim.DeriveIcaoAirlineFromCallsign(flightPlan.callsign);
            }
            outbox.Broadcast(SimMessageMapper.ToFlightPlanUpdate(session.LocalId, netId, flightPlan));
        }

        /// <summary>The weather at our aircraft, to everyone.</summary>
        public void BroadcastWeather(string metar) => outbox.Broadcast(new WeatherUpdate { Metar = metar });
    }
}
