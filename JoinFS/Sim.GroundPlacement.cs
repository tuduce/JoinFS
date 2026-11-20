using System;

namespace JoinFS
{
    public partial class Sim
    {
        /// <summary>
        /// How close the object in the local sim must be to the received position (metres) for its own
        /// ground reading to count as the ground under the aircraft. Swift keeps elevations for 50 m.
        /// </summary>
        const double LocalGroundRadius = 50.0;

        /// <summary>Horizontal distance between an object's position in the local sim and a received position, metres</summary>
        static double HorizontalDistance(Pos simPosition, ref AircraftPosition position)
        {
            const double earthRadius = 6371000.0;
            // Pos.geo: x longitude, z latitude, radians
            double dLat = position.latitude - simPosition.geo.z;
            double dLon = position.longitude - simPosition.geo.x;
            if (dLon > Math.PI) dLon -= 2.0 * Math.PI;
            else if (dLon < -Math.PI) dLon += 2.0 * Math.PI;
            double x = dLon * Math.Cos(position.latitude);
            return earthRadius * Math.Sqrt(dLat * dLat + x * x);
        }

        /// <summary>
        /// Ground log: one line whenever an aircraft's placement state or the source of its CG height
        /// changes (Monitor, network), with the values it was placed by
        /// </summary>
        void LogGroundPlacement(Aircraft aircraft, GroundPlacement.Result placement, double localGround, double adjustment)
        {
            if (aircraft.groundLogState == placement.State && aircraft.groundLogCgSource == placement.CgSource)
            {
                return;
            }
            aircraft.groundLogState = placement.State;
            aircraft.groundLogCgSource = placement.CgSource;
            main.MonitorNetwork("GroundPlacement '" + aircraft.flightPlan.callsign + "' state=" + placement.State +
                " weight=" + placement.Weight.ToString("F2") +
                " localGround=" + (double.IsNaN(localGround) ? "n/a" : localGround.ToString("F2") + "m") +
                " cg=" + placement.CgHeight.ToString("F2") + "m (" + placement.CgSource + ")" +
                " adjustment=" + (adjustment * 100.0).ToString("F0") + "cm" +
                " altitude=" + placement.Altitude.ToString("F2") + "m" +
                " groundToSim=" + aircraft.groundToSim);
        }
    }
}
