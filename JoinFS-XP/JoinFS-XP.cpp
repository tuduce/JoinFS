// JoinFS-XP.cpp : Defines the exported functions for the DLL application.
// JoinFS-XP.cpp : Defines the exported functions for the DLL application.
//Compile for linux with g++ -o name.xpl Common.c Link.cpp JoinFS-XP.cpp -shared -rdynamic -nodefaultlibs -undefined_warning -fPIC -std=c++17
//

#include <stdlib.h>
#include <cstdio>
#include <string.h>
#include <string>
#include <inttypes.h>
#include <math.h>
#include <stdint.h>

#include <vector>
#include <map>
#include <fstream>
#include <string>

#define XPLM200
#define XPLM210
#define XPLM300

#ifdef _WINDOWS
#include <WinSock2.h>
#define IBM 1

#else

#include <sys/sysinfo.h>
#include <sys/time.h>
#include <sys/socket.h>
#define LIN 1

#define min std::min
#define max std::max
#define sprintf_s snprintf
#define strcpy_s snprintf
#define strcat_s strcat
#endif


using namespace std;

static const double GEODESIC_EPSILON = 0.0000001;
static const double CATCH_UP_RATE = 1.5;
static const double PI = 3.14159265359;
static const double FEET_PER_METRE = 3.28084;


#include "XPMPMultiplayer.h"
#include "XPMPAircraft.h"

using namespace XPMP2;

#include "XPLMPlanes.h"
#include "XPLMDataAccess.h"
#include "XPLMProcessing.h"
#include "XPLMDisplay.h"
#include "XPLMGraphics.h"
#include "XPLMUtilities.h"
#include "XPLMScenery.h"
#include "XPLMPlugin.h"

#include "Link.h"
#include "Common.h"

using namespace Common;


static double PCFreq = 0.0;

#ifdef _WINDOWS
static __int64 CounterStart = 0;
#else
static int64_t CounterStart = 0;
struct timeval ts_start, ts_end;
#endif


void StartCounter()
{
#ifdef _WINDOWS
	LARGE_INTEGER li;
    if(!QueryPerformanceFrequency(&li))
    {
        DebugMsg("Failed to query performance counter\n");
    }

    PCFreq = double(li.QuadPart);

    QueryPerformanceCounter(&li);
    CounterStart = li.QuadPart;
#else
	gettimeofday(&ts_start, NULL);
#endif
}

static double GetTime()
{
#ifdef _WINDOWS
	LARGE_INTEGER li;
    QueryPerformanceCounter(&li);
    return double(li.QuadPart-CounterStart)/PCFreq;
#else
	gettimeofday(&ts_end, NULL);
	//time = timespec_sub(ts_end, ts_start);
	return (double)(ts_end.tv_sec - ts_start.tv_sec + 1e-6 * (ts_end.tv_usec - ts_start.tv_usec));
#endif
}

/// Save string copy
inline char* strScpy(char* dest, const char* src, size_t size)
{
	strcpy_s(dest, size, src);
	dest[size - 1] = 0;               // this ensures zero-termination!
	return dest;
}

struct Position
{
	// network state
	double nx, ny, nz;
	double nvx, nvy, nvz;
	double nax, nay, naz;
	double np, nb, nh;
	double nop, nob, noh;
	double navx, navy, navz;

	// sim state
	double x, y, z;
	double vx, vy, vz;
	double p, b, h;

	// blend timers
	double netStateTime;
	double netRealTime;
	double netSimTime;
	double simTime;

	// is on ground
	bool ground;
	double elevation;
	double altitude;
	XPLMProbeRef probe;

	void Reset()
	{
		netRealTime = netSimTime = netStateTime = simTime = 0.0;
	}

	void Init()
	{
		nx = ny = nz = 0.0;
		nvx = nvy = nvz = 0.0;
		nax = nay = naz = 0.0;
		np = nb = nh = 0.0;
		nop = nob = noh = 0.0;
		navx = navy = navz = 0.0;
		x = y = z = 0.0;
		vx = vy = vz = 0.0;
		p = b = h = 0.0;
		ground = false;
		elevation = -1.0;
		altitude = 0.0;
		probe = NULL;
		Reset();
	}

	void Close()
	{
		Reset();

		// check for valid probe
		if (probe != NULL)
		{
			// destroy probe
			XPLMDestroyProbe(probe);
			probe = NULL;
		}
	}

	bool NetValid()
	{
		return netStateTime != 0.0;
	}
};


// User Aircraft
struct UserAircraft
{
	// current position
	Position pos;

	// x-plane references
	XPLMDataRef dr_x, dr_y, dr_z;
	XPLMDataRef dr_vx, dr_vy, dr_vz;
	XPLMDataRef dr_p, dr_b, dr_h;
	
	// model references
	XPLMDataRef dr_callsign;
	XPLMDataRef dr_icaoType;

    // position references
    XPLMDataRef dr_override_position;
    XPLMDataRef dr_override_control;
    XPLMDataRef dr_longitude, dr_latitude, dr_altitude;
    XPLMDataRef dr_avx, dr_avy, dr_avz;
    XPLMDataRef dr_ax, dr_ay, dr_az;
    XPLMDataRef dr_rudder, dr_elevator, dr_aileron;
    XPLMDataRef dr_brakeLeft, dr_brakeRight;

    // plane state references
    XPLMDataRef dr_elevatorTrim, dr_aileronTrim, dr_rudderTrim;
    XPLMDataRef dr_spoilers, dr_hook, dr_wing;

    // aircraft state references
    XPLMDataRef dr_engineCount;
    XPLMDataRef dr_engineType;
    XPLMDataRef dr_minPitch;
    XPLMDataRef dr_maxPitch;
    XPLMDataRef dr_generator;
	XPLMDataRef dr_primer;
    XPLMDataRef dr_fuelSelect;
    XPLMDataRef dr_fuelPump;
    XPLMDataRef dr_ignition;
    XPLMDataRef dr_igniters;
    XPLMDataRef dr_throttle;
    XPLMDataRef dr_mixture;
    XPLMDataRef dr_prop;
    XPLMDataRef dr_cowl;
    XPLMDataRef dr_combustion;
    XPLMDataRef dr_rpm;
	XPLMDataRef dr_antiIce;
    XPLMDataRef dr_fuelAvailable;
	XPLMDataRef dr_propFeather;
	XPLMDataRef dr_egt;
	XPLMDataRef dr_oilPressure;
	XPLMDataRef dr_oilTemperature;
	XPLMDataRef dr_fuelPressure;
	XPLMDataRef dr_manifold;
	XPLMDataRef dr_alternateAir;
	XPLMDataRef dr_cht;
	XPLMDataRef dr_fuelFlow;
	XPLMDataRef dr_propRpm;
	XPLMDataRef dr_afterburner;
	XPLMDataRef dr_n1;
	XPLMDataRef dr_n2;
	XPLMDataRef dr_pressureRatio;
	XPLMDataRef dr_itt;
    XPLMDataRef dr_squawk;
    XPLMDataRef dr_navLight;
    XPLMDataRef dr_beaconLight;
    XPLMDataRef dr_landingLight;
    XPLMDataRef dr_taxiLight;
    XPLMDataRef dr_strobeLight;
    XPLMDataRef dr_panelLight;
	XPLMDataRef dr_flap_ratio_actual;
	XPLMDataRef dr_flap_ratio_handle;
	XPLMDataRef dr_flap_ratio_indicator;
	XPLMDataRef dr_flap_detents;
    XPLMDataRef dr_battery;
    XPLMDataRef dr_brakeParking;
    XPLMDataRef dr_pitot;
    XPLMDataRef dr_gear;
    XPLMDataRef dr_canopy;
    XPLMDataRef dr_avionics;
	XPLMDataRef dr_obs1;
	XPLMDataRef dr_obs2;
	XPLMDataRef dr_com1Active;
    XPLMDataRef dr_com2Active;
    XPLMDataRef dr_nav1Active;
    XPLMDataRef dr_nav2Active;
    XPLMDataRef dr_adf1Active;
    XPLMDataRef dr_adf2Active;
    XPLMDataRef dr_com1Standby;
    XPLMDataRef dr_com2Standby;
    XPLMDataRef dr_nav1Standby;
    XPLMDataRef dr_nav2Standby;
    XPLMDataRef dr_autopilot;
    XPLMDataRef dr_autopilotState;
    XPLMDataRef dr_apHeading;
    XPLMDataRef dr_apAltitude;
    XPLMDataRef dr_apVertical;
    XPLMDataRef dr_apAirspeed;
    XPLMDataRef dr_gyro;
    XPLMDataRef dr_altimeter;
    XPLMDataRef dr_dmeSelect;
	XPLMDataRef dr_fuel;
	XPLMDataRef dr_indicatedAirSpeed;
	XPLMDataRef dr_indicatedAltimeter;
	XPLMDataRef dr_indicatedVerticalSpeed;
	XPLMDataRef dr_indicatedTurnRate;
	XPLMDataRef dr_indicatedTurnSlip;

	// states
	float indicatedAirSpeed;
	float indicatedAirSpeed_target;
	float gyro;
	float gyro_target;
	float indicatedAltimeter;
	float indicatedAltimeter_target;
	float indicatedVerticalSpeed;
	float indicatedVerticalSpeed_target;
	float indicatedTurnRate;
	float indicatedTurnRate_target;
	float indicatedTurnSlip;
	float indicatedTurnSlip_target;
	float propRpm[4];
	float propRpm_target[4];

	void GetState()
	{
		// get values
		indicatedAirSpeed = XPLMGetDataf(dr_indicatedAirSpeed);
		gyro = XPLMGetDataf(dr_gyro);
		indicatedAltimeter = XPLMGetDataf(dr_indicatedAltimeter);
		indicatedVerticalSpeed = XPLMGetDataf(dr_indicatedVerticalSpeed);
		indicatedTurnRate = XPLMGetDataf(dr_indicatedTurnRate);
		indicatedTurnSlip = XPLMGetDataf(dr_indicatedTurnSlip);
		XPLMGetDatavf(dr_prop, propRpm, 0, 4);

		// set targets
		indicatedAirSpeed_target = indicatedAirSpeed;
		gyro_target = gyro;
		indicatedAltimeter_target = indicatedAltimeter;
		indicatedVerticalSpeed_target = indicatedVerticalSpeed;
		indicatedTurnRate_target = indicatedTurnRate;
		indicatedTurnSlip_target = indicatedTurnSlip;
		XPLMGetDatavf(dr_prop, propRpm_target, 0, 4);
	}

	void BlendState(float& state, float target)
	{
		state = state * 0.99f + target * 0.01f;
	}

	void AdvanceState()
	{
		// update states
		BlendState(indicatedAirSpeed, indicatedAirSpeed_target);
		BlendState(gyro, gyro_target);
		BlendState(indicatedAltimeter, indicatedAltimeter_target);
		BlendState(indicatedVerticalSpeed, indicatedVerticalSpeed_target);
		BlendState(indicatedTurnRate, indicatedTurnRate_target);
		BlendState(indicatedTurnSlip, indicatedTurnSlip_target);
		BlendState(propRpm[0], propRpm_target[0]);
		// update instruments
		XPLMSetDataf(dr_indicatedAirSpeed, indicatedAirSpeed);
//		XPLMSetDataf(dr_gyro, gyro);
		XPLMSetDataf(dr_indicatedAltimeter, indicatedAltimeter);
		XPLMSetDataf(dr_indicatedVerticalSpeed, indicatedVerticalSpeed);
		XPLMSetDataf(dr_indicatedTurnRate, indicatedTurnRate);
		XPLMSetDataf(dr_indicatedTurnSlip, indicatedTurnSlip);
		XPLMSetDatavf(dr_prop, propRpm, 0, 4);
	}

