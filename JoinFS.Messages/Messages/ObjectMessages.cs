using System;
using System.Collections.Generic;

// Canonical messages about simulated objects (aircraft, boats, vehicles). The structs moved here
// from JoinFS/Jfp2/Codecs (PositionUpdate, IdentityUpdate, VariableSyncUpdate, EventUpdate,
// FlightPlanUpdate) were already protocol-neutral; the JFP2 codecs encode them, and the legacy
// plugin maps its own messages onto them.

namespace JoinFS.Net
{
    [Flags]
    public enum PositionStateFlags : byte
    {
        None = 0,
        OnGround = 1 << 0,
        /// <summary>The sender applies elevation correction (Settings.ElevationCorrection).</summary>
        ElevationCorrection = 1 << 1,
        /// <summary>The object is a user's own aircraft, not an additional/AI object.</summary>
        UserControlled = 1 << 2,
        Paused = 1 << 3,
    }

    /// <summary>
    /// One aircraft's motion state for one tick. Everything that identifies the aircraft (model,
    /// callsign, ...) is in <see cref="IdentityUpdate"/> and is only sent when it changes.
    /// </summary>
    public struct PositionUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.Position;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        /// <summary>Obj.netId, or uint.MaxValue for the shared-cockpit update of the recipient's own aircraft.</summary>
        public uint ObjectId;
        public double NetTime;
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public float Pitch;
        public float Bank;
        public float Heading;
        public float VelocityX, VelocityY, VelocityZ;
        public float AngularVelocityX, AngularVelocityY, AngularVelocityZ;
        public float AccelerationX, AccelerationY, AccelerationZ;
        /// <summary>-1..1 control axis values.</summary>
        public float Rudder, Elevator, Aileron, BrakeLeft, BrakeRight;
        public float Elevation;
        /// <summary>"STATIC CG TO GROUND", feet; NaN when the sender didn't provide it.</summary>
        public float StaticCgToGround;
        public PositionStateFlags StateFlags;
    }

    /// <summary>
    /// One non-aircraft object's (boat, vehicle, ...) motion state for one tick. Identity travels
    /// separately in <see cref="IdentityUpdate"/> with IsAircraft = false.
    /// </summary>
    public struct ObjectPositionUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.ObjectPosition;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint ObjectId;
        public double NetTime;
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public float Pitch;
        public float Bank;
        public float Heading;
        public float VelocityX, VelocityY, VelocityZ;
        public float AngularVelocityX, AngularVelocityY, AngularVelocityZ;
        public float AccelerationX, AccelerationY, AccelerationZ;
        public float Height;
        /// <summary>OnGround, ElevationCorrection and Paused are meaningful here.</summary>
        public PositionStateFlags StateFlags;
    }

    /// <summary>
    /// What an object is: model, livery, type and (for aircraft) callsign and registration. Sent
    /// when it changes, plus a periodic heartbeat, and always before the object's first position.
    /// </summary>
    public struct IdentityUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.Identity;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint ObjectId;
        public bool IsAircraft;
        public bool IsPlane;
        public string Callsign;
        public string Model;
        public string Livery;
        public string IcaoType;
        public string IcaoAirline;
        public string Registration;
        public string FlightNumber;
        public string ClassCode;
        public string Wtc;
        public bool ClassCodeConfirmed;
        public byte TypeRole;

        public readonly bool SameAs(in IdentityUpdate other) =>
            ObjectId == other.ObjectId && IsAircraft == other.IsAircraft && IsPlane == other.IsPlane
            && Callsign == other.Callsign && Model == other.Model && Livery == other.Livery
            && IcaoType == other.IcaoType && IcaoAirline == other.IcaoAirline && Registration == other.Registration
            && FlightNumber == other.FlightNumber && ClassCode == other.ClassCode && Wtc == other.Wtc
            && ClassCodeConfirmed == other.ClassCodeConfirmed && TypeRole == other.TypeRole;
    }

    public enum VariableKind : byte
    {
        Int32 = 0,
        Float32 = 1,
        String8 = 2,
    }

    public struct VariableEntry
    {
        /// <summary>VariableMgr.CreateVuid hash of the variable name.</summary>
        public uint Vuid;
        public VariableKind Kind;
        public int IntValue;
        public float FloatValue;
        public string StringValue;
    }

    /// <summary>A batch of simulator variable values for one object.</summary>
    public struct VariableSyncUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.VariableSync;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        // No Owner field: unlike legacy's wire encoding, every other canonical object message
        // (Position, ObjectPosition, Identity, Event, RemoveObject) identifies the owner purely via
        // MessageMeta.Sender, which the core's translation path already preserves as the true
        // origin through a relay (docs/reference/joinfs-architecture.md §6). Confirmed redundant
        // for VariableSync too: this codebase's only sender always sets it to its own id (trivially
        // equal to what Sender reads as), JFP2's own codec never puts it on the wire at all, and no
        // golden fixture exercises a divergent value - see docs/network-plugin-architecture.md
        // §2.11 item 3. LegacyPlugin still reads/writes the wire's Owner field (for byte fidelity)
        // but logs if a received value ever disagrees with meta.Sender, as insurance.
        public uint ObjectId;
        public List<VariableEntry> Entries;
    }

    /// <summary>A simulator event (key event with data) raised on an object.</summary>
    public struct EventUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.Event;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint ObjectId;
        public uint EventId;
        public uint Data;
    }

    /// <summary>The sender stopped showing one of its objects.</summary>
    public struct RemoveObject : IMessage
    {
        public static MessageKind Kind => MessageKind.RemoveObject;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint ObjectId;
    }

    public struct FlightPlanUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.FlightPlan;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        /// <summary>The node that owns the aircraft.</summary>
        public NodeId Owner;
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

    /// <summary>Ask radar/ATC views to show or hide an object (send-only today: no receiver acts on it).</summary>
    public struct ShowOnRadar : IMessage
    {
        public static MessageKind Kind => MessageKind.ShowOnRadar;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public NodeId Owner;
        public uint ObjectId;
        public bool Show;
    }
}
