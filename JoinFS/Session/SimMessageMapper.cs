using JoinFS.Net;
using System.Collections.Generic;

namespace JoinFS
{
    /// <summary>
    /// Conversions between the simulator's types (Sim.cs) and canonical messages (JoinFS/Net/Messages).
    /// Pure functions: no network, no Main, no settings (callers pass what they need).
    /// </summary>
    public static class SimMessageMapper
    {
        /// <summary>What an object is, for the protocols that send identity (inline or separately).</summary>
        public static IdentityUpdate BuildIdentity(Sim.Obj obj, uint objectId)
        {
            Sim.Aircraft aircraft = obj as Sim.Aircraft;
            return new IdentityUpdate
            {
                ObjectId = objectId,
                IsAircraft = aircraft != null,
                IsPlane = obj is Sim.Plane,
                Callsign = aircraft?.flightPlan.callsign ?? "",
                Model = obj.ModelTitle,
                Livery = obj.ownerLivery,
                IcaoType = aircraft?.flightPlan.icaoType ?? "",
                IcaoAirline = aircraft?.flightPlan.icaoAirline ?? "",
                Registration = aircraft?.flightPlan.registration ?? "",
                FlightNumber = aircraft?.flightPlan.flightNumber ?? "",
                ClassCode = obj.ownerClassCode,
                Wtc = obj.ownerWtc,
                ClassCodeConfirmed = obj.ownerClassCodeConfirmed,
                TypeRole = (byte)obj.typerole,
            };
        }

        public static PositionUpdate ToPositionUpdate(uint objectId, Sim.Aircraft aircraft, ref Sim.AircraftPosition position, double netTime, bool elevationCorrection)
        {
            PositionStateFlags flags = PositionStateFlags.None;
            if (position.ground != 0) flags |= PositionStateFlags.OnGround;
            if (elevationCorrection) flags |= PositionStateFlags.ElevationCorrection;
            if (aircraft.user) flags |= PositionStateFlags.UserControlled;
            if (aircraft.paused) flags |= PositionStateFlags.Paused;
            return new PositionUpdate
            {
                ObjectId = objectId,
                NetTime = netTime,
                Latitude = position.latitude,
                Longitude = position.longitude,
                Altitude = position.altitude,
                Pitch = position.pitch,
                Bank = position.bank,
                Heading = position.heading,
                VelocityX = position.velocityX,
                VelocityY = position.velocityY,
                VelocityZ = position.velocityZ,
                AngularVelocityX = position.angularVelocityX,
                AngularVelocityY = position.angularVelocityY,
                AngularVelocityZ = position.angularVelocityZ,
                AccelerationX = position.accelerationX,
                AccelerationY = position.accelerationY,
                AccelerationZ = position.accelerationZ,
                Rudder = position.rudder,
                Elevator = position.elevator,
                Aileron = position.aileron,
                BrakeLeft = position.brakeLeft,
                BrakeRight = position.brakeRight,
                Elevation = position.elevation,
                StaticCgToGround = position.staticCgToGround,
                StateFlags = flags,
            };
        }

        public static Sim.AircraftPosition ToAircraftPosition(in PositionUpdate update) => new()
        {
            latitude = update.Latitude,
            longitude = update.Longitude,
            altitude = update.Altitude,
            pitch = update.Pitch,
            bank = update.Bank,
            heading = update.Heading,
            velocityX = update.VelocityX,
            velocityY = update.VelocityY,
            velocityZ = update.VelocityZ,
            angularVelocityX = update.AngularVelocityX,
            angularVelocityY = update.AngularVelocityY,
            angularVelocityZ = update.AngularVelocityZ,
            accelerationX = update.AccelerationX,
            accelerationY = update.AccelerationY,
            accelerationZ = update.AccelerationZ,
            rudder = update.Rudder,
            elevator = update.Elevator,
            aileron = update.Aileron,
            brakeLeft = update.BrakeLeft,
            brakeRight = update.BrakeRight,
            elevation = update.Elevation,
            radarAltitude = 0.0f,
            ground = (update.StateFlags & PositionStateFlags.OnGround) != 0 ? 1 : 0,
            staticCgToGround = update.StaticCgToGround,
        };

