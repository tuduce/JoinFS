using System;
using System.Collections.Generic;
using System.IO;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The variable files of each model, from the sim thread's <c>ModelVariableStore</c> (variables.txt). The lists belong to the sim
    /// thread, so they are read by asking it and changed by posting to it, as the old VariablesForm did.
    /// </summary>
    class LiveVariablesCatalog : IVariablesCatalog
    {
        // The files JoinFS provides by kind of model (ModelVariableStore.GetDefault). They are not edited.
        static readonly HashSet<string> BUILT_IN = new(StringComparer.OrdinalIgnoreCase)
        {
            "Plane.txt", "Rotorcraft.txt", "SingleProp.txt", "TwinProp.txt", "QuadProp.txt", "SingleTurbine.txt", "TwinTurbine.txt", "QuadTurbine.txt",
        };

        readonly Main main;

        public LiveVariablesCatalog(Main main)
        {
            this.main = main;
        }

        bool Connected => main.sim != null && main.sim.View.Connected;

        public IReadOnlyList<VariableAssignment> GetAssignments()
        {
            List<VariableAssignment> assignments = [];
            List<(string Model, List<string> Files)> own = main.sim == null ? null : main.InvokeOnSim(sim => sim.modelVariableStore.GetAssignments());
            if (own != null)
            {
                foreach ((string model, List<string> files) in own)
                {
                    assignments.Add(new VariableAssignment(model, files));
                }
            }
            return assignments;
        }

#if XPLANE
        // there is one model to give, and no list of models to pick it from
        public bool CanPickModel => false;
#else
        public bool CanPickModel => true;
#endif

        public string FilesFolder => Path.Combine(main.documentsPath, "Variables");

        public IReadOnlyList<string> GetFiles(string model)
        {
            // the lists are only known while the simulator is connected
            if (!Connected)
            {
                return [];
            }
            return main.InvokeOnSim(sim => new List<string>(sim.GetModelVariables(model))) ?? [];
        }

        public void AddFiles(string model, IReadOnlyList<string> files)
        {
            List<string> added = [.. files];
            // change and save the lists on the sim thread, which owns them
            main.SimCommand(sim => sim.modelVariableStore.AddFiles(model, added));
        }

        public void RemoveFile(string model, int index)
        {
            main.SimCommand(sim => sim.modelVariableStore.RemoveFileAt(model, index));
        }

        public bool IsBuiltIn(string file) => BUILT_IN.Contains(file);

        public void Apply()
        {
            // the new lists are read as the simulator connects, so connect again, on the sim thread
            if (Connected)
            {
                main.SimCommand(sim =>
                {
                    sim.Close();
                    sim.Connect();
                });
            }
        }
    }
}
