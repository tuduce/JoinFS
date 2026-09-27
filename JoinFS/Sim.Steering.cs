#if SIMCONNECT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using JoinFS.Properties;
using JoinFS.Net;




#if SIMCONNECT
#if P3D
//using LockheedMartin.Prepar3D.SimConnect;
using Microsoft.FlightSimulator.SimConnect;
#else
using Microsoft.FlightSimulator.SimConnect;
#endif
#endif

namespace JoinFS
{
    public partial class Sim
    {
        /// <summary>
        /// Update object velocity in the simulator
        /// </summary>
        /// <param name="aircraft"></param>
        void UpdateSimObjectVelocity(Obj obj)
        {
            try
            {
                // check for controlled object with valid position
                if (simconnect != null && obj.remoteFlightControl && obj.SimValid && obj.NetValid && obj.Created)
                {
                    // check if object is paused
                    if (obj.paused)
                    {
                        // reset to target position
                        UpdateObject(obj, obj.netPosition);
                        // zero sim velocity
                        simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, new ObjectVelocity());
#if (FS2020 || FS2024)
                        // set orientation
                        simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(obj.netPosition.angles));
                        obj.simPosition.angles = obj.netPosition.angles.Clone();
#endif
                    }
                    else
                    {
                        float delay = 0.0f;
                        if (obj.owner == Obj.Owner.Network)
                        {
                            // Pass the network delay through a low-pass filter to smooth out the values
                            // and avoid jittering.
                            // Get the current delay and the previous delay
                            // Previous delay is a property of a node, but assigning it to the object
                            // makes for quicker access to the value. Ugly, but it here time matters.
                            float prevDelay = obj.prevDelay;
                            delay = main.network.GetNodeRTT(obj.ownerNuid);
                            float alpha = 0.75f;
                            delay = alpha * delay + (1.0f - alpha) * prevDelay;
                            obj.prevDelay = delay;
                        }

                        // calculate time deltas
                        double simDeltaTime = main.ElapsedTime - obj.simTime;
                        // delay is measured round-trip, so divide by two
                        double netDeltaTime = obj.netRealTime - obj.netStateTime + main.ElapsedTime - obj.netSimTime + 0.52*delay;
                        // limit extraploation to two seconds
                        simDeltaTime = Math.Min(2.0, Math.Max(-2.0, simDeltaTime));
                        netDeltaTime = Math.Min(2.0, Math.Max(-2.0, netDeltaTime));
                        // extrapolate positions and velocity
                        Pos simPosition = obj.simPosition.Extrapolate(obj.netVelocity, simDeltaTime);
                        Pos netPosition = obj.netPosition.Extrapolate(obj.netVelocity, netDeltaTime);
                        Vel netVelocity = obj.netVelocity.Extrapolate(netDeltaTime);

                        // get V between network and sim positions
                        double distance = Vector.GeodesicDistance(simPosition.geo.x, simPosition.geo.z, netPosition.geo.x, netPosition.geo.z);
                        double bearing = Vector.GeodesicBearing(simPosition.geo.x, simPosition.geo.z, netPosition.geo.x, netPosition.geo.z);

                        // largest difference in altitude before reset
                        double altitudeDeltaLimit = 50.0;
#if (FS2020 || FS2024)
                        // FS2020 has an issue where the aircraft remains glued to the ground, so reset much earlier when the altitude diverts on the ground
                        if (simPosition.ground != 0) altitudeDeltaLimit = 0.2;
#endif

                        // check if object is beyond specific distance
                        if (distance > 50.0 || Math.Abs(simPosition.geo.y - netPosition.geo.y) > altitudeDeltaLimit)
                        {
                            // reset to target position
                            UpdateObject(obj, netPosition);
                            // update sim velocity
                            simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, new ObjectVelocity(netVelocity.linear, netVelocity.angular, netVelocity.acc));
#if (FS2020 || FS2024)
                            // set orientation
                            simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
                            obj.simPosition.angles = netPosition.angles.Clone();
#endif
                        }
                        else
                        {
                            // get world space relative position
                            Vector deltaGeo = new(distance * Math.Sin(bearing), netPosition.geo.y - simPosition.geo.y, distance * Math.Cos(bearing));
                            // get delta between current and network orientations
                            Vector deltaAngles = Vector.AnglesDelta(simPosition.angles, netPosition.angles);

                            // add delta to velocity to catch up
                            netVelocity.linear += deltaGeo * 1.5;

                            // only catch up the orientation if no high angular turns are being made
                            if (Math.Abs(simPosition.angles.x) < Math.PI * 0.25 && Math.Abs(simPosition.angles.z) < Math.PI * 0.5)
                            {
                                if (Math.Abs(netVelocity.angular.x) < 0.2 && Math.Abs(netVelocity.angular.y) < 0.2 && Math.Abs(netVelocity.angular.z) < 0.2)
                                {
                                    // add delta to angular velocity to catch up
                                    netVelocity.angular += deltaAngles * 1.5;
                                }
#if (FS2020 || FS2024)
                                // set orientation
                                simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
                                obj.simPosition.angles = netPosition.angles.Clone();
#endif
                            }
                            else
                            {
                                // set orientation
                                simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
                                obj.simPosition.angles = netPosition.angles.Clone();
                            }

                            // update sim velocity
                            simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, new ObjectVelocity(netVelocity.linear.InvRotate(simPosition.angles), netVelocity.angular * 0.3, netVelocity.acc.InvRotate(simPosition.angles)));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("ERROR - " + ex.Message);
            }
        }

        /// <summary>
        /// Frame counter
        /// </summary>
        public int frameCount = 0;

        /// <summary>
        /// A FRAME event arrived during the current dispatch
        /// </summary>
        bool frameDue = false;

        /// <summary>
        /// Per-frame work, once per dispatch that contained a FRAME event
        /// </summary>
        void ProcessFrame()
        {
            if (frameDue == false)
            {
                return;
            }
            frameDue = false;

            // for each object
            foreach (var obj in objectList)
            {
                // check for aerobatics
//                if (Math.Abs(obj.simPosition.angles.x) > Math.PI * 0.25 || Math.Abs(obj.simPosition.angles.z) > Math.PI * 0.5)
                {
                    // update object velocity
                    UpdateSimObjectVelocity(obj);
                }
            }
        }
    }
}
#endif
