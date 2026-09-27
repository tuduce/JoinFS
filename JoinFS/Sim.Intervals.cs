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
        /// Interval mask per (local, remote) object pair, keyed by object reference rather than
        /// scanned linearly - see RebuildIntervalMasks (rebuild, once per updateIntervalsTimer
        /// interval) for how entries are populated.
        /// </summary>
        readonly Dictionary<(Obj localObject, Obj remoteObject), int> intervalMasks = [];

        /// <summary>
        /// Get the interval mask for a pair of objects
        /// </summary>
        /// <param name="localObject">Local object</param>
        /// <param name="remoteObject">Remote object</param>
        /// <returns>Interval mask value</returns>
        int GetIntervalMask(Obj localObject, Obj remoteObject)
        {
            // return the mask for this pair, or 0 (always process) if there isn't one
            return intervalMasks.TryGetValue((localObject, remoteObject), out int mask) ? mask : 0;
        }

        /// <summary>
        /// Remove interval masks for an object
        /// </summary>
        /// <param name="object"></param>
        void RemoveIntervalMask(Obj obj)
        {
            // object removal is rare (not per-tick), so a scan here costs no more than the
            // List.FindAll this replaced
            List<(Obj, Obj)> keys = intervalMasks.Keys.Where(k => k.localObject == obj || k.remoteObject == obj).ToList();
            foreach (var key in keys)
            {
                intervalMasks.Remove(key);
            }
        }
    }
}