        public static ObjectPositionUpdate ToObjectPositionUpdate(Sim.Obj obj, ref Sim.ObjectPositionVelocity pv, bool elevationCorrection)
        {
            PositionStateFlags flags = PositionStateFlags.None;
            if (pv.ground != 0) flags |= PositionStateFlags.OnGround;
            if (elevationCorrection) flags |= PositionStateFlags.ElevationCorrection;
            if (obj.paused) flags |= PositionStateFlags.Paused;
            return new ObjectPositionUpdate
            {
                ObjectId = obj.netId,
                NetTime = obj.simTime,
                Latitude = pv.latitude,
                Longitude = pv.longitude,
                Altitude = pv.altitude,
                Pitch = pv.pitch,
                Bank = pv.bank,
                Heading = pv.heading,
                VelocityX = pv.velocityX,
                VelocityY = pv.velocityY,
                VelocityZ = pv.velocityZ,
                AngularVelocityX = pv.angularVelocityX,
                AngularVelocityY = pv.angularVelocityY,
                AngularVelocityZ = pv.angularVelocityZ,
                AccelerationX = pv.accelerationX,
                AccelerationY = pv.accelerationY,
                AccelerationZ = pv.accelerationZ,
                Height = pv.height,
                StateFlags = flags,
            };
        }

        public static Sim.ObjectPositionVelocity ToPositionVelocity(in ObjectPositionUpdate update) => new()
        {
            latitude = update.Latitude,
            longitude = update.Longitude,
            altitude = update.Altitude,
            pitch = update.Pitch,
            bank = update.Bank,
            heading = update.Heading,
            velocityX = update.VelocityX,
            velocityY = update.VelocityY,
            velocityZ = update.VelocityZ,
            angularVelocityX = update.AngularVelocityX,
            angularVelocityY = update.AngularVelocityY,
            angularVelocityZ = update.AngularVelocityZ,
            accelerationX = update.AccelerationX,
            accelerationY = update.AccelerationY,
            accelerationZ = update.AccelerationZ,
            height = update.Height,
            ground = (update.StateFlags & PositionStateFlags.OnGround) != 0 ? 1 : 0,
        };

        /// <summary>The simulator's three variable dictionaries as one list of entries.</summary>
        public static List<VariableEntry> ToVariableEntries(Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s)
        {
            var entries = new List<VariableEntry>((integers?.Count ?? 0) + (floats?.Count ?? 0) + (string8s?.Count ?? 0));
            if (integers != null) foreach (var kv in integers) entries.Add(new VariableEntry { Vuid = kv.Key, Kind = VariableKind.Int32, IntValue = kv.Value });
            if (floats != null) foreach (var kv in floats) entries.Add(new VariableEntry { Vuid = kv.Key, Kind = VariableKind.Float32, FloatValue = kv.Value });
            if (string8s != null) foreach (var kv in string8s) entries.Add(new VariableEntry { Vuid = kv.Key, Kind = VariableKind.String8, StringValue = kv.Value });
            return entries;
        }

        /// <summary>Entries back into the simulator's three dictionaries (null where there are none of that kind).</summary>
        public static void SplitVariables(List<VariableEntry> entries, out Dictionary<uint, int> integers, out Dictionary<uint, float> floats, out Dictionary<uint, string> string8s)
        {
            integers = null;
            floats = null;
            string8s = null;
            foreach (var entry in entries)
            {
                switch (entry.Kind)
                {
                    case VariableKind.Int32: (integers ??= [])[entry.Vuid] = entry.IntValue; break;
                    case VariableKind.Float32: (floats ??= [])[entry.Vuid] = entry.FloatValue; break;
                    case VariableKind.String8: (string8s ??= [])[entry.Vuid] = entry.StringValue; break;
                }
            }
        }

        public static FlightPlanUpdate ToFlightPlanUpdate(NodeId owner, uint netId, Sim.FlightPlan flightPlan) => new()
        {
            Owner = owner,
            ObjectId = netId,
            FormatVersion = 1,
            IcaoType = flightPlan.icaoType,
            Departure = flightPlan.departure,
            Destination = flightPlan.destination,
            Rules = flightPlan.rules,
            Route = flightPlan.route,
            Remarks = flightPlan.remarks,
            Alternate = flightPlan.alternate,
            Speed = flightPlan.speed,
            Altitude = flightPlan.altitude,
            Callsign = flightPlan.callsign,
            Registration = flightPlan.registration,
            IcaoAirline = flightPlan.icaoAirline,
            FlightNumber = flightPlan.flightNumber,
        };

        /// <summary>Copy a received flight plan into <paramref name="target"/> (airports upper-cased).</summary>
        public static void CopyTo(in FlightPlanUpdate update, Sim.FlightPlan target)
        {
            target.icaoType = update.IcaoType;
            target.departure = update.Departure.ToUpperInvariant();
            target.destination = update.Destination.ToUpperInvariant();
            target.rules = update.Rules;
            target.route = update.Route;
            target.remarks = update.Remarks;
            target.alternate = update.Alternate;
            target.speed = update.Speed;
            target.altitude = update.Altitude;
            target.callsign = update.Callsign;
            target.registration = update.Registration;
            target.icaoAirline = update.IcaoAirline;
            target.flightNumber = update.FlightNumber;
        }
    }
}
