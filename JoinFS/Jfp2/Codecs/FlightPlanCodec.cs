using System;
using System.Buffers.Binary;
using System.Collections.Generic;

// docs/protocol-v2-implementation-plan.md Phase 5: a mechanical port of the legacy FlightPlan message
// (docs/network-protocol.md §8.5, docs/protocol-changes-v26.4-v26.5.md §1.4). Every field is always
// present (no dataVersion>=21003/21006 conditional reads - the legacy write side already writes all
// 13 fields unconditionally on any current build, per protocol-changes-v26.4-v26.5.md §1.4, so this
// simply collapses the version gate the same way Phase 2/3's codecs already did for Status/Identity).
//
// No separate OwnerNuid field - matching the precedent already set by VariableSync/Position/Identity
// (design doc §6.5 and the Phase 3/4 implementation notes): the owner is implicit from the JFP2
// sender, since a JFP2 peer only ever sends FlightPlan about its own aircraft (see
// Network.SendFlightPlanUpdate's call sites). True third-party relay is out of scope until
// Jfp2Bridge exists.

namespace JoinFS.Jfp2.Codecs
{
    public struct FlightPlanUpdate
    {
        /// <summary>Obj.netId - the aircraft this flight plan describes.</summary>
        public uint ObjectId;
        public string IcaoType;
        public string Departure;
        public string Destination;
        public string Rules;
        public string Route;
        public string Remarks;
        public string Alternate;
        public string Speed;
        public string Altitude;
        public string Callsign;
        public string Registration;
        public string IcaoAirline;
        public string FlightNumber;
    }

    public sealed class FlightPlanV1Codec : ICodec<FlightPlanUpdate>
    {
        public byte MessageClass => MessageClasses.FlightPlan;
        public byte SchemaVersion => 1;

        public int Encode(in FlightPlanUpdate v, Span<byte> dest)
        {
            var bytes = new List<byte>(160);
            Span<byte> id = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(id, v.ObjectId);
            bytes.AddRange(id.ToArray());
            WireText.WriteString(bytes, v.IcaoType);
            WireText.WriteString(bytes, v.Departure);
            WireText.WriteString(bytes, v.Destination);
            WireText.WriteString(bytes, v.Rules);
            WireText.WriteString(bytes, v.Route);
            WireText.WriteString(bytes, v.Remarks);
            WireText.WriteString(bytes, v.Alternate);
            WireText.WriteString(bytes, v.Speed);
            WireText.WriteString(bytes, v.Altitude);
            WireText.WriteString(bytes, v.Callsign);
            WireText.WriteString(bytes, v.Registration);
            WireText.WriteString(bytes, v.IcaoAirline);
            WireText.WriteString(bytes, v.FlightNumber);
            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public FlightPlanUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new FlightPlanUpdate();
            v.ObjectId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            v.IcaoType = WireText.ReadString(src, ref i);
            v.Departure = WireText.ReadString(src, ref i);
            v.Destination = WireText.ReadString(src, ref i);
            v.Rules = WireText.ReadString(src, ref i);
            v.Route = WireText.ReadString(src, ref i);
            v.Remarks = WireText.ReadString(src, ref i);
            v.Alternate = WireText.ReadString(src, ref i);
            v.Speed = WireText.ReadString(src, ref i);
            v.Altitude = WireText.ReadString(src, ref i);
            v.Callsign = WireText.ReadString(src, ref i);
            v.Registration = WireText.ReadString(src, ref i);
            v.IcaoAirline = WireText.ReadString(src, ref i);
            v.FlightNumber = WireText.ReadString(src, ref i);
            return v;
        }
    }
}
