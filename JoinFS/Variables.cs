using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;

namespace JoinFS
{
    /// <summary>
    /// Variable Manager
    /// </summary>
    public class VariableMgr
    {
        /// <summary>
        /// Simconnect definition
        /// </summary>
        public enum ScDefinition
        {
            ID0 = 100,
        }

        /// <summary>
        /// next allocated simconnect definition
        /// </summary>
        ScDefinition nextDefinition = ScDefinition.ID0;
        public ScDefinition NextDefinition { get { return nextDefinition++; } }

        /// <summary>
        /// Simconnect Request
        /// </summary>
        public enum ScRequest
        {
            ID0 = 100,
        }

        /// <summary>
        /// next allocated simconnect definition
        /// </summary>
        ScRequest nextRequest = ScRequest.ID0;
        public ScRequest NextRequest { get { return nextRequest++; } }

        /// <summary>
        /// SimConnect events
        /// </summary>
        public enum ScEvent
        {
            EVENT0 = 200,
        };

        /// <summary>
        /// next allocated simconnect event
        /// </summary>
        ScEvent nextEvent = ScEvent.EVENT0;
        public ScEvent NextEvent { get { return nextEvent++; } }

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

        /// <summary>
        /// List of variable definitions
        /// </summary>
        public Dictionary<uint, Definition> definitions = new Dictionary<uint, Definition>();

        /// <summary>
        /// List of variable files
        /// </summary>
        Dictionary<string, List<uint>> files = new Dictionary<string, List<uint>>();

        /// <summary>
        /// List of alias vuids
        /// </summary>
        Dictionary<uint, uint> aliasVuids = new Dictionary<uint, uint>();

        /// <summary>
        /// List of vuids from simconnect definitions
        /// </summary>
        Dictionary<ScDefinition, uint> vuids = new Dictionary<ScDefinition, uint>();

        /// <summary>
        /// Create unique variable ID from a string
        /// </summary>
        public static uint CreateVuid(string str)
        {
            // create unique id
            uint vuid = LocalNode.HashString(str);
            if (vuid == 0) vuid = 1;
            return vuid;
        }

        /// <summary>
        /// Check for vuid alias
        /// </summary>
        uint LookupVuid(uint vuid)
        {
            // check for alias vuid
            return aliasVuids.ContainsKey(vuid) ? aliasVuids[vuid] : vuid;
        }

        /// <summary>
        /// Main instance
        /// </summary>
        Main main;

        /// <summary>
        /// Variables path
        /// </summary>
        readonly string path = ".";

