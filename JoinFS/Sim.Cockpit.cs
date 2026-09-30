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
        /// Schedule follow aircraft
        /// </summary>
        /// <param name="aircraft"></param>
        public void ScheduleFollow(Aircraft aircraft)
        {
            // check if not scheduled
            // set scheduled follow
            followAircraft ??= aircraft;
            WakeSimThread();
        }

        /// <summary>
        /// Follow another aircraft
        /// </summary>
        /// <param name="aircraft">Aircraft</param>
        public void FollowAircraft(Aircraft aircraft)
        {
            // get aircraft position
            Pos position = aircraft ?. Position;
            // check for valid aircraft
            if (userAircraft != null && aircraft.owner != Obj.Owner.Me && position != null)
            {
                // get rate of change of geodesic position
                double xRate = Vector.GeodesicDistance(position.geo.x, position.geo.z, position.geo.x + Vector.GEODESIC_EPSILON, position.geo.z);
                double zRate = Vector.GeodesicDistance(position.geo.x, position.geo.z, position.geo.x, position.geo.z + Vector.GEODESIC_EPSILON);

                // get distance
                double distance = (double)Settings.Default.FollowDistance;

                // set position
                Pos newPosition = new();
                newPosition.geo.x = position.geo.x - (distance * Math.Sin(position.angles.y)) / xRate * Vector.GEODESIC_EPSILON;
                newPosition.geo.z = position.geo.z - (distance * Math.Cos(position.angles.y)) / zRate * Vector.GEODESIC_EPSILON;
                newPosition.geo.y = position.geo.y;
                newPosition.angles = position.angles.Clone();

                // update aircraft
                UpdateObject(userAircraft, newPosition, aircraft.netVelocity);
            }
        }

        /// <summary>
        /// State before entering another aircraft
        /// </summary>
        bool savedBroadcast;
        Pos savedPosition = new();
        Vel savedVelocity = new();

        /// <summary>
        /// Schedule an enter
        /// </summary>
        /// <param name="aircraft"></param>
        public void ScheduleEnterAircraft(Aircraft aircraft)
        {
            // check if not scheduled
            // schedule
            enterAircraft ??= aircraft;
            WakeSimThread();
        }

        /// <summary>
        /// Enter the cockpit of another aircraft
        /// </summary>
        /// <param name="aircraft">Aircraft</param>
        public bool EnterAircraft(Aircraft aircraft)
        {
            // check that the aircraft can be entered
            if (aircraft.owner != Obj.Owner.Me)
            {
                // get aircraft position
                Pos aircraftPosition = aircraft ?. Position;
                // check user aircraft
                if (userAircraft != null && userAircraft.SimValid && aircraftPosition != null)
                {
                    // save broadcast state
                    savedBroadcast = userAircraft.broadcast;
                    // switch off broadcast
                    userAircraft.broadcast = false;
                    // under remote control
                    userAircraft.remoteFlightControl = true;
                    ResetObject(userAircraft);

                    // get entered aircraft position and velocity
                    Pos position = userAircraft.simPosition;
                    Vel velocity = userAircraft.netVelocity;

                    // save position of user aircraft
                    savedPosition = position.Clone();
                    // save velocity
                    savedVelocity = new Vel(velocity.linear.InvRotate(position.angles), velocity.angular.Clone(), velocity.acc.InvRotate(position.angles));

                    // update aircraft
                    UpdateObject(userAircraft, aircraftPosition, aircraft.netVelocity);
                    // copy net time
                    userAircraft.netRealTime = aircraft.netRealTime;
                    userAircraft.netStateTime = aircraft.netStateTime;
                    userAircraft.netSimTime = aircraft.netSimTime;

                    // update net position
                    userAircraft.netPosition = aircraft.Position.Clone();

                    // set entered aircraft
                    enteredAircraft = aircraft;
                    // remove aircraft from simulator
                    RemoveObjectFromSim(aircraft);

                    // check if aircraft is broadcast
                    if (IsBroadcast(aircraft) && main.network.Connected)
                    {
                        // notify session
                        main.network.SimSender.SendRemoveObjectMessage(aircraft.netId);
                    }

                    // delay variable broadcast
                    userAircraft.variableStartTime = main.ElapsedTime + 6.0;

                    // refresh
#if !SERVER && !CONSOLE
                    main.aircraftForm ?. refresher.Schedule();
                    main.objectsForm ?. refresher.Schedule();
#endif

                    // entered
                    return true;
                }
            }

            // not entered
            return false;
        }

        /// <summary>
        /// Schedule a leave
        /// </summary>
        public void ScheduleLeave()
        {
            // leave
            leaveAircraft = true;
            WakeSimThread();
        }

        /// <summary>
        /// Leave the currently entered cockpit
        /// </summary>
        public void LeaveAircraft()
        {
            // check for entered aircraft
            if (enteredAircraft != null)
            {
                // check user aircraft
                if (userAircraft != null)
                {
                    // reset aircraft
                    ResetObject(userAircraft);
                    // update position and velocity
                    UpdateObject(userAircraft, savedPosition, savedVelocity);
                    // restore broadcast
                    userAircraft.broadcast = savedBroadcast;
                    // reset remote control state
                    userAircraft.remoteFlightControl = false;
                }

                // get entered aircraft
                Aircraft aircraft = enteredAircraft;
                // no longer in other aircraft
                enteredAircraft = null;
                // remove aircraft
                RemoveObjectFromList(aircraft);

                // refresh
#if !SERVER && !CONSOLE
                main.aircraftForm ?. refresher.Schedule();
                main.objectsForm ?. refresher.Schedule();
#endif
            }
        }

        /// <summary>
        /// Set share cockpit state
        /// </summary>
        /// <param name="nodeGuid">Guid of owner node</param>
        /// <param name="shareCockpit">State</param>
        public void ShareCockpit(NodeId nodeNuid, byte share)
        {
            // check if aircraft found
            if (objectList.Find(o => o.ownerNuid == nodeNuid && o is Aircraft && (o as Aircraft).user) is Aircraft aircraft)
            {
                // set state
                aircraft.cockpitShare = share;
            }
        }
    }
}