	// remote flight control
    bool overrideFlightControl;
	double endOverrideTime;

	void Init()
	{
		pos.Init();
		overrideFlightControl = false;
		endOverrideTime = 0.0;
	}

	void Close()
	{
		pos.Close();
		int enable = 0;
		XPLMSetDatavi(dr_override_position, &enable, 0, 1);
//		XPLMSetDatai(dr_override_control, 0);
		overrideFlightControl = false;
		endOverrideTime = 0.0;
	}
};


// which labels to show
static char labelFlags = 0;
static float labelColour[3] = { 0.0f, 0.0f, 0.0f };

/// Time it shall take to fly/roll a full circle [seconds]
constexpr float PLANE_CIRCLE_TIME_S = 20.0f;
/// Time it shall take to fly/roll a full circle [minutes]
constexpr float PLANE_CIRCLE_TIME_MIN = PLANE_CIRCLE_TIME_S / 60.0f;
/// Engine / prop rotation assumptions: rotations per minute
constexpr float PLANE_PROP_RPM = 300.0f;

static XPLMDataRef dr_time = XPLMFindDataRef("sim/time/total_running_time_sec");

																			
/// Returns a number between 0.0 and 1.0, increasing over the course of 10 seconds, then restarting
inline float GetTimeFragment()
{
	const float t = XPLMGetDataf(dr_time);
	return std::fmod(t, PLANE_CIRCLE_TIME_S) / PLANE_CIRCLE_TIME_S;
}

// Injected Aircraft
struct InjectedAircraft : public Aircraft
{
	// current position
	Position pos;

	// x-plane references
    XPLMDataRef dr_beacon, dr_landing, dr_nav, dr_strobe, dr_taxi;

	// callsign
	char callsign[Link::MAX_CALLSIGN_LENGTH];
	// nickname
	char nickname[Link::MAX_NICKNAME_LENGTH];

	// state
	double distance;
	double speed;

	/// Constructor just passes on all parameters to library
	InjectedAircraft(const std::string& _icaoType,
		const std::string& _icaoAirline,
		const std::string& _livery,
		char* _callsign,
		char* _nickname,
		XPMPPlaneID _modeS_id = 0,
		const std::string& _cslId = "") :
		Aircraft(_icaoType, _icaoAirline, _livery, _modeS_id, _cslId)
	{
		// in our sample implementation, label, radar and info texts
		// are not dynamic. In others, they might be, then update them
		// in UpdatePosition()

		// set callsign and nickname
		strcpy_s(callsign, Link::MAX_CALLSIGN_LENGTH, _callsign);
		strcpy_s(nickname, Link::MAX_NICKNAME_LENGTH, _nickname);

		// initialize
		distance = 0.0;
		speed = 0.0;

		// Label
		UpdateLabel();

		// Radar
		acRadar.code = 7654;
		acRadar.mode = xpmpTransponderMode_ModeC;

		// informational texts
		strScpy(acInfoTexts.icaoAcType, _icaoType.c_str(), sizeof(acInfoTexts.icaoAcType));
		strScpy(acInfoTexts.icaoAirline, _icaoAirline.c_str(), sizeof(acInfoTexts.icaoAirline));
		strScpy(acInfoTexts.tailNum, "D-EVEL", sizeof(acInfoTexts.tailNum));

		// initialize position
		pos.Init();
	}


	/// Custom implementation for the virtual function providing updates values
	virtual void UpdatePosition(float, int)
	{
		SetLocalLoc((float)pos.x, (float)pos.y, (float)pos.z);
		SetPitch((float)pos.p);
		SetHeading((float)pos.h);
		SetRoll((float)pos.b);

		// Plane configuration info
		// This fills a large array of float values:
		const double r = 0;        // a value between 0 and 1
		SetNoseWheelAngle(r * 90.0f - 45.0f);  // turn nose wheel -45°..+45°
		SetSpoilerRatio(r);
		SetSpeedbrakeRatio(r);
		SetSlatRatio(r);
		SetWingSweepRatio(0.0f);
		SetThrustRatio(0.5f);

		// tires don't roll in the air
		SetTireDeflection(0.0f);
		SetTireRotAngle(0.0f);
		SetTireRotRpm(0.0f);

		// For simplicity, we keep engine and prop rotation identical...probably unrealistic
		SetEngineRotRpm(1, 0);
		// 2nd engine shall turn 4 times slower...
		SetEngineRotRpm(2, 0);
		SetPropRotRpm(0);

		// Current position of engine / prop: keeps turning as per engine/prop speed:
		float deg = std::fmod(PLANE_PROP_RPM * PLANE_CIRCLE_TIME_MIN * GetTimeFragment() * 360.0f,
			360.0f);
		SetEngineRotAngle(1, deg);
		// 2nd engine shall turn 4 times slower...
		deg = std::fmod(PLANE_PROP_RPM / 4 * PLANE_CIRCLE_TIME_MIN * GetTimeFragment() * 360.0f,
			360.0f);
		SetEngineRotAngle(2, deg);

		SetPropRotAngle(deg);

		// no reversers and no moment of touch-down in flight
		SetThrustReversRatio(0.0f);
		SetReversDeployRatio(0.0f);
		SetTouchDown(false);
	}

	void UpdateLabel()
	{
		static char part[128];

		label.clear();
		// nickname
		if (labelFlags & 0x01)
		{
			label += nickname;
			label += " ";
		}
		// callsign
		if (labelFlags & 0x02)
		{
			label += callsign;
			label += " ";
		}
		// distance
		if (labelFlags & 0x04)
		{
			if (distance < 1.0)
				sprintf_s(part, 128, "%.2fnm ", distance);
			else
				sprintf_s(part, 128, "%.3gnm ", distance);
			label += part;
		}
		// altitude
		if (labelFlags & 0x08)
		{
			sprintf_s(part, 128, "%.0fft ", pos.altitude * FEET_PER_METRE);
			label += part;
		}
		// speed
		if (labelFlags & 0x10)
		{
			sprintf_s(part, 128, "%.0fkt ", speed);
			label += part;
		}

		// set label colour
		colLabel[0] = labelColour[0];
		colLabel[1] = labelColour[1];
		colLabel[2] = labelColour[2];
	}

	void Close()
	{
		pos.Close();
		callsign[0] = '\0';
		nickname[0] = '\0';
	}
};


// maximum network aircraft
static const int MAX_AIRCRAFT = 19;
// blend rate
static const double TIME_ERROR_RATE = 0.02;

// current time
static double now = 0.0;


// user aircraft
static UserAircraft userAircraft;
// injected aircraft
static InjectedAircraft* injectedAircraft[MAX_AIRCRAFT];

static XPLMDataRef dr_elevator;
static XPLMDataRef dr_aileron;
static XPLMDataRef dr_rudder;
static XPLMDataRef dr_gear;
static XPLMDataRef dr_flap_ratio;
static XPLMDataRef dr_speedBrake;
static XPLMDataRef dr_wing;
static XPLMDataRef dr_brakeLeft;
static XPLMDataRef dr_brakeRight;
static XPLMDataRef dr_brakeParking;
static XPLMDataRef dr_elevatorTrim;
static XPLMDataRef dr_aileronTrim;
static XPLMDataRef dr_rudderTrim;
static XPLMDataRef dr_throttle;
static XPLMDataRef dr_prop;
static XPLMDataRef dr_mixture;


static XPLMDataRef dr_livery = NULL;

// matrix and screen
static XPLMDataRef	s_matrix_wrl = NULL;
static XPLMDataRef	s_matrix_proj = NULL;
static XPLMDataRef	s_screen_width = NULL;
static XPLMDataRef	s_screen_height = NULL;


// variable dataref
struct VariableDataRef
{
	XPLMDataRef dr;
	float scalar;
	float invScalar;
	int index;
	char type;
	char name[Link::MAX_DATAREF_LENGTH];
};

// list of variable datarefs
static map<unsigned int, VariableDataRef> updatedVariables;

// list of requested datarefs
static map<unsigned int, VariableDataRef> requestedVariables;


// create vuid from a string
static unsigned int CreateVuid(const char* name)
{
	unsigned int hash1 = (5381 << 16) + 5381;
	unsigned int hash2 = hash1;
	int length = (int)strlen(name);

	for (int i = 0; i < length; i += 2)
	{
		hash1 = ((hash1 << 5) + hash1) ^ name[i];
		if (i == length - 1)
			break;
		hash2 = ((hash2 << 5) + hash2) ^ name[i + 1];
	}

	unsigned int vuid = hash1 + (hash2 * 1566083941);
	return vuid == 0 ? 1 : vuid;
}


// vuids
static unsigned int navVuid = CreateVuid("light states1");
static unsigned int beaconVuid = CreateVuid("light states2");
static unsigned int landingVuid = CreateVuid("light states3");
static unsigned int taxiVuid = CreateVuid("light states4");
static unsigned int strobeVuid = CreateVuid("light states5");
static unsigned int flapsVuid = CreateVuid("flaps handle percent");
static unsigned int gearVuid = CreateVuid("gear handle position");
static unsigned int propVuid = CreateVuid("prop rpm:1");


static XPLMDataRef GetDataRef(const char* name)
{
    // get data ref
    XPLMDataRef ref = XPLMFindDataRef(name);
    // check result
    if (ref == NULL)
    {
        // log
        DebugMsg("Failed to find data reference '%s'.\n", name);
    }
    return ref;
}


