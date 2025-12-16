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
        /// Set of variables
        /// </summary>
        public class Set
        {
            /// <summary>
            /// Delay time when changing a variable value
            /// </summary>
            public const double MASTER_DELAY = 5.0;
            public const double SLAVE_DELAY = 3.0;

            /// <summary>
            /// List of simconnect requests
            /// </summary>
            public Dictionary<uint, ScRequest> scRequests = [];
            Dictionary<ScRequest, uint> scRequestVuids = [];

            /// <summary>
            /// List of integer variables
            /// </summary>
            public Dictionary<uint, int> integers = [];

            /// <summary>
            /// List of float variables
            /// </summary>
            public Dictionary<uint, float> floats = [];

            /// <summary>
            /// List of string8 variables
            /// </summary>
            public Dictionary<uint, string> string8s = [];

            /// <summary>
            /// List of change times
            /// </summary>
            public Dictionary<uint, double> startTimes = [];

            /// <summary>
            /// Main instance
            /// </summary>
            Main main;

            /// <summary>
            /// Variables manager
            /// </summary>
            VariableMgr variableMgr;

            /// <summary>
            /// Simulator Id
            /// </summary>
            public uint simId;

            /// <summary>
            /// Is the object injected
            /// </summary>
            public bool injected;

            /// <summary>
            /// Convert integer to BCD
            /// </summary>
            static int ConvertToBCD(int value)
            {
                int result = 0;
                result += (value % 10);
                result += (value / 10 % 10) << 4;
                result += (value / 100 % 10) << 8;
                result += (value / 1000 % 10) << 12;
                return result;
            }

            /// <summary>
            /// Convert integer from BCD
            /// </summary>
            static int ConvertFromBCD(int value)
            {
                int result = 0;
                result += value & 0xf;
                result += ((value >> 4) & 0xf) * 10;
                result += ((value >> 8) & 0xf) * 100;
                result += ((value >> 12) & 0xf) * 1000;
                return result;
            }

            /// <summary>
            /// Convert integer to SimConnect
            /// </summary>
            static int ConvertToSimConnect(string units, int value)
            {
                // check units
                if (units == "bco16" || units == "frequency bcd16")
                {
                    return ConvertToBCD(value);
                }
                else if (units == "frequency bcd32" || units == "frequency adf bcd32")
                {
                    return ConvertToBCD(value) << 16;
                }
                // no conversion
                return value;
            }

            /// <summary>
            /// Convert integer from SimConnect
            /// </summary>
            static int ConvertFromSimConnect(string units, int value)
            {
                // check units
                if (units == "bco16")
                {
                    return ConvertFromBCD(value);
                }
                else if (units == "frequency bcd16")
                {
                    return ConvertFromBCD(value) + 10000;
                }
                else if (units == "frequency bcd32" || units == "frequency adf bcd32")
                {
                    return ConvertFromBCD(value >> 16);
                }
                // no conversion
                return value;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId"></param>
            public Set(Main main, uint simId, bool injected, List<string> files)
            {
                this.main = main;
                this.variableMgr = main.variableMgr;
                this.simId = simId;
                this.injected = injected;
                // for each file
                foreach (var file in files)
                {
                    // add variables
                    AddFromFile(file);
                }
            }

            /// <summary>
            /// Get integer variable
            /// </summary>
            public int GetInteger(uint vuid)
            {
                // return value
                return integers.ContainsKey(vuid) ? integers[vuid] : 0;
            }

            /// <summary>
            /// Get float variable
            /// </summary>
            public float GetFloat(uint vuid)
            {
                // return value
                return floats.ContainsKey(vuid) ? floats[vuid] : 0.0f;
            }

            /// <summary>
            /// Get string8 variable
            /// </summary>
            public string GetString8(uint vuid)
            {
                // return value
                return string8s.ContainsKey(vuid) ? string8s[vuid] : "";
            }

            /// <summary>
            /// Add variables from a file
            /// </summary>
            public void AddFromFile(string filename)
            {
                // check for file in definitions
                List<uint> vuids = variableMgr.GetFromFile(filename);

                // for each variable
                foreach (var vuid in vuids)
                {
                    // check for definition
                    if (variableMgr.definitions.ContainsKey(vuid))
                    {
                        // get definition
                        Definition definition = variableMgr.definitions[vuid];

                        // initialize start time
                        startTimes[vuid] = 0.0;

                        // check if sim connected and object is valid
                        if (main.sim != null && main.sim.Connected && simId != uint.MaxValue)
                        {
#if XPLANE || CONSOLE
                            // check for xplane
                            if ((main.sim ?. xplane).IsConnected)
                            {
                                // request from xplane
                                main.sim.xplane.RequestVariable(simId, vuid);
                            }
#else
                            if (definition.scName.Length > 0)
                            {
                                // simconnect request
                                ScRequest scRequest = variableMgr.NextRequest;
                                // add request
                                scRequests[vuid] = scRequest;
                                scRequestVuids[scRequest] = vuid;
                                // request the varaible from simconnect
                                main.sim?.RequestVariable(scRequest, definition.scDefinition, simId);
                            }
#endif
                        }
                    }
                }
            }

            /// <summary>
            /// Stop requests
            /// </summary>
            public void StopRequests()
            {
                // check for valid object
                if (simId != uint.MaxValue)
                {
                    // for each request
                    foreach (var request in scRequests)
                    {
                        // stop request
                        main.sim?.StopRequest(request.Value, ScDefinition.ID0, simId);
                    }

                    // clear requests
                    scRequests.Clear();
                    scRequestVuids.Clear();
                }
            }

            /// <summary>
            /// Process variable data
            /// </summary>
            /// <param name="id"></param>
            /// <param name="data"></param>
            public void DetectSimconnect(ScRequest scRequest, object data)
            {
                // check for request
                if (scRequestVuids.ContainsKey(scRequest))
                {
                    // get vuid
                    uint vuid = scRequestVuids[scRequest];
                    // check for variable
                    if (variableMgr.definitions.ContainsKey(vuid))
                    {
                        // get definition
                        Definition definition = variableMgr.definitions[vuid];
                        switch (definition.type)
                        {
                            case Definition.Type.INTEGER:
                                {
                                    // get new value
                                    int value = ((Sim.IntegerStruct)data).value;
                                    // check for mask
                                    if (definition.mask != 0)
                                    {
                                        // apply mask
                                        value = (value & definition.mask) != 0 ? 1 : 0;
                                    }
                                    // detect integer
                                    DetectInteger(vuid, ConvertFromSimConnect(definition.scUnits, value), false);
                                }
                                break;
                            case Definition.Type.FLOAT:
                                {
                                    // detect float
                                    DetectFloat(vuid, ((Sim.FloatStruct)data).value, false);
                                }
                                break;
                            case Definition.Type.STRING8:
                                {
                                    // detect string8
                                    DetectString8(vuid, ((Sim.String8Struct)data).value);
                                }
                                break;
                        }
                    }
                }
            }

            /// <summary>
            /// Is this the user aircraft
            /// </summary>
            bool UserAircraft { get { return main.sim?.userAircraft != null && main.sim.userAircraft.simId == simId; } }

            /// <summary>
            /// Is the object a slave
            /// </summary>
            double DelayTime
            {
                get
                {
                    // check if user aircraft and entered another aircraft
                    if (UserAircraft && main.sim?.enteredAircraft != null)
                    {
                        // user is in another aircraft
                        return SLAVE_DELAY;
                    }
                    else
                    {
                        // injected aircraft are slaves
                        return injected ? SLAVE_DELAY : MASTER_DELAY;
                    }
                }
            }

            /// <summary>
            /// Process integer variable from X-Plane
            /// </summary>
            public void DetectInteger(uint vuid, int value, bool xplane)
            {
                // check for definition
                if (variableMgr.definitions.ContainsKey(vuid))
                {
                    // get definition
                    Definition definition = variableMgr.definitions[vuid];
                    // check type
                    if (definition.type == Definition.Type.INTEGER)
                    {
                        // check if value has changed
                        if (integers.ContainsKey(vuid) == false || value != integers[vuid])
                        {
                            // temporarily block updates
                            startTimes[vuid] = main.ElapsedTime + DelayTime;
                            // check for mask
                            if (definition.mask != 0)
                            {
#if XPLANE || CONSOLE
                                // check for mask variable
                                if (main.sim != null && main.sim.xplane.IsConnected && integers.ContainsKey(definition.maskVuid))
                                {
                                    // temporarily block updates
                                    startTimes[definition.maskVuid] = main.ElapsedTime + DelayTime;
                                    // update masked value
                                    if (value != 0)
                                    {
                                        integers[definition.maskVuid] |= definition.mask;
                                    }
                                    else
                                    {
                                        integers[definition.maskVuid] &= ~definition.mask;
                                    }
                                }

                                // monitor
                                main.MonitorVariables("DETECT VARIABLE - OBJECT:" + simId + " - " + definition.drName + " = " + value);
#else
                                // monitor
                                main.MonitorVariables("DETECT VARIABLE - OBJECT:" + simId + " - " + definition.scName + "(" + definition.mask + ") = " + value);
#endif
                            }
                            else
                            {
#if XPLANE || CONSOLE
                                // get dataref name
                                string name = definition.drName;
                                // add index
                                if (definition.drIndex > 0) name += ":" + definition.drIndex;
                                // monitor
                                main.MonitorVariables("DETECT VARIABLE - OBJECT:" + simId + " - " + name + " = " + value);
#else
                                main.MonitorVariables("DETECT VARIABLE - OBJECT:" + simId + " - " + definition.scName + " = " + value);
#endif
                            }
                            // update the local value
                            integers[vuid] = value;
                        }
                    }
                    else
                    {
                        main.MonitorEvent("ERROR - Unexpected variable type - " + definition.drName + " - " + definition.scName);
                    }
                }
            }

            /// <summary>
            /// Process float variable
            /// </summary>
            public void DetectFloat(uint vuid, float value, bool xplane)
            {
                // check for definition
                if (variableMgr.definitions.ContainsKey(vuid))
                {
                    // get definition
                    Definition definition = variableMgr.definitions[vuid];
                    // check type
                    if (definition.type == Definition.Type.FLOAT)
                    {
                        // check if value has changed
                        if (floats.ContainsKey(vuid) == false || Math.Abs(value - floats[vuid]) > 0.001f)
                        {
                            // temporarily block updates
                            startTimes[vuid] = main.ElapsedTime + DelayTime;

#if XPLANE || CONSOLE
                            // get dataref name
                            string name = definition.drName;
                            // add index
                            if (definition.drIndex > 0) name += ":" + definition.drIndex;
                            // monitor
                            main.MonitorVariables("DETECT VARIABLE - OBJECT:" + simId + " - " + name + " = " + value);
#else
                            // monitor
                            main.MonitorVariables("DETECT VARIABLE - OBJECT:" + simId + " - " + definition.scName + " = " + value);
#endif
                            // update the local value
                            floats[vuid] = value;
                        }
                    }
                    else
                    {
                        main.MonitorEvent("ERROR - Unexpected variable type - " + definition.drName + " - " + definition.scName);
                    }
                }
            }

            /// <summary>
            /// Process string8 variable
            /// </summary>
            public void DetectString8(uint vuid, string value)
            {
                // check for definition
                if (variableMgr.definitions.ContainsKey(vuid))
                {
                    // get definition
                    Definition definition = variableMgr.definitions[vuid];
                    // check type
                    if (definition.type == Definition.Type.STRING8)
                    {
                        // check if value has changed
                        if (string8s.ContainsKey(vuid) == false || value != string8s[vuid])
                        {
                            // temporarily block updates
                            startTimes[vuid] = main.ElapsedTime + DelayTime;
#if XPLANE || CONSOLE
                            // get dataref name
                            string name = definition.drName;
                            // add index
                            if (definition.drIndex > 0) name += ":" + definition.drIndex;
                            // monitor
                            main.MonitorVariables("DETECT VARIABLE - OBJECT:" + simId + " - " + name + " = " + value);
#else
                            main.MonitorVariables("DETECT VARIABLE - OBJECT:" + simId + " - " + definition.scName + " = " + value);
#endif
                            // update the local value
                            string8s[vuid] = value;
                        }
                    }
                    else
                    {
                        main.MonitorEvent("ERROR - Unexpected variable type - " + definition.drName + " - " + definition.scName);
                    }
                }
            }

            /// <summary>
            /// Update variables from a stream
            /// </summary>
            /// <param name="reader"></param>
            public void UpdateInteger(uint vuid, int value)
            {
                try
                {
                    // get actual vuid
                    vuid = variableMgr.LookupVuid(vuid);
                    // check for valid variable
                    if (variableMgr.definitions.ContainsKey(vuid))
                    {
                        // get definition
                        Definition definition = variableMgr.definitions[vuid];

                        // reject any shared cockpit updates intended for pilot only
                        if (UserAircraft && (definition.pilot == false || main.sim.userAircraft.remoteFlightControl) || injected && definition.injected)
                        {
                            // check for newer value
                            if (startTimes.ContainsKey(vuid) == false || startTimes[vuid] < main.ElapsedTime)
                            {
                                // check if value is different
                                if (integers.ContainsKey(vuid) == false || value != integers[vuid])
                                {
                                    // check if sim connected
                                    if (main.sim != null && main.sim.Connected && simId != uint.MaxValue)
                                    {
#if XPLANE || CONSOLE
                                        // check for valid dataref
                                        if (definition.drName.Length > 0)
                                        {
                                            // update x-plane
                                            main.sim ?. xplane.UpdateInteger(simId, vuid, value);
                                            // monitor
                                            main.MonitorVariables("UPDATE VARIABLE - OBJECT:" + simId + " - " + definition.drName + " = " + value);
                                        }
#else
                                        // check type and ignore mask values
                                        if (definition.type == Definition.Type.INTEGER && definition.mask == 0)
                                        {
                                            // check for smoke
                                            if (definition.smokeIndex >= 0)
                                            {
                                                // check for smoke on
                                                if (value != 0)
                                                {
                                                    // smoke on
                                                    main.sim?.DoSimEvent(simId, Sim.Event.SMOKE_ON, (uint)definition.smokeIndex);
                                                }
                                                else
                                                {
                                                    // smoke off
                                                    main.sim?.DoSimEvent(simId, Sim.Event.SMOKE_OFF, (uint)definition.smokeIndex);
                                                }
                                            }
                                            // check for event
                                            else if (definition.scEventName.Length > 0)
                                            {
                                                // event update
                                                main.sim?.DoSimEvent(simId, definition, (uint)ConvertToSimConnect(definition.scUnits, (int)(value * definition.scEventScalar)));
                                            }
                                            else if (definition.scName.Length > 0)
                                            {
                                                // update simconnect
                                                Sim.IntegerStruct data = new()
                                                {
                                                    value = ConvertToSimConnect(definition.scUnits, value)
                                                };
                                                main.sim?.UpdateVariable(definition.scDefinition, simId, data);
                                            }
                                            // monitor
                                            main.MonitorVariables("UPDATE VARIABLE - OBJECT:" + simId + " - " + definition.scName + " = " + value);
                                        }
                                        else
                                        {
                                            // update the value directly
                                            integers[vuid] = value;
                                            // monitor
                                            main.MonitorVariables("UPDATE VARIABLE - OBJECT:" + simId + " - " + definition.scName + "(" + definition.mask + ") = " + value);
                                        }
#endif
                                        // temporarily block updates
                                        startTimes[vuid] = main.ElapsedTime + DelayTime;
                                    }
                                    // sim not connected
                                    else
                                    {
                                        // update the value directly
                                        integers[vuid] = value;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // passive update
                            integers[vuid] = value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    main.MonitorEvent("ERROR - Updating variable - " + ex.Message);
                }
            }

            /// <summary>
            /// Update variables from a stream
            /// </summary>
            /// <param name="reader"></param>
            public void UpdateIntegers(Dictionary<uint, int> variables)
            {
                // for each variable
                foreach (var variable in variables)
                {
                    // update integer
                    UpdateInteger(variable.Key, variable.Value);
                }
            }

            /// <summary>
            /// Update variables from a stream
            /// </summary>
            /// <param name="reader"></param>
            public void UpdateFloats(Dictionary<uint, float> variables)
            {
                try
                {
                    // get delay time
                    double delayTime = DelayTime;

                    // for each variable
                    foreach (var variable in variables)
                    {
                        // get vuid
                        uint vuid = variableMgr.LookupVuid(variable.Key);
                        // get value
                        float value = variable.Value;
                        // check for valid variable
                        if (variableMgr.definitions.ContainsKey(vuid))
                        {
                            // get definition
                            Definition definition = variableMgr.definitions[vuid];

                            // reject any shared cockpit updates intended for pilot only
                            if (UserAircraft && (definition.pilot == false || main.sim.userAircraft.remoteFlightControl) || injected && definition.injected)
                            {
                                // check for newer value
                                if (startTimes.ContainsKey(vuid) == false || startTimes[vuid] < main.ElapsedTime)
                                {
                                    // check if value is different
                                    if (floats.ContainsKey(vuid) == false || Math.Abs(value - floats[vuid]) > 0.001f)
                                    {
                                        // check if sim connected
                                        if (main.sim != null && main.sim.Connected && simId != uint.MaxValue)
                                        {
#if XPLANE || CONSOLE
                                            // check for valid dataref
                                            if (definition.drName.Length > 0)
                                            {
                                                // update x-plane
                                                main.sim.xplane.UpdateFloat(simId, vuid, value);
                                                // monitor
                                                main.MonitorVariables("UPDATE VARIABLE - OBJECT:" + simId + " - " + definition.drName + " = " + value);
                                            }
#else
                                            // check type
                                            if (definition.type == Definition.Type.FLOAT)
                                            {
                                                // check for event
                                                if (definition.scEventName.Length > 0)
                                                {
                                                    // get scaled value
                                                    uint scaledValue = (uint)(value * definition.scEventScalar);
                                                    if (floats.ContainsKey(vuid) == false || Math.Abs((int)scaledValue - (int)(floats[vuid] * definition.scEventScalar)) > 2)
                                                    {
                                                        // event update
                                                        main.sim?.DoSimEvent(simId, definition, scaledValue);
                                                        // monitor
                                                        main.MonitorVariables("UPDATE VARIABLE - OBJECT:" + simId + " - " + definition.scName + " = " + value);
                                                    }
                                                }
                                                else if (definition.scName.Length > 0)
                                                {
                                                    // update simconnect variable
                                                    Sim.FloatStruct data = new()
                                                    {
                                                        value = value
                                                    };
                                                    main.sim?.UpdateVariable(definition.scDefinition, simId, data);
                                                    // monitor
                                                    main.MonitorVariables("UPDATE VARIABLE - OBJECT:" + simId + " - " + definition.scName + " = " + value);
                                                }
                                            }
                                            else
                                            {
                                                main.MonitorEvent("ERROR - Unexpected variable type - " + definition.drName + " - " + definition.scName);
                                            }
#endif
                                            // temporarily block updates
                                            startTimes[vuid] = main.ElapsedTime + delayTime;
                                        }
                                        // sim not connected
                                        else
                                        {
                                            // update the value directly
                                            floats[vuid] = value;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    main.MonitorEvent("ERROR - Updating variable - " + ex.Message);
                }
            }

            /// <summary>
            /// Update variables from a stream
            /// </summary>
            /// <param name="reader"></param>
            public void UpdateString8(Dictionary<uint, string> variables)
            {
                try
                {
                    // get delay time
                    double delayTime = DelayTime;

                    // for each variable
                    foreach (var variable in variables)
                    {
                        // get vuid
                        uint vuid = variableMgr.LookupVuid(variable.Key);
                        // get value
                        string value = variable.Value;
                        // check for valid variable
                        if (variableMgr.definitions.ContainsKey(vuid))
                        {
                            // get definition
                            Definition definition = variableMgr.definitions[vuid];

                            // reject any shared cockpit updates intended for pilot only
                            if (UserAircraft && (definition.pilot == false || main.sim.userAircraft.remoteFlightControl) || injected && definition.injected)
                            {
                                // check for newer value
                                if (startTimes.ContainsKey(vuid) == false || startTimes[vuid] < main.ElapsedTime)
                                {
                                    // check if value is different
                                    if (string8s.ContainsKey(vuid) == false || value != string8s[vuid])
                                    {
                                        // check if sim connected
                                        if (main.sim != null && main.sim.Connected && simId != uint.MaxValue)
                                        {
#if XPLANE || CONSOLE
                                            // check for valid dataref
                                            if (definition.drName.Length > 0)
                                            {
                                                // update x-plane
                                                main.sim.xplane.UpdateString8(simId, vuid, value);
                                                // monitor
                                                main.MonitorVariables("UPDATE VARIABLE - OBJECT:" + simId + " - " + definition.drName + " = " + value);
                                            }
#else
                                            // check type
                                            if (definition.type == Definition.Type.STRING8 && definition.scName.Length > 0)
                                            {
                                                // update simconnect
                                                Sim.String8Struct data = new()
                                                {
                                                    value = value
                                                };
                                                main.sim.UpdateVariable(definition.scDefinition, simId, data);
                                                // monitor
                                                main.MonitorVariables("UPDATE VARIABLE - OBJECT:" + simId + " - " + definition.scName + " = " + value);
                                            }
                                            else
                                            {
                                                main.MonitorEvent("ERROR - Unexpected variable type - " + definition.drName + " - " + definition.scName);
                                            }
#endif
                                            // temporarily block updates
                                            startTimes[vuid] = main.ElapsedTime + delayTime;
                                        }
                                        // sim not connected
                                        else
                                        {
                                            // update the value directly
                                            string8s[vuid] = value;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    main.MonitorEvent("ERROR - Updating variable - " + ex.Message);
                }
            }
        }
    }
}
