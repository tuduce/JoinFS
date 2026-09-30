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
        /// Set a new aircraft for getting the weather
        /// </summary>
        /// <param name="aircraft">Aircraft to monitor</param>
        public void SetWeatherAircraft(Aircraft aircraft)
        {
            // set aircraft
            weatherAircraft = aircraft;
            // check for valid aircraft
            if (weatherAircraft != null)
            {
                // set weather from aircraft
                SetWeatherObservation(weatherAircraft.metar);
            }
        }

        /// <summary>
        /// Set weather from observation
        /// </summary>
        /// <param name="metar">METAR</param>
        public void SetWeatherObservation(string metar)
        {
            // schedule metar change
            scheduleMetar ??= metar;
            WakeSimThread();
        }

        /// <summary>
        /// Set weather from observation
        /// </summary>
        /// <param name="metar">METAR</param>
        public void SetWeatherObservation(NodeId nuid, string metar)
        {
            // get all aircraft for this node
            List<Obj> nodeAircraft = objectList.FindAll(o => o.ownerNuid == nuid && o is Aircraft);
            // for each object
            foreach (var obj in nodeAircraft)
            {
                // check for weather aircraft
                if (obj == weatherAircraft)
                {
                    // set weather from aircraft
                    SetWeatherObservation(metar);
                }
                // set weather for the aircraft
                (obj as Aircraft).SetWeather(metar);
            }
        }
    }
}
