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
        /// Remove object from simulator
        /// </summary>
        /// <param name="obj">Object</param>
        public void RemoveObjectFromSim(Obj obj)
        {
            // check if object is in the sim
            if (Connected && obj.Injected && obj.Created)
            {
#if XPLANE || CONSOLE
                // remove from xplane
                xplane.RemoveAircraft(obj.simId);
#elif SIMCONNECT
                // stop its position feed, then remove from simconnect
                CancelPositionFeed(obj);
                simconnect?.RemoveObject(obj.simId, Requests.REMOVE_OBJECT);
#endif

                // check for aircraft
                if (obj is Aircraft)
                {
                    // aircraft
                    Aircraft aircraft = obj as Aircraft;
                    // message
                    main.MonitorEvent("Removing aircraft '" + aircraft.flightPlan.callsign + "' - User '" + ((aircraft.owner == Obj.Owner.Network) ? aircraft.ownerNuid.ToString() : "Me") + "' - ID '" + aircraft.simId + "' - Sub '" + obj.ModelTitle + "'");
                }
                else
                {
                    // message
                    main.MonitorEvent("Removing object - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - ID '" + obj.simId + "' - Sub '" + obj.ModelTitle + "'");
                }

                // reset sim ID
                obj.simId = uint.MaxValue;
                // create variables
                CreateModelVariables(obj);
            }

            // update match
            if (obj.Injected)
            {
                UpdateObject(obj, obj.ownerModel, obj.ownerLivery, obj.ownerIcaoType, obj.ownerIcaoAirline, obj.ownerClassCode, obj.ownerWtc, obj.ownerClassCodeConfirmed, obj.typerole);
            }
        }

        /// <summary>
        /// Remove an object from the list
        /// </summary>
        /// <param name="object"></param>
        void RemoveObjectFromList(Obj obj)
        {
            // check if object is in list
            if (objectList.Contains(obj))
            {
                // check for aircraft
                if (obj is Aircraft)
                {
                    // aircraft
                    Aircraft aircraft = obj as Aircraft;
                    // message
                    main.MonitorEvent("Delisting aircraft '" + aircraft.flightPlan.callsign + "' - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - Model '" + obj.ownerModel + "'");
                }
                else
                {
                    // message
                    main.MonitorEvent("Delisting object - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - Model '" + obj.ownerModel + "'");
                }

#if SIMCONNECT
                // stop its position feed
                CancelPositionFeed(obj);
#endif
                // remove object from the list
                RemoveFromListAndIndex(obj);

                // check for aircraft
                if (obj is Aircraft)
                {
                    // check for weather aircraft
                    if (obj == weatherAircraft)
                    {
                        // disable weather aircraft
                        SetWeatherAircraft(null);
                    }
                    // check for cockpit aircraft
                    if (obj == enteredAircraft)
                    {
                        // leave cockpit
                        LeaveAircraft();
                    }
                }

                // check for creating aircraft
                if (obj == creatingObject)
                {
                    // no longer creating
                    creatingObject = null;
                }

                // check for user aircraft
                if (obj == userAircraft)
                {
                    // reset user aircraft
                    userAircraft = null;
                }

                // check for tracking heading object
                if (obj == trackHeadingObject)
                {
                    // reset tracking object
                    trackHeadingObject = null;
                }

                // check for tracking bearing object
                if (obj == trackBearingObject)
                {
                    // reset tracking object
                    trackBearingObject = null;
                }

                // remove interval masks
                RemoveIntervalMask(obj);

                // check if local object
                if (IsBroadcast(obj) && main.network.Connected)
                {
                    // notify session
                    main.network.SimSender.SendRemoveObjectMessage(obj.netId);
                }

                // stop variable requests
                obj.variableSet ?. StopRequests();
            }
        }

        /// <summary>
        /// Remove object
        /// </summary>
        /// <param name="object">Object to remove</param>
        void RemoveObject(Obj obj)
        {
            // remove from simulator
            RemoveObjectFromSim(obj);
            // remove from list
            RemoveObjectFromList(obj);
        }

        /// <summary>
        /// Remove all objects belonging to a node
        /// </summary>
        /// <param name="ownerGuid">Node</param>
        public void RemoveObject(NodeId ownerNuid)
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object is controlled by leaving node
                if (obj.ownerNuid == ownerNuid)
                {
                    // add object to remove list
                    removeList.Add(obj);
                }
            }

            DoRemove();
        }

        /// <summary>
        /// Remove all objects belonging to a specific node object
        /// </summary>
        /// <param name="ownerGuid">Node</param>
        public void RemoveObject(NodeId ownerNuid, uint netId)
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object has link to recorder object
                if (obj.ownerNuid == ownerNuid && obj.netId == netId)
                {
                    // add object to remove list
                    removeList.Add(obj);
                }
            }

            DoRemove();
        }

        /// <summary>
        /// Remove all objects belonging to a node
        /// </summary>
        /// <param name="ownerGuid">Node</param>
        public void RemoveObjectsFromSim(NodeId ownerNuid)
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object is controlled by leaving node
                if (obj.ownerNuid == ownerNuid)
                {
                    RemoveObjectFromSim(obj);
                }
            }
        }

        /// <summary>
        /// Remove all controlled objects
        /// </summary>
        /// <param name="ownerGuid">Node</param>
        public void RemoveInjectedObjects()
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object is injected by leaving node
                if (obj.Injected)
                {
                    // add object to remove list
                    removeList.Add(obj);
                }
            }

            DoRemove();
        }

        /// <summary>
        /// Schedule a remove
        /// </summary>
        /// <param name="model"></param>
        public void ScheduleRemoveObjects()
        {
            // set schedule
            scheduleRemoveObjects = true;
            WakeSimThread();
        }

        /// <summary>
        /// Schedule a remove
        /// </summary>
        /// <param name="model"></param>
        public void RemoveObjects()
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // only remove objects we injected
                if (obj.Injected)
                {
                    // add object to remove list
                    removeList.Add(obj);
                }
            }

            DoRemove();
        }

        /// <summary>
        /// Schedule a remove
        /// </summary>
        /// <param name="model"></param>
        public void ScheduleRemoveModel(string model)
        {
            // set schedule
            scheduleRemove ??= model;
            WakeSimThread();
        }

        /// <summary>
        /// Remove all object using a given model
        /// </summary>
        /// <param name="model">Model</param>
        public void RemoveObjectsFromSim(string model)
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object needs replacing
                if (obj.ownerModel.Equals(model, StringComparison.Ordinal))
                {
                    // remove from simulator
                    RemoveObjectFromSim(obj);
                }
            }
        }

        /// <summary>
        /// Remove all objects in the remove list
        /// </summary>
        void DoRemove()
        {
            // for each object in remove list
            foreach (Obj obj in removeList)
            {
                RemoveObject(obj);
            }

            removeList.Clear();
        }

        /// <summary>
        /// Reset network positioning of object
        /// </summary>
        /// <param name="ownerGuid">Owner guid</param>
        /// <param name="netId">Network ID</param>
        public static void ResetObject(Obj obj)
        {
            // check for valid object
            if (obj != null)
            {
                // reset network times
                obj.netStateTime = 0.0;
                obj.netRealTime = 0.0;
                obj.netSimTime = 0.0;
            }
        }

        /// <summary>
        /// Reset network positioning of object
        /// </summary>
        /// <param name="ownerGuid">Owner guid</param>
        /// <param name="netId">Network ID</param>
        public void ResetObject(NodeId ownerNuid, uint netId)
        {
            // get object
            ResetObject(FindObject(ownerNuid, netId));
        }

        /// <summary>
        /// Update the model for an object
        /// </summary>
        /// <param name="model"></param>
        public async void UpdateObject(Obj obj, string model, string livery, string icaoType, string icaoAirline, string classCode, string wtc, bool classCodeConfirmed, int typerole)
        {
            obj.typerole = typerole;
            // update model
            obj.ownerModel = model;
            obj.ownerIcaoType = icaoType;
            obj.ownerIcaoAirline = icaoAirline;
            obj.ownerLivery = livery;
            obj.ownerClassCode = classCode;
            obj.ownerWtc = wtc;
            obj.ownerClassCodeConfirmed = classCodeConfirmed;
#if FS2024
            // model match - livery is only meaningful as a matching signal on FS2024, which is the
            // only sim that reports a real livery name via SimConnect; other builds still carry the
            // value through (e.g. for network relay) even though they can never populate it locally
            (obj.subModel, obj.subType, obj.subTrace) = await main.substitution?.Match(obj.ownerModel, obj.ownerLivery, obj.ownerIcaoType, obj.ownerIcaoAirline, obj.ownerClassCode, obj.ownerWtc, obj.ownerClassCodeConfirmed, obj.typerole, (obj as Aircraft)?.flightPlan.registration ?? "");
#else
            (obj.subModel, obj.subType, obj.subTrace) = await main.substitution ?. Match(obj.ownerModel, obj.ownerIcaoType, obj.ownerIcaoAirline, obj.ownerClassCode, obj.ownerWtc, obj.ownerClassCodeConfirmed, obj.typerole, (obj as Aircraft)?.flightPlan.registration ?? "");
#endif
            // reset failed flag
            obj.failed = false;
        }

        /// <summary>
        /// Update object position directly
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="positionVelocity">Position</param>
        public void UpdateObject(Obj obj, ref ObjectPosition position)
        {
#if XPLANE || CONSOLE
            // update xplane position
            xplane.UpdateAircraft(obj.simId, ref position);
#elif SIMCONNECT
            // check for valid object
            if (simconnect != null && obj.Created)
            {
                // update object position and velocity - forward on-ground to the sim's own placement whenever the
                // sender reports the object on-ground, for any aircraft type, so the sim's own gear/ground-contact
                // physics handles it instead of fighting an externally-driven altitude every tick (see
                // helicopters-on-elevated-platforms feature)
                ObjectPositionUpdate update = new(ref position)
                {
                    ground = obj.trustingPlatformGround ? 1 : 0
                };
                simconnect.SetData(Definitions.OBJECT_POSITION_UPDATE, obj.simId, update);
                // update stored position
                obj.simPosition = new Pos(ref position);
            }
#endif
            // store current time
            obj.simTime = main.ElapsedTime;
        }

        /// <summary>
        /// Update object velocity directly
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="positionVelocity">Velocity</param>
        public void UpdateObject(Obj obj, ref ObjectVelocity velocity)
        {
#if XPLANE || CONSOLE
            // update xplane position
            xplane.UpdateAircraft(obj.simId, ref velocity);
#elif SIMCONNECT
            // check for valid aircraft
            if (simconnect != null && obj.Created)
            {
                // update object position and velocity
                simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, velocity);
            }
