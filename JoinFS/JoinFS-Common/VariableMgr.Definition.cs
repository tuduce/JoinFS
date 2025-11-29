using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JoinFS
{
    public partial class VariableMgr
    {
        /// <summary>
        /// Variable definition
        /// </summary>
        public class Definition
        {
            public enum Type
            {
                INTEGER,
                FLOAT,
                STRING8
            }

            public Type type;
            public string drName;
            public float drScalar;
            public int drIndex;
            public ScDefinition scDefinition;
            public string scName;
            public string scUnits;
            public ScEvent scEvent;
            public string scEventName = "";
            public double scEventScalar = 1.0;
            public int mask;
            public uint maskVuid;
            public bool pilot;
            public bool injected;
            public int smokeIndex;

            public Definition(Type type, string drName, float drScalar, int drIndex, ScDefinition scDefinition, string scName, string scUnits)
            {
                this.type = type;
                this.drName = drName;
                this.drScalar = drScalar;
                this.drIndex = drIndex;
                this.scDefinition = scDefinition;
                this.scName = scName;
                this.scUnits = scUnits;
                this.smokeIndex = -1;
            }
        }
    }
}