        /// <summary>
        /// Constructor
        /// </summary>
        public VariableMgr(Main main)
        {
            this.main = main;

            try
            {
                // get variables path
                path = Path.Combine(main.documentsPath, "Variables");
                // check if it does not exist
                if (Directory.Exists(path) == false)
                {
                    // create storage path
                    Directory.CreateDirectory(path);
                }
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }

            // writer
            StreamWriter writer = null;

            // get plane variables
            string filename = Path.Combine(path, "Plane.txt");
            try
            {
                writer = File.CreateText(filename);
                writer.WriteLine("INTEGER|sim/cockpit/electrical/battery_on|ELECTRICAL MASTER BATTERY|bool|TOGGLE_MASTER_BATTERY|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/nav_lights_on|LIGHT STATES|mask:0||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/beacon_lights_on|LIGHT STATES|mask:1||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/landing_lights_on|LIGHT STATES|mask:2||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/taxi_light_on|LIGHT STATES|mask:3||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/strobe_lights_on|LIGHT STATES|mask:4||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:5||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:6||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:7||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:8||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:9||INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel/controls/parkbrake|BRAKE PARKING POSITION|bool|PARKING_BRAKES*16383");
                writer.WriteLine("FLOAT|sim/flightmodel/controls/l_brake_add|BRAKE LEFT POSITION|position|AXIS_LEFT_BRAKE_SET*16383|PILOT");
                writer.WriteLine("FLOAT|sim/flightmodel/controls/r_brake_add|BRAKE RIGHT POSITION|position|AXIS_RIGHT_BRAKE_SET*16383|PILOT");
                writer.WriteLine("INTEGER|sim/cockpit2/controls/gear_handle_down|GEAR HANDLE POSITION|bool||INJECTED");
                writer.WriteLine("FLOAT|sim/flightmodel/controls/flaprat|FLAPS HANDLE PERCENT|percent over 100|FLAPS_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/flightmodel/controls/canopy_ratio|CANOPY OPEN|percent over 100||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/gyros/psi_vac_ind_degm|PLANE HEADING DEGREES GYRO|degrees");
                writer.WriteLine("INTEGER|sim/cockpit/misc/barometer_setting*0.029536|KOHLSMAN SETTING MB|millibars|KOHLSMAN_SET*16");
                writer.WriteLine("INTEGER|sim/cockpit/switches/pitot_heat_on|PITOT HEAT|bool|PITOT_HEAT_SET");
                writer.WriteLine("INTEGER|sim/cockpit2/switches/avionics_power_on|AVIONICS MASTER SWITCH|bool|AVIONICS_MASTER_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav1_obs_degm|NAV OBS:1|degrees|VOR1_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav2_obs_degm|NAV OBS:2|degrees|VOR2_SET");
                writer.WriteLine("INTEGER|sim/cockpit2/radios/actuators/audio_selection_com1|COM TRANSMIT:1|bool|COM1_TRANSMIT_SELECT");
                writer.WriteLine("INTEGER|sim/cockpit2/radios/actuators/audio_selection_com2|COM TRANSMIT:2|bool|COM2_TRANSMIT_SELECT");
                writer.WriteLine("INTEGER|sim/cockpit/radios/transponder_code|TRANSPONDER CODE:1|bco16|XPNDR_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/com1_freq_hz|COM ACTIVE FREQUENCY:1|frequency bcd16|COM_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/com2_freq_hz|COM ACTIVE FREQUENCY:2|frequency bcd16|COM2_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav1_freq_hz|NAV ACTIVE FREQUENCY:1|frequency bcd16|NAV1_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav2_freq_hz|NAV ACTIVE FREQUENCY:2|frequency bcd16|NAV2_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/com1_stdby_freq_hz|COM STANDBY FREQUENCY:1|frequency bcd16|COM_STBY_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/com2_stdby_freq_hz|COM STANDBY FREQUENCY:2|frequency bcd16|COM2_STBY_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav1_stdby_freq_hz|NAV STANDBY FREQUENCY:1|frequency bcd16|NAV1_STBY_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav2_stdby_freq_hz|NAV STANDBY FREQUENCY:2|frequency bcd16|NAV2_STBY_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/adf1_freq_hz|ADF ACTIVE FREQUENCY:1|frequency adf bcd32|ADF_COMPLETE_SET");
                //                    writer.WriteLine("INTEGER|sim/cockpit/radios/adf2_freq_hz|ADF ACTIVE FREQUENCY:2|frequency adf bcd32");
                writer.WriteLine("INTEGER||AI TRAFFIC ISIFR|bool");
                writer.WriteLine("STRING8||AI TRAFFIC FROMAIRPORT|");
                writer.WriteLine("STRING8||AI TRAFFIC TOAIRPORT|");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/autopilot_mode|AUTOPILOT MASTER|bool");
                writer.WriteLine("INTEGER||AUTOPILOT HEADING LOCK|bool|AP_PANEL_HEADING_HOLD");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/heading_mag|AUTOPILOT HEADING LOCK DIR|degrees|HEADING_BUG_SET");
                writer.WriteLine("INTEGER||AUTOPILOT ALTITUDE LOCK|bool|AP_PANEL_ALTITUDE_HOLD");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/altitude|AUTOPILOT ALTITUDE LOCK VAR|feet|AP_ALT_VAR_SET_ENGLISH");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/vertical_velocity|AUTOPILOT VERTICAL HOLD VAR|feet/minute|AP_VS_VAR_SET_ENGLISH");
                writer.WriteLine("INTEGER||AUTOPILOT AIRSPEED HOLD|bool|AP_AIRSPEED_HOLD");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/airspeed|AUTOPILOT AIRSPEED HOLD VAR|knots|AP_SPD_VAR_SET");
                writer.WriteLine("INTEGER||AUTOPILOT ATTITUDE HOLD|bool|AP_ATT_HOLD");
                writer.WriteLine("INTEGER||AUTOPILOT APPROACH HOLD|bool|AP_APR_HOLD");
                writer.WriteLine("INTEGER||AUTOPILOT BACKCOURSE HOLD|bool|AP_BC_HOLD");
                writer.WriteLine("INTEGER||AUTOPILOT NAV1 LOCK|bool|AP_NAV1_HOLD");
                writer.WriteLine("INTEGER||AUTOPILOT THROTTLE ARM|bool|AUTO_THROTTLE_ARM");
                writer.WriteLine("INTEGER|sim/cockpit/switches/DME_radio_selector|SELECTED DME|number|TOGGLE_DME");
                //writer.WriteLine("INTEGER|sim/flightmodel/position/indicated_airspeed|AIRSPEED INDICATED|knots");
                //                    writer.WriteLine("INTEGER|sim/flightmodel/misc/h_ind|INDICATED ALTITUDE|feet");
                //writer.WriteLine("INTEGER|sim/flightmodel/position/vh_ind_fpm|VERTICAL SPEED|feet per second");
                //writer.WriteLine("INTEGER|sim/flightmodel/misc/turnrate_roll|TURN INDICATOR RATE|radians per second");
                //writer.WriteLine("INTEGER|sim/flightmodel/misc/slip|TURN COORDINATOR BALL|position");
                writer.WriteLine("FLOAT|sim/flightmodel2/controls/elevator_trim|ELEVATOR TRIM PCT|position|ELEVATOR_TRIM_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/flightmodel2/controls/aileron_trim|AILERON TRIM PCT|position|AILERON_TRIM_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/flightmodel2/controls/rudder_trim|RUDDER TRIM PCT|position|RUDDER_TRIM_SET*16383|INJECTED");
                writer.WriteLine("INTEGER||SMOKE ENABLE|bool||INJECTED");
                writer.WriteLine("FLOAT||FUEL TANK CENTER QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK CENTER2 QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK CENTER3 QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK LEFT MAIN QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK LEFT AUX QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK LEFT TIP QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK RIGHT MAIN QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK RIGHT AUX QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK RIGHT TIP QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK EXTERNAL1 QUANTITY|gallons|");
                writer.WriteLine("FLOAT||FUEL TANK EXTERNAL2 QUANTITY|gallons|");
                writer.WriteLine("INTEGER||PAYLOAD STATION WEIGHT:1|pounds|");
                writer.WriteLine("INTEGER||PAYLOAD STATION WEIGHT:2|pounds|");
                writer.WriteLine("INTEGER||PAYLOAD STATION WEIGHT:3|pounds|");
                writer.WriteLine("INTEGER||PAYLOAD STATION WEIGHT:4|pounds|");

                //writer.WriteLine("FLOAT|sim/flightmodel/controls/lsplrdef|SPOILERS HANDLE POSITION|position|SPOILERS_SET*16383");
                //writer.WriteLine("FLOAT|sim/flightmodel/controls/tailhook_ratio|TAILHOOK POSITION|position|SET_TAIL_HOOK_HANDLE*16383");
                //writer.WriteLine("FLOAT||FOLDING WING LEFT PERCENT|position");
                //writer.WriteLine("FLOAT||FOLDING WING RIGHT PERCENT|position");
                //writer.WriteLine("FLOAT||FOLDING WING HANDLE POSITION|bool|SET_WING_FOLD");
            }
            catch (Exception ex)
            {
                // message
                main.ShowMessage(ex.Message);
            }
            finally
            {
                // close writer
                if (writer != null) writer.Close();
            }