// intialize user aircraft data
static void InitUserAircraft(UserAircraft& aircraft)
{
    aircraft.Init();

	aircraft.dr_callsign = GetDataRef("sim/aircraft/view/acf_tailnum");
	aircraft.dr_icaoType = GetDataRef("sim/aircraft/view/acf_ICAO");
	aircraft.dr_override_position = GetDataRef("sim/operation/override/override_planepath");
    aircraft.dr_override_control = GetDataRef("sim/operation/override/override_flightcontrol");
    aircraft.dr_longitude = GetDataRef("sim/flightmodel/position/longitude");
    aircraft.dr_latitude = GetDataRef("sim/flightmodel/position/latitude");
    aircraft.dr_altitude = GetDataRef("sim/flightmodel/position/elevation");
    aircraft.dr_p = GetDataRef("sim/flightmodel/position/theta");
    aircraft.dr_b = GetDataRef("sim/flightmodel/position/phi");
    aircraft.dr_h = GetDataRef("sim/flightmodel/position/psi");
    aircraft.dr_x = GetDataRef("sim/flightmodel/position/local_x");
    aircraft.dr_y = GetDataRef("sim/flightmodel/position/local_y");
    aircraft.dr_z = GetDataRef("sim/flightmodel/position/local_z");
    aircraft.dr_vx = GetDataRef("sim/flightmodel/position/local_vx");
    aircraft.dr_vy = GetDataRef("sim/flightmodel/position/local_vy");
    aircraft.dr_vz = GetDataRef("sim/flightmodel/position/local_vz");
    aircraft.dr_avx = GetDataRef("sim/flightmodel/position/Q");
    aircraft.dr_avy = GetDataRef("sim/flightmodel/position/R");
    aircraft.dr_avz = GetDataRef("sim/flightmodel/position/P");
    aircraft.dr_ax = GetDataRef("sim/flightmodel/position/local_ax");
    aircraft.dr_ay = GetDataRef("sim/flightmodel/position/local_ay");
    aircraft.dr_az = GetDataRef("sim/flightmodel/position/local_az");
    aircraft.dr_rudder = GetDataRef("sim/joystick/yoke_heading_ratio");
    aircraft.dr_elevator = GetDataRef("sim/joystick/yoke_pitch_ratio");
    aircraft.dr_aileron = GetDataRef("sim/joystick/yoke_roll_ratio");
    aircraft.dr_brakeLeft = GetDataRef("sim/flightmodel/controls/l_brake_add");
    aircraft.dr_brakeRight = GetDataRef("sim/flightmodel/controls/r_brake_add");
    aircraft.dr_elevatorTrim = GetDataRef("sim/cockpit2/controls/elevator_trim");
    aircraft.dr_aileronTrim = GetDataRef("sim/cockpit2/controls/aileron_trim");
    aircraft.dr_rudderTrim = GetDataRef("sim/cockpit2/controls/rudder_trim");
    aircraft.dr_spoilers = GetDataRef("sim/flightmodel/controls/lsplrdef");
    aircraft.dr_hook = GetDataRef("sim/flightmodel/controls/tailhook_ratio");
    aircraft.dr_engineCount = GetDataRef("sim/aircraft/engine/acf_num_engines");
    aircraft.dr_engineType = GetDataRef("sim/aircraft/prop/acf_en_type");
    aircraft.dr_minPitch = GetDataRef("sim/aircraft/prop/acf_min_pitch");
    aircraft.dr_maxPitch = GetDataRef("sim/aircraft/prop/acf_max_pitch");
    aircraft.dr_generator = GetDataRef("sim/cockpit/electrical/generator_on");
    aircraft.dr_primer = GetDataRef("sim/cockpit2/engine/actuators/primer_on");
	aircraft.dr_fuelSelect = GetDataRef("sim/cockpit2/fuel/fuel_tank_selector");
    aircraft.dr_fuelPump = GetDataRef("sim/cockpit/engine/fuel_pump_on");
    aircraft.dr_ignition = GetDataRef("sim/cockpit2/engine/actuators/ignition_on");
    aircraft.dr_igniters = GetDataRef("sim/cockpit/engine/igniters_on");
    aircraft.dr_throttle = GetDataRef("sim/cockpit2/engine/actuators/throttle_ratio");
    aircraft.dr_mixture = GetDataRef("sim/cockpit2/engine/actuators/mixture_ratio");
    aircraft.dr_prop = GetDataRef("sim/cockpit2/engine/actuators/prop_ratio");
    aircraft.dr_cowl = GetDataRef("sim/cockpit2/engine/actuators/cowl_flap_ratio");
    aircraft.dr_combustion = GetDataRef("sim/flightmodel2/engines/has_fuel_flow_after_mixture");
    aircraft.dr_rpm = GetDataRef("sim/cockpit2/engine/indicators/engine_speed_rpm");
    aircraft.dr_antiIce = GetDataRef("sim/cockpit/switches/anti_ice_inlet_heat_per_engine");
    aircraft.dr_fuelAvailable = GetDataRef("sim/flightmodel2/engines/has_fuel_flow_before_mixture");
    aircraft.dr_propFeather = GetDataRef("sim/cockpit2/engine/actuators/prop_mode");
    aircraft.dr_egt = GetDataRef("sim/flightmodel/engine/ENGN_EGT_c");
    aircraft.dr_oilPressure = GetDataRef("sim/flightmodel/engine/ENGN_oil_press_psi");
    aircraft.dr_oilTemperature = GetDataRef("sim/flightmodel/engine/ENGN_oil_temp_c");
    aircraft.dr_fuelPressure = GetDataRef("sim/cockpit2/engine/indicators/fuel_pressure_psi");
    aircraft.dr_manifold = GetDataRef("sim/cockpit2/engine/indicators/MPR_in_hg");
    aircraft.dr_alternateAir = GetDataRef("sim/cockpit2/switches/alternate_static_air_ratio");
    aircraft.dr_cht = GetDataRef("sim/flightmodel/engine/ENGN_CHT_c");
    aircraft.dr_fuelFlow = GetDataRef("sim/flightmodel/engine/ENGN_FF_");
    aircraft.dr_propRpm = GetDataRef("sim/flightmodel/engine/POINT_tacrad");
    aircraft.dr_afterburner = GetDataRef("sim/cockpit2/engine/actuators/afterburner_enabled");
    aircraft.dr_n1 = GetDataRef("sim/flightmodel/engine/ENGN_N1_");
    aircraft.dr_n2 = GetDataRef("sim/flightmodel/engine/ENGN_N2_");
    aircraft.dr_pressureRatio = GetDataRef("sim/flightmodel/engine/ENGN_EPR");
    aircraft.dr_itt = GetDataRef("sim/flightmodel/engine/ENGN_ITT_c");
	aircraft.dr_squawk = GetDataRef("sim/cockpit/radios/transponder_code");
    aircraft.dr_navLight = GetDataRef("sim/cockpit/electrical/nav_lights_on");
    aircraft.dr_beaconLight = GetDataRef("sim/cockpit/electrical/beacon_lights_on");
    aircraft.dr_landingLight = GetDataRef("sim/cockpit/electrical/landing_lights_on");
    aircraft.dr_taxiLight = GetDataRef("sim/cockpit/electrical/taxi_light_on");
    aircraft.dr_strobeLight = GetDataRef("sim/cockpit/electrical/strobe_lights_on");
    aircraft.dr_panelLight = GetDataRef("sim/cockpit/electrical/cockpit_lights");
	aircraft.dr_flap_ratio_actual = GetDataRef("sim/flightmodel/controls/flaprat");
	aircraft.dr_flap_ratio_handle = GetDataRef("sim/cockpit2/controls/flap_ratio");
	aircraft.dr_flap_ratio_indicator = GetDataRef("sim/flightmodel2/controls/flap_handle_deploy_ratio");
	aircraft.dr_flap_detents = GetDataRef("sim/aircraft/controls/acf_flap_detents");
    aircraft.dr_battery = GetDataRef("sim/cockpit/electrical/battery_on");
    aircraft.dr_brakeParking = GetDataRef("sim/cockpit2/controls/parking_brake_ratio");
    aircraft.dr_pitot = GetDataRef("sim/cockpit/switches/pitot_heat_on");
    aircraft.dr_gear = GetDataRef("sim/cockpit/switches/gear_handle_status");
    aircraft.dr_canopy = GetDataRef("sim/cockpit/switches/canopy_req");
    aircraft.dr_avionics = GetDataRef("sim/cockpit2/switches/avionics_power_on");
	aircraft.dr_obs1 = GetDataRef("sim/cockpit/radios/nav1_obs_degm");
	aircraft.dr_obs2 = GetDataRef("sim/cockpit/radios/nav2_obs_degm");
	aircraft.dr_com1Active = GetDataRef("sim/cockpit/radios/com1_freq_hz");
	aircraft.dr_com2Active = GetDataRef("sim/cockpit/radios/com2_freq_hz");
    aircraft.dr_nav1Active = GetDataRef("sim/cockpit/radios/nav1_freq_hz");
    aircraft.dr_nav2Active = GetDataRef("sim/cockpit/radios/nav2_freq_hz");
    aircraft.dr_adf1Active = GetDataRef("sim/cockpit/radios/adf1_freq_hz");
    aircraft.dr_adf2Active = GetDataRef("sim/cockpit/radios/adf2_freq_hz");
    aircraft.dr_com1Standby = GetDataRef("sim/cockpit/radios/com1_stdby_freq_hz");
    aircraft.dr_com2Standby = GetDataRef("sim/cockpit/radios/com2_stdby_freq_hz");
    aircraft.dr_nav1Standby = GetDataRef("sim/cockpit/radios/nav1_stdby_freq_hz");
    aircraft.dr_nav2Standby = GetDataRef("sim/cockpit/radios/nav2_stdby_freq_hz");
    aircraft.dr_autopilot = GetDataRef("sim/cockpit/autopilot/autopilot_mode");
    aircraft.dr_autopilotState = GetDataRef("sim/cockpit/autopilot/autopilot_state");
    aircraft.dr_apHeading = GetDataRef("sim/cockpit/autopilot/heading_mag");
    aircraft.dr_apAltitude = GetDataRef("sim/cockpit/autopilot/altitude");
    aircraft.dr_apVertical = GetDataRef("sim/cockpit/autopilot/vertical_velocity");
    aircraft.dr_apAirspeed = GetDataRef("sim/cockpit/autopilot/airspeed");
    aircraft.dr_gyro = GetDataRef("sim/cockpit/gyros/psi_vac_ind_degm");
    aircraft.dr_altimeter = GetDataRef("sim/cockpit/misc/barometer_setting");
    aircraft.dr_dmeSelect = GetDataRef("sim/cockpit/switches/DME_radio_selector");
	aircraft.dr_fuel = GetDataRef("sim/flightmodel/weight/m_fuel");
	aircraft.dr_indicatedAirSpeed = GetDataRef("sim/flightmodel/position/indicated_airspeed");
	aircraft.dr_indicatedAltimeter = GetDataRef("sim/flightmodel/misc/h_ind");
	aircraft.dr_indicatedVerticalSpeed = GetDataRef("sim/flightmodel/position/vh_ind_fpm");
	aircraft.dr_indicatedTurnRate = GetDataRef("sim/flightmodel/misc/turnrate_roll");
	aircraft.dr_indicatedTurnSlip = GetDataRef("sim/flightmodel/misc/slip");
}


// update the ground elevation beneath the aircraft
void DoElevationCorrection(Position& pos)
{
	// check for probe
	if (pos.probe == NULL)
	{
		// create probe
		pos.probe = XPLMCreateProbe(xplm_ProbeY);
	}

	// check for probe
	if (pos.probe != NULL)
	{
		XPLMProbeInfo_t info;
		info.structSize = sizeof(info);
		XPLMProbeResult result = XPLMProbeTerrainXYZ(pos.probe, (float)pos.nx, (float)pos.ny, (float)pos.nz, &info);

		// get ground elevation
		if (result == xplm_ProbeHitTerrain)
		{
			// calculate height 
			double height = pos.altitude - pos.elevation;
			// check if close to the ground
			if (height < 50.0)
			{
				// calculate proportion to adjust by
				double proportion = 1.0 - height * 0.02;
				// calculate local elevation from sea level
				double localElevation = info.locationY - pos.ny + pos.altitude;
				// blend altitude
				pos.ny += (localElevation - pos.elevation) * proportion;
			}
		}
	}
}


void Available(void*)
{
    DebugMsg("Now available.\n");
}


void OverrideFlightControl()
{
	// check if not remote control
	if (userAircraft.overrideFlightControl == false)
	{
		// under remote flight control
		userAircraft.overrideFlightControl = true;
		int enable = 1;
		XPLMSetDatavi(userAircraft.dr_override_position, &enable, 0, 1);
		// enable override
//		XPLMSetDatai(userAircraft.dr_override_control, 1);

		// get state
		userAircraft.GetState();
	}

	// reset timer
	userAircraft.endOverrideTime = now + 1.0;
}


// update aircraft position
void SetUserPosition(UserAircraft& aircraft)
{
	// update position
	XPLMSetDatad(aircraft.dr_x, aircraft.pos.x);
	XPLMSetDatad(aircraft.dr_y, aircraft.pos.y);
	XPLMSetDatad(aircraft.dr_z, aircraft.pos.z);
	XPLMSetDataf(aircraft.dr_p, (float)aircraft.pos.p);
	XPLMSetDataf(aircraft.dr_b, (float)aircraft.pos.b);
	XPLMSetDataf(aircraft.dr_h, (float)aircraft.pos.h);
}

