using System;
using System.Collections.Generic;

namespace JoinFS
{
    /// <summary>
    /// One aircraft as the websocket feed publishes it. Kept outside the CONSOLE-only WebSocketServer so
    /// the change filter below can be unit tested.
    /// </summary>
    public struct AircraftSnapshot
    {
        public string callsign, nickname, guid;
        // "pilot" (a real connected pilot), "recorded" (Recorder-replayed) or "ai" (other
        // non-pilot traffic) - mirrors the desktop Aircraft Dialog's (R)/(A) distinction
        public string trafficType;
        public string registration, icaoAirline, flightNumber;
        public double altitude, speed, latitude, longitude;
        public int heading;
        public string com1, com2, squawk;
        public string icaoType, from, to, rules, route, remarks, livery;
        public int gear;
        public double flaps;
        public int lightNav, lightBeacon, lightLanding, lightTaxi, lightStrobe;
        public bool eng1, eng2, eng3, eng4;
        public double rotorRpm;
        public bool onGround;

        /// <summary>
        /// This snapshot with every number at the precision the feed sends. The single source of the wire
        /// rounding: serialization and change detection both use it, so a difference that cannot be seen
        /// by a client is never treated as a change.
        /// </summary>
        public readonly AircraftSnapshot Rounded()
        {
            AircraftSnapshot r = this;
            r.altitude = Math.Round(altitude, 0);
            r.speed = Math.Round(speed, 1);
            r.latitude = Math.Round(latitude, 6);
            r.longitude = Math.Round(longitude, 6);
            r.flaps = Math.Round(flaps, 3);
            r.rotorRpm = Math.Round(rotorRpm, 1);
            return r;
        }

        static bool SamePosition(in AircraftSnapshot a, in AircraftSnapshot b) =>
            a.altitude == b.altitude && a.speed == b.speed &&
            a.latitude == b.latitude && a.longitude == b.longitude &&
            a.heading == b.heading && a.onGround == b.onGround;

        static bool SameDetails(in AircraftSnapshot a, in AircraftSnapshot b) =>
            a.callsign == b.callsign && a.nickname == b.nickname && a.trafficType == b.trafficType &&
            a.registration == b.registration && a.icaoAirline == b.icaoAirline && a.flightNumber == b.flightNumber &&
            a.com1 == b.com1 && a.com2 == b.com2 && a.squawk == b.squawk &&
            a.icaoType == b.icaoType && a.from == b.from && a.to == b.to &&
            a.rules == b.rules && a.route == b.route && a.remarks == b.remarks && a.livery == b.livery &&
            a.gear == b.gear && a.flaps == b.flaps &&
            a.lightNav == b.lightNav && a.lightBeacon == b.lightBeacon &&
            a.lightLanding == b.lightLanding && a.lightTaxi == b.lightTaxi && a.lightStrobe == b.lightStrobe &&
            a.eng1 == b.eng1 && a.eng2 == b.eng2 && a.eng3 == b.eng3 && a.eng4 == b.eng4 &&
            a.rotorRpm == b.rotorRpm;

        /// <summary>Whether two rounded snapshots carry the same values on the wire.</summary>
        internal static bool SameWireValues(in AircraftSnapshot rounded, in AircraftSnapshot otherRounded) =>
            SamePosition(rounded, otherRounded) && SameDetails(rounded, otherRounded);
    }

    /// <summary>
    /// Decides which aircraft the websocket feed publishes. The feed flushes on a fixed tick
    /// (<see cref="FlushDue"/>) - one batched message per tick, never more - and on each flush an aircraft is
    /// included when what a client sees changed, or when it has been silent for the keep-alive interval so
    /// that a map purging silent markers keeps it. Not thread-safe: call it from the work thread only.
    /// </summary>
    public sealed class AircraftFeedFilter
    {
        sealed class State
        {
            public AircraftSnapshot lastSent;
            public double lastSentTime;
            public AircraftSnapshot latest;
        }

        readonly Func<double> nowSeconds;
        readonly double flushIntervalSeconds;
        double lastFlushTime = double.NegativeInfinity;
        readonly double keepAliveSeconds;
        readonly Dictionary<Guid, State> states = [];

        public AircraftFeedFilter(Func<double> nowSeconds, double flushIntervalSeconds, double keepAliveSeconds)
        {
            this.nowSeconds = nowSeconds;
            this.flushIntervalSeconds = flushIntervalSeconds;
            this.keepAliveSeconds = keepAliveSeconds;
        }

        /// <summary>Whether this aircraft has been seen and not yet forgotten by <see cref="Retain"/>.</summary>
        public bool Contains(Guid key) => states.ContainsKey(key);

        /// <summary>
        /// True at most once per flush interval; the caller evaluates the aircraft and publishes one message
        /// only when this returns true.
        /// </summary>
        public bool FlushDue()
        {
            double now = nowSeconds();
            if (now - lastFlushTime < flushIntervalSeconds) return false;
            lastFlushTime = now;
            return true;
        }

        /// <summary>
        /// Whether this aircraft belongs in the current flush. Returning true records the snapshot as sent.
        /// </summary>
        public bool ShouldSend(Guid key, in AircraftSnapshot snapshot)
        {
            AircraftSnapshot rounded = snapshot.Rounded();
            double now = nowSeconds();

            if (!states.TryGetValue(key, out State state))
            {
                states[key] = new State { lastSent = rounded, lastSentTime = now, latest = rounded };
                return true;
            }

            state.latest = rounded;
            double sinceLastSent = now - state.lastSentTime;

            bool send = !AircraftSnapshot.SameWireValues(rounded, state.lastSent) || sinceLastSent >= keepAliveSeconds;

            if (send)
            {
                state.lastSent = rounded;
                state.lastSentTime = now;
            }
            return send;
        }

        /// <summary>
        /// Forget every aircraft not in <paramref name="stillPresent"/>; one that returns is sent as new.
        /// </summary>
        public void Retain(HashSet<Guid> stillPresent)
        {
            if (states.Count <= stillPresent.Count) return;
            List<Guid> gone = [];
            foreach (Guid key in states.Keys)
                if (!stillPresent.Contains(key)) gone.Add(key);
            foreach (Guid key in gone) states.Remove(key);
        }

        /// <summary>
        /// The latest (rounded) state of every known aircraft, whether or not it was due to be sent -
        /// the initial snapshot for a client that just connected.
        /// </summary>
        public IEnumerable<AircraftSnapshot> Known()
        {
            foreach (State state in states.Values) yield return state.latest;
        }
    }
}
