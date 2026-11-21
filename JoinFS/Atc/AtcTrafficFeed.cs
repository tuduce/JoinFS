using System;
using System.Collections.Generic;
using JoinFS.Fsd;

namespace JoinFS
{
    /// <summary>
    /// What the ATC client is told about traffic (docs/ATC/atc-fsd-plan.md §4.7): an <c>@</c> for every aircraft once a
    /// second, a <c>$FP</c> when an aircraft is first seen and when its flight plan changes, a <c>#DP</c> when it has
    /// been gone for 5 s. It also finds an aircraft by its FSD callsign for the queries the gateway answers. Runs on
    /// the app thread, keeps only what it has sent, and reaches the client through the lines it is given to send.
    /// </summary>
    sealed class AtcTrafficFeed
    {
        /// <summary>How often every aircraft's position is sent, ms (risk R2: the one constant to change).</summary>
        public const long IntervalMs = 1000;

        /// <summary>An aircraft missing from the traffic for this long is deleted, ms.</summary>
        public const long GoneAfterMs = 5000;

        /// <summary>The pilot rating of an aircraft: 1, a plain pilot.</summary>
        const int PilotRating = 1;

        /// <summary>Ident (<c>Y</c>) is kept for this long after it was last seen, ms: the simulator holds it for 18 s, but the sample may miss it.</summary>
        public const long IdentHoldMs = 5000;

        /// <summary>Where an unsolicited <c>$FP</c> goes: every controller.</summary>
        const string AllControllers = "*A";

        sealed class Entry
        {
            public string Callsign;
            public string Plan;
            public long LastSeenMs;
            public long LastIdentMs = long.MinValue;
        }

        /// <summary>A controller's amendment, and the pilot's plan text it was made against (it is dropped when that changes).</summary>
        sealed class Amendment
        {
            public FsdFlightPlan Plan;
            public string Base;
        }

        readonly IAtcTraffic traffic;
        readonly Dictionary<(ulong, ulong), Entry> known = new Dictionary<(ulong, ulong), Entry>();
        readonly Dictionary<(ulong, ulong), Amendment> amended = new Dictionary<(ulong, ulong), Amendment>();
        long nextMs = long.MinValue;

        public AtcTrafficFeed(IAtcTraffic traffic)
        {
            this.traffic = traffic;
        }

        /// <summary>The client is new (or gone): it knows nothing, so the next tick sends everything.</summary>
        public void Reset()
        {
            known.Clear();
            amended.Clear();
            nextMs = long.MinValue;
        }

        /// <summary>Send what is due. <paramref name="send"/> writes one line to the client.</summary>
        public void Tick(long nowMs, Action<FsdLine> send)
        {
            if (nowMs < nextMs) return;
            nextMs = nowMs + IntervalMs;

            IReadOnlyList<AtcAircraft> aircraft = traffic.Aircraft;
            string[] names = Names(aircraft);
            var seen = new HashSet<(ulong, ulong)>();

            // first the deletes, so a callsign that moves to another aircraft is free before it is used again
            for (int i = 0; i < aircraft.Count; i++)
            {
                var key = (aircraft[i].Owner, aircraft[i].ObjectId);
                if (!seen.Add(key)) continue;
                if (known.TryGetValue(key, out Entry entry) && entry.Callsign != names[i])
                {
                    send(FsdDelete.Pilot(entry.Callsign).ToLine());
                    known.Remove(key);
                    amended.Remove(key);
                }
            }
            List<(ulong, ulong)> gone = null;
            foreach (KeyValuePair<(ulong, ulong), Entry> pair in known)
            {
                if (!seen.Contains(pair.Key) && nowMs - pair.Value.LastSeenMs >= GoneAfterMs)
                    (gone ??= new List<(ulong, ulong)>()).Add(pair.Key);
            }
            if (gone != null)
            {
                foreach ((ulong, ulong) key in gone)
                {
                    send(FsdDelete.Pilot(known[key].Callsign).ToLine());
                    known.Remove(key);
                    amended.Remove(key);
                }
            }

            // then the position of every aircraft, and its flight plan when new or changed
            seen.Clear();
            for (int i = 0; i < aircraft.Count; i++)
            {
                AtcAircraft a = aircraft[i];
                var key = (a.Owner, a.ObjectId);
                if (!seen.Add(key)) continue;

                bool isNew = !known.TryGetValue(key, out Entry entry);
                if (isNew) known[key] = entry = new Entry { Callsign = names[i] };
                if (a.Ident) entry.LastIdentMs = nowMs;
                bool identHeld = entry.LastIdentMs != long.MinValue && nowMs - entry.LastIdentMs < IdentHoldMs;

                send(Position(a, names[i], ModeLetter(a.Transponder, identHeld)).ToLine());

                FsdLine plan = AtcFlightPlans.ToFsd(names[i], AllControllers, a.Plan).ToLine();
                string planText = plan.ToString();
                if (isNew)
                {
                    entry.Plan = planText;
                    send(plan);
                }
                else if (entry.Plan != planText)
                {
                    entry.Plan = planText;
                    amended.Remove(key); // the pilot's own plan changed: the amendment no longer holds
                    send(plan);
                }
                entry.LastSeenMs = nowMs;
            }
        }