// update aircraft velocity
void SetUserVelocity(UserAircraft& aircraft)
{
	XPLMSetDataf(aircraft.dr_vx, (float)aircraft.pos.vx);
	XPLMSetDataf(aircraft.dr_vy, (float)aircraft.pos.vy);
	XPLMSetDataf(aircraft.dr_vz, (float)aircraft.pos.vz);
}


// connect callback
void OnConnect(Link::ConnectMsg& msg)
{
}

// disconnect callback
void OnDisconnect()
{
    // reset user aircraft
    userAircraft.Close();
    // for each network aircraft
    for (int i = 0; i < MAX_AIRCRAFT; i++)
    {
		// check for valid aircraft
		if (injectedAircraft[i] != NULL)
		{
			// reset aircraft
			injectedAircraft[i]->Close();
			delete injectedAircraft[i];
			injectedAircraft[i] = NULL;
		}
    }
}


/// <summary>
/// TCAS enabled
/// </summary>
static char tcas = 0;


/// Callback function for the case that we might get AI access later
void CPRequestAIAgain(void*)
{
	// Well...we just try again ;-)
	XPMPMultiplayerEnable(CPRequestAIAgain);
}


// heartbeat callback
void OnHeartbeat(Link::HeartbeatMsg& msg)
{
	// get flags
	float r = (float)((msg.labelColour >> 16) & 0xff) * (1.0f / 255.0f);
	float g = (float)((msg.labelColour >> 8) & 0xff) * (1.0f / 255.0f);
	float b = (float)((msg.labelColour) & 0xff) * (1.0f / 255.0f);

	// check if label has changed
	if (msg.labelFlags != labelFlags || r != labelColour[0] || g != labelColour[1] || b != labelColour[2])
	{
		// save flags
		labelFlags = msg.labelFlags;
		// save current colour
		labelColour[0] = r;
		labelColour[1] = g;
		labelColour[2] = b;

		// for each aircraft
		for (int i = 0; i < MAX_AIRCRAFT; i++)
		{
			// update label
			if (injectedAircraft[i] != NULL)
			{
				injectedAircraft[i]->UpdateLabel();
			}
		}
	}

	// check if TCAS has changed
	if (msg.tcas != tcas)
	{
		// save state
		tcas = msg.tcas;
		// check new state
		if (tcas)
		{
			// enable multiplayer
			const char* res = XPMPMultiplayerEnable(CPRequestAIAgain);
			if (res[0])
			{
				DebugMsg("Could not enable AI planes: %s", res);
			}
		}
		else
		{
			// disable multiplayer
			XPMPMultiplayerDisable();
		}
	}
}


void OnModel(Link::ModelMsg& msg)
{
	// check for valid index
    if (msg.index >= 1 && msg.index <= MAX_AIRCRAFT)
    {
		// livery
		char* livery = NULL;
		// find space character
		char* space = strchr(msg.model, ' ');
		// check for livery
		if (space != NULL)
		{
			// terminate airline
			*space = '\0';
			// get livery
			livery = space + 1;
		}
		else
		{
			// set livery to terminator
			livery = msg.model + strlen(msg.model);
		}

		// get aircraft
		InjectedAircraft* aircraft = injectedAircraft[msg.index - 1];

		// check if slot is empty
		if (aircraft == NULL)
		{
			// create aircraft
			aircraft = injectedAircraft[msg.index - 1] = new InjectedAircraft(string(msg.icaoType), string(msg.model), string(livery), msg.callsign, msg.nickname, msg.index);
		}
		else
		{
			// check if model has changed
			if (msg.icaoType != aircraft->acIcaoType || msg.model != aircraft->acIcaoAirline || livery != aircraft->acLivery)
			{
				DebugMsg("Aircraft %d model changed from '%s %s %s' to '%s %s %s'.\n", msg.index, aircraft->acIcaoType, aircraft->acIcaoAirline, aircraft->acLivery, msg.icaoType, msg.model, livery);
				aircraft->ChangeModel(string(msg.icaoType), string(msg.model), livery);
			}
		}

		// update callsign
		strScpy(aircraft->callsign, msg.callsign, Link::MAX_CALLSIGN_LENGTH);
		// update nickname
		strScpy(aircraft->nickname, msg.nickname, Link::MAX_NICKNAME_LENGTH);
	}
}


void OnAircraftPosition(Link::AircraftPositionMsg& msg)
{
	// check for valid index (including user aircraft)
	if (msg.index == 0 || msg.index >= 1 && msg.index <= MAX_AIRCRAFT && injectedAircraft[msg.index - 1] != NULL)
	{
		// override
		if (msg.index == 0) OverrideFlightControl();

		// get position data
		Position& pos = (msg.index == 0) ? userAircraft.pos : injectedAircraft[msg.index - 1]->pos;

		// reject old updates
		if (msg.netTime > pos.netStateTime)
		{
			// convert to sim position
			double nx, ny, nz;
			XPLMWorldToLocal(msg.latitude, msg.longitude, msg.altitude, &nx, &ny, &nz);
			// save old orientation
			pos.nop = pos.np;
			pos.nob = pos.nb;
			pos.noh = pos.nh;
			// update state
			pos.nx = nx;
			pos.ny = ny;
			pos.nz = nz;
			pos.nvy = msg.vy;
			pos.nvz = msg.vz;
			pos.nvx = msg.vx;
			pos.nvy = msg.vy;
			pos.nvz = msg.vz;
			pos.nax = msg.ax;
			pos.nay = msg.ay;
			pos.naz = msg.az;
			pos.np = msg.pitch;
			pos.nb = msg.bank;
			pos.nh = msg.heading;
			pos.navx = msg.avx;
			pos.navy = msg.avy;
			pos.navz = msg.avz;
			pos.elevation = msg.elevation;
			pos.altitude = msg.altitude;
			pos.ground = (msg.ground & 0x01) != 0;

			// update network time
			pos.netStateTime = msg.netTime;
			// check for first update
			if (pos.netRealTime == 0.0)
			{
				// set time
				pos.netRealTime = pos.netStateTime;
			}
			else
			{
				// update real time
				pos.netRealTime += now - pos.netSimTime;
				double error = pos.netStateTime - pos.netRealTime;
				pos.netRealTime += error * TIME_ERROR_RATE;
			}
			pos.netSimTime = now;

			// check if correction specified
			if ((msg.ground & 0x02) != 0)
			{
				// update ground elevation
				DoElevationCorrection(pos);
			}
		}

		// check for initial update
		if (pos.simTime == 0.0)
		{
			// initialize position
			pos.x = pos.nx;
			pos.y = pos.ny;
			pos.z = pos.nz;
			pos.vx = pos.nvx;
			pos.vy = pos.nvy;
			pos.vz = pos.nvz;
			pos.p = pos.np;
			pos.b = pos.nb;
			pos.h = pos.nh;
			// check for user aircraft
			if (msg.index == 0)
			{
				// set position
				SetUserPosition(userAircraft);
				SetUserVelocity(userAircraft);
			}
			// intialize sim time
			pos.simTime = now;
		}
	}
}


void OnObjectPosition(Link::ObjectPositionMsg& msg)
{
	// check for valid index (including user aircraft)
	if (msg.index == 0 || msg.index >= 1 && msg.index <= MAX_AIRCRAFT && injectedAircraft[msg.index - 1] != NULL)
	{
		// override
		if (msg.index == 0)
		{
			OverrideFlightControl();
			userAircraft.pos.Reset();
		}

		// get position data
		Position& pos = (msg.index == 0) ? userAircraft.pos : injectedAircraft[msg.index - 1]->pos;

		// convert to sim position
		double nx, ny, nz;
		XPLMWorldToLocal(msg.latitude, msg.longitude, msg.altitude, &nx, &ny, &nz);
		pos.nx = nx;
		pos.ny = ny;
		pos.nz = nz;
		pos.np = msg.pitch;
		pos.nb = msg.bank;
		pos.nh = msg.heading;

		// update state
		pos.x = nx;
		pos.y = ny;
		pos.z = nz;
		pos.p = msg.pitch;
		pos.b = msg.bank;
		pos.h = msg.heading;
		pos.elevation = msg.elevation;
		pos.ground = (msg.ground & 0x01) != 0;

		// check if correction specified
		if ((msg.ground & 0x02) != 0)
		{
			// update ground elevation
			DoElevationCorrection(pos);
		}

		// check for user aircraft
		if (msg.index == 0)
		{
			// set position
			SetUserPosition(userAircraft);
		}
	}
}


void OnObjectVelocity(Link::ObjectVelocityMsg& msg)
{
	// check for valid index (including user aircraft)
	if (msg.index == 0 || msg.index >= 1 && msg.index <= MAX_AIRCRAFT && injectedAircraft[msg.index - 1] != NULL)
	{
		// override
		if (msg.index == 0)
		{
			OverrideFlightControl();
			userAircraft.pos.Reset();
		}

		// get position data
		Position& pos = (msg.index == 0) ? userAircraft.pos : injectedAircraft[msg.index - 1]->pos;

		// update state
		pos.nvx = msg.vx;
		pos.nvy = msg.vy;
		pos.nvz = msg.vz;
		pos.navx = msg.avx;
		pos.navy = msg.avy;
		pos.navz = msg.avz;
		pos.nax = msg.ax;
		pos.nay = msg.ay;
		pos.naz = msg.az;

		pos.vx = msg.vx;
		pos.vy = msg.vy;
		pos.vz = msg.vz;

		// check for user aircraft
		if (msg.index == 0)
		{
			// set velocity
			SetUserVelocity(userAircraft);
		}
	}
}


