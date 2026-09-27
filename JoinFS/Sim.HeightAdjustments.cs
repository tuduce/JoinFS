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
        /// list of height adjustments (HeightForm), keyed by model - see HeightAdjustmentStore for
        /// why it doesn't go through Main.SimCommand/PostToSim like everything else here.
        /// </summary>
        readonly HeightAdjustmentStore heightAdjustmentStore;

        /// <summary>
        /// get the height adjustment for a model
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public int GetHeightAdjustment(Substitution.Model model) => heightAdjustmentStore.Get(model);

        /// <summary>
        /// update a height adjustment
        /// </summary>
        /// <param name="model"></param>
        /// <param name="adjustment"></param>
        public void UpdateHeightAdjustment(Substitution.Model model, int adjustment) => heightAdjustmentStore.Update(model, adjustment);

        /// <summary>
        /// Load height adjustment
        /// </summary>
        public void LoadHeightAdjustments() => heightAdjustmentStore.Load();

        /// <summary>
        /// Save height adjustments
        /// </summary>
        public void SaveHeightAdjustments() => heightAdjustmentStore.Save();
    }
}
