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
        /// List of user defined variable sets for different models. Public: VariablesForm edits it
        /// directly (through Main.SimCommand, since the sim thread owns it).
        /// </summary>
        public readonly ModelVariableStore modelVariableStore;

        /// <summary>
        /// Load model variables
        /// </summary>
        void LoadModelVariables() => modelVariableStore.Load();

        /// <summary>
        /// Get list of variables for a particular model
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        public List<string> GetModelVariables(string title) => modelVariableStore.Get(title);

        /// <summary>
        /// Load model variables for a particular object
        /// </summary>
        void CreateModelVariables(Obj obj)
        {
            // variable lists
            List<string> files = [];
            // check if sim is connected
            if (Connected)
            {
                // get files from model
                files = GetModelVariables(obj.ModelTitle);
            }
            else if (obj is Plane)
            {
                // add plane variables as default
                files.Add("Plane.txt");
            }
            else if (obj is Helicopter)
            {
                // add rotorcraft variables as default
                files.Add("Rotorcraft.txt");
            }
            // stop requests
            obj.variableSet ?. StopRequests();
            // reload variables
            obj.variableSet = new VariableMgr.Set(main, obj.simId, obj.Injected, files);
        }
    }
}