/// <summary>
/// SimConnect events
/// </summary>
enum Event
{
	OBJECT_ADDED,
	OBJECT_REMOVED,
	FRAME,
	PAUSE,
	RUDDER_SET,
	ELEVATOR_SET,
	AILERON_SET,
	RUDDER_TRIM_SET,
	ELEVATOR_TRIM_SET,
	AILERON_TRIM_SET,
	BRAKE_LEFT_SET,
	BRAKE_RIGHT_SET,
	TOGGLE_BATTERY,
	TOGGLE_ALTERNATOR1,
	TOGGLE_ALTERNATOR2,
	TOGGLE_ALTERNATOR3,
	TOGGLE_ALTERNATOR4,
	SELECT_ENGINE,
	SELECT_1,
	SELECT_2,
	SELECT_3,
	SELECT_4,
	TOGGLE_PARKING,
	PITOT_SET,
	GEAR_SET,
	XPNDR_SET,
	TOGGLE_NAV_LIGHTS,
	TOGGLE_BEACON_LIGHTS,
	TOGGLE_LANDING_LIGHTS,
	TOGGLE_TAXI_LIGHTS,
	TOGGLE_STROBES,
	TOGGLE_PANEL_LIGHTS,
	TOGGLE_RECOGNITION_LIGHTS,
	TOGGLE_WING_LIGHTS,
	TOGGLE_LOGO_LIGHTS,
	TOGGLE_CABIN_LIGHTS,
	FLAPS_INCR,
	FLAPS_DECR,
	TOGGLE_AIRCRAFT_EXIT,
	FUEL_SELECTOR1,
	FUEL_SELECTOR2,
	FUEL_SELECTOR3,
	FUEL_SELECTOR4,
	TOGGLE_STARTER1,
	TOGGLE_STARTER2,
	TOGGLE_STARTER3,
	TOGGLE_STARTER4,
	TOGGLE_FUEL1,
	TOGGLE_FUEL2,
	TOGGLE_FUEL3,
	TOGGLE_FUEL4,
	THROTTLE1_SET,
	THROTTLE2_SET,
	THROTTLE3_SET,
	THROTTLE4_SET,
	MIXTURE1_SET,
	MIXTURE2_SET,
	MIXTURE3_SET,
	MIXTURE4_SET,
	PROPELLOR1_SET,
	PROPELLOR2_SET,
	PROPELLOR3_SET,
	PROPELLOR4_SET,
	COWL1_SET,
	COWL2_SET,
	COWL3_SET,
	COWL4_SET,
	AVIONICS_SET,
	TX1,
	TX2,
	OBS1_SET,
	OBS2_SET,
	COM1_ACTIVE,
	COM2_ACTIVE,
	NAV1_ACTIVE,
	NAV2_ACTIVE,
	ADF1_ACTIVE,
	ADF2_ACTIVE,
	COM1_STANDBY,
	COM2_STANDBY,
	NAV1_STANDBY,
	NAV2_STANDBY,
	SMOKE_ON,
	SMOKE_OFF,
	SPOILERS_SET,
	HOOK_SET,
	WING_SET,
	TOGGLE_AUTOPILOT,
	TOGGLE_AP_HEADING,
	AP_HEADING_VAR,
	TOGGLE_AP_ALTITUDE,
	AP_ALTITUDE_VAR,
	AP_VERTICAL_VAR,
	TOGGLE_AP_AIRSPEED,
	AP_AIRSPEED_VAR,
	TOGGLE_AP_ATTITUDE,
	TOGGLE_AP_APPROACH,
	TOGGLE_AP_BACKCOURSE,
	TOGGLE_AP_NAV,
	TOGGLE_AP_THROTTLE,
	TOGGLE_DME,
	KOHLSMAN_SET,
	GYRO,
	EVENT_00011000,
	EVENT_00011001,
	EVENT_00011002,
	EVENT_00011003,
	EVENT_00011004,
	EVENT_00011005,
	EVENT_00011006,
	EVENT_00011007,
	EVENT_00011008,
	EVENT_00011009,
	EVENT_0001100A,
};


static void ToggleFlag(XPLMDataRef dr, int index, unsigned int mask)
{
    // get current flags
    int flags;
    if (XPLMGetDatavi(dr, &flags, index, 1) == 1)
    {
        // change flags
        flags ^= mask;
        XPLMSetDatavi(dr, &flags, index, 1);
    }
}


static void ToggleFlag(XPLMDataRef dr, unsigned int mask)
{
    // get current flags
    int flags = XPLMGetDatai(dr);
    {
        // change flags
        flags ^= mask;
        XPLMSetDatai(dr, flags);
    }
}


// convert fuel tank selection to X-Plane
static int ToFuelTank(int data)
{
	switch (data)
	{
	case 0: return 0;
	case 1: return 4;
	case 2: return 1;
	case 3: return 3;
	}
	return 0;
}


// convert fuel tank selection from X-Plane
static int FromFuelTank(int data)
{
	switch (data)
	{
	case 0: return 0;
	case 1: return 2;
	case 3: return 3;
	case 4: return 1;
	}
	return 0;
}


void OnEvent(Link::EventMsg& msg)
{
    // convert data to float
    float fdata = (float)msg.data * (1.0f / 16384.0f);
    int iData = msg.data;

	// check for injected aircraft
	if (msg.index >= 1 && msg.index <= MAX_AIRCRAFT && injectedAircraft[msg.index - 1] != NULL)
	{
		// check event
		switch (msg.simEvent)
		{
		case RUDDER_SET:            injectedAircraft[msg.index - 1]->SetYokeHeadingRatio(-fdata);     break;
		case ELEVATOR_SET:          injectedAircraft[msg.index - 1]->SetYokePitchRatio(-fdata);       break;
		case AILERON_SET:           injectedAircraft[msg.index - 1]->SetYokeRollRatio(-fdata);        break;
		}
	}

    // check for user aircraft
    if (msg.index == 0)
    {
		// check event
        switch (msg.simEvent)
        {
        case RUDDER_SET:            XPLMSetDataf(userAircraft.dr_rudder, -fdata);         break;
        case ELEVATOR_SET:          XPLMSetDataf(userAircraft.dr_elevator, -fdata);       break;
        case AILERON_SET:           XPLMSetDataf(userAircraft.dr_aileron, -fdata);        break;
//        case RUDDER_TRIM_SET:       XPLMSetDataf(userAircraft.dr_rudderTrim, fdata);     break;
//        case ELEVATOR_TRIM_SET:     XPLMSetDataf(userAircraft.dr_elevatorTrim, fdata);   break;
//        case AILERON_TRIM_SET:      XPLMSetDataf(userAircraft.dr_aileronTrim, fdata);    break;
//        case BRAKE_LEFT_SET:        XPLMSetDataf(userAircraft.dr_brakeLeft, fdata);      break;
//        case BRAKE_RIGHT_SET:       XPLMSetDataf(userAircraft.dr_brakeRight, fdata);     break;
        case TOGGLE_ALTERNATOR1:    XPLMSetDatavi(userAircraft.dr_generator, &iData, 0, 1);      break;
        case TOGGLE_ALTERNATOR2:    XPLMSetDatavi(userAircraft.dr_generator, &iData, 1, 1);      break;
        case TOGGLE_ALTERNATOR3:    XPLMSetDatavi(userAircraft.dr_generator, &iData, 2, 1);      break;
        case TOGGLE_ALTERNATOR4:    XPLMSetDatavi(userAircraft.dr_generator, &iData, 3, 1);      break;
        case TOGGLE_PARKING:
//            fdata = (msg.data != 0) ? 1.0f : 0.0f; 
//            XPLMSetDataf(userAircraft.dr_brakeParking, fdata);    
            break;
//		case PITOT_SET:				XPLMSetDatai(userAircraft.dr_pitot, msg.data);                   break;
		case TOGGLE_FUEL1:          XPLMSetDatavi(userAircraft.dr_fuelPump, &iData, 0, 1);           break;
//		case GEAR_SET:              XPLMSetDatai(userAircraft.dr_gear, msg.data);                    break;
//        case XPNDR_SET:             XPLMSetDatai(userAircraft.dr_squawk, msg.data);                  break;
//        case TOGGLE_NAV_LIGHTS:     XPLMSetDatai(userAircraft.dr_navLight, msg.data);                break;
//        case TOGGLE_BEACON_LIGHTS:  XPLMSetDatai(userAircraft.dr_beaconLight, msg.data);             break;
//        case TOGGLE_LANDING_LIGHTS: XPLMSetDatai(userAircraft.dr_landingLight, msg.data);            break;
//        case TOGGLE_TAXI_LIGHTS:    XPLMSetDatai(userAircraft.dr_taxiLight, msg.data);               break;
//        case TOGGLE_STROBES:        XPLMSetDatai(userAircraft.dr_strobeLight, msg.data);             break;
//        case FUEL_SELECTOR1:        XPLMSetDatai(userAircraft.dr_fuelSelect, ToFuelTank(iData));     break;
        //case FUEL_SELECTOR2:        XPLMSetDatai(userAircraft.dr_fuelSelect, &msg.data, 1, 1);      break;
        //case FUEL_SELECTOR3:        XPLMSetDatai(userAircraft.dr_fuelSelect, &msg.data, 2, 1);      break;
        //case FUEL_SELECTOR4:        XPLMSetDatai(userAircraft.dr_fuelSelect, &msg.data, 3, 1);      break;
//		case AVIONICS_SET:			XPLMSetDatai(userAircraft.dr_avionics, iData);					break;
		//case TOGGLE_STARTER1:       XPLMSetDatai(userAircraft.dr_ignition, msg.data ? 3 : 0);        break;
		case FLAPS_DECR:			
		case FLAPS_INCR:
		{
			//int detents = XPLMGetDatai(userAircraft.dr_flap_detents);
			//float ratio = detents > 0 ? (float)msg.data / detents : 0.0f;
			//XPLMSetDataf(userAircraft.dr_flap_ratio_actual, ratio);
			//XPLMSetDataf(userAircraft.dr_flap_ratio_handle, ratio);
			//XPLMSetDataf(userAircraft.dr_flap_ratio_indicator, ratio);
			break;
		}
		case THROTTLE1_SET:         XPLMSetDatavf(userAircraft.dr_throttle, &fdata, 0, 1);           break;
        case THROTTLE2_SET:         XPLMSetDatavf(userAircraft.dr_throttle, &fdata, 1, 1);           break;
        case THROTTLE3_SET:         XPLMSetDatavf(userAircraft.dr_throttle, &fdata, 2, 1);           break;
        case THROTTLE4_SET:         XPLMSetDatavf(userAircraft.dr_throttle, &fdata, 3, 1);           break;
        case MIXTURE1_SET:          XPLMSetDatavf(userAircraft.dr_mixture, &fdata, 0, 1);            break;
        case MIXTURE2_SET:          XPLMSetDatavf(userAircraft.dr_mixture, &fdata, 1, 1);            break;
        case MIXTURE3_SET:          XPLMSetDatavf(userAircraft.dr_mixture, &fdata, 2, 1);            break;
        case MIXTURE4_SET:          XPLMSetDatavf(userAircraft.dr_mixture, &fdata, 3, 1);            break;
        case PROPELLOR1_SET:        XPLMSetDatavf(userAircraft.dr_prop, &fdata, 0, 1);               break;
        case PROPELLOR2_SET:        XPLMSetDatavf(userAircraft.dr_prop, &fdata, 1, 1);               break;
        case PROPELLOR3_SET:        XPLMSetDatavf(userAircraft.dr_prop, &fdata, 2, 1);               break;
        case PROPELLOR4_SET:        XPLMSetDatavf(userAircraft.dr_prop, &fdata, 3, 1);               break;
//		case OBS1_SET:				XPLMSetDataf(userAircraft.dr_obs1, (float)msg.data);			 break;
//		case OBS2_SET:				XPLMSetDataf(userAircraft.dr_obs2, (float)msg.data);			 break;
//		case COM1_ACTIVE:           XPLMSetDatai(userAircraft.dr_com1Active, msg.data);              break;
//		case COM2_ACTIVE:           XPLMSetDatai(userAircraft.dr_com2Active, msg.data);              break;
//        case NAV1_ACTIVE:           XPLMSetDatai(userAircraft.dr_nav1Active, msg.data);              break;
//        case NAV2_ACTIVE:           XPLMSetDatai(userAircraft.dr_nav2Active, msg.data);              break;
//        case ADF1_ACTIVE:           XPLMSetDatai(userAircraft.dr_adf1Active, msg.data);              break;
//        case ADF2_ACTIVE:           XPLMSetDatai(userAircraft.dr_adf2Active, msg.data);              break;
//        case COM1_STANDBY:          XPLMSetDatai(userAircraft.dr_com1Standby, msg.data);             break;
//        case COM2_STANDBY:          XPLMSetDatai(userAircraft.dr_com2Standby, msg.data);             break;
//        case NAV1_STANDBY:          XPLMSetDatai(userAircraft.dr_nav1Standby, msg.data);             break;
//        case NAV2_STANDBY:          XPLMSetDatai(userAircraft.dr_nav2Standby, msg.data);             break;
        case WING_SET:
            fdata = (msg.data != 0) ? 1.0f : 0.0f; 
            XPLMSetDataf(userAircraft.dr_wing, fdata);
            break;
//        case TOGGLE_AUTOPILOT:      XPLMSetDatai(userAircraft.dr_autopilot, msg.data);               break;
//        case TOGGLE_AP_HEADING:     ToggleFlag(userAircraft.dr_autopilotState, 0x002);               break;
//        case TOGGLE_AP_ALTITUDE:    ToggleFlag(userAircraft.dr_autopilotState, 0x020);               break;
//        case TOGGLE_AP_AIRSPEED:    ToggleFlag(userAircraft.dr_autopilotState, 0x008);               break;
//        case TOGGLE_AP_ATTITUDE:    ToggleFlag(userAircraft.dr_autopilotState, 0x080);               break;
//        case TOGGLE_AP_APPROACH:    ToggleFlag(userAircraft.dr_autopilotState, 0x400);               break;
//        case TOGGLE_AP_NAV:         ToggleFlag(userAircraft.dr_autopilotState, 0x100);               break;
//        case TOGGLE_AP_THROTTLE:    ToggleFlag(userAircraft.dr_autopilotState, 0x001);               break;
//        case AP_ALTITUDE_VAR:       XPLMSetDataf(userAircraft.dr_apAltitude, (float)msg.data);                    break;
//        case AP_AIRSPEED_VAR:       XPLMSetDataf(userAircraft.dr_apAirspeed, (float)msg.data);                break;
//		case AP_HEADING_VAR:		XPLMSetDataf(userAircraft.dr_apHeading, (float)msg.data); break;
//		case KOHLSMAN_SET:          XPLMSetDataf(userAircraft.dr_altimeter, (float)msg.data * (1.0f / (33.8638866666667f * 16.0f))); break;
		}
    }
}


