#include <stdio.h>

#ifdef _WINDOWS
#include <WinSock2.h>
#define IBM 1
// local socket
static SOCKET localSocket = INVALID_SOCKET;

#else

#include <stdlib.h> 
#include <unistd.h> 
#include <string.h> 
#include <sys/types.h> 
#include <sys/socket.h> 
#include <arpa/inet.h> 
#include <netinet/in.h>
#include <netdb.h>
#include <sys/ioctl.h>
#define LIN 1
int INVALID_SOCKET = -1;
int localSocket = INVALID_SOCKET;

#endif


#include "XPLMProcessing.h"

#include "Common.h"
#include "Link.h"



using namespace Common;

#define MAXLINE 1024

char buffer[MAXLINE];

struct sockaddr_in servaddr, clientAddress;


static const short DATA_VERSION = 21024;
static const short PORT = 7472;


// drop connection time
static float dropTime = 0.0f;


// message types
enum MessageId
{
    MSG_CONNECT = 0,
    MSG_DISCONNECT,
	MSG_HEARTBEAT,
	MSG_MODEL,
    MSG_AIRCRAFT_POSITION,
    MSG_OBJECT_POSITION,
    MSG_OBJECT_VELOCITY,
    MSG_PLANE_STATE,
    MSG_AIRCRAFT_STATE,
    MSG_PISTON_STATE,
    MSG_TURBINE_STATE,
	MSG_FUEL,
    MSG_EVENT,
	MSG_REMOVE,
	MSG_INTEGER_VARIABLE,
	MSG_FLOAT_VARIABLE,
	MSG_STRING8_VARIABLE,
	MSG_GET_DEFINITION,
	MSG_DEFINITION,
	MSG_REQUEST_VARIABLE
};


void OnConnect(Link::ConnectMsg& msg);
void OnDisconnect();
void OnHeartbeat(Link::HeartbeatMsg& msg);
void OnModel(Link::ModelMsg& msg);
void OnAircraftPosition(Link::AircraftPositionMsg& msg);
void OnObjectPosition(Link::ObjectPositionMsg& msg);
void OnObjectVelocity(Link::ObjectVelocityMsg& msg);
void OnEvent(Link::EventMsg& msg);
void OnFuel(Link::FuelMsg& msg);
void OnRemove(Link::RemoveMsg& msg);
void OnIntegerVariable(Link::IntegerVariableMsg& msg);
void OnFloatVariable(Link::FloatVariableMsg& msg);
void OnString8Variable(Link::String8VariableMsg& msg);
void OnDefinition(Link::DefinitionMsg& msg);
void OnRequestVariable(Link::RequestVariableMsg& msg);


// open link
bool Link::Open()
{
    Disconnect();

		// set local address
	sockaddr_in local;
	local.sin_family = AF_INET;
	local.sin_addr.s_addr = INADDR_ANY;
	local.sin_port = htons(PORT);
	
	// check if socket is open
    if (localSocket != INVALID_SOCKET)
    {
        // close first
        Close();
		DebugMsg("Closed Socket\n");
	}

#ifdef _WINDOWS
	
	// start sockets
    WSAData data;

    // start sockets
    if (WSAStartup(MAKEWORD(2, 2), &data) == 0)
    {
        // open socket
        localSocket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        if (localSocket != INVALID_SOCKET)
        {
            // bind port
            if (bind(localSocket, (sockaddr*)&local, sizeof(local)) != SOCKET_ERROR)
            {
                // non-blocking
                u_long mode = 1;
                ioctlsocket(localSocket, FIONBIO, &mode);
                // success
                DebugMsg("Listening for the client on port %d.\n", PORT);
                return true;
            }
            else
            {
                DebugMsg("Failed to bind socket, %d\n", WSAGetLastError());
            }
        }
        else
        {
            DebugMsg("Failed to create socket, %d\n", WSAGetLastError());
        }
    }
    else
    {
        DebugMsg("Failed to start WinSock, %d\n", WSAGetLastError());
    }
#else
	if (localSocket == -1)
	{
		// Creating socket file descriptor 
		if ((localSocket = socket(AF_INET, SOCK_DGRAM, 0)) < 0)
		{
			DebugMsg("Socket creation failed!\n");
			exit(EXIT_FAILURE);
		}


		// Bind the socket with the server address 
		if (bind(localSocket, (struct sockaddr*) & local, sizeof(local)) < 0)
		{
			DebugMsg("Bind failed");
			exit(EXIT_FAILURE);
		}

		// non-blocking
		u_long mode = 1;
		ioctl(localSocket, FIONBIO, &mode);

		// success
		DebugMsg("Started listening on port %d.\n", PORT);
		return true;
	}
#endif

    // failed
    return false;
}


