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
using JoinFS.Estimation;




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
        /// Steering for injected objects (Estimation/)
        /// </summary>
        static ISteeringLaw CreateSteering(string name) =>
#if (FS2020 || FS2024)
            // FS2020 has an issue where the aircraft remains glued to the ground, so reset much earlier when the altitude diverts on the ground
            EstimationRegistry.CreateSteering(name, setAttitudeEveryFrame: true, groundAltitudeLimit: 0.2);
#else
            EstimationRegistry.CreateSteering(name, setAttitudeEveryFrame: false, groundAltitudeLimit: ClassicSteering.ResetDistance);
#endif

        /// <summary>The steering law (-steering), made on first use, once the command line has been read</summary>
        SteeringSchedule steeringSchedule;

        /// <summary>
        /// Update object velocity in the simulator: predict where the object is now from its newest
        /// network sample, and steer it there
        /// </summary>
        void UpdateSimObjectVelocity(Obj obj)
        {
            try
            {
                // check for controlled object with valid position
                if (simconnect != null && obj.remoteFlightControl && obj.SimValid && obj.NetValid && obj.Created)
                {
                    KinematicState sample = new(obj.netPosition, obj.netVelocity);
                    SteeringCommand command;
                    steeringSchedule ??= new SteeringSchedule(CreateSteering, EstimationRegistry.SelectedSteering);
                    // check if object is paused
                    if (obj.paused)
                    {
                        command = steeringSchedule.At(main.ElapsedTime, out _).Hold(sample);
                    }
                    else
                    {
                        double now = main.ElapsedTime;
                        // the link to the owner, for the network delay
                        PeerTiming peer = obj.owner == Obj.Owner.Network ? new PeerTiming(true, main.network.GetNodeRTT(obj.ownerNuid)) : PeerTiming.None;
                        // the object's state now
                        double age = obj.Clock.SampleAge(now, peer);
                        KinematicState target = obj.Estimator.Predict(sample, age);
                        ISteeringLaw steering = steeringSchedule.At(now, out string steeringName);
                        estimationLog?.OnPrediction(obj, now, age, target, steeringName);
                        command = steering.Steer(target, sample, obj.simPosition, now - obj.simTime);
                    }
                    ApplySteering(obj, command);
                }
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        /// <summary>
        /// Set a steering command on an object, in the order <see cref="SteeringCommand"/> gives
        /// </summary>
        void ApplySteering(Obj obj, in SteeringCommand command)
        {
            if (command.ResetTo != null)
            {
                // reset to target position
                UpdateObject(obj, command.ResetTo);
                simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, command.Velocity);
                SetAttitude(obj, command.Attitude);
            }
            else
            {
                SetAttitude(obj, command.Attitude);
                simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, command.Velocity);
            }
        }

        /// <summary>
        /// Set an object's orientation, when there is one to set
        /// </summary>
        void SetAttitude(Obj obj, Vector attitude)
        {
            if (attitude != null)
            {
                simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(attitude));
                obj.simPosition.angles = attitude.Clone();
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