void OnFuel(Link::FuelMsg& msg)
{
	// check for user aircraft
	if (msg.index == 0)
	{
		float fuel[9];
		fuel[0] = msg.leftMain;
		fuel[1] = msg.rightMain;
		fuel[2] = msg.centre1;
		fuel[3] = msg.leftTip;
		fuel[4] = msg.rightTip;
		fuel[5] = msg.centre2;
		fuel[6] = msg.leftAux;
		fuel[7] = msg.rightAux;
		fuel[8] = msg.centre3;
		XPLMSetDatavf(userAircraft.dr_fuel, fuel, 0, 9);
	}
}


void OnRemove(Link::RemoveMsg& msg)
{
	// check index
	if (msg.index >= 1 && msg.index <= MAX_AIRCRAFT)
	{
		// get aircraft
		InjectedAircraft* aircraft = injectedAircraft[msg.index - 1];

		// check for valid aircraft
		if (aircraft != NULL)
		{
			// message
			DebugMsg("Removing aircraft %d, model '%s %s %s'\n", msg.index, aircraft->acIcaoType.c_str(), aircraft->acIcaoAirline.c_str(), aircraft->acLivery.c_str());
			// remove aircraft
			aircraft->Close();
			delete aircraft;
			injectedAircraft[msg.index - 1] = NULL;
		}
	}
}


void OnIntegerVariable(Link::IntegerVariableMsg& msg)
{
	// check for user aircraft
	if (msg.index == 0)
	{
		// get dataref
		map<unsigned int, VariableDataRef>::iterator vdr = updatedVariables.find(msg.vuid);
		// check for dataref
		if (vdr != updatedVariables.end())
		{
			// check if dataref is accessible
			if (XPLMCanWriteDataRef(vdr->second.dr))
			{
				// get data type
				XPLMDataTypeID types = XPLMGetDataRefTypes(vdr->second.dr);
				if (types & xplmType_Int)
				{
					// update as integer
					XPLMSetDatai(vdr->second.dr, vdr->second.scalar == 0.0f ? msg.value : (int)(msg.value * vdr->second.scalar));
				}
				else if (types & xplmType_Float)
				{
					// update as float
					XPLMSetDataf(vdr->second.dr, vdr->second.scalar == 0.0f ? (float)msg.value : msg.value * vdr->second.scalar);
				}
				else if (types & xplmType_IntArray)
				{
					// update integer array
					int value = vdr->second.scalar == 0.0 ? msg.value : (int)(msg.value * vdr->second.scalar);
					XPLMSetDatavi(vdr->second.dr, &value, vdr->second.index > 0 ? vdr->second.index - 1 : 0, 1);
				}
				else if (types & xplmType_FloatArray)
				{
					// update integer array
					float value = vdr->second.scalar == 0.0 ? msg.value : msg.value * vdr->second.scalar;
					XPLMSetDatavf(vdr->second.dr, &value, vdr->second.index > 0 ? vdr->second.index - 1 : 0, 1);
				}
				else
				{
					DebugMsg("DataRef is the wrong type for an integer, %s\n", vdr->second.name);
				}
			}
			else
			{
				DebugMsg("DataRef is not writable, %s\n", vdr->second.name);
			}
		}
		else
		{
			// get definition
			static Link::GetDefinitionMsg getMsg;
			getMsg.vuid = msg.vuid;
			Link::Send(getMsg);
		}
	}
	// check for injected aircraft
	else if (msg.index >= 1 && msg.index <= MAX_AIRCRAFT && injectedAircraft[msg.index - 1] != NULL)
	{
		// check for lights
		if (msg.vuid == navVuid)
		{
			injectedAircraft[msg.index - 1]->SetLightsNav(msg.value ? true : false);
		}
		else if (msg.vuid == beaconVuid)
		{
			injectedAircraft[msg.index - 1]->SetLightsBeacon(msg.value ? true : false);
		}
		else if (msg.vuid == landingVuid)
		{
			injectedAircraft[msg.index - 1]->SetLightsLanding(msg.value ? true : false);
		}
		else if (msg.vuid == taxiVuid)
		{
			injectedAircraft[msg.index - 1]->SetLightsTaxi(msg.value ? true : false);
		}
		else if (msg.vuid == strobeVuid)
		{
			injectedAircraft[msg.index - 1]->SetLightsStrobe(msg.value ? true : false);
		}
		else if (msg.vuid == gearVuid)
		{
			injectedAircraft[msg.index - 1]->SetGearRatio(msg.value ? 1.0f : 0.0f);
		}
		else if (msg.vuid == propVuid)
		{
			injectedAircraft[msg.index - 1]->SetPropRotRpm((float)msg.value);
		}
	}
}


void OnFloatVariable(Link::FloatVariableMsg& msg)
{
	// check for user aircraft
	if (msg.index == 0)
	{
		// get dataref
		map<unsigned int, VariableDataRef>::iterator vdr = updatedVariables.find(msg.vuid);
		// check for dataref
		if (vdr != updatedVariables.end())
		{
			// check if dataref is accessible
			if (XPLMCanWriteDataRef(vdr->second.dr))
			{
				// get data type
				XPLMDataTypeID types = XPLMGetDataRefTypes(vdr->second.dr);
				if (types & xplmType_Float)
				{
					// update as float
					XPLMSetDataf(vdr->second.dr, vdr->second.scalar == 0.0f ? msg.value : msg.value * vdr->second.scalar);
				}
				else if (types & xplmType_Int)
				{
					// update as integer
					XPLMSetDatai(vdr->second.dr, vdr->second.scalar == 0.0f ? (int)msg.value : (int)(msg.value * vdr->second.scalar));
				}
				else if (types & xplmType_FloatArray)
				{
					// update float array
					float value = vdr->second.scalar == 0.0 ? msg.value : msg.value * vdr->second.scalar;
					XPLMSetDatavf(vdr->second.dr, &value, vdr->second.index > 0 ? vdr->second.index - 1 : 0, 1);
				}
				else if (types & xplmType_IntArray)
				{
					// update float array
					int value = vdr->second.scalar == 0.0 ? (int)msg.value : (int)(msg.value * vdr->second.scalar);
					XPLMSetDatavi(vdr->second.dr, &value, vdr->second.index > 0 ? vdr->second.index - 1 : 0, 1);
				}
				else
				{
					DebugMsg("DataRef is the wrong type for a float, %s\n", vdr->second.name);
				}
			}
			else
			{
				DebugMsg("DataRef is not writable, %s\n", vdr->second.name);
			}
		}
		else
		{
			// get definition
			static Link::GetDefinitionMsg getMsg;
			getMsg.vuid = msg.vuid;
			Link::Send(getMsg);
		}
	}
	// check for injected aircraft
	else if (msg.index >= 1 && msg.index <= MAX_AIRCRAFT && injectedAircraft[msg.index - 1] != NULL)
	{
		// check for flaps
		if (msg.vuid == flapsVuid)
		{
			injectedAircraft[msg.index - 1]->SetFlapRatio(msg.value);
		}
	}
}


void OnString8Variable(Link::String8VariableMsg& msg)
{
	// get dataref
	map<unsigned int, VariableDataRef>::iterator vdr = updatedVariables.find(msg.vuid);
	// check for dataref
	if (vdr != updatedVariables.end())
	{
		// check for user aircraft
		if (msg.index == 0)
		{
			// check if dataref is accessible
			if (XPLMCanWriteDataRef(vdr->second.dr))
			{
				// update data
				XPLMSetDatab(vdr->second.dr, msg.value, 0, (int)strlen(msg.value));
			}
			else
			{
				DebugMsg("DataRef is not writable, %s\n", vdr->second.name);
			}
		}
		// check for injected aircraft
		else if (msg.index >= 1 && msg.index <= MAX_AIRCRAFT)
		{
		}
	}
	else
	{
		// get definition
		static Link::GetDefinitionMsg getMsg;
		getMsg.vuid = msg.vuid;
		Link::Send(getMsg);
	}
}


void OnDefinition(Link::DefinitionMsg& msg)
{
	// check that definition is not already known
	if (updatedVariables.find(msg.vuid) == updatedVariables.end())
	{
		// get dataref
		XPLMDataRef dataRef = GetDataRef(msg.dataRefName);
		// check for valid dataref
		if (dataRef != NULL)
		{
			// new variable dataref
			VariableDataRef vdr;
			// get scalar
			vdr.scalar = msg.scalar;
			vdr.invScalar = vdr.scalar == 0.0f ? 1.0f : 1.0f / vdr.scalar;
			// get index
			vdr.index = msg.drIndex;
			// get type
			vdr.type = msg.type;
			// get name
			strcpy_s(vdr.name, Link::MAX_DATAREF_LENGTH, msg.dataRefName);
			vdr.name[Link::MAX_DATAREF_LENGTH - 1] = '\0';
			// get dataref
			vdr.dr = dataRef;

			updatedVariables[msg.vuid] = vdr;
		}
	}
}


void OnRequestVariable(Link::RequestVariableMsg& msg)
{
	// check for user aircraft
	if (msg.index == 0)
	{
		// check that definition is not already known
		if (requestedVariables.find(msg.vuid) == requestedVariables.end())
		{
			// get dataref
			XPLMDataRef dataRef = GetDataRef(msg.dataRefName);
			// check for valid dataref
			if (dataRef != NULL)
			{
				// new variable dataref
				VariableDataRef vdr;
				// get scalar
				vdr.scalar = msg.scalar;
				vdr.invScalar = vdr.scalar == 0.0f ? 1.0f : 1.0f / vdr.scalar;
				// get index
				vdr.index = msg.drIndex;
				// get type
				vdr.type = msg.type;
				// get name
				strcpy_s(vdr.name, Link::MAX_DATAREF_LENGTH, msg.dataRefName);
				vdr.name[Link::MAX_DATAREF_LENGTH - 1] = '\0';
				// get dataref by name
				vdr.dr = dataRef;

				requestedVariables[msg.vuid] = vdr;
			}
		}
	}
}