// close link
void Link::Close()
{
    Disconnect();

#ifdef _WINDOWS
	
	// check for valid socket
    if (localSocket != INVALID_SOCKET)
    {
        // close socket
        closesocket(localSocket);
        localSocket = INVALID_SOCKET;
    }

    // finish
    WSACleanup();

#else
	// check for valid socket
	if (localSocket != -1)
	{
		// close socket
		close(localSocket);
		localSocket = -1;
	}
#endif

	DebugMsg("No longer listening on port %d.\n", PORT);
}


// disconnect
void Link::Disconnect()
{
    // check if connected
    if (IsConnected())
    {
		// disconnect
		OnDisconnect();
        // message
        DebugMsg("Disconnected.\n");

		// send disconnect message
		static DisconnectMsg msg;
		Send(msg);
	}
    // initialize client address
    memset(&clientAddress, 0, sizeof(clientAddress));
}


// is a client connected
bool Link::IsConnected()
{
    // connected if client address is valid
    return clientAddress.sin_port != 0;
}


// process link
void Link::DoWork()
{
    //static char buffer[1024];
    //sockaddr_in address;
    
#ifdef _WINDOWS
	int addressSize = sizeof(servaddr);
#else
	socklen_t addressSize = sizeof(servaddr);
#endif

	int len = sizeof(clientAddress);  //len is value/resuslt 

	// read messages
    int result;
    do
    {
        // check for message
        //result = recvfrom(localSocket, buffer, 1024, 0, (SOCKADDR*)&address, &addressSize);

		result = recvfrom(localSocket, (char*)buffer, MAXLINE, 0, (struct sockaddr*) & servaddr, &addressSize);

		// check for valid packet
        if (result > 0 && result >= sizeof (Msg))
        {
			// copy high byte from incoming version
			NODE_VERSION &= 0xff;
			NODE_VERSION |= ((Msg*)buffer)->nodeVersion & 0xff00;
			
			// check version
            if (((Msg*)buffer)->version == DATA_VERSION)
            {
                // update drop time
                dropTime = XPLMGetElapsedTime() + 120.0f;

                // check message type
                switch (((Msg*)buffer)->id)
                {
                case MSG_CONNECT:
					if (IsConnected() == false)
					{
						// callback
						OnConnect(*(ConnectMsg*)buffer);
					}
                    // save address
                    memcpy(&clientAddress, &servaddr, sizeof(servaddr));
					// message
                    DebugMsg("Client connected.\n");
                    break;

				case MSG_DISCONNECT:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						DebugMsg("Client disconnected.\n");
						Disconnect();
					}
					break;

				case MSG_HEARTBEAT:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnHeartbeat(*(HeartbeatMsg*)buffer);
					}
					break;

				case MSG_MODEL:
                    // check from client
                    if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
                    {
                        // callback
                        OnModel(*(ModelMsg*)buffer);
                    }
                    break;

                case MSG_AIRCRAFT_POSITION:
                    // check from client
                    if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
                    {
                        // callback
						OnAircraftPosition(*(AircraftPositionMsg*)buffer);
                    }
                    break;

                case MSG_OBJECT_POSITION:
                    // check from client
                    if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
                    {
                        // callback
						OnObjectPosition(*(ObjectPositionMsg*)buffer);
                    }
                    break;

				case MSG_OBJECT_VELOCITY:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnObjectVelocity(*(ObjectVelocityMsg*)buffer);
					}
					break;

				case MSG_EVENT:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnEvent(*(EventMsg*)buffer);
					}
					break;

				case MSG_FUEL:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnFuel(*(FuelMsg*)buffer);
					}
					break;

				case MSG_REMOVE:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnRemove(*(RemoveMsg*)buffer);
					}
					break;

				case MSG_INTEGER_VARIABLE:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnIntegerVariable(*(IntegerVariableMsg*)buffer);
					}
					break;

				case MSG_FLOAT_VARIABLE:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnFloatVariable(*(FloatVariableMsg*)buffer);
					}
					break;

				case MSG_STRING8_VARIABLE:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnString8Variable(*(String8VariableMsg*)buffer);
					}
					break;

				case MSG_DEFINITION:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnDefinition(*(DefinitionMsg*)buffer);
					}
					break;

				case MSG_REQUEST_VARIABLE:
					// check from client
					if (memcmp(&servaddr, &clientAddress, sizeof(clientAddress)) == 0)
					{
						// callback
						OnRequestVariable(*(RequestVariableMsg*)buffer);
					}
					break;
				}
            }
			// data version mismatch
			else
			{
				DebugMsg("Data version mismatch.\n");
				// check for valid send
				if (localSocket != INVALID_SOCKET && SocketAvailable())
				{
					// send disconnect
					DisconnectMsg msg;
					msg.nodeVersion = NODE_VERSION;
					msg.version = DATA_VERSION;
					msg.id = MSG_DISCONNECT;
					// send message
					sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&servaddr, sizeof(servaddr));
				}
			}
        }
    } while (result > 0);

    // check drop time
    if (IsConnected() && XPLMGetElapsedTime() > dropTime)
    {
        // disconnect
        Disconnect();
        // message
        DebugMsg("Client connection was dropped due to time out.\n");
    }
}