#endif
        }

        /// <summary>
        /// Update object position
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="position">Position</param>
        public void UpdateObject(Obj obj, Pos position)
        {
            // check for valid object
            if (Connected && obj.Created)
            {
                // set position
                ObjectPosition objectPosition = new(position);
                // update object
                UpdateObject(obj, ref objectPosition);
            }
        }

        /// <summary>
        /// Update object velocity
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="velocity">Velocity</param>
        public void UpdateObject(Obj obj, Pos position, Vel velocity)
        {
            // check for valid object
            if (Connected && obj.Created)
            {
                // update object position
                UpdateObject(obj, position);
                ObjectVelocity objectVelocity = new(velocity.linear.InvRotate(position.angles), velocity.angular, velocity.acc.InvRotate(position.angles));
                // update object
                UpdateObject(obj, ref objectVelocity);
            }
        }

        /// <summary>
        /// Update network time
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="netTime">Network time</param>
        /// <param name="receivedAt">Local time (ElapsedTime) the update arrived; 0 when it did not come
        /// off the network (recorder playback, local updates), meaning "now". Using the arrival time
        /// instead of the time the update is processed keeps queueing delays out of the extrapolation.</param>
        public void UpdateObject(Obj obj, double netTime, double receivedAt = 0.0)
        {
            // local time of this update - never before the previous one, so time only moves forward
            double localTime = receivedAt > 0.0 ? Math.Max(receivedAt, obj.netSimTime) : main.ElapsedTime;
            // store remote state time
            obj.netStateTime = netTime;
            // check for first update
            if (obj.netRealTime == 0.0)
            {
                // set position and velocity
                UpdateObject(obj, obj.netPosition, obj.netVelocity);
                // set time
                obj.netRealTime = obj.netStateTime;
            }
            else
            {
                // update estimated network time
                obj.netRealTime += localTime - obj.netSimTime;
                // calculate error between network update and estimated time
                double error = obj.netStateTime - obj.netRealTime;
                // gradually merge to remove error over time
                obj.netRealTime += error * TIME_ERROR_RATE;
            }
            // store local time at which state was updated
            obj.netSimTime = localTime;
        }

        /// <summary>
        /// Update object position and velocity
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="netTime">Network time</param>
        /// <param name="positionVelocity">Position and Velocity</param>
        public void UpdateObject(Obj obj, double netTime, ref ObjectPositionVelocity positionVelocity, double receivedAt = 0.0)
        {
            // check for first update and reject old updates
            if (obj.NetValid == false || netTime > obj.netStateTime)
            {
                // update position
                obj.netPosition = new Pos(ref positionVelocity);
                // save old orientation
                obj.oldEuler = obj.netPosition.angles.Clone();
                // update velocity
                obj.netVelocity = new Vel(ref positionVelocity);

                // update network time
                UpdateObject(obj, netTime, receivedAt);
            }
        }

        /// <summary>
        /// Update object position and velocity
        /// </summary>
        /// <param name="ownerGuid">Owner of the object</param>
        /// <param name="netId">Owner's sim ID</param>
        /// <param name="engine">Aircraft engine</param>
        public Obj UpdateObject(NodeId ownerNuid, uint netId, string model, string livery, string icaoType, string icaoAirline, string classCode, string wtc, bool classCodeConfirmed, int typerole, double netTime, ref ObjectPositionVelocity positionVelocity, double receivedAt = 0.0)
        {
            // get object
            Obj obj = FindObject(ownerNuid, netId);
            if (obj == null)
            {
                // create new object in list
                obj = new(ownerNuid, netId)
                {
                    // set expire time
                    expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME
                };
                // model
                UpdateObject(obj, model, livery, icaoType, icaoAirline, classCode, wtc, classCodeConfirmed, typerole);
                // update position and velocity
                UpdateObject(obj, netTime, ref positionVelocity, receivedAt);
                // create variables
                CreateModelVariables(obj);
                // add to object list
                AddObjectToList(obj);

                // message
                main.MonitorEvent("Listing object - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - Model '" + obj.ownerModel + "'");

                return obj;
            }
            else
            {
                // set expire time
                obj.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;

                // check if model has changed
                if (model.Equals(obj.ownerModel) == false)
                {
                    // check if creating this object
                    if (creatingObject != obj)
                    {
                        // remove object
                        RemoveObjectFromSim(obj);
                    }
                }
                else
                {
                    // update position and velocity
                    UpdateObject(obj, netTime, ref positionVelocity, receivedAt);
                }

                return obj;
            }
        }

        /// <summary>
        /// Pause or unpause an object
        /// </summary>
        /// <param name="ownerNuid">Owner ID</param>
        /// <param name="netId">Network ID</param>
        /// <param name="pause">Pause state</param>
        public void PauseObject(NodeId ownerNuid, uint netId, bool pause)
        {
            // check for valid object
            if (FindObject(ownerNuid, netId) is Obj obj)
            {
                // update state
                obj.paused = pause;
            }
        }

        /// <summary>
        /// Prevent object from timing out
        /// </summary>
        /// <param name="ownerNuid">Owner ID</param>
        /// <param name="netId">Network ID</param>
        public void TouchObject(NodeId ownerNuid, uint netId)
        {
            // check for valid object
            if (FindObject(ownerNuid, netId) is Obj obj)
            {
                // set expire time
                obj.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;
            }
        }

        /// <summary>
        /// Get the object that should be controlled
        /// </summary>
        /// <param name="obj">Object to check</param>
        /// <returns>Controlled object</returns>
        Obj GetControlledObject(Obj obj)
        {
            // check for entered aircraft
            if (userAircraft != null && (obj == enteredAircraft || userAircraft.remoteFlightControl && obj.ownerNuid == main.network.Peers.shareFlightControls && obj is Aircraft && (obj as Aircraft).user))
            {
                // control user aircraft instead
                return userAircraft;
            }

            // control specified object
            return obj;
        }

        /// <summary>
        /// Is this model part of Tacpack
        /// </summary>
        /// <returns></returns>
        public static bool IsTacpackModel(string model)
        {
            // check for valid model
            if (model != null)
            {
                //// check for "FSXatWar"
                //if (model.Length >= 8 && model.Substring(0, 8).Equals("FSXatWar", StringComparison.OrdinalIgnoreCase))
                //{
                //    return true;
                //}

                // check for "VRS_"
                if (model.Length >= 4 && model[..4].Equals("VRS_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // check for "VACMI"
                if (model.Length >= 5 && model[..5].Equals("VACMI", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // not Tacpack
            return false;
        }

        /// <summary>
        /// Is the object to be broadcast
        /// </summary>
        /// <param name="obj">Object</param>
        /// <returns>Broadcast</returns>
        public bool IsBroadcast(Obj obj)
        {
            // get altitude
            double altitude = obj.Position != null ? obj.Position.geo.y * Sim.FEET_PER_METRE : 0.0;
            return obj.owner != Obj.Owner.Network && altitude < 150000.0 && (obj.broadcast || main.log.BroadcastName(obj.ModelTitle) || Settings.Default.AutoBroadcast || Settings.Default.BroadcastTacpack && IsTacpackModel(obj.ModelTitle));
        }
    }
}