static float DoModel(float elapsed, float elapsedLoop, int counter, void*)
{
    // check if client is connected
    if (Link::IsConnected())
    {
        // get filename of aircraft
        static char filename[256];
        static char path[512];
		filename[0] = '\0';
		path[0] = '\0';
		XPLMGetNthAircraftModel(XPLM_USER_AIRCRAFT, filename, path);

        // initialize model message
        static Link::ModelMsg msg;
        msg.user = 1;
        msg.plane = 1;
		msg.livery = (dr_livery != NULL) ? XPLMGetDatai(dr_livery) : 0;
		msg.nickname[0] = '\0';
		XPLMGetDatab(userAircraft.dr_callsign, msg.callsign, 0, Link::MAX_CALLSIGN_LENGTH);
		strcpy_s(msg.model, Link::MAX_MODEL_LENGTH, path);
		XPLMGetDatab(userAircraft.dr_icaoType, msg.icaoType, 0, Link::MAX_ICAOTYPE_LENGTH);
		// send user position
        Link::Send(msg);
    }

    // every four seconds
    return 4.0f;
}


static float DoAircraftPosition(float elapsed, float elapsedLoop, int counter, void*)
{
	// process link
	Link::DoWork();

	// check if client is connected
    if (Link::IsConnected() && userAircraft.overrideFlightControl == false)
    {
		// check for probe
		if (userAircraft.pos.probe == NULL)
		{
			// create probe
			userAircraft.pos.probe = XPLMCreateProbe(xplm_ProbeY);
		}

		// initialize position message
        static Link::AircraftPositionMsg msg;
        msg.index = 0;
        msg.netTime = now;
        msg.longitude = XPLMGetDatad(userAircraft.dr_longitude);
        msg.latitude = XPLMGetDatad(userAircraft.dr_latitude);
        msg.altitude = XPLMGetDatad(userAircraft.dr_altitude);
        msg.pitch = XPLMGetDataf(userAircraft.dr_p);
        msg.bank = XPLMGetDataf(userAircraft.dr_b);
        msg.heading = XPLMGetDataf(userAircraft.dr_h);
        msg.vx = XPLMGetDataf(userAircraft.dr_vx);
        msg.vy = XPLMGetDataf(userAircraft.dr_vy);
        msg.vz = XPLMGetDataf(userAircraft.dr_vz);
        msg.avx = XPLMGetDataf(userAircraft.dr_avx);
        msg.avy = XPLMGetDataf(userAircraft.dr_avy);
        msg.avz = XPLMGetDataf(userAircraft.dr_avz);
        msg.ax = XPLMGetDataf(userAircraft.dr_ax);
        msg.ay = XPLMGetDataf(userAircraft.dr_ay);
        msg.az = XPLMGetDataf(userAircraft.dr_az);
        msg.rudder = (short)(XPLMGetDataf(userAircraft.dr_rudder) * 16384.0f);
        msg.elevator = (short)(XPLMGetDataf(userAircraft.dr_elevator) * 16384.0f);
        msg.aileron = (short)(XPLMGetDataf(userAircraft.dr_aileron) * 16384.0f);
        msg.brakeLeft = (short)(XPLMGetDataf(userAircraft.dr_brakeLeft) * 32768.0 - 16384);
        msg.brakeRight = (short)(XPLMGetDataf(userAircraft.dr_brakeRight) * 32768.0 - 16384);

		// check for probe and valid height
		if (userAircraft.pos.probe != NULL)
		{
			XPLMProbeInfo_t info;
			info.structSize = sizeof(info);
			XPLMProbeResult result = XPLMProbeTerrainXYZ(userAircraft.pos.probe, (float)XPLMGetDatad(userAircraft.dr_x), (float)XPLMGetDatad(userAircraft.dr_y), (float)XPLMGetDatad(userAircraft.dr_z), &info);

			// get ground elevation
			if (result == xplm_ProbeHitTerrain)
			{
				// calculate local elevation from sea level
				userAircraft.pos.elevation = info.locationY - (float)XPLMGetDatad(userAircraft.dr_y) + (float)msg.altitude;
			}
		}

		msg.elevation = (float)userAircraft.pos.elevation;
		double height = msg.altitude - msg.elevation;
		msg.ground = height < 1.0 ? 1 : 0;

		// send user position
        Link::Send(msg);
	}

	// check if override has timed out
	if (userAircraft.overrideFlightControl && now > userAircraft.endOverrideTime)
	{
		// under remote flight control
		userAircraft.overrideFlightControl = false;
		// stop override
		int enable = 0;
		XPLMSetDatavi(userAircraft.dr_override_position, &enable, 0, 1);
//		XPLMSetDatai(userAircraft.dr_override_control, 0);
	}

	// ten updates per second
    return 0.1f;
}


static float DoState(float elapsed, float elapsedLoop, int counter, void*)
{
	// check if any labels are being shown
	if (labelFlags)
	{
		// for each injected aircraft
		for (int i = 0; i < MAX_AIRCRAFT; i++)
		{
			// get aircraft
			InjectedAircraft* aircraft = injectedAircraft[i];

			// check if aircraft is valid
			if (aircraft != NULL && aircraft->pos.NetValid())
			{
				// calculate distance
				double dx = aircraft->pos.x - XPLMGetDatad(userAircraft.dr_x);
				double dz = aircraft->pos.z - XPLMGetDatad(userAircraft.dr_z);
				aircraft->distance = 0.00054 * sqrt(dx * dx + dz * dz);
				// calculate speed
				aircraft->speed = 1.944 * sqrt(aircraft->pos.vx * aircraft->pos.vx + aircraft->pos.vz * aircraft->pos.vz);
			}
		}
	}

	// one per second
    return 1.0f;
}

static float DoVariables(float elapsed, float elapsedLoop, int counter, void*)
{
	// check if client is connected
	if (Link::IsConnected())
	{
		// for each variable
		for (map<unsigned int, VariableDataRef>::iterator i = requestedVariables.begin(); i != requestedVariables.end(); i++)
		{
			// get vuid
			unsigned int vuid = i->first;
			// check variable type
			switch (i->second.type)
			{
				case 0:
				{
					Link::IntegerVariableMsg msg;
					msg.index = 0;
					msg.vuid = vuid;
					msg.value = 0;
					switch (XPLMGetDataRefTypes(i->second.dr))
					{
					case xplmType_Int: msg.value = i->second.scalar == 0.0 ? XPLMGetDatai(i->second.dr) : (int)(XPLMGetDatai(i->second.dr) * i->second.invScalar); break;
					case xplmType_Float: msg.value = i->second.scalar == 0.0 ? (int)XPLMGetDataf(i->second.dr) : (int)(XPLMGetDataf(i->second.dr) * i->second.invScalar); break;
					case xplmType_IntArray:
						XPLMGetDatavi(i->second.dr, &msg.value, i->second.index, 1);
						if (i->second.scalar != 0.0)
						{
							msg.value = (int)(msg.value * i->second.invScalar);
						}
						break;
					case xplmType_FloatArray:
						float value;
						XPLMGetDatavf(i->second.dr, &value, i->second.index, 1);
						msg.value = i->second.scalar == 0.0 ? (int)value : (int)(value * i->second.invScalar);
						break;
					}
					Link::Send(msg);
				}
				break;

				case 1:
				{
					Link::FloatVariableMsg msg;
					msg.index = 0;
					msg.vuid = vuid;
					msg.value = 0.0f;
					switch (XPLMGetDataRefTypes(i->second.dr))
					{
					case xplmType_Int: msg.value = i->second.scalar == 0.0 ? (float)XPLMGetDatai(i->second.dr) : (float)XPLMGetDatai(i->second.dr) * i->second.invScalar; break;
					case xplmType_Float: msg.value = i->second.scalar == 0.0 ? XPLMGetDataf(i->second.dr) : XPLMGetDataf(i->second.dr) * i->second.invScalar; break;
					case xplmType_IntArray:
						int value;
						XPLMGetDatavi(i->second.dr, &value, i->second.index, 1);
						msg.value = i->second.scalar == 0.0 ? (float)value : (float)value * i->second.invScalar;
						break;
					case xplmType_FloatArray:
						XPLMGetDatavf(i->second.dr, &msg.value, i->second.index, 1);
						if (i->second.scalar != 0.0)
						{
							msg.value = msg.value * i->second.invScalar;
						}
						break;
					}
					Link::Send(msg);
				}
				break;

				case 2:
				{
					Link::String8VariableMsg msg;
					msg.index = 0;
					msg.vuid = vuid;
					msg.value[0] = '\0';
					switch (XPLMGetDataRefTypes(i->second.dr))
					{
					case xplmType_Data: XPLMGetDatab(i->second.dr, msg.value, 0, 8); break;
					}
					Link::Send(msg);
				}
				break;
			}
		}
	}

	// one per second
	return 1.0f;
}

static float DoHeartbeat(float elapsed, float elapsedLoop, int counter, void*)
{
    // check if client is connected
    if (Link::IsConnected())
    {
        // get x-plane version
        int xplaneVersion, xplmVersion;
        XPLMHostApplicationID id;
        XPLMGetVersions(&xplaneVersion, &xplmVersion, &id);
		// send heartbeat
		Link::HeartbeatMsg msg;
		msg.xplaneVersion = xplaneVersion;
		msg.acquiredPlanes = 1;
		msg.aircraftCount = 19;
		msg.labelFlags = 0;
		msg.labelColour = 0;
		msg.tcas = 0;
		Link::Send(msg);
	}

	// for each network aircraft
	for (int i = 0; i < MAX_AIRCRAFT; i++)
	{
		// get aircraft
		InjectedAircraft* aircraft = injectedAircraft[i];

		// check for timed out aircraft
		if (aircraft != NULL && aircraft->pos.NetValid() && now - aircraft->pos.netSimTime > 10.0)
		{
			// message
			DebugMsg("Removing aircraft %d, model '%s %s %s'\n", i + 1, aircraft->acIcaoType.c_str(), aircraft->acIcaoAirline.c_str(), aircraft->acLivery.c_str());
			// remove aircraft
			aircraft->Close();
			delete aircraft;
			injectedAircraft[i] = NULL;
		}
	}

	// every two seconds
    return 2.0f;
}


/// <summary>
/// calculate difference between two angles
/// </summary>
/// <param name="angle1">First angle</param>
/// <param name="angle2">Second angle</param>
/// <returns>Difference between two angles</returns>
static double GDA(double angle1, double angle2)
{
    // difference
	double delta = angle2 - angle1;
    // move into range
    if (delta <= -180.0) delta += 360.0;
    else if (delta > 180.0) delta -= 360.0;
    // return result
    return delta;
}