        /// <summary>The aircraft that has this FSD callsign now, or false.</summary>
        public bool TryFind(string callsign, out AtcAircraft aircraft)
        {
            IReadOnlyList<AtcAircraft> all = traffic.Aircraft;
            string[] names = Names(all);
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == callsign)
                {
                    aircraft = all[i];
                    return true;
                }
            }
            aircraft = null;
            return false;
        }

        /// <summary>
        /// The <c>$FP</c> of an aircraft found by <see cref="TryFind"/>, from the aircraft to the asker: the last amendment
        /// of a controller when there is one that still holds, else the pilot's own plan (stage 11).
        /// </summary>
        public FsdLine FlightPlanFor(string callsign, string asker, AtcAircraft aircraft)
        {
            var key = (aircraft.Owner, aircraft.ObjectId);
            if (amended.TryGetValue(key, out Amendment amendment))
            {
                if (amendment.Base == PlanText(callsign, aircraft))
                {
                    FsdFlightPlan plan = amendment.Plan;
                    return new FsdFlightPlan
                    {
                        From = callsign, To = asker, Rules = plan.Rules, Equipment = plan.Equipment,
                        TrueAirspeedKnots = plan.TrueAirspeedKnots, Departure = plan.Departure,
                        EstimatedDepartureTime = plan.EstimatedDepartureTime, ActualDepartureTime = plan.ActualDepartureTime,
                        CruiseAltitude = plan.CruiseAltitude, Destination = plan.Destination,
                        HoursEnroute = plan.HoursEnroute, MinutesEnroute = plan.MinutesEnroute,
                        HoursFuel = plan.HoursFuel, MinutesFuel = plan.MinutesFuel,
                        Alternate = plan.Alternate, Remarks = plan.Remarks, Route = plan.Route,
                    }.ToLine();
                }
                amended.Remove(key); // the pilot's own plan changed since
            }
            return AtcFlightPlans.ToFsd(callsign, asker, aircraft.Plan).ToLine();
        }

        /// <summary>
        /// Keep a controller's amendment for the aircraft it names (by its FSD callsign), replacing the earlier one. It holds
        /// until the pilot's own flight plan changes or the aircraft leaves. False when no aircraft of this node's view has that callsign.
        /// </summary>
        public bool Amend(FsdFlightPlan amendment)
        {
            if (!TryFind(amendment.Aircraft, out AtcAircraft aircraft)) return false;
            amended[(aircraft.Owner, aircraft.ObjectId)] = new Amendment { Plan = amendment, Base = PlanText(amendment.Aircraft, aircraft) };
            return true;
        }

        /// <summary>The pilot's plan as the feed compares it: the <c>$FP</c> text addressed to the controllers.</summary>
        static string PlanText(string callsign, AtcAircraft aircraft) =>
            AtcFlightPlans.ToFsd(callsign, AllControllers, aircraft.Plan).ToLine().ToString();

        /// <summary>The answer to <c>$CQ ..:RN</c>: the owner's nickname, no sector file, the pilot rating.</summary>
        public static FsdLine RealNameFor(string callsign, string asker, AtcAircraft aircraft)
        {
            string name = string.IsNullOrWhiteSpace(aircraft.Nickname) ? callsign : aircraft.Nickname.Replace(':', ' ');
            return FsdQuery.Response(callsign, asker, "RN", name, "", PilotRating.ToString()).ToLine();
        }

        /// <summary>
        /// The mode letter of <c>@</c> (§4.7), as FSD names them: ident <c>Y</c>, mode C <c>N</c>, mode A and standby <c>S</c>.
        /// An unknown mode is <c>N</c>, as it was before stage 9.
        /// </summary>
        public static char ModeLetter(AtcTransponder transponder, bool identHeld)
        {
            if (identHeld) return 'Y';
            return transponder is AtcTransponder.Standby or AtcTransponder.ModeA ? 'S' : 'N';
        }

        static FsdPosition Position(AtcAircraft a, string callsign, char mode) => new FsdPosition
        {
            Mode = mode,
            Callsign = callsign,
            Squawk = a.Squawk,
            Rating = PilotRating,
            Latitude = a.Latitude,
            Longitude = a.Longitude,
            AltitudeFeet = (int)Math.Round(a.AltitudeFeet),
            GroundSpeedKnots = (int)Math.Round(a.GroundSpeedKnots),
            PitchBankHeading = FsdPitchBankHeading.Pack(a.PitchDegrees, a.BankDegrees, a.HeadingDegrees),
        };

        static string[] Names(IReadOnlyList<AtcAircraft> aircraft)
        {
            var input = new FsdAircraft[aircraft.Count];
            for (int i = 0; i < input.Length; i++)
                input[i] = new FsdAircraft(aircraft[i].Owner, aircraft[i].ObjectId, aircraft[i].FlightPlanCallsign);
            return FsdCallsign.Aircraft(input);
        }
    }
}
