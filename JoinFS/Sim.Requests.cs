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
        /// Request list of models and liverlies
        /// </summary>
        public void RequestSimulatorModels()
        {
#if SIMCONNECT && FS2024
            // check for FS connection
            if (simconnect != null)
            {
                requestModelListInProgress = true;
                // aircraft/helicopter/balloon are enumerated as three separate SimConnect requests;
                // track all three so completion only fires once every one of them has reported back
                pendingModelListRequests.Clear();
                pendingModelListRequests.Add(Requests.GET_MODELS_AIRCRAFT);
                pendingModelListRequests.Add(Requests.GET_MODELS_HELICOPTER);
                pendingModelListRequests.Add(Requests.GET_MODELS_BALLOON);
                simconnect.RequestSimulatorModels();
            }
#endif
        }

        /// <summary>
        /// Request information about aircraft in the sim
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void RequestInfo()
        {
#if SIMCONNECT
            // check for FS connection
            simconnect?.RequestDataByType(Requests.OBJECT_INFO, Definitions.OBJECT_GET_INFO, 10000);
#endif
        }

        /// <summary>
        /// Request position of network aircraft in the sim
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void RequestPosition()
        {
#if SIMCONNECT
            // check for FS connection
            if (simconnect != null)
            {
                double now = main.ElapsedTime;
                // for each object
                foreach (var obj in objectList)
                {
                    if (obj.Created == false)
                    {
                        continue;
                    }

                    // the feed this object needs now: every frame for objects we steer (injected),
                    // broadcast or record; every second for the rest
                    Definitions definition;
                    bool everyFrame = true;
                    if (obj.Injected)
                    {
                        definition = obj is Aircraft ? Definitions.AIRCRAFT_POSITION : Definitions.OBJECT_POSITION;
                    }
                    else if (obj.owner == Obj.Owner.Me || IsBroadcast(obj) || main.recorder.recording && obj.record)
                    {
                        definition = obj is Aircraft ? Definitions.AIRCRAFT_POSITION : Definitions.OBJECT_POSITION_VELOCITY;
                    }
                    else
                    {
                        definition = Definitions.OBJECT_POSITION;
                        everyFrame = false;
                    }

                    // own persistent request ID per object - see PositionPollRequestIdBase
                    if (obj.positionRequestId < 0)
                    {
                        obj.positionRequestId = NextPositionPollRequestId();
                    }

                    // (re)subscribe when what's needed changed
                    if (obj.positionFeedDefinition != definition || obj.positionFeedEveryFrame != everyFrame || obj.positionFeedSimId != obj.simId)
                    {
                        CancelPositionFeed(obj);
                        simconnect.SubscribeData((Requests)obj.positionRequestId, definition, obj.simId, everyFrame);
                        obj.positionFeedDefinition = definition;
                        obj.positionFeedEveryFrame = everyFrame;
                        obj.positionFeedSimId = obj.simId;
                        // give the feed time to start before falling back to polling
                        obj.nextPositionFallbackTime = now + 1.0;
                    }

                    // safety net: when the feed goes quiet (frames not being drawn, the sim busy loading,
                    // etc.) poll as before, so positions keep flowing
                    double quiet = everyFrame ? 0.25 : 2.5;
                    if (now - obj.simTime > quiet && now >= obj.nextPositionFallbackTime)
                    {
                        if (obj.positionFallbackRequestId < 0)
                        {
                            obj.positionFallbackRequestId = NextPositionPollRequestId();
                        }
                        simconnect.RequestData((Requests)obj.positionFallbackRequestId, definition, obj.simId);
                        obj.nextPositionFallbackTime = now + (everyFrame ? PositionSendInterval : 0.8);
                    }
                }
            }
#endif
        }

#if SIMCONNECT
        /// <summary>
        /// Stop an object's position feed (when it leaves the sim or needs a different one)
        /// </summary>
        void CancelPositionFeed(Obj obj)
        {
            if (obj.positionFeedDefinition is Definitions definition && obj.positionFeedSimId != uint.MaxValue)
            {
                simconnect?.UnsubscribeData((Requests)obj.positionRequestId, definition, obj.positionFeedSimId);
            }
            ForgetPositionFeed(obj);
        }
#endif

        /// <summary>
        /// Forget an object's feed without cancelling it (the connection or the object is already gone)
        /// </summary>
        static void ForgetPositionFeed(Obj obj)
        {
            obj.positionFeedDefinition = null;
            obj.positionFeedSimId = uint.MaxValue;
        }


        /// <summary>
        /// Request position of network aircraft in the sim
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void RequestWeather()
        {
#if SIMCONNECT
            // get user aircraft
            if (objectList.Find(o => o.owner == Obj.Owner.Me) is Aircraft aircraft)
            {
                // check for FS connection
                // request weather
                simconnect?.WeatherRequest(Requests.WEATHER, aircraft.simPosition.geo.z * (180.0 / Math.PI), aircraft.simPosition.geo.x * (180.0 / Math.PI), aircraft.simPosition.geo.y * FEET_PER_METRE);
            }
#endif
        }
    }
}