// Time step advance position
static void AdvancePosition(Position& pos, double time)
{
    // get frame time
	double dt = time - pos.simTime;

    // don't process more than 200 frames per second and network data is valid
    if (dt > 0.005f && pos.NetValid())
    {
        double ndt = pos.netRealTime - pos.netStateTime + time - pos.netSimTime;

        // limit to no more than two seconds extrapolation
        ndt = min(2.0, max(-2.0, ndt));

        // extrapolate network position
        double ndt2 = ndt * ndt;
        double nx = pos.nx + pos.nvx * ndt + pos.nax * ndt2;
        double ny = pos.ny + pos.nvy * ndt + pos.nay * ndt2;
        double nz = pos.nz + pos.nvz * ndt + pos.naz * ndt2;

        // extrapolate heading
		double nh = pos.nh + pos.navy * ndt;
        if (nh >= 360.0) nh -= 360.0;
        else if (nh < 0.0) nh += 360.0;

        // extrapolate pitch
		double np = pos.np + pos.navx * ndt;
        if (np >= 180.0) np -= 360.0;
        else if (np < -180.0) np += 360.0;

        // extrapolate bank
		double nb = pos.nb + pos.navz * ndt;
        if (nb >= 180.0) nb -= 360.0;
        else if (nb < -180.0) nb += 360.0;

        // extrapolate network velocity
		double nvx = pos.nvx + pos.nax * ndt;
		double nvy = pos.nvy + pos.nay * ndt;
		double nvz = pos.nvz + pos.naz * ndt;
		double navx = pos.navx;
		double navy = pos.navy;
		double navz = 0.0;// pos.navz;

        // interpolate sim position
        double dx = nx - pos.x;
        double dy = ny - pos.y;
        double dz = nz - pos.z;

        // check if too far from target
        if (dx > 50.0 || dy > 50.0 || dz > 50.0)
        {
            // copy network position
			pos.x = pos.nx;
			pos.y = pos.ny;
			pos.z = pos.nz;
			pos.vx = pos.nvx;
			pos.vy = pos.nvy;
			pos.vz = pos.nvz;
			pos.h = pos.nh;
			pos.p = pos.np;
			pos.b = pos.nb;
        }
        else
        {
			double dh = GDA(pos.h, nh);
			double dp = GDA(pos.p, np);
			double db = GDA(pos.b, nb);

            // catch up
            nvx += dx * CATCH_UP_RATE;
            nvy += dy * CATCH_UP_RATE;
            nvz += dz * CATCH_UP_RATE;

            // only catch up the orientation if no high angular turns are being made
	        if (fabs(pos.p) > -45.0 && fabs(pos.p) < 45.0 && fabs(pos.b) > -90.0 && fabs(pos.b) < 90.0)
	        {
		        if (fabs(pos.navx) < 10.0 && fabs(pos.navy) < 10.0 && fabs(pos.navz) < 10.0)
		        {
			        // add delta to angular velocity to catch up
			        navx += dp * CATCH_UP_RATE;
			        navy += dh * CATCH_UP_RATE;
			        navz += db * CATCH_UP_RATE;
		        }
	        }
	        else
	        {
				double t = min(1.0, max(0.0, (time - pos.netSimTime) * 10.0));
				pos.p = pos.nop + GDA(pos.nop, pos.np) * t;
				pos.h = pos.noh + GDA(pos.noh, pos.nh) * t;
				pos.b = pos.nob + GDA(pos.nob, pos.nb) * t;
			}

            // set velocity
			pos.vx = nvx;
			pos.vy = nvy;
			pos.vz = nvz;

            // advance sim position
			pos.x += pos.vx * dt;
			pos.y += pos.vy * dt;
			pos.z += pos.vz * dt;

            // advance heading
			pos.h += navy * dt;
            if (pos.h >= 360.0) pos.h -= 360.0;
            else if (pos.h < 0.0) pos.h += 360.0;

            // advance pitch
			pos.p += navx * dt;
            if (pos.p >= 180.0) pos.p -= 360.0;
            else if (pos.p < -180.0) pos.p += 360.0;

            // advance bank
			pos.b += navz * dt;
            if (pos.b >= 180.0) pos.b -= 360.0;
            else if (pos.b < -180.0) pos.b += 360.0;
        }

		// update sim time
		pos.simTime = time;
	}
}


static float DoFrame(float elapsed, float elapsedLoop, int counter, void*)
{
    // get current time
    now = GetTime();

    // check for remote flight control
    if (userAircraft.overrideFlightControl)
    {
        // advance position
        AdvancePosition(userAircraft.pos, now);

		// update position
		SetUserPosition(userAircraft);
		SetUserVelocity(userAircraft);

		// advance other variables
		userAircraft.AdvanceState();
	}

	// for each network aircraft
    for (int i = 0; i < MAX_AIRCRAFT; i++)
    {
		// get aircraft
		InjectedAircraft* aircraft = injectedAircraft[i];

		// check if aircraft is valid
		if (aircraft != NULL)
		{
			// advance position
			AdvancePosition(aircraft->pos, now);
		}
    }

	return -1.0f;
}


// vector transform
static void mult_matrix_vec(float dst[4], const float m[16], const float v[4])
{
	dst[0] = v[0] * m[0] + v[1] * m[4] + v[2] * m[8] + v[3] * m[12];
	dst[1] = v[0] * m[1] + v[1] * m[5] + v[2] * m[9] + v[3] * m[13];
	dst[2] = v[0] * m[2] + v[1] * m[6] + v[2] * m[10] + v[3] * m[14];
	dst[3] = v[0] * m[3] + v[1] * m[7] + v[2] * m[11] + v[3] * m[15];
}


/// This is a callback the XPMP2 calls regularly to learn about configuration settings.
/// Only 3 are left, all of them integers.
int CBIntPrefsFunc(const char *, [[maybe_unused]] const char * item, int defaultVal)
{
	// We always want to replace dataRefs and textures upon load to make the most out of the .obj files
	if (!strcmp(item, XPMP_CFG_ITM_REPLDATAREFS)) return 1;
	if (!strcmp(item, XPMP_CFG_ITM_REPLTEXTURE)) return 1;      // actually...this is ON by default anyway, just to be sure
#if DEBUG
	// in debug version of the plugin we provide most complete log output
	if (!strcmp(item, XPMP_CFG_ITM_MODELMATCHING)) return 1;
	if (!strcmp(item, XPMP_CFG_ITM_LOGLEVEL)) return 0;       // DEBUG logging level
#endif
	// Otherwise we just accept defaults
	return defaultVal;
}


/// This is the callback for the plane notifier function, which just logs some info to Log.txt
/// @note Plane notifier functions are completely optional and actually rarely used,
///       because you should know already by other means when your plugin creates/modifies/deletes a plane.
///       So this is for pure demonstration (and testing) purposes.
void CBPlaneNotifier(XPMPPlaneID            inPlaneID,
	XPMPPlaneNotification  inNotification,
	void *                 /*inRefcon*/)
{
	Aircraft* aircraft = (Aircraft*)XPMP2::AcFindByID(inPlaneID);
	if (aircraft)
	{
		DebugMsg("Aircraft %d of type '%s %s %s' %s\n",
			inPlaneID,
			aircraft->acIcaoType.c_str(),
			aircraft->acIcaoAirline.c_str(),
			aircraft->acLivery.c_str(),
			inNotification == xpmp_PlaneNotification_Created ? "created" :
			inNotification == xpmp_PlaneNotification_ModelChanged ? "changed" : "destroyed");
	}
}


// start entry
PLUGIN_API int XPluginStart(char* outName, char* outSig, char *outDesc)
{
	// initialize plugin information
	strcpy_s(outName, 64, "JoinFS");
	strcpy_s(outSig, 64, "JoinFS.JoinFS");
	strcpy_s(outDesc, 64, "JoinFS plugin. https://joinfs.net");

	// start system timer
	StartCounter();

	// initialize user aircraft
	InitUserAircraft(userAircraft);

	// initialize injected aircraft list
	for (int i = 0; i < MAX_AIRCRAFT; i++)
	{
		// empty slot
		injectedAircraft[i] = NULL;
	}

	XPLMEnableFeature("XPLM_USE_NATIVE_PATHS", 1);

	// livery
	dr_livery = GetDataRef("sim/aircraft/view/acf_livery_index");

	// matrices and screen dimensions
	s_matrix_wrl = XPLMFindDataRef("sim/graphics/view/world_matrix");
	s_matrix_proj = XPLMFindDataRef("sim/graphics/view/projection_matrix_3d");
	s_screen_width = XPLMFindDataRef("sim/graphics/view/window_width");
	s_screen_height = XPLMFindDataRef("sim/graphics/view/window_height");

	// success
	return 1;
}

PLUGIN_API void	XPluginStop(void)
{
	// close user aircraft
	userAircraft.Close();

	// for each injected aircraft
	for (int i = 0; i < MAX_AIRCRAFT; i++)
	{
		// check for valid aircraft
		if (injectedAircraft[i] != NULL)
		{
			// remove aircraft
			injectedAircraft[i]->Close();
			delete injectedAircraft[i];
			injectedAircraft[i] = NULL;
		}
	}

	// clear variables
	updatedVariables.clear();
	requestedVariables.clear();
}

// enable plugin entry point
PLUGIN_API int XPluginEnable(void)
{
	// open link
	Link::Open();

	// initialize callbacks
	XPLMRegisterFlightLoopCallback(DoModel, 4.0f, NULL);
	XPLMRegisterFlightLoopCallback(DoAircraftPosition, 0.1f, NULL);
	XPLMRegisterFlightLoopCallback(DoState, 1.0f, NULL);
	XPLMRegisterFlightLoopCallback(DoVariables, 1.0f, NULL);
	XPLMRegisterFlightLoopCallback(DoHeartbeat, 2.0f, NULL);
	XPLMRegisterFlightLoopCallback(DoFrame, -1.0f, NULL);

//	XPLMRegisterDrawCallback(DoLabels, xplm_Phase_Window, 0, NULL);

	// The path separation character, one out of /\:
	char pathSep = XPLMGetDirectorySeparator()[0];
	// The plugin's path, results in something like ".../Resources/plugins/XPMP2-Sample/64/XPMP2-Sample.xpl"
	char szPath[256];
	XPLMGetPluginInfo(XPLMGetMyID(), nullptr, szPath, nullptr, nullptr);
	*(strrchr(szPath, pathSep)) = 0;   // Cut off the plugin's file name
	*(strrchr(szPath, pathSep) + 1) = 0; // Cut off the "64" directory name, but leave the dir separation character
	// We search in a subdirectory named "Resources" for all we need
	std::string resourcePath = szPath;
	resourcePath += "Resources";            // should now be something like ".../Resources/plugins/XPMP2-Sample/Resources"

	// Try initializing XPMP2:
	const char *res = XPMPMultiplayerInit(Common::productName, resourcePath.c_str(), CBIntPrefsFunc, "C172");
	if (res[0])
	{
		DebugMsg("Initialization of XPMP2 failed: %s", res);
		return 0;
	}

	// Load our CSL models
	res = XPMPLoadCSLPackage(resourcePath.c_str());     // CSL folder root path
	if (res[0])
	{
		DebugMsg("Error while loading CSL packages: %s", res);
	}

	// Register the plane notifer function
	// (this is rarely used in actual applications, but used here for
	//  demonstration and testing purposes)
	XPMPRegisterPlaneNotifierFunc(CBPlaneNotifier, NULL);

    return 1;
}

PLUGIN_API void XPluginDisable(void)
{
//	XPLMUnregisterDrawCallback(DoLabels, xplm_Phase_Window, 0, NULL);

	// unregister callbacks
	XPLMUnregisterFlightLoopCallback(DoModel, NULL);
	XPLMUnregisterFlightLoopCallback(DoAircraftPosition, NULL);
	XPLMUnregisterFlightLoopCallback(DoState, NULL);
	XPLMUnregisterFlightLoopCallback(DoVariables, NULL);
	XPLMUnregisterFlightLoopCallback(DoHeartbeat, NULL);
	XPLMUnregisterFlightLoopCallback(DoFrame, NULL);

	Link::Close();
}

PLUGIN_API void XPluginReceiveMessage(
	XPLMPluginID	inFromWho,
	int				inMessage,
	void *			inParam)
{
}

