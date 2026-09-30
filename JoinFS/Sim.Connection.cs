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
        /// Is a simulator currently connecting
        /// </summary>
        public bool Connecting { get { return Connected == false && checkConnectionCount < CHECK_CONNECTION_ATTEMPTS; } }

        /// <summary>
        /// Try connection
        /// </summary>
        public void Connect()
        {
            // reset connection attempts
            checkConnectionCount = 0;
            checkConnectionTimer.Reset();
        }

        /// <summary>
        /// Close the connection once the current dispatch has finished. Closing inside a
        /// SimConnect callback would dispose the connection while it is still dispatching.
        /// </summary>
        public void ScheduleClose()
        {
            scheduleClose = true;
        }

        /// <summary>
        /// Close connection
        /// </summary>
        public void Close()
        {
            // disable weather aircraft
            SetWeatherAircraft(null);
            // leave cockpit of other aircraft
            LeaveAircraft();
            // remove all simulator objects
            foreach (var obj in objectList)
            {
                // check for simulator object
                if (obj.Injected == false)
                {
                    // add to remove list
                    removeList.Add(obj);
                }
            }
            // clear objects
            DoRemove();
            // reset user aircraft
            userAircraft = null;
#if XPLANE || CONSOLE
            xplane.Close();
#elif SIMCONNECT
            // close simconnect
            simconnect?.Dispose();
            simconnect = null;
            // the feeds went with the connection
            foreach (var obj in objectList)
            {
                ForgetPositionFeed(obj);
            }
#endif
            simulatorName = "";
            // set connection attempts
            checkConnectionCount = CHECK_CONNECTION_ATTEMPTS;
            checkConnectionTimer.Reset();
            // clear model matching
            main.ScheduleSubstitutionClear();
            // refresh
#if !SERVER && !CONSOLE
            main.aircraftForm ?. refresher.Schedule();
            main.objectsForm ?. refresher.Schedule();
            // show message
            main.MonitorEvent("Disconnected from simulator");
#endif
        }

        /// <summary>
        /// Check connection to FS
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void CheckConnection()
        {
            // check for existing connection
            if (Connected == false)
            {
#if SIMCONNECT
                try
                {
                    // message
                    main.MonitorEvent("Looking for simulator (" + (checkConnectionCount + 1) + "/" + CHECK_CONNECTION_ATTEMPTS + ")");

                    Random rand = new((int)DateTime.Now.Ticks);
                    string name = "";
                    for (int i = 0; i < 10; i++)
                    {
                        name += (char)rand.Next((int)'A', (int)'Z');
                    }
                    // create simconnect interface
                    simconnect = new SimConnectInterface(this, main, name);

                    if (simconnect.Valid)
                    {
                        // no need to ask
                        Settings.Default.AskSimConnect = true;
                    }
                    else
                    {
                        // delete simconnect
                        simconnect.Dispose();
                        simconnect = null;
                    }
                }
                catch (System.IO.FileNotFoundException)
                {
                    // simconnect message
                    main.scheduleAskSimConnect = true;
                    main.MonitorEvent("SimConnect not installed.");
                    simconnect = null;
                }
                catch (Exception ex)
                {
                    main.MonitorError(ex, "Failed to initialize SimConnect");
                    simconnect = null;
                }
#elif XPLANE || CONSOLE
                if (main.settingsXplane)
                {
                    // message
                    main.MonitorEvent("Looking for simulator (" + (checkConnectionCount + 1) + "/" + CHECK_CONNECTION_ATTEMPTS + ")");
                    // try xplane
                    xplane.Open();
                }
#endif
            }
        }
    }
}
