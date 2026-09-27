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
        /// Interval mask for a pair of objects
        /// </summary>
        class IntervalMask
        {
            /// <summary>
            /// Object on this node
            /// </summary>
            public Obj localObject;
            /// <summary>
            /// object on remote node
            /// </summary>
            public Obj remoteObject;
            /// <summary>
            /// Interval mask
            /// </summary>
            public int mask = 0;
        }

        /// <summary>
        /// List of interval masks between pair of objects
        /// </summary>
        readonly List<IntervalMask> intervalMasks = [];

        /// <summary>
        /// Get the interval mask for a pair of objects
        /// </summary>
        /// <param name="localObject">Local object</param>
        /// <param name="remoteObject">Remote object</param>
        /// <returns>Interval mask value</returns>
        int GetIntervalMask(Obj localObject, Obj remoteObject)
        {
            // get interval
            IntervalMask intervalMask = intervalMasks.Find(i => i.localObject == localObject && i.remoteObject == remoteObject);
            // check if interval found
            if (intervalMask != null)
            {
                // return mask
                return intervalMask.mask;
            }
            else
            {
                // always process
                return 0;
            }
        }

        /// <summary>
        /// Remove interval masks for an object
        /// </summary>
        /// <param name="object"></param>
        void RemoveIntervalMask(Obj obj)
        {
            // get all masks referencing object
            List<IntervalMask> list = intervalMasks.FindAll(i => i.localObject == obj || i.remoteObject == obj);
            // for each interval mask
            foreach (var intervalMask in list)
            {
                // remove
                intervalMasks.Remove(intervalMask);
            }
        }
    }
}
