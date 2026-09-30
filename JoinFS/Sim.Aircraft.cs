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
        /// Aircraft
        /// </summary>
        public abstract partial class Aircraft : Obj
        {
            public bool user = false;
            public string originalCallsign = "";
            /// <summary>
            /// ICAO type designator as first reported by the sim for this aircraft object, kept separate
            /// from flightPlan.icaoType (which the Flight Plan dialog's OK handler permanently overwrites
            /// with whatever was typed) so a later "fetch fresh from the sim" (see FlightPlanForm's Clear
            /// button) has something live to read back - same reasoning as originalCallsign.
            /// </summary>
            public string originalIcaoType = "";
            public FlightPlan flightPlan = new();
            public byte cockpitShare = 0;
            public string airport = "";
            public string metar = "";
            public string wind = "";

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Aircraft(uint simId, string callsign, string icaoType, string model, string livery, string icaoAirline, bool isUser)
            {
                // set sim ID
                this.simId = simId;
                netId = simId;
                // update info
                originalCallsign = callsign;
                originalIcaoType = icaoType;
                flightPlan.callsign = callsign;
                // ATC ID is actually the tail number/registration, not a real callsign - see Substitution.cs
                flightPlan.registration = callsign;
                flightPlan.icaoType = icaoType;
                ownerModel = model;
                ownerLivery = livery;
#if FS2024
                flightPlan.icaoAirline = icaoAirline;
#endif
                subModel = null;
                // check for this user
                if (isUser)
                {
                    // user is the owner
                    owner = Owner.Me;
                    user = true;
                    broadcast = true;
                    record = true;
                }
                else
                {
                    owner = Owner.Sim;
                }
            }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="ownerGuid">Node</param>
            /// <param name="netId">Network ID</param>
            public Aircraft(NodeId ownerNuid, uint netId) : base(ownerNuid, netId)
            {
            }

            /// <summary>
            /// Set weather for this aircraft
            /// </summary>
            /// <param name="metar">Metar observation</param>
            public void SetWeather(string metar)
            {
                // update metar
                this.metar = metar;
                // find wind in metar
                Match match = MetarWindRegex().Match(metar);
                // check if found
                if (match.Success)
                {
                    // set wind
                    this.wind = match.Value;
                }
                else
                {
                    // find wind in metar
                    match = MetarWindMpsRegex().Match(metar);
                    // check if found
                    if (match.Success)
                    {
                        // set wind
                        this.wind = match.Value;
                    }
                }
            }

            [GeneratedRegex(@"\d{5}KT", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
            private static partial Regex MetarWindRegex();

            [GeneratedRegex(@"\d{5}MPS", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
            private static partial Regex MetarWindMpsRegex();

            /// <summary>
            /// State of shared cockpit
            /// </summary>
            public bool CockpitShared { get { return (cockpitShare & 0x01) != 0; } }
            public bool FlightControlsShared { get { return (cockpitShare & 0x02) != 0; } }

            public override Obj CloneView()
            {
                Aircraft view = (Aircraft)base.CloneView();
                view.flightPlan = flightPlan?.Clone();
                return view;
            }
        }

        /// <summary>
        /// Plane
        /// </summary>
        public class Plane : Aircraft
        {

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Plane(uint simId, string callsign, string type, string model, string livery, string icaoAirline, bool isUser) : base(simId, callsign, type, model, livery, icaoAirline, isUser) { }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="ownerGuid">Node</param>
            /// <param name="netId">Network ID</param>
            public Plane(NodeId ownerNuid, uint netId) : base(ownerNuid, netId) { }
        }

        /// <summary>
        /// Helicopter
        /// </summary>
        public class Helicopter : Aircraft
        {

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Helicopter(uint simId, string callsign, string type, string model, string livery, string icaoAirline, bool isUser) : base(simId, callsign, type, model, livery, icaoAirline, isUser) { }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="ownerGuid">Node</param>
            /// <param name="netId">Network ID</param>
            public Helicopter(NodeId ownerNuid, uint netId) : base(ownerNuid, netId) { }
        }
    }
}