            // get rotorcraft variables
            filename = Path.Combine(path, "Rotorcraft.txt");
            try
            {
                writer = File.CreateText(filename);
                writer.WriteLine("INTEGER|sim/cockpit/electrical/battery_on|ELECTRICAL MASTER BATTERY|bool|TOGGLE_MASTER_BATTERY|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/nav_lights_on|LIGHT STATES|mask:0||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/beacon_lights_on|LIGHT STATES|mask:1||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/landing_lights_on|LIGHT STATES|mask:2||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/taxi_light_on|LIGHT STATES|mask:3||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/electrical/strobe_lights_on|LIGHT STATES|mask:4||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:5||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:6||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:7||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:8||INJECTED");
                writer.WriteLine("INTEGER||LIGHT STATES|mask:9||INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel/controls/parkbrake|BRAKE PARKING POSITION|bool|PARKING_BRAKES*16383");
                writer.WriteLine("INTEGER|sim/cockpit2/controls/gear_handle_down|GEAR HANDLE POSITION|bool|GEAR_SET|INJECTED");
                writer.WriteLine("FLOAT|sim/flightmodel/controls/flaprat|FLAPS HANDLE PERCENT|percent over 100|FLAPS_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/flightmodel/controls/canopy_ratio|CANOPY OPEN|percent over 100||INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit/gyros/psi_vac_ind_degm|PLANE HEADING DEGREES GYRO|degrees");
                writer.WriteLine("INTEGER|sim/cockpit/misc/barometer_setting*0.029536|KOHLSMAN SETTING MB|millibars|KOHLSMAN_SET*16");
                writer.WriteLine("INTEGER|sim/cockpit/switches/pitot_heat_on|PITOT HEAT|bool|PITOT_HEAT_SET");
                writer.WriteLine("INTEGER|sim/cockpit2/switches/avionics_power_on|AVIONICS MASTER SWITCH|bool|AVIONICS_MASTER_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav1_obs_degm|NAV OBS:1|degrees|VOR1_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav2_obs_degm|NAV OBS:2|degrees|VOR2_SET");
                writer.WriteLine("INTEGER|sim/cockpit2/radios/actuators/audio_selection_com1|COM TRANSMIT:1|bool|COM1_TRANSMIT_SELECT");
                writer.WriteLine("INTEGER|sim/cockpit2/radios/actuators/audio_selection_com2|COM TRANSMIT:2|bool|COM2_TRANSMIT_SELECT");
                writer.WriteLine("INTEGER|sim/cockpit/radios/transponder_code|TRANSPONDER CODE:1|bco16|XPNDR_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/com1_freq_hz|COM ACTIVE FREQUENCY:1|frequency bcd16|COM_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/com2_freq_hz|COM ACTIVE FREQUENCY:2|frequency bcd16|COM2_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav1_freq_hz|NAV ACTIVE FREQUENCY:1|frequency bcd16|NAV1_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav2_freq_hz|NAV ACTIVE FREQUENCY:2|frequency bcd16|NAV2_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/com1_stdby_freq_hz|COM STANDBY FREQUENCY:1|frequency bcd16|COM_STBY_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/com2_stdby_freq_hz|COM STANDBY FREQUENCY:2|frequency bcd16|COM2_STBY_RADIO_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav1_stdby_freq_hz|NAV STANDBY FREQUENCY:1|frequency bcd16|NAV1_STBY_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/nav2_stdby_freq_hz|NAV STANDBY FREQUENCY:2|frequency bcd16|NAV2_STBY_SET");
                writer.WriteLine("INTEGER|sim/cockpit/radios/adf1_freq_hz|ADF ACTIVE FREQUENCY:1|frequency adf bcd32|ADF_COMPLETE_SET");
                //                    writer.WriteLine("INTEGER|sim/cockpit/radios/adf2_freq_hz|ADF ACTIVE FREQUENCY:2|frequency adf bcd32");
                writer.WriteLine("INTEGER||AI TRAFFIC ISIFR|bool");
                writer.WriteLine("STRING8||AI TRAFFIC FROMAIRPORT|");
                writer.WriteLine("STRING8||AI TRAFFIC TOAIRPORT|");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/autopilot_mode|AUTOPILOT MASTER|bool");
                writer.WriteLine("INTEGER||AUTOPILOT HEADING LOCK|bool|AP_PANEL_HEADING_HOLD");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/heading_mag|AUTOPILOT HEADING LOCK DIR|degrees|HEADING_BUG_SET");
                writer.WriteLine("INTEGER||AUTOPILOT ALTITUDE LOCK|bool|AP_PANEL_ALTITUDE_HOLD");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/altitude|AUTOPILOT ALTITUDE LOCK VAR|feet|AP_ALT_VAR_SET_ENGLISH");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/vertical_velocity|AUTOPILOT VERTICAL HOLD VAR|feet/minute|AP_VS_VAR_SET_ENGLISH");
                writer.WriteLine("INTEGER||AUTOPILOT AIRSPEED HOLD|bool|AP_AIRSPEED_HOLD");
                writer.WriteLine("INTEGER|sim/cockpit/autopilot/airspeed|AUTOPILOT AIRSPEED HOLD VAR|knots|AP_SPD_VAR_SET");
                writer.WriteLine("INTEGER||AUTOPILOT ATTITUDE HOLD|bool|AP_ATT_HOLD");
                writer.WriteLine("INTEGER||AUTOPILOT APPROACH HOLD|bool|AP_APR_HOLD");
                writer.WriteLine("INTEGER||AUTOPILOT BACKCOURSE HOLD|bool|AP_BC_HOLD");
                writer.WriteLine("INTEGER||AUTOPILOT NAV1 LOCK|bool|AP_NAV1_HOLD");
                writer.WriteLine("INTEGER||AUTOPILOT THROTTLE ARM|bool|AUTO_THROTTLE_ARM");
                writer.WriteLine("INTEGER|sim/cockpit/switches/DME_radio_selector|SELECTED DME|number|TOGGLE_DME");
                //                    writer.WriteLine("INTEGER|sim/flightmodel/position/indicated_airspeed|AIRSPEED INDICATED|knots");
                //                    writer.WriteLine("INTEGER|sim/flightmodel/misc/h_ind|INDICATED ALTITUDE|feet");
                //                  writer.WriteLine("INTEGER|sim/flightmodel/position/vh_ind_fpm|VERTICAL SPEED|feet per second");
                //                    writer.WriteLine("INTEGER|sim/flightmodel/misc/turnrate_roll|TURN INDICATOR RATE|radians per second");
                //                    writer.WriteLine("INTEGER|sim/flightmodel/misc/slip|TURN COORDINATOR BALL|position");
                writer.WriteLine("FLOAT|sim/cockpit2/switches/rotor_brake_ratio|ROTOR BRAKE HANDLE POS|position|ROTOR_BRAKE");
                writer.WriteLine("INTEGER|sim/cockpit2/switches/clutch_engage|ROTOR CLUTCH SWITCH POS|bool|ROTOR_CLUTCH_SWITCH_SET");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/governor_on|ROTOR GOV SWITCH POS|bool|ROTOR_GOV_SWITCH_SET");
                writer.WriteLine("FLOAT||ROTOR LATERAL TRIM PCT|position|ROTOR_LATERAL_TRIM_SET*16383");
            }
            catch (Exception ex)
            {
                // message
                main.ShowMessage(ex.Message);
            }
            finally
            {
                // close writer
                if (writer != null) writer.Close();
            }

