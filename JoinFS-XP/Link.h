
namespace Link
{
	// length of nickname
	static const int MAX_NICKNAME_LENGTH = 20;
	// length of callsign
	static const int MAX_CALLSIGN_LENGTH = 40;
	// length of model name
	static const int MAX_MODEL_LENGTH = 256;
	// length of ICAO type
	static const int MAX_ICAOTYPE_LENGTH = 40;
	// max length of a dataref
	static const int MAX_DATAREF_LENGTH = 128;

    // current node verison
	static short NODE_VERSION = 0x000b;

#pragma pack(push, 1)

    struct Msg
    {
        short nodeVersion;
        char flags;
        short guaranteedId;
        char guaranteedIndex;
        char guaranteedCount;
		unsigned int senderIp;
		unsigned short senderPort;
		unsigned char senderLocal;
		unsigned int recipientIp;
		unsigned short recipientPort;
		unsigned char recipientLocal;
		short version;
        char id;

        Msg()
        {
            nodeVersion = NODE_VERSION;
            flags = 0;
            guaranteedId = 0;
            guaranteedIndex = 0;
            guaranteedCount = 0;
			senderIp = 0;
			senderPort = 0;
			senderLocal = 0;
			recipientIp = 0;
			recipientPort = 0;
			recipientLocal = 0;
			version = 0;
			id = 0;
		}
    };

	struct ConnectMsg : public Msg
	{
		char connectFlags;
	};

	struct HeartbeatMsg : public Msg
	{
		short xplaneVersion;
		char acquiredPlanes;
		char aircraftCount;
		char labelFlags;
		int labelColour;
		char tcas;
	};

	struct DisconnectMsg : public Msg
    {
    };

    struct ModelMsg : public Msg
    {
        char index;
        char active;
        char user;
        char plane;
		char livery;
		char nickname[MAX_NICKNAME_LENGTH];
		char callsign[MAX_CALLSIGN_LENGTH];
		char model[MAX_MODEL_LENGTH];
		char icaoType[MAX_ICAOTYPE_LENGTH];
	};

    struct AircraftPositionMsg : public Msg
    {
        char index;
        double netTime;
        double latitude;
        double longitude;
        double altitude;
        float pitch;
        float bank;
        float heading;
        float vx;
        float vy;
        float vz;
        float avx;
        float avy;
        float avz;
        float ax;
        float ay;
        float az;
        short rudder;
        short elevator;
        short aileron;
        short brakeLeft;
        short brakeRight;
		float elevation;
		char ground;
    };

    struct ObjectPositionMsg : public Msg
    {
        char index;
        double latitude;
        double longitude;
        double altitude;
        float pitch;
        float bank;
        float heading;
		float elevation;
		char ground;
    };

    struct ObjectVelocityMsg : public Msg
    {
        char index;
        float vx;
        float vy;
        float vz;
        float avx;
        float avy;
        float avz;
        float ax;
        float ay;
        float az;
    };

	struct FuelMsg : public Msg
	{
		char index;
		char centre1;
		char centre2;
		char centre3;
		char leftMain;
		char leftAux;
		char leftTip;
		char rightMain;
		char rightAux;
		char rightTip;
		char external1;
		char external2;
	};

	struct EventMsg : public Msg
	{
		char index;
		short simEvent;
		int data;
	};

	struct RemoveMsg : public Msg
	{
		char index;
	};

	struct IntegerVariableMsg : public Msg
	{
		char index;
		unsigned int vuid;
		int value;
	};

	struct FloatVariableMsg : public Msg
	{
		char index;
		unsigned int vuid;
		float value;
	};

	struct String8VariableMsg : public Msg
	{
		char index;
		unsigned int vuid;
		char value[8];
	};

	struct GetDefinitionMsg : public Msg
	{
		unsigned int vuid;
	};

	struct DefinitionMsg : public Msg
	{
		unsigned int vuid;
		float scalar;
		int drIndex;
		char type;
		char dataRefName[128];
	};

	struct RequestVariableMsg : public Msg
	{
		char index;
		unsigned int vuid;
		float scalar;
		int drIndex;
		char type;
		char dataRefName[128];
	};

#pragma pack(pop)

    bool Open();
    void Close();
    void Disconnect();
    bool IsConnected();
    void DoWork();
    bool SocketAvailable();
	void Send(HeartbeatMsg& msg);
	void Send(DisconnectMsg& msg);
	void Send(ModelMsg& msg);
    void Send(AircraftPositionMsg& msg);
	void Send(FuelMsg& msg);
	void Send(ObjectPositionMsg& msg);
	void Send(ObjectVelocityMsg& msg);
	void Send(IntegerVariableMsg& msg);
	void Send(FloatVariableMsg& msg);
	void Send(String8VariableMsg& msg);
	void Send(GetDefinitionMsg& msg);
}