bool Link::SocketAvailable()
{
#ifdef _WINDOWS
    fd_set fds;
    FD_ZERO(&fds);
    FD_SET(localSocket, &fds);
    select(1, NULL, &fds, NULL, NULL);
    return FD_ISSET(localSocket, &fds) != 0;
#else
	return true;
#endif
}


void Link::Send(HeartbeatMsg& msg)
{
	// check for valid send
	if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
	{
		// initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
		msg.id = MSG_HEARTBEAT;
		// send position message
		sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
	}
}


void Link::Send(DisconnectMsg& msg)
{
	// check for valid send
	if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
	{
		// initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
		msg.id = MSG_DISCONNECT;
		// send position message
		sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
	}
}


// send model
void Link::Send(ModelMsg& msg)
{
    // check for valid send
    if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
    {
        // initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
        msg.id = MSG_MODEL;
        // send position message
        sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
    }
}

// send update
void Link::Send(AircraftPositionMsg& msg)
{
    // check for valid send
    if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
    {
        // initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
        msg.id = MSG_AIRCRAFT_POSITION;
        // send update message
        sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
    }
}

// send fuel
void Link::Send(FuelMsg& msg)
{
	// check for valid send
	if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
	{
		// initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
		msg.id = MSG_FUEL;
		// send update message
		sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
	}
}

// send position
void Link::Send(ObjectPositionMsg& msg)
{
    // check for valid send
    if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
    {
        // initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
        msg.id = MSG_OBJECT_POSITION;
        // send position message
        sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
    }
}

// send velocity
void Link::Send(ObjectVelocityMsg& msg)
{
	// check for valid send
	if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
	{
		// initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
		msg.id = MSG_OBJECT_VELOCITY;
		// send velocity message
		sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
	}
}

// send integer variable
void Link::Send(IntegerVariableMsg& msg)
{
	// check for valid send
	if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
	{
		// initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
		msg.id = MSG_INTEGER_VARIABLE;
		// send velocity message
		sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
	}
}

// send float variable
void Link::Send(FloatVariableMsg& msg)
{
	// check for valid send
	if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
	{
		// initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
		msg.id = MSG_FLOAT_VARIABLE;
		// send velocity message
		sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
	}
}

// send string variable
void Link::Send(String8VariableMsg& msg)
{
	// check for valid send
	if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
	{
		// initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
		msg.id = MSG_STRING8_VARIABLE;
		// send velocity message
		sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
	}
}

// send get definition
void Link::Send(GetDefinitionMsg& msg)
{
	// check for valid send
	if (localSocket != INVALID_SOCKET && IsConnected() && SocketAvailable())
	{
		// initialize header
		msg.nodeVersion = NODE_VERSION;
		msg.version = DATA_VERSION;
		msg.id = MSG_GET_DEFINITION;
		// send velocity message
		sendto(localSocket, (const char*)&msg, sizeof(msg), 0, (const struct sockaddr*)&clientAddress, sizeof(clientAddress));
	}
}