            // get rotorcraft variables
            filename = Path.Combine(path, "SingleProp.txt");
            try
            {
                writer = File.CreateText(filename);
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:0|GENERAL ENG COMBUSTION:1|bool||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:0|GENERAL ENG THROTTLE LEVER POSITION:1|position|THROTTLE1_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:0|GENERAL ENG MIXTURE LEVER POSITION:1|position|MIXTURE1_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/prop_ratio:0|GENERAL ENG PROPELLER LEVER POSITION:1|position|PROP_PITCH1_SET*16383|PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/primer_on:0|RECIP ENG PRIMER:1|bool");
                writer.WriteLine("FLOAT|sim/cockpit2/switches/alternate_static_air_ratio:0|RECIP ENG ALTERNATE AIR POSITION:1|position");
                writer.WriteLine("INTEGER||RECIP ENG LEFT MAGNETO:1|bool");
                writer.WriteLine("INTEGER||RECIP ENG RIGHT MAGNETO:1|bool");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:0|RECIP ENG FUEL AVAILABLE:1|bool||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:0|RECIP ENG FUEL FLOW:1|kilograms per second||PILOT");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANKS USED:1|mask||PILOT");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/prop_rotation_speed_rad_sec:0*9.549|PROP RPM:1|rpm||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:0|GENERAL ENG MASTER ALTERNATOR:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:0|GENERAL ENG FUEL PUMP SWITCH:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:0|GENERAL ENG STARTER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:0|GENERAL ENG ANTI ICE POSITION:1|bool|ANTI_ICE_SET_ENG1");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:0||");
                writer.WriteLine("INTEGER||PROP FEATHER SWITCH:1|bool");
            }
            catch (Exception ex)
            {
                // message
                main.ShowMessage(ex.Message);
            }
            finally
            {
                // close writer
                if (writer != null) writer.Close();
            }

            // get rotorcraft variables
            filename = Path.Combine(path, "TwinProp.txt");
            try
            {
                writer = File.CreateText(filename);
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:0|GENERAL ENG COMBUSTION:1|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:1|GENERAL ENG COMBUSTION:2|bool||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:0|GENERAL ENG THROTTLE LEVER POSITION:1|position|THROTTLE1_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:1|GENERAL ENG THROTTLE LEVER POSITION:2|position|THROTTLE2_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:0|GENERAL ENG MIXTURE LEVER POSITION:1|position|MIXTURE1_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:1|GENERAL ENG MIXTURE LEVER POSITION:2|position|MIXTURE2_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/prop_ratio:0|GENERAL ENG PROPELLER LEVER POSITION:1|position|PROP_PITCH1_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/prop_ratio:1|GENERAL ENG PROPELLER LEVER POSITION:2|position|PROP_PITCH2_SET*16383|PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/primer_on:0|RECIP ENG PRIMER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/primer_on:1|RECIP ENG PRIMER:2|bool");
                writer.WriteLine("FLOAT|sim/cockpit2/switches/alternate_static_air_ratio:0|RECIP ENG ALTERNATE AIR POSITION:1|position");
                writer.WriteLine("FLOAT|sim/cockpit2/switches/alternate_static_air_ratio:1|RECIP ENG ALTERNATE AIR POSITION:2|position");
                writer.WriteLine("INTEGER||RECIP ENG LEFT MAGNETO:1|bool");
                writer.WriteLine("INTEGER||RECIP ENG LEFT MAGNETO:2|bool");
                writer.WriteLine("INTEGER||RECIP ENG RIGHT MAGNETO:1|bool");
                writer.WriteLine("INTEGER||RECIP ENG RIGHT MAGNETO:2|bool");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:0|RECIP ENG FUEL AVAILABLE:1|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:1|RECIP ENG FUEL AVAILABLE:2|bool||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:0|RECIP ENG FUEL FLOW:1|kilograms per second||PILOT");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:1|RECIP ENG FUEL FLOW:2|kilograms per second||PILOT");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANKS USED:1|mask||PILOT");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANKS USED:2|mask||PILOT");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/prop_rotation_speed_rad_sec:0*9.549|PROP RPM:1|rpm||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/prop_rotation_speed_rad_sec:1*9.549|PROP RPM:2|rpm||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:0|GENERAL ENG MASTER ALTERNATOR:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:1|GENERAL ENG MASTER ALTERNATOR:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:0|GENERAL ENG FUEL PUMP SWITCH:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:1|GENERAL ENG FUEL PUMP SWITCH:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:0|GENERAL ENG STARTER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:1|GENERAL ENG STARTER:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:0|GENERAL ENG ANTI ICE POSITION:1|bool|ANTI_ICE_SET_ENG1");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:1|GENERAL ENG ANTI ICE POSITION:2|bool|ANTI_ICE_SET_ENG2");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANK SELECTOR:1|enum");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANK SELECTOR:2|enum");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:0||");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:1||");
                writer.WriteLine("INTEGER||PROP FEATHER SWITCH:1|bool");
                writer.WriteLine("INTEGER||PROP FEATHER SWITCH:2|bool");
            }
            catch (Exception ex)
            {
                // message
                main.ShowMessage(ex.Message);
            }
            finally
            {
                // close writer
                if (writer != null) writer.Close();
            }

            // get rotorcraft variables
            filename = Path.Combine(path, "QuadProp.txt");
            try
            {
                writer = File.CreateText(filename);
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:0|GENERAL ENG COMBUSTION:1|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:1|GENERAL ENG COMBUSTION:2|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:2|GENERAL ENG COMBUSTION:3|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:3|GENERAL ENG COMBUSTION:4|bool||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:0|GENERAL ENG THROTTLE LEVER POSITION:1|position|THROTTLE1_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:1|GENERAL ENG THROTTLE LEVER POSITION:2|position|THROTTLE2_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:2|GENERAL ENG THROTTLE LEVER POSITION:3|position|THROTTLE3_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:3|GENERAL ENG THROTTLE LEVER POSITION:4|position|THROTTLE4_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:0|GENERAL ENG MIXTURE LEVER POSITION:1|position|MIXTURE1_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:1|GENERAL ENG MIXTURE LEVER POSITION:2|position|MIXTURE2_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:2|GENERAL ENG MIXTURE LEVER POSITION:3|position|MIXTURE3_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:3|GENERAL ENG MIXTURE LEVER POSITION:4|position|MIXTURE4_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/prop_ratio:0|GENERAL ENG PROPELLER LEVER POSITION:1|position|PROP_PITCH1_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/prop_ratio:1|GENERAL ENG PROPELLER LEVER POSITION:2|position|PROP_PITCH2_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/prop_ratio:2|GENERAL ENG PROPELLER LEVER POSITION:3|position|PROP_PITCH3_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/prop_ratio:3|GENERAL ENG PROPELLER LEVER POSITION:4|position|PROP_PITCH4_SET*16383|PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/primer_on:0|RECIP ENG PRIMER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/primer_on:1|RECIP ENG PRIMER:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/primer_on:2|RECIP ENG PRIMER:3|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/primer_on:3|RECIP ENG PRIMER:4|bool");
                writer.WriteLine("FLOAT|sim/cockpit2/switches/alternate_static_air_ratio:0|RECIP ENG ALTERNATE AIR POSITION:1|position");
                writer.WriteLine("FLOAT|sim/cockpit2/switches/alternate_static_air_ratio:1|RECIP ENG ALTERNATE AIR POSITION:2|position");
                writer.WriteLine("FLOAT|sim/cockpit2/switches/alternate_static_air_ratio:2|RECIP ENG ALTERNATE AIR POSITION:3|position");
                writer.WriteLine("FLOAT|sim/cockpit2/switches/alternate_static_air_ratio:3|RECIP ENG ALTERNATE AIR POSITION:4|position");
                writer.WriteLine("INTEGER||RECIP ENG LEFT MAGNETO:1|bool");
                writer.WriteLine("INTEGER||RECIP ENG LEFT MAGNETO:2|bool");
                writer.WriteLine("INTEGER||RECIP ENG LEFT MAGNETO:3|bool");
                writer.WriteLine("INTEGER||RECIP ENG LEFT MAGNETO:4|bool");
                writer.WriteLine("INTEGER||RECIP ENG RIGHT MAGNETO:1|bool");
                writer.WriteLine("INTEGER||RECIP ENG RIGHT MAGNETO:2|bool");
                writer.WriteLine("INTEGER||RECIP ENG RIGHT MAGNETO:3|bool");
                writer.WriteLine("INTEGER||RECIP ENG RIGHT MAGNETO:4|bool");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:0|RECIP ENG FUEL AVAILABLE:1|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:1|RECIP ENG FUEL AVAILABLE:2|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:2|RECIP ENG FUEL AVAILABLE:3|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:3|RECIP ENG FUEL AVAILABLE:4|bool||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:0|RECIP ENG FUEL FLOW:1|kilograms per second||PILOT");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:1|RECIP ENG FUEL FLOW:2|kilograms per second||PILOT");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:2|RECIP ENG FUEL FLOW:3|kilograms per second||PILOT");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:3|RECIP ENG FUEL FLOW:4|kilograms per second||PILOT");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANKS USED:1|mask||PILOT");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANKS USED:2|mask||PILOT");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANKS USED:3|mask||PILOT");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANKS USED:4|mask||PILOT");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/prop_rotation_speed_rad_sec:0*9.549|PROP RPM:1|rpm||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/prop_rotation_speed_rad_sec:1*9.549|PROP RPM:2|rpm||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/prop_rotation_speed_rad_sec:2*9.549|PROP RPM:3|rpm||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/prop_rotation_speed_rad_sec:3*9.549|PROP RPM:4|rpm||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:0|GENERAL ENG MASTER ALTERNATOR:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:1|GENERAL ENG MASTER ALTERNATOR:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:2|GENERAL ENG MASTER ALTERNATOR:3|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:3|GENERAL ENG MASTER ALTERNATOR:4|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:0|GENERAL ENG FUEL PUMP SWITCH:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:1|GENERAL ENG FUEL PUMP SWITCH:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:2|GENERAL ENG FUEL PUMP SWITCH:3|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:3|GENERAL ENG FUEL PUMP SWITCH:4|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:0|GENERAL ENG STARTER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:1|GENERAL ENG STARTER:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:2|GENERAL ENG STARTER:3|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:3|GENERAL ENG STARTER:4|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:0|GENERAL ENG ANTI ICE POSITION:1|bool|ANTI_ICE_SET_ENG1");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:1|GENERAL ENG ANTI ICE POSITION:2|bool|ANTI_ICE_SET_ENG2");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:2|GENERAL ENG ANTI ICE POSITION:3|bool|ANTI_ICE_SET_ENG3");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:3|GENERAL ENG ANTI ICE POSITION:4|bool|ANTI_ICE_SET_ENG4");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANK SELECTOR:1|enum");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANK SELECTOR:2|enum");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANK SELECTOR:3|enum");
                writer.WriteLine("INTEGER||RECIP ENG FUEL TANK SELECTOR:4|enum");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:0||");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:1||");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:2||");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:3||");
                writer.WriteLine("INTEGER||PROP FEATHER SWITCH:1|bool");
                writer.WriteLine("INTEGER||PROP FEATHER SWITCH:2|bool");
                writer.WriteLine("INTEGER||PROP FEATHER SWITCH:3|bool");
                writer.WriteLine("INTEGER||PROP FEATHER SWITCH:4|bool");
            }
            catch (Exception ex)
            {
                // message
                main.ShowMessage(ex.Message);
            }
            finally
            {
                // close writer
                if (writer != null) writer.Close();
            }

            // get rotorcraft variables
            filename = Path.Combine(path, "SingleTurbine.txt");
            try
            {
                writer = File.CreateText(filename);
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:0|GENERAL ENG COMBUSTION:1|bool||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:0|GENERAL ENG THROTTLE LEVER POSITION:1|position|THROTTLE1_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:0|GENERAL ENG MIXTURE LEVER POSITION:1|position|MIXTURE1_SET*16383|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N1_percent:0|TURB ENG N1:1|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N2_percent:0|TURB ENG N2:1|percent||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:0|TURB ENG CORRECTED FF:1|kilograms per second||PILOT");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/ITT_deg_C:0|TURB ENG ITT:1|degrees||PILOT");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:0|GENERAL ENG MASTER ALTERNATOR:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:0|GENERAL ENG FUEL PUMP SWITCH:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:0|GENERAL ENG STARTER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:0|GENERAL ENG ANTI ICE POSITION:1|bool|ANTI_ICE_SET_ENG1");
                writer.WriteLine("INTEGER||TURB ENG TANK SELECTOR:1|enum");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:0||");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/afterburner_enabled:0|TURB ENG AFTERBURNER:1|bool");
            }
            catch (Exception ex)
            {
                // message
                main.ShowMessage(ex.Message);
            }
            finally
            {
                // close writer
                if (writer != null) writer.Close();
            }

            // get rotorcraft variables
            filename = Path.Combine(path, "TwinTurbine.txt");
            try
            {
                writer = File.CreateText(filename);
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:0|GENERAL ENG COMBUSTION:1|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:1|GENERAL ENG COMBUSTION:2|bool||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:0|GENERAL ENG THROTTLE LEVER POSITION:1|position|THROTTLE1_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:1|GENERAL ENG THROTTLE LEVER POSITION:2|position|THROTTLE2_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:0|GENERAL ENG MIXTURE LEVER POSITION:1|position|MIXTURE1_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:1|GENERAL ENG MIXTURE LEVER POSITION:2|position|MIXTURE2_SET*16383|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N1_percent:0|TURB ENG N1:1|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N1_percent:1|TURB ENG N1:2|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N2_percent:0|TURB ENG N2:1|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N2_percent:1|TURB ENG N2:2|percent||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:0|TURB ENG CORRECTED FF:1|kilograms per second||PILOT");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:1|TURB ENG CORRECTED FF:2|kilograms per second||PILOT");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/ITT_deg_C:0|TURB ENG ITT:1|degrees||PILOT");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/ITT_deg_C:1|TURB ENG ITT:2|degrees||PILOT");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:0|GENERAL ENG MASTER ALTERNATOR:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:1|GENERAL ENG MASTER ALTERNATOR:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:0|GENERAL ENG FUEL PUMP SWITCH:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:1|GENERAL ENG FUEL PUMP SWITCH:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:0|GENERAL ENG STARTER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:1|GENERAL ENG STARTER:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:0|GENERAL ENG ANTI ICE POSITION:1|bool|ANTI_ICE_SET_ENG1");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:1|GENERAL ENG ANTI ICE POSITION:2|bool|ANTI_ICE_SET_ENG2");
                writer.WriteLine("INTEGER||TURB ENG TANK SELECTOR:1|enum");
                writer.WriteLine("INTEGER||TURB ENG TANK SELECTOR:2|enum");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:0||");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:1||");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/afterburner_enabled:0|TURB ENG AFTERBURNER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/afterburner_enabled:1|TURB ENG AFTERBURNER:2|bool");
            }
            catch (Exception ex)
            {
                // message
                main.ShowMessage(ex.Message);
            }
            finally
            {
                // close writer
                if (writer != null) writer.Close();
            }

            // get rotorcraft variables
            filename = Path.Combine(path, "QuadTurbine.txt");
            try
            {
                writer = File.CreateText(filename);
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:0|GENERAL ENG COMBUSTION:1|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:1|GENERAL ENG COMBUSTION:2|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:2|GENERAL ENG COMBUSTION:3|bool||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/has_fuel_flow_after_mixture:3|GENERAL ENG COMBUSTION:4|bool||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:0|GENERAL ENG THROTTLE LEVER POSITION:1|position|THROTTLE1_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:1|GENERAL ENG THROTTLE LEVER POSITION:2|position|THROTTLE2_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:2|GENERAL ENG THROTTLE LEVER POSITION:3|position|THROTTLE3_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/throttle_ratio:3|GENERAL ENG THROTTLE LEVER POSITION:4|position|THROTTLE4_SET*16383|PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:0|GENERAL ENG MIXTURE LEVER POSITION:1|position|MIXTURE1_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:1|GENERAL ENG MIXTURE LEVER POSITION:2|position|MIXTURE2_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:2|GENERAL ENG MIXTURE LEVER POSITION:3|position|MIXTURE3_SET*16383|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/actuators/mixture_ratio:3|GENERAL ENG MIXTURE LEVER POSITION:4|position|MIXTURE4_SET*16383|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N1_percent:0|TURB ENG N1:1|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N1_percent:1|TURB ENG N1:2|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N1_percent:2|TURB ENG N1:3|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N1_percent:3|TURB ENG N1:4|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N2_percent:0|TURB ENG N2:1|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N2_percent:1|TURB ENG N2:2|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N2_percent:2|TURB ENG N2:3|percent||PILOT|INJECTED");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/N2_percent:3|TURB ENG N2:4|percent||PILOT|INJECTED");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:0|TURB ENG CORRECTED FF:1|kilograms per second||PILOT");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:1|TURB ENG CORRECTED FF:2|kilograms per second||PILOT");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:2|TURB ENG CORRECTED FF:3|kilograms per second||PILOT");
                writer.WriteLine("FLOAT|sim/cockpit2/engine/indicators/fuel_flow_kg_sec:3|TURB ENG CORRECTED FF:4|kilograms per second||PILOT");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/ITT_deg_C:0|TURB ENG ITT:1|degrees||PILOT");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/ITT_deg_C:1|TURB ENG ITT:2|degrees||PILOT");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/ITT_deg_C:2|TURB ENG ITT:3|degrees||PILOT");
                writer.WriteLine("INTEGER|sim/flightmodel2/engines/ITT_deg_C:3|TURB ENG ITT:4|degrees||PILOT");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:0|GENERAL ENG MASTER ALTERNATOR:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:1|GENERAL ENG MASTER ALTERNATOR:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:2|GENERAL ENG MASTER ALTERNATOR:3|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/electrical/generator_on:3|GENERAL ENG MASTER ALTERNATOR:4|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:0|GENERAL ENG FUEL PUMP SWITCH:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:1|GENERAL ENG FUEL PUMP SWITCH:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:2|GENERAL ENG FUEL PUMP SWITCH:3|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/fuel_pump_on:3|GENERAL ENG FUEL PUMP SWITCH:4|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:0|GENERAL ENG STARTER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:1|GENERAL ENG STARTER:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:2|GENERAL ENG STARTER:3|bool");
                writer.WriteLine("INTEGER|sim/cockpit/engine/igniters_on:3|GENERAL ENG STARTER:4|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:0|GENERAL ENG ANTI ICE POSITION:1|bool|ANTI_ICE_SET_ENG1");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:1|GENERAL ENG ANTI ICE POSITION:2|bool|ANTI_ICE_SET_ENG2");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:2|GENERAL ENG ANTI ICE POSITION:3|bool|ANTI_ICE_SET_ENG3");
                writer.WriteLine("INTEGER|sim/cockpit2/ice/cowling_thermal_anti_ice_per_engine:3|GENERAL ENG ANTI ICE POSITION:4|bool|ANTI_ICE_SET_ENG4");
                writer.WriteLine("INTEGER||TURB ENG TANK SELECTOR:1|enum");
                writer.WriteLine("INTEGER||TURB ENG TANK SELECTOR:2|enum");
                writer.WriteLine("INTEGER||TURB ENG TANK SELECTOR:3|enum");
                writer.WriteLine("INTEGER||TURB ENG TANK SELECTOR:4|enum");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:0||");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:1||");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:2||");
                writer.WriteLine("INTEGER|sim/cockpit2/fuel/fuel_tank_selector:3||");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/afterburner_enabled:0|TURB ENG AFTERBURNER:1|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/afterburner_enabled:1|TURB ENG AFTERBURNER:2|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/afterburner_enabled:2|TURB ENG AFTERBURNER:3|bool");
                writer.WriteLine("INTEGER|sim/cockpit2/engine/actuators/afterburner_enabled:3|TURB ENG AFTERBURNER:4|bool");
            }
            catch (Exception ex)
            {
                // message
                main.ShowMessage(ex.Message);
            }
            finally
            {
                // close writer
                if (writer != null) writer.Close();
            }
        }

        /// <summary>
        /// Reset the variable manager
        /// </summary>
        public void Reset()
        {
            // clear definitions
            definitions.Clear();
            // clear file list
            files.Clear();
            // clear aliases
            aliasVuids.Clear();
            // clear SimConnect definition
            vuids.Clear();
            // reset IDs
            nextDefinition = ScDefinition.ID0;
            nextRequest = ScRequest.ID0;
        }

        /// <summary>
        /// Load variables
        /// </summary>
        /// <param name="filename"></param>
        public List<uint> GetFromFile(string filename)
        {
            // check if file already loaded
            if (files.ContainsKey(filename))
            {
                // return variables
                return files[filename];
            }

            // get full path
            string fullpath = Path.Combine(path, filename);

            // check for file
            if (File.Exists(fullpath))
            {
                // reader
                StreamReader reader = null;
                try
                {
                    // open file
                    reader = File.OpenText(fullpath);
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // variable
                        Definition newDefinition;

                        // parse line
                        string[] parts = line.Split('|');
                        // check for three parts
                        if (parts.Length >= 4)
                        {
                            // get parts
                            string typeName = parts[0].ToLower();
                            string scName = parts[2].ToLower();
                            string scUnits = parts[3].ToLower();

                            // type
                            Definition.Type type;

                            // interpret variable type
                            if (typeName == "integer")
                            {
                                type = Definition.Type.INTEGER;
                            }
                            else if (typeName == "float")
                            {
                                type = Definition.Type.FLOAT;
                            }
                            else if (typeName == "string8")
                            {
                                type = Definition.Type.STRING8;
                            }
                            else
                            {
                                throw new Exception("Invalid variable type, " + typeName);
                            }

                            // get dataref
                            string drName = "";
                            float drScalar = 0.0f;
                            int drIndex = 0;
                            string[] starParts = parts[1].Split('*');
                            // check for dataref name
                            if (starParts.Length > 0)
                            {
                                // check for dataref scalar
                                if (starParts.Length > 1)
                                {
                                    // get event scalar
                                    float.TryParse(starParts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out drScalar);
                                }
                                string[] colonParts = starParts[0].Split(':');
                                // check for name
                                if (colonParts.Length > 0)
                                {
                                    // get dataref name
                                    drName = colonParts[0];
                                }
                                if (colonParts.Length > 1)
                                {
                                    // get event index
                                    int.TryParse(colonParts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out drIndex);
                                }
                            }

                            // create new variable
                            newDefinition = new Definition(type, drName, drScalar, drIndex, NextDefinition, scName, scUnits);

                            // check for mask units
                            if (newDefinition.scUnits.Length >= 6 && newDefinition.scUnits.Substring(0, 5) == "mask:")
                            {
                                // read flag bit position 
                                if (uint.TryParse(newDefinition.scUnits.Substring(5), NumberStyles.Number, CultureInfo.InvariantCulture, out uint bit) && bit < 32)
                                {
                                    // create mask
                                    newDefinition.mask = 1 << (int)bit;
                                    // get vuid for simconnect mask variable
                                    newDefinition.maskVuid = CreateVuid(scName);
                                    // reset units to mask
                                    newDefinition.scUnits = "mask";
                                }
                                else
                                {
                                    // error
                                    throw new Exception("Failed to parse variable - " + line);
                                }
                            }

                            // vuid
                            uint vuid = 0;
                            uint aliasVuid = 0;

                            // check for valid simconnect variable
                            if (scName.Length > 0)
                            {
                                // create primary vuid
                                vuid = newDefinition.mask == 0 ? CreateVuid(scName) : CreateVuid(scName + newDefinition.mask.ToString(CultureInfo.InvariantCulture));
                                // check for dataref
                                if (parts[1].Length > 0)
                                {
                                    // create alias
                                    aliasVuid = CreateVuid(parts[1]);
                                }
                            }
                            else if (parts[1].Length > 0)
                            {
                                // create primary vuid
                                vuid = CreateVuid(parts[1]);
                            }

                            // check for existing variable
                            if (definitions.ContainsKey(vuid))
                            {
                                // check for vuid hash collision
                                if (definitions[vuid].scName != newDefinition.scName)
                                {
                                    // monitor
                                    main.MonitorEvent("ERROR - Duplicate hash ID for variable - " + line);
                                }
                            }
                            else
                            {
                                // get name parts
                                string[] scNameParts = scName.Split(':');
                                // check for smoke
                                if (scNameParts.Length == 2 && scNameParts[0] == "smoke enable")
                                {
                                    // get smoke index
                                    int.TryParse(scNameParts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out newDefinition.smokeIndex);
                                }

                                // check for set event
                                if (parts.Length >= 5)
                                {
                                    // get event data
                                    string[] eventParts = parts[4].Split('*');
                                    // check for event name
                                    if (eventParts.Length > 0)
                                    {
                                        // get event name
                                        newDefinition.scEventName = eventParts[0];
                                        newDefinition.scEvent = NextEvent;
                                    }
                                    // check for event scalar
                                    if (eventParts.Length > 1)
                                    {
                                        // get event scalar
                                        double.TryParse(eventParts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out newDefinition.scEventScalar);
                                    }
                                }

                                // process options
                                for (int index = 5; index < parts.Length; index++)
                                {
                                    // check for pilot option
                                    if (parts[index].ToLower() == "pilot")
                                    {
                                        // set pilot flag
                                        newDefinition.pilot = true;
                                    }
                                    // check for injected option
                                    else if (parts[index].ToLower() == "injected")
                                    {
                                        // set injected flag
                                        newDefinition.injected = true;
                                    }
                                }

                                // check for mask
                                if (newDefinition.mask != 0)
                                {
                                    // check for existing definition
                                    if (definitions.ContainsKey(newDefinition.maskVuid) == false)
                                    {
                                        // definition for the mask variable
                                        Definition maskDefinition = new Definition(Definition.Type.INTEGER, "", 1.0f, 0, NextDefinition, scName, "mask");
                                        // copy flags
                                        maskDefinition.injected = newDefinition.injected;
                                        maskDefinition.pilot = newDefinition.pilot;
                                        // check for simulator
                                        if (main.sim != null)
                                        {
                                            // register sim variable
                                            main.sim.RegisterIntegerVariable(maskDefinition);
                                        }
                                        // add definition
                                        definitions.Add(newDefinition.maskVuid, maskDefinition);
                                        // add vuid
                                        vuids[maskDefinition.scDefinition] = newDefinition.maskVuid;

                                        // check if file is not known
                                        if (files.ContainsKey(filename) == false)
                                        {
                                            // add empty list
                                            files.Add(filename, new List<uint>());
                                        }
                                        // add variable to this file
                                        files[filename].Add(newDefinition.maskVuid);
                                    }
                                }

                                // register with simconnect
                                if (main.sim != null)
                                {
                                    switch (type)
                                    {
                                        case Definition.Type.INTEGER:
                                            main.sim.RegisterIntegerVariable(newDefinition);
                                            break;
                                        case Definition.Type.FLOAT:
                                            main.sim.RegisterFloatVariable(newDefinition);
                                            break;
                                        case Definition.Type.STRING8:
                                            main.sim.RegisterString8Variable(newDefinition);
                                            break;
                                        default:
                                            main.MonitorEvent("ERROR - Invalid variable type - " + line);
                                            break;
                                    }

                                    // register event
                                    main.sim.RegisterVariableEvent(newDefinition);
                                }

                                // add definition
                                definitions.Add(vuid, newDefinition);
                                // add vuid
                                vuids[newDefinition.scDefinition] = vuid;
                                // check for alias
                                if (aliasVuid != 0)
                                {
                                    // add alias vuid
                                    aliasVuids[aliasVuid] = vuid;
                                }

#if XPLANE || CONSOLE
                                // check for simulator and valid dataref
                                if (main.sim != null)
                                {
                                    // update X-Plane
                                    main.sim.xplane.SendDefinition(vuid);
                                }
#endif
                            }

                            // check if file is not known
                            if (files.ContainsKey(filename) == false)
                            {
                                // add empty list
                                files.Add(filename, new List<uint>());
                            }
                            // add variable to this file
                            files[filename].Add(vuid);
                        }
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage("ERROR - Failed to load variables - " + ex.Message);
                }
                finally
                {
                    // close reader
                    if (reader != null) reader.Close();
                }
            }

            // check for loaded file
            if (files.ContainsKey(filename))
            {
                // return variables
                return files[filename];
            }

            // return empty list
            return new List<uint>();
        }

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
            public Dictionary<uint, ScRequest> scRequests = new Dictionary<uint, ScRequest>();
            Dictionary<ScRequest, uint> scRequestVuids = new Dictionary<ScRequest, uint>();

            /// <summary>
            /// List of integer variables
            /// </summary>
            public Dictionary<uint, int> integers = new Dictionary<uint, int>();

            /// <summary>
            /// List of float variables
            /// </summary>
            public Dictionary<uint, float> floats = new Dictionary<uint, float>();

            /// <summary>
            /// List of string8 variables
            /// </summary>
            public Dictionary<uint, string> string8s = new Dictionary<uint, string>();

            /// <summary>
            /// List of change times
            /// </summary>
            public Dictionary<uint, double> startTimes = new Dictionary<uint, double>();

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
                                main.sim ?. RequestVariable(scRequest, definition.scDefinition, simId);
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
                        main.sim ?. StopRequest(request.Value, ScDefinition.ID0, simId);
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
            bool UserAircraft { get { return main.sim ?. userAircraft != null && main.sim.userAircraft.simId == simId; } }

            /// <summary>
            /// Is the object a slave
            /// </summary>
            double DelayTime
            {
                get
                {
                    // check if user aircraft and entered another aircraft
                    if (UserAircraft && main.sim ?. enteredAircraft != null)
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
                                                    main.sim ?. DoSimEvent(simId, Sim.Event.SMOKE_ON, (uint)definition.smokeIndex);
                                                }
                                                else
                                                {
                                                    // smoke off
                                                    main.sim ?. DoSimEvent(simId, Sim.Event.SMOKE_OFF, (uint)definition.smokeIndex);
                                                }
                                            }
                                            // check for event
                                            else if (definition.scEventName.Length > 0)
                                            {
                                                // event update
                                                main.sim ?. DoSimEvent(simId, definition, (uint)ConvertToSimConnect(definition.scUnits, (int)(value * definition.scEventScalar)));
                                            }
                                            else if (definition.scName.Length > 0)
                                            {
                                                // update simconnect
                                                Sim.IntegerStruct data = new Sim.IntegerStruct
                                                {
                                                    value = ConvertToSimConnect(definition.scUnits, value)
                                                };
                                                main.sim ?. UpdateVariable(definition.scDefinition, simId, data);
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
                                                        main.sim ?. DoSimEvent(simId, definition, scaledValue);
                                                        // monitor
                                                        main.MonitorVariables("UPDATE VARIABLE - OBJECT:" + simId + " - " + definition.scName + " = " + value);
                                                    }
                                                }
                                                else if (definition.scName.Length > 0)
                                                {
                                                    // update simconnect variable
                                                    Sim.FloatStruct data = new Sim.FloatStruct
                                                    {
                                                        value = value
                                                    };
                                                    main.sim ?. UpdateVariable(definition.scDefinition, simId, data);
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
                                                Sim.String8Struct data = new Sim.String8Struct
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
