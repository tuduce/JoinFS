using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using JoinFS.Net;

namespace JoinFS
{
    /// <summary>
    /// The aircraft of <see cref="Sim.View"/> for the ATC client (docs/ATC/atc-fsd-plan.md §4.7): the session's aircraft,
    /// the user's own and the recorder's. The simulator's own AI aircraft that are not broadcast are not part of the
    /// session. Reads the immutable snapshot only, so it needs no lock and never touches <c>sim.objectList</c>.
    /// </summary>
    sealed class SimViewTraffic : IAtcTraffic
    {
        /// <summary>The owner id of the recorder's aircraft: no node has it, and it differs from this node's.</summary>
        const ulong RecorderOwner = ulong.MaxValue;

        const double MetresPerSecondToKnots = 1.9438444925;

        readonly Func<SimSnapshot> view;
        readonly Func<NodeId> localId;
        readonly Func<NodeId, string> nickname;
        readonly Func<Sim.Aircraft, int> squawkOf;
        readonly Func<Sim.Aircraft, (int State, bool Ident)> transponderOf;

        /// <param name="view">The latest snapshot (<c>Main.sim.View</c>).</param>
        /// <param name="localId">This node's id.</param>
        /// <param name="nickname">A node's nickname; the invalid id is this node.</param>
        /// <param name="squawkOf">The aircraft's transponder code as the simulator variable holds it (BCD: 0x1200 is the code 1200).</param>
        /// <param name="transponderOf">The aircraft's transponder state (<see cref="AtcAircraft.StateUnknown"/> when its node does not
        /// send it) and ident; absent means every aircraft is unknown.</param>
        public SimViewTraffic(Func<SimSnapshot> view, Func<NodeId> localId, Func<NodeId, string> nickname, Func<Sim.Aircraft, int> squawkOf,
            Func<Sim.Aircraft, (int State, bool Ident)> transponderOf = null)
        {
            this.view = view;
            this.localId = localId;
            this.nickname = nickname;
            this.squawkOf = squawkOf;
            this.transponderOf = transponderOf;
        }

        public IReadOnlyList<AtcAircraft> Aircraft
        {
            get
            {
                var list = new List<AtcAircraft>();
                SimSnapshot snapshot = view();
                if (snapshot == null) return list;

                foreach (Sim.Obj obj in snapshot.Objects)
                {
                    if (obj is not Sim.Aircraft aircraft) continue;
                    if (aircraft.owner == Sim.Obj.Owner.Sim && !aircraft.broadcast) continue;
                    Sim.Pos position = aircraft.Position;
                    if (position == null) continue;

                    double latitude = position.geo.z * (180.0 / Math.PI);
                    double longitude = position.geo.x * (180.0 / Math.PI);
                    if (!double.IsFinite(latitude) || !double.IsFinite(longitude)) continue;

                    ulong owner = aircraft.owner switch
                    {
                        Sim.Obj.Owner.Network => Bits(aircraft.ownerNuid),
                        Sim.Obj.Owner.Recorder => RecorderOwner,
                        _ => Bits(localId()),
                    };

                    double speed = aircraft.netVelocity == null ? 0.0 : Math.Sqrt(
                        aircraft.netVelocity.linear.x * aircraft.netVelocity.linear.x +
                        aircraft.netVelocity.linear.z * aircraft.netVelocity.linear.z) * MetresPerSecondToKnots;

                    (int state, bool ident) = transponderOf?.Invoke(aircraft) ?? (AtcAircraft.StateUnknown, false);

                    list.Add(new AtcAircraft(
                        owner, aircraft.netId, aircraft.flightPlan?.callsign ?? "",
                        nickname(aircraft.owner == Sim.Obj.Owner.Network ? aircraft.ownerNuid : default) ?? "",
                        Squawk(squawkOf(aircraft)),
                        latitude, longitude,
                        Finite(position.geo.y * Sim.FEET_PER_METRE), Finite(speed),
                        // JoinFS holds pitch nose down positive, and bank as the simulator's PLANE BANK DEGREES (left wing down positive)
                        Finite(-position.angles.x * (180.0 / Math.PI)), Finite(-position.angles.z * (180.0 / Math.PI)),
                        Heading(position.angles.y),
                        Plan(aircraft.flightPlan), state, ident));
                }
                return list;
            }
        }

        /// <summary>The code as four decimal digits from the BCD value; a value that is not a code (a digit above 7) is 0.</summary>
        public static int Squawk(int bcd)
        {
            if (bcd < 0 || bcd > 0x7777) return 0;
            int code = 0;
            for (int shift = 12; shift >= 0; shift -= 4)
            {
                int digit = bcd >> shift & 0xF;
                if (digit > 7) return 0;
                code = code * 10 + digit;
            }
            return code;
        }

        static AtcFlightPlanData Plan(Sim.FlightPlan plan) => plan == null ? AtcFlightPlanData.Empty : new AtcFlightPlanData(
            plan.rules ?? "", plan.icaoType ?? "", plan.speed ?? "", plan.departure ?? "", plan.altitude ?? "",
            plan.destination ?? "", plan.alternate ?? "", plan.remarks ?? "", plan.route ?? "");

        static double Finite(double value) => double.IsFinite(value) ? value : 0.0;

        /// <summary>Compass degrees in [0, 360): a recorder's running heading is unwrapped (361, 722, ...).</summary>
        static double Heading(double radians)
        {
            if (!double.IsFinite(radians)) return 0.0;
            double degrees = radians * (180.0 / Math.PI) % 360.0;
            return degrees < 0.0 ? degrees + 360.0 : degrees;
        }

        static ulong Bits(NodeId id)
        {
            Span<byte> bytes = stackalloc byte[NodeId.WireSize];
            id.WriteTo(bytes);
            return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }
    }
}
