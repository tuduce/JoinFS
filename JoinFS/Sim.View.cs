using System;
using System.Collections.Generic;
using JoinFS.Net;

namespace JoinFS
{
    /// <summary>
    /// What other threads may read of the simulator (docs/sim-thread-architecture.md §2.1): an
    /// immutable snapshot the sim thread publishes by swapping one reference, at most
    /// <see cref="Sim.ViewInterval"/> old and at once after structural changes.
    ///
    /// The objects are copies (<see cref="Sim.Obj.CloneView"/>) - read them, don't change them. To
    /// act on one, post a command to the sim thread with its <see cref="Sim.Obj.Source"/>.
    /// </summary>
    public sealed class SimSnapshot
    {
        public static readonly SimSnapshot Empty = new();

        public bool Connected { get; init; }
        public bool Connecting { get; init; }
        /// <summary>Sim.GetSimulatorName(): "Not connected" (localised) when not connected</summary>
        public string SimulatorName { get; init; } = "";
        public string SimulatorVersion { get; init; } = "0";

        /// <summary>Copies of every object in Sim.objectList, in list order</summary>
        public IReadOnlyList<Sim.Obj> Objects { get; init; } = [];
        public Sim.Aircraft UserAircraft { get; init; }
        public Sim.Aircraft EnteredAircraft { get; init; }
        public Sim.Aircraft WeatherAircraft { get; init; }
        public Sim.Obj TrackHeadingObject { get; init; }
        public Sim.Obj TrackBearingObject { get; init; }
        public Sim.FlightPlan UserFlightPlan { get; init; } = new();

        /// <summary>The weather observation waiting to be applied (null when none)</summary>
        public string CurrentMetar { get; init; }
        /// <summary>FRAME events received (SimConnect builds; 0 otherwise)</summary>
        public int FrameCount { get; init; }

        /// <summary>Sim thread mailbox: items waiting when last drained, the most since the previous snapshot, and the longest wait (seconds)</summary>
        public int MailboxDepth { get; init; }
        public int MailboxPeak { get; init; }
        public double MailboxMaxAge { get; init; }

        /// <summary>ElapsedTime when published</summary>
        public double Time { get; init; }

        /// <summary>The copy of an object, by owner and network id</summary>
        public Sim.Obj Find(NodeId ownerNuid, uint netId)
        {
            foreach (var obj in Objects)
            {
                if (obj.ownerNuid == ownerNuid && obj.netId == netId) return obj;
            }
            return null;
        }

        /// <summary>The copies matching <paramref name="match"/></summary>
        public List<Sim.Obj> FindAll(Predicate<Sim.Obj> match)
        {
            List<Sim.Obj> found = [];
            foreach (var obj in Objects)
            {
                if (match(obj)) found.Add(obj);
            }
            return found;
        }
    }

    public partial class Sim
    {
        /// <summary>Longest time between snapshots (seconds)</summary>
        public const double ViewInterval = 0.1;

        volatile SimSnapshot view = SimSnapshot.Empty;

        /// <summary>
        /// The latest snapshot. Any thread may read it; the sim thread's own code should read the
        /// live state instead.
        /// </summary>
        public SimSnapshot View => view;

        bool viewDirty = true;
        double nextViewTime = 0.0;
        int viewObjectCount = -1;
        Aircraft viewUserAircraft;
        bool viewConnected;

        /// <summary>The object is still in the list (sim thread): a command's object may have gone since it was posted</summary>
        public bool IsLive(Obj obj) => obj != null && objectList.Contains(obj);

        /// <summary>Publish a new snapshot at the next opportunity (sim thread)</summary>
        public void MarkViewDirty()
        {
            viewDirty = true;
        }

        /// <summary>
        /// Publish a snapshot if something structural changed or <see cref="ViewInterval"/> has
        /// passed (sim thread). The mailbox figures come from <see cref="SimService"/>.
        /// </summary>
        /// <returns>True when a snapshot was published</returns>
        public bool PublishViewIfDue(double now, int mailboxDepth = 0, int mailboxPeak = 0, double mailboxMaxAge = 0.0)
        {
            bool connected = Connected;
            if (viewDirty == false && now < nextViewTime && objectList.Count == viewObjectCount && userAircraft == viewUserAircraft && connected == viewConnected)
            {
                return false;
            }
            viewDirty = false;
            nextViewTime = now + ViewInterval;
            viewObjectCount = objectList.Count;
            viewUserAircraft = userAircraft;
            viewConnected = connected;

            // copy every object, keeping references between them pointing at the copies
            Dictionary<Obj, Obj> copies = new(objectList.Count);
            List<Obj> objects = new(objectList.Count);
            foreach (var obj in objectList)
            {
                Obj copy = obj.CloneView();
                copies[obj] = copy;
                objects.Add(copy);
            }
            Obj CopyOf(Obj obj) => obj == null ? null : copies.TryGetValue(obj, out Obj copy) ? copy : obj.CloneView();

            view = new SimSnapshot
            {
                Connected = connected,
                Connecting = Connecting,
                SimulatorName = GetSimulatorName(),
                SimulatorVersion = GetSimulatorVersion(),
                Objects = objects,
                UserAircraft = CopyOf(userAircraft) as Aircraft,
                EnteredAircraft = CopyOf(enteredAircraft) as Aircraft,
                WeatherAircraft = CopyOf(weatherAircraft) as Aircraft,
                TrackHeadingObject = CopyOf(trackHeadingObject),
                TrackBearingObject = CopyOf(trackBearingObject),
                UserFlightPlan = userFlightPlan.Clone(),
                CurrentMetar = scheduleMetar,
#if SIMCONNECT
                FrameCount = frameCount,
#endif
                MailboxDepth = mailboxDepth,
                MailboxPeak = mailboxPeak,
                MailboxMaxAge = mailboxMaxAge,
                Time = now,
            };
            return true;
        }
    }
}
