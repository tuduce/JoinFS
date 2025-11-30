using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
#if !CONSOLE
using System.Windows.Forms;
#endif
using System.Globalization;
using JoinFS.Properties;

namespace JoinFS
{
    public class XPlane
    {
        /// <summary>
        /// main form
        /// </summary>
        Main main;

        /// <summary>
        /// maximum length of callsign
        /// </summary>
        const int MAX_CALLSIGN_LENGTH = 40;

        /// <summary>
        /// maximum length of model string
        /// </summary>
        const int MAX_MODEL_LENGTH = 256;

        /// <summary>
        /// maximum length of type string
        /// </summary>
        const int MAX_ICAOTYPE_LENGTH = 40;

        /// <summary>
        /// maximum length of dataref string
        /// </summary>
        const int MAX_DATAREF_LENGTH = 128;

        /// <summary>
        /// maximum number if aircraft in X-Plane
        /// </summary>
        const int MAX_AIRCRAFT = 20;

        /// <summary>
        /// X-Plane connection version
        /// </summary>
        const short DATA_VERSION = 21023;

        /// <summary>
        /// X-Plane version
        /// </summary>
        short version = 0;

        /// <summary>
        /// Already displayed acquired warning?
        /// </summary>
        public bool AcquiredWarning { get; private set; } = false;

        /// <summary>
        /// Already displayed aircraft warning?
        /// </summary>
        public bool AircraftWarning { get; private set; } = false;

        /// <summary>
        /// Is x-plane open
        /// </summary>
        public bool IsOpen { get { return localNode.IsOpen; } }

        /// <summary>
        /// Is x-plane connected
        /// </summary>
        public bool IsConnected { get; private set; } = false;

        // message types
        enum MessageId
        {
            CONNECT = 0,
            DISCONNECT,
            HEARTBEAT,
            MODEL,
            AIRCRAFT_POSITION,
            OBJECT_POSITION,
            OBJECT_VELOCITY,
            PLANE_STATE,
            AIRCRAFT_STATE,
            PISTON_ENGINE_STATE,
            TURBINE_ENGINE_STATE,
            FUEL_STATE,
            EVENT,
            REMOVE,
            INTEGER_VARIABLE,
            FLOAT_VARIABLE,
            STRING8_VARIABLE,
            GET_DEFINITION,
            DEFINITION,
            REQUEST_VARIABLE
        };


        /// <summary>
        /// Local node
        /// </summary>
        public LocalNode localNode;

        /// <summary>
        /// UDP port
        /// </summary>
        const short CLIENT_PORT = 7370;

        /// <summary>
        /// UDP port
        /// </summary>
        const short PLUGIN_PORT = 7472;

        /// <summary>
        /// End point of the plugin
        /// </summary>
        IPEndPoint pluginEndPoint;

        /// <summary>
        /// Model update callback
        /// </summary>
        /// <param name="?"></param>
        public delegate void ModelNotify(uint simId, bool user, bool plane, string callsign, string model, string icaoType);
        public ModelNotify modelNotify;

        /// <summary>
        /// Position update callback
        /// </summary>
        /// <param name="?"></param>
        public delegate void AircraftPositionNotify(uint simId, double simTime, ref Sim.AircraftPosition position);
        public AircraftPositionNotify aircraftPositionNotify;

        /// <summary>
        /// Connected callback
        /// </summary>
        /// <param name="?"></param>
        public delegate void ConnectedNotify(short version);
        public ConnectedNotify connectedNotify;

        /// <summary>
        /// Remove callback
        /// </summary>
        /// <param name="?"></param>
        public delegate void RemoveNotify(uint simId);
        public RemoveNotify removeNotify;

        /// <summary>
        /// Constructor
        /// </summary>
        public XPlane(Main main)
        {
            // save main form
            this.main = main;

            // add maximum aircraft
            for (int a = 0; a < MAX_AIRCRAFT; a++)
            {
                // add aircraft
                aircraftList.Add(new NetAircraft());
            }

            // create node
            localNode = new LocalNode(main)
            {
                // notifications
                nodeError = main.MonitorEvent,
                receiveNotify = ReceiveMsg
            };
        }

        /// <summary>
        /// Clean an injection folder
        /// </summary>
        /// <param name="index"></param>
        void CleanInjectionFolder(int index)
        {
            try
            {
                // get injection folder
                string injectFolder = Path.Combine(main.substitution.simFolder, "Resources", "plugins", "JoinFS", "inject", index.ToString("D3", CultureInfo.InvariantCulture));

                // check if folder exists
                if (Directory.Exists(injectFolder))
                {
                    // delete folder and contents
                    Directory.Delete(injectFolder, true);
                }
            }
            catch { }
        }

        /// <summary>
        /// clean all injection folders
        /// </summary>
        void CleanInjectionFolders()
        {
            // for all injection folders
            for (int index = 0; index < MAX_AIRCRAFT; index++)
            {
                // clean folder
                CleanInjectionFolder(index);
            }
        }

        /// <summary>
        /// Open connection to plugin
        /// </summary>
        public void Open()
        {
            // check if node is close
            if (localNode.IsOpen == false)
            {
                // try different ports
                int port = CLIENT_PORT;
                while (localNode.Open(port) == false && port < CLIENT_PORT + 100) port++;
            }

            // check port is open
            if (localNode.IsOpen)
            {
                // get plugin address
                string addressStr = Settings.Default.XPlanePluginAddress;
                // check for empty address
                if (addressStr.Length == 0)
                {
                    // use loopback
                    addressStr = "127.0.0.1";
                }
                if (IPAddress.TryParse(addressStr, out IPAddress address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    // create end point
                    pluginEndPoint = new IPEndPoint(address, PLUGIN_PORT);

                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.CONNECT);
                    // flags
                    byte flags = 1;
                    message.Write(flags);
                    // send message
                    localNode.Send(pluginEndPoint);
                }
                else
                {
                    // message
                    main.MonitorEvent("Invalid X-Plane address '" + addressStr + "' in File|Settings. Should be in the format 'x.x.x.x', where x is 0-255.");
                }
            }
            else
            {
                // message
                main.MonitorEvent("ERROR. Failed to find a port to connect to the X-Plane plugin.");
            }
        }

        /// <summary>
        /// Close connection
        /// </summary>
        public void Close()
        {
            // remove all aircraft
            for (int index = 0; index < aircraftList.Count; index++)
            {
                // aircraft no longer in use
                aircraftList[index].inUse = false;
            }

            // check if node is open
            if (localNode.IsOpen)
            {
                // prepare message
                BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                // add header
                message.Write(DATA_VERSION);
                // add message ID
                message.Write((byte)MessageId.DISCONNECT);
                // send message
                localNode.Send(pluginEndPoint);

                localNode.Close();
            }

            // reset
            version = 0;
            AcquiredWarning = false;
            AircraftWarning = false;
            IsConnected = false;
        }

        /// <summary>
        /// Time of next heartbeat
        /// </summary>
        double heartbeatTime = 0.0;

        /// <summary>
        /// Process plugin link
        /// </summary>
        public void DoWork()
        {
            // check for heartbeat
            if (IsOpen && main.ElapsedTime > heartbeatTime)
            {
                // prepare heartbeat message
                BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                // add header
                message.Write(DATA_VERSION);
                // add message ID
                message.Write((byte)MessageId.HEARTBEAT);
                // xplane version N/A
                message.Write((short)0);
                // acquired aircraft
                message.Write((byte)1);
                // aircraft count
                message.Write((byte)MAX_AIRCRAFT);
                // add label flags
                byte flags = 0;
                if (Settings.Default.ShowNicknames) flags |= 0x01;
                if (Settings.Default.ShowCallsign) flags |= 0x02;
                if (Settings.Default.ShowDistance) flags |= 0x04;
                if (Settings.Default.ShowAltitude) flags |= 0x08;
                if (Settings.Default.ShowSpeed) flags |= 0x10;
                message.Write(flags);
                message.Write(Settings.Default.ColourLabel.ToArgb());
                // tcas
                message.Write((byte)(main.settingsTcas ? 1 : 0));

                // send message
                localNode.Send(pluginEndPoint);

                // next heartbeat
                heartbeatTime = main.ElapsedTime + 2.0;
            }

            // check if open
            if (IsConnected)
            {
                // check for resend
                if (aircraftList[0].inUse && main.ElapsedTime > aircraftList[0].resendTime)
                {
                    // check for notification
                    aircraftPositionNotify?.Invoke(IndexToSimId(0), aircraftList[0].simTime, ref aircraftList[0].position);
                    // set next resend
                    aircraftList[0].resendTime = main.ElapsedTime + 1.0;
                }
            }

            // process node
            localNode.DoWork();
        }

        /// <summary>
        /// Install the X-Plane plugin
        /// </summary>
        public void InstallPlugin(string folder)
        {
            try
            {
                // get plugins folder
                folder += Path.DirectorySeparatorChar + "Resources" + Path.DirectorySeparatorChar + "plugins";

                // check for plugins folder
                if (Directory.Exists(folder))
                {
                    // create plugin folder
                    folder += Path.DirectorySeparatorChar + "JoinFS";
                    if (Directory.Exists(folder) == false) Directory.CreateDirectory(folder);

                    // 64-bit folder
                    string folder64 = folder + Path.DirectorySeparatorChar + "64";
                    if (Directory.Exists(folder64) == false) Directory.CreateDirectory(folder64);
                    File.Copy("win.xpl", folder64 + Path.DirectorySeparatorChar + "win.xpl", true);
                    File.Copy("lin.xpl", folder64 + Path.DirectorySeparatorChar + "lin.xpl", true);

                    // resources folder
                    string resourcesFolder = folder + Path.DirectorySeparatorChar + "Resources";
                    if (Directory.Exists(resourcesFolder) == false) Directory.CreateDirectory(resourcesFolder);

                    // create Doc8643.txt
                    using (Stream data = new MemoryStream(Properties.Resources_XPLANE.XPMP2_Doc8643))
                    {
                        Stream file = File.Create(resourcesFolder + Path.DirectorySeparatorChar + "Doc8643.txt");
                        data.CopyTo(file);
                        file.Close();
                    }

                    // create MapIcons.png
                    using (Stream data = new MemoryStream(Properties.Resources_XPLANE.XPMP2_MapIcons))
                    {
                        Stream file = File.Create(resourcesFolder + Path.DirectorySeparatorChar + "MapIcons.png");
                        data.CopyTo(file);
                        file.Close();
                    }

                    // create Obj8DataRefs.txt
                    using (Stream data = new MemoryStream(Properties.Resources_XPLANE.XPMP2_Obj8DataRefs))
                    {
                        Stream file = File.Create(resourcesFolder + Path.DirectorySeparatorChar + "Obj8DataRefs.txt");
                        data.CopyTo(file);
                        file.Close();
                    }

                    // create related.txt
                    using (Stream data = new MemoryStream(Properties.Resources_XPLANE.XPMP2_related))
                    {
                        Stream file = File.Create(resourcesFolder + Path.DirectorySeparatorChar + "related.txt");
                        data.CopyTo(file);
                        file.Close();
                    }

                    // CSL folder
                    string cslFolder = resourcesFolder + Path.DirectorySeparatorChar + "CSL";
                    if (Directory.Exists(cslFolder) == false) Directory.CreateDirectory(cslFolder);
                    // GA folder
                    string gaFolder = cslFolder + Path.DirectorySeparatorChar + "BB_GA";
                    if (Directory.Exists(gaFolder) == false) Directory.CreateDirectory(gaFolder);

                    // create Credits.txt
                    using (Stream data = new MemoryStream(Properties.Resources_XPLANE.XPMP2_Credits))
                    {
                        Stream file = File.Create(gaFolder + Path.DirectorySeparatorChar + "Credits.txt");
                        data.CopyTo(file);
                        file.Close();
                    }

                    // create xsb_aircraft.txt
                    using (Stream data = new MemoryStream(Properties.Resources_XPLANE.XPMP2_xsb_aircraft))
                    {
                        Stream file = File.Create(gaFolder + Path.DirectorySeparatorChar + "xsb_aircraft.txt");
                        data.CopyTo(file);
                        file.Close();
                    }

                    // C172 folder
                    string c172Folder = gaFolder + Path.DirectorySeparatorChar + "C172";
                    if (Directory.Exists(c172Folder) == false) Directory.CreateDirectory(c172Folder);

                    // create C172_r2.obj
                    using (Stream data = new MemoryStream(Properties.Resources_XPLANE.XPMP2_C172_r2))
                    {
                        Stream file = File.Create(c172Folder + Path.DirectorySeparatorChar + "C172_r2.obj");
                        data.CopyTo(file);
                        file.Close();
                    }

                    // create r2.dds
                    using (Stream data = new MemoryStream(Properties.Resources_XPLANE.XPMP2_r2))
                    {
                        Stream file = File.Create(c172Folder + Path.DirectorySeparatorChar + "r2.dds");
                        data.CopyTo(file);
                        file.Close();
                    }

                    // message
                    main.ShowMessage(Resources.strings.PluginInstalled);
                }
                else
                {
                    // message
                    main.ShowMessage(Resources.strings.LocatePluginFolder + ": " + folder);
                }
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }

        /// <summary>
        /// Install the X-Plane plugin UI
        /// </summary>
        public void InstallPluginUI()
        {
#if XPLANE
            // ask to install X-Plane plugin
            if (MessageBox.Show(Resources.strings.InstallPlugin, Main.name, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                // close simulator
                main.sim ?. Close();
                // show dialog for installing plugin
                XPlaneForm xplaneForm = new XPlaneForm(main, Settings.Default.XPlaneFolder);
                // open dialog
                if (xplaneForm.ShowDialog() == DialogResult.OK)
                {
                    // save folder
                    Settings.Default.XPlaneFolder = xplaneForm.GetFolder();
                    // get specified folder
                    InstallPlugin(Settings.Default.XPlaneFolder);
                }
            }
#endif
        }

        /// <summary>
        /// Receive an incoming message
        /// </summary>
        /// <param name="nuid">Sender nuid</param>
        /// <param name="reader">Message reader</param>
        void ReceiveMsg(IPEndPoint endPoint, LocalNode.Nuid nuid, BinaryReader reader)
        {
            try
            {
                // read data version
                short dataVersion = reader.ReadInt16();
                // check version
                if (dataVersion != DATA_VERSION)
                {
                    // message
                    main.MonitorEvent("Incorrect X-Plane plugin detected");
                    // install plugin
                    main.scheduleAskPlugin = true;
                }
                else
                {
                    // get message ID
                    MessageId messageId = (MessageId)reader.ReadByte();
                    // check message ID
                    switch (messageId)
                    {
                        case MessageId.HEARTBEAT:
                            try
                            {
                                // get version
                                version = reader.ReadInt16();
                                // get acquired planes
                                byte acquiredPlanes = reader.ReadByte();
                                // get aircraft count
                                byte aircraftCount = reader.ReadByte();

                                // check if not currently connected
                                if (IsConnected == false)
                                {
                                    // check for notification
                                    connectedNotify?.Invoke(version);
                                    // reset heartbeat
                                    heartbeatTime = 0.0;
                                    // reset resend time
                                    aircraftList[0].inUse = false;
                                    aircraftList[0].resendTime = double.MaxValue;
                                    // now connected
                                    IsConnected = true;

                                    // clean folders
                                    CleanInjectionFolders();
                                }

                                // check if not acquired aircraft
                                if (AcquiredWarning == false && acquiredPlanes == 0)
                                {
                                    // show warning
                                    main.ShowMessage(Resources.strings.AcquiredWarning);
                                    // warning displayed
                                    AcquiredWarning = true;
                                }
                                // check if not enough aircraft
                                else if (AircraftWarning == false && aircraftCount < AircraftCount)
                                {
                                    // show warning
                                    main.ShowMessage(Resources.strings.AircraftWarning);
                                    // warning displayed
                                    AircraftWarning = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read x-plane Heartbeat message. " + ex.Message);
                            }
                            break;

                        case MessageId.DISCONNECT:
                            try
                            {
                                Close();
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read x-plane Disconnect message. " + ex.Message);
                            }
                            break;

                        case MessageId.MODEL:
                            try
                            {
                                if (IsConnected)
                                {
                                    char[] str;
                                    int length;
                                    // make message
                                    byte index = reader.ReadByte();
                                    byte active = reader.ReadByte();
                                    bool user = reader.ReadByte() != 0;
                                    bool plane = reader.ReadByte() != 0;
                                    byte livery = reader.ReadByte();
                                    // read nickname
                                    str = reader.ReadChars(Network.MAX_NICKNAME_LENGTH);
                                    // find nickname length
                                    length = 0;
                                    while (length < Network.MAX_NICKNAME_LENGTH && str[length] != '\0') length++;
                                    string nickname = new String(str, 0, length);
                                    // read callsign
                                    str = reader.ReadChars(MAX_CALLSIGN_LENGTH);
                                    // find callsign length
                                    length = 0;
                                    while (length < MAX_CALLSIGN_LENGTH && str[length] != '\0') length++;
                                    string callsign = new String(str, 0, length);
                                    // read model
                                    str = reader.ReadChars(MAX_MODEL_LENGTH);
                                    // find model length
                                    length = 0;
                                    while (length < MAX_MODEL_LENGTH && str[length] != '\0') length++;
                                    string path = new String(str, 0, length);
                                    // read icao type
                                    str = reader.ReadChars(MAX_ICAOTYPE_LENGTH);
                                    // find icao type length
                                    length = 0;
                                    while (length < MAX_ICAOTYPE_LENGTH && str[length] != '\0') length++;
                                    string icaoType = new String(str, 0, length);

                                    // get model type from path
                                    string[] names = path.Split('\\', '/');
                                    // extract type from path
                                    string type = (names.Length > 1) ? names[names.Length - 2] : icaoType;

                                    // check for notification
                                    modelNotify ?. Invoke(IndexToSimId(index), user, plane, callsign, type, icaoType);
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read x-plane Model message. " + ex.Message);
                            }
                            break;

                        case MessageId.AIRCRAFT_POSITION:
                            try
                            {
                                if (IsConnected)
                                {
                                    // read index
                                    byte index = reader.ReadByte();
                                    // check index
                                    if (index < aircraftList.Count)
                                    {
                                        // read simulator time
                                        aircraftList[index].simTime = reader.ReadDouble();
                                        // read position
                                        Sim.AircraftPosition position = new Sim.AircraftPosition();
                                        Sim.Read(dataVersion, reader, ref position);
                                        // read plane state
                                        position.latitude *= Math.PI / 180.0;
                                        position.longitude *= Math.PI / 180.0;
                                        position.pitch *= -(float)(Math.PI / 180.0);
                                        position.bank *= -(float)(Math.PI / 180.0);
                                        position.heading *= (float)(Math.PI / 180.0);
                                        position.velocityZ = -position.velocityZ;
                                        position.angularVelocityX *= -(float)(Math.PI / 180.0);
                                        position.angularVelocityY *= (float)(Math.PI / 180.0);
                                        position.angularVelocityZ *= -(float)(Math.PI / 180.0);
                                        position.accelerationZ = -position.accelerationZ;
                                        // save position
                                        aircraftList[index].position = position;
                                        // check for notification
                                        aircraftPositionNotify?.Invoke(IndexToSimId(index), aircraftList[index].simTime, ref position);
                                        // set next resend
                                        aircraftList[index].resendTime = main.ElapsedTime + 1.0;
                                        // in use
                                        aircraftList[index].inUse = true;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read x-plane Update message. " + ex.Message);
                            }
                            break;

                        case MessageId.INTEGER_VARIABLE:
                            try
                            {
                                if (IsConnected && main.sim != null && main.sim.userAircraft != null)
                                {
                                    // read index
                                    byte index = reader.ReadByte();
                                    // read vuid
                                    uint vuid = reader.ReadUInt32();
                                    // read value
                                    int value = reader.ReadInt32();
                                    // process variable
                                    main.sim.userAircraft.variableSet ?. DetectInteger(vuid, value, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read x-plane INTEGER_VARIABLE message. " + ex.Message);
                            }
                            break;

                        case MessageId.FLOAT_VARIABLE:
                            try
                            {
                                if (IsConnected && main.sim != null && main.sim.userAircraft != null)
                                {
                                    // read index
                                    byte index = reader.ReadByte();
                                    // read vuid
                                    uint vuid = reader.ReadUInt32();
                                    // read value
                                    float value = reader.ReadSingle();
                                    // process variable
                                    main.sim.userAircraft.variableSet ?. DetectFloat(vuid, value, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read x-plane FLOAT_VARIABLE message. " + ex.Message);
                            }
                            break;

                        case MessageId.STRING8_VARIABLE:
                            try
                            {
                                if (IsConnected && main.sim != null && main.sim.userAircraft != null)
                                {
                                    // read index
                                    byte index = reader.ReadByte();
                                    // read vuid
                                    uint vuid = reader.ReadUInt32();
                                    // read value
                                    char[] str = reader.ReadChars(8);
                                    // find callsign length
                                    int length = 0;
                                    while (length < 8 && str[length] != '\0') length++;
                                    string value = new String(str, 0, length);
                                    // process variable
                                    main.sim.userAircraft.variableSet ?. DetectString8(vuid, value);
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read x-plane INTEGER_VARIABLE message. " + ex.Message);
                            }
                            break;

                        case MessageId.GET_DEFINITION:
                            try
                            {
                                // read vuid
                                uint vuid = reader.ReadUInt32();
                                // send definition
                                SendDefinition(vuid);
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read x-plane GET_DEFINITION message. " + ex.Message);
                            }
                            break;
                    }
                }
            }
            catch (Sim.ReadException ex)
            {
                // message
                main.MonitorEvent(ex.Message);
            }
            catch (Exception ex)
            {
                // message
                main.MonitorEvent(ex.Message);
            }
        }

        /// <summary>
        /// Network aircraft
        /// </summary>
        class NetAircraft
        {
            /// <summary>
            /// Time of next resend
            /// </summary>
            public double resendTime = double.MaxValue;
            /// <summary>
            /// latest sim time
            /// </summary>
            public double simTime = 0.0f;
            /// <summary>
            /// latest position
            /// </summary>
            public Sim.AircraftPosition position = new Sim.AircraftPosition();

            /// <summary>
            /// distance from user
            /// </summary>
            public double distance = 0.0f;

            /// <summary>
            /// Is currently in use
            /// </summary>
            public bool inUse = false;
        }

        /// <summary>
        /// List of network aircraft
        /// </summary>
        List<NetAircraft> aircraftList = new List<NetAircraft>();

        /// <summary>
        /// Convert sim ID to index
        /// </summary>
        /// <param name="simId">sim ID</param>
        /// <returns>Index</returns>
        int SimIdToIndex(uint simId)
        {
            // check for valid ID
            if (simId >= 1000 && simId < 1000 + MAX_AIRCRAFT)
            {
                // convert ID
                return (int)(simId - 1000);
            }

            // invalid ID
            return -1;
        }

        /// <summary>
        /// Convert index to sim ID
        /// </summary>
        /// <param name="index">Aircraft index</param>
        /// <returns>Sim ID</returns>
        uint IndexToSimId(int index)
        {
            // check for valid index
            if (index >= 0 && index < MAX_AIRCRAFT)
            {
                // convert index
                return (uint)(1000 + index);
            }

            // invalid index
            return uint.MaxValue;
        }

        /// <summary>
        /// Inject new aircraft
        /// </summary>
        public uint InjectAircraft(double distance)
        {
            // check if connected
            if (IsConnected)
            {
                // search for available aircraft
                for (int index = 1; index < aircraftList.Count; index++)
                {
                    // get aircraft
                    NetAircraft aircraft = aircraftList[index];
                    // check if not in use
                    if (aircraft.inUse == false)
                    {
                        // use this slot
                        aircraft.distance = distance;
                        aircraft.inUse = true;
                        return IndexToSimId(index);
                    }
                }
            }

            // failed to inject aircraft
            return uint.MaxValue;
        }

        /// <summary>
        /// Remove an aircraft from X-Plane
        /// </summary>
        public void RemoveAircraft(uint simId)
        {
            // convert to index
            int index = SimIdToIndex(simId);
            // check for valid index
            if (index >= 0)
            {
                // get aircraft
                if (aircraftList[index].inUse)
                {
                    // aircraft no longer in use
                    aircraftList[index].inUse = false;
                    // prepare heartbeat message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.REMOVE);
                    // add index
                    message.Write((byte)SimIdToIndex(simId));
                    // send message
                    localNode.Send(pluginEndPoint);
                }
            }
        }

        /// <summary>
        /// Number of aircraft in use
        /// </summary>
        uint AircraftCount
        {
            get
            {
                // aircraft count
                uint count = 0;
                // for each aircraft
                foreach (var aircraft in aircraftList)
                {
                    // check if aircraft in use
                    if (aircraft.inUse) count++;
                }
                // return result
                return count;
            }
        }

        /// <summary>
        /// Update network aircraft
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="simId"></param>
        /// <returns></returns>
        public void UpdateAircraft(uint simId, bool isUser, string nickname, string callsign, Substitution.Model model, string icaoType)
        {
            if (IsOpen && model != null)
            {
                // convert to index
                int simIndex = SimIdToIndex(simId);
                // check for valid index
                if (simIndex >= 0 && simIndex < aircraftList.Count)
                {
                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.MODEL);
                    // add sim ID
                    message.Write((byte)simIndex);
                    // add list count
                    message.Write((byte)aircraftList.Count);
                    // write user
                    message.Write((byte)(isUser ? 1 : 0));
                    // plane
                    message.Write((byte)1);
                    // livery
                    message.Write((byte)0);
                    // nickname
                    message.Write(nickname.ToCharArray());
                    for (int i = nickname.Length; i < Network.MAX_NICKNAME_LENGTH; i++)
                    {
                        // write zero character
                        message.Write('\0');
                    }
                    // callsign
                    message.Write(callsign.ToCharArray());
                    for (int i = callsign.Length; i < MAX_CALLSIGN_LENGTH; i++)
                    {
                        // write zero character
                        message.Write('\0');
                    }
                    // model
                    message.Write(model.variation.ToCharArray());
                    for (int i = model.variation.Length; i < MAX_MODEL_LENGTH; i++)
                    {
                        // write zero character
                        message.Write('\0');
                    }
                    // ICAO type
                    message.Write(model.type.ToCharArray());
                    for (int i = model.type.Length; i < MAX_ICAOTYPE_LENGTH; i++)
                    {
                        // write zero character
                        message.Write('\0');
                    }

                    // send message
                    localNode.Send(pluginEndPoint);
                }
            }
        }


        /// <summary>
        /// Update network aircraft
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="simId"></param>
        /// <returns></returns>
        public void UpdateAircraft(uint simId, float distance, double netTime, Sim.AircraftPosition position)
        {
            if (IsOpen)
            {
                // convert to index
                int index = SimIdToIndex(simId);
                // check for valid index
                if (index >= 0)
                {
                    // update distance
                    aircraftList[index].distance = distance;
                    // convert to X-Plane coordinates
                    position.latitude *= 180.0 / Math.PI;
                    position.longitude *= 180.0 / Math.PI;
                    position.pitch *= (float)(-180.0 / Math.PI);
                    position.bank *= (float)(-180.0 / Math.PI);
                    position.heading *= (float)(180.0 / Math.PI);
                    position.velocityZ = -position.velocityZ;
                    position.angularVelocityX = -position.angularVelocityX * (float)(180.0 / Math.PI);
                    position.angularVelocityY = position.angularVelocityY * (float)(180.0 / Math.PI);
                    position.angularVelocityZ = -position.angularVelocityZ * (float)(180.0 / Math.PI);
                    position.accelerationZ = -position.accelerationZ;

                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.AIRCRAFT_POSITION);
                    // add sim ID
                    message.Write((byte)index);
                    // write time
                    message.Write(netTime);
                    // write position
                    Sim.Write(message, ref position);
                    // send message
                    localNode.Send(pluginEndPoint);
                }
            }
        }


        /// <summary>
        /// Update network aircraft
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="simId"></param>
        /// <returns></returns>
        public void UpdateAircraft(uint simId, ref Sim.ObjectPosition position)
        {
            if (IsOpen)
            {
                // convert to index
                int index = SimIdToIndex(simId);
                // check for valid index
                if (index >= 0)
                {
                    // create new position
                    Sim.ObjectPosition xplanePosition = position;
                    // convert to X-Plane coordinates
                    xplanePosition.latitude *= 180.0 / Math.PI;
                    xplanePosition.longitude *= 180.0 / Math.PI;
                    xplanePosition.pitch *= (float)(-180.0 / Math.PI);
                    xplanePosition.bank *= (float)(-180.0 / Math.PI);
                    xplanePosition.heading *= (float)(180.0 / Math.PI);

                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.OBJECT_POSITION);
                    // add sim ID
                    message.Write((byte)index);
                    // write position
                    message.Write(xplanePosition.latitude);
                    message.Write(xplanePosition.longitude);
                    message.Write(xplanePosition.altitude);
                    message.Write(xplanePosition.pitch);
                    message.Write(xplanePosition.bank);
                    message.Write(xplanePosition.heading);
                    message.Write(xplanePosition.height);
                    // ground flags
                    byte flags = 0;
                    if (xplanePosition.ground != 0) flags |= 0x01;
                    if (Settings.Default.ElevationCorrection) flags |= 0x02;
                    message.Write(flags);
                    // send message
                    localNode.Send(pluginEndPoint);
                }
            }
        }


        /// <summary>
        /// Update network aircraft
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="simId"></param>
        /// <returns></returns>
        public void UpdateAircraft(uint simId, ref Sim.ObjectVelocity velocity)
        {
            if (IsOpen)
            {
                // convert to index
                int index = SimIdToIndex(simId);
                // check for valid index
                if (index >= 0)
                {
                    // create new velocity
                    Sim.ObjectVelocity xplaneVelocity = velocity;
                    // convert to X-Plane coordinates
                    xplaneVelocity.velocityZ = -velocity.velocityZ;
                    xplaneVelocity.angularVelocityX = -velocity.angularVelocityX * (float)(180.0 / Math.PI);
                    xplaneVelocity.angularVelocityY = velocity.angularVelocityY * (float)(180.0 / Math.PI);
                    xplaneVelocity.angularVelocityZ = -velocity.angularVelocityZ * (float)(180.0 / Math.PI);
                    xplaneVelocity.accelerationZ = -velocity.accelerationZ;

                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.OBJECT_VELOCITY);
                    // add sim ID
                    message.Write((byte)index);
                    // write position
                    message.Write(xplaneVelocity.velocityX);
                    message.Write(xplaneVelocity.velocityY);
                    message.Write(xplaneVelocity.velocityZ);
                    message.Write(xplaneVelocity.angularVelocityX);
                    message.Write(xplaneVelocity.angularVelocityY);
                    message.Write(xplaneVelocity.angularVelocityZ);
                    message.Write(xplaneVelocity.accelerationX);
                    message.Write(xplaneVelocity.accelerationY);
                    message.Write(xplaneVelocity.accelerationZ);
                    // send message
                    localNode.Send(pluginEndPoint);
                }
            }
        }

        /// <summary>
        /// Update network aircraft
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="simId"></param>
        /// <returns></returns>
        public void DoEvent(uint simId, Sim.Event simEvent, uint data)
        {
            if (IsOpen)
            {
                main.MonitorNetwork("SimEvent: ID=" + simId + ", Event=" + Sim.EventToString(simEvent) + ", Data=" + data);

                // convert to index
                int index = SimIdToIndex(simId);
                // check for valid index
                if (index >= 0)
                {
                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.EVENT);
                    // add index
                    message.Write((byte)index);
                    // write event
                    message.Write((short)simEvent);
                    // write data
                    message.Write(data);
                    // send message
                    localNode.Send(pluginEndPoint);
                }
            }
        }

        /// <summary>
        /// Update integer variable
        /// </summary>
        public void UpdateInteger(uint simId, uint vuid, int value)
        {
            if (IsOpen)
            {
                // convert to index
                int index = SimIdToIndex(simId);
                // check for valid index
                if (index >= 0)
                {
                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.INTEGER_VARIABLE);
                    // add sim ID
                    message.Write((byte)index);
                    // write variable ID
                    message.Write(vuid);
                    // write value
                    message.Write(value);
                    // send message
                    localNode.Send(pluginEndPoint);
                }
            }
        }

        /// <summary>
        /// Update float variable
        /// </summary>
        public void UpdateFloat(uint simId, uint vuid, float value)
        {
            if (IsOpen)
            {
                // convert to index
                int index = SimIdToIndex(simId);
                // check for valid index
                if (index >= 0)
                {
                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.FLOAT_VARIABLE);
                    // add sim ID
                    message.Write((byte)index);
                    // write variable ID
                    message.Write(vuid);
                    // write value
                    message.Write(value);
                    // send message
                    localNode.Send(pluginEndPoint);
                }
            }
        }

        /// <summary>
        /// Update string variable
        /// </summary>
        public void UpdateString8(uint simId, uint vuid, string value)
        {
            if (IsOpen)
            {
                // convert to index
                int index = SimIdToIndex(simId);
                // check for valid index
                if (index >= 0)
                {
                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(DATA_VERSION);
                    // add message ID
                    message.Write((byte)MessageId.STRING8_VARIABLE);
                    // add sim ID
                    message.Write((byte)index);
                    // write variable ID
                    message.Write(vuid);
                    // write value
                    message.Write(value.ToCharArray());
                    for (int i = value.Length; i < 8; i++)
                    {
                        // write zero character
                        message.Write('\0');
                    }
                    // send message
                    localNode.Send(pluginEndPoint);
                }
            }
        }

        /// <summary>
        /// Update integer variable
        /// </summary>
        public void SendDefinition(uint vuid)
        {
            if (IsOpen && IsConnected)
            {
                // check if vuid is known
                if (main.sim != null && main.variableMgr.definitions.ContainsKey(vuid))
                {
                    // get definition
                    VariableMgr.Definition definition = main.variableMgr.definitions[vuid];
                    // check for valid dataref
                    if (definition.drName.Length > 0)
                    {
                        // prepare message
                        BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                        // add header
                        message.Write(DATA_VERSION);
                        // add message ID
                        message.Write((byte)MessageId.DEFINITION);
                        // add vuid
                        message.Write(vuid);
                        // add scalar
                        message.Write(definition.drScalar);
                        // add index
                        message.Write(definition.drIndex);
                        // add type
                        message.Write((byte)definition.type);
                        // dataref
                        message.Write(definition.drName.ToCharArray());
                        for (int i = definition.drName.Length; i < MAX_DATAREF_LENGTH; i++)
                        {
                            // write zero character
                            message.Write('\0');
                        }
                        // send message
                        localNode.Send(pluginEndPoint);
                    }
                }
            }
        }

        /// <summary>
        /// Register a variable to receive updates
        /// </summary>
        public void RequestVariable(uint simId, uint vuid)
        {
            if (IsOpen && IsConnected)
            {
                // convert to index
                int index = SimIdToIndex(simId);
                // check for valid index
                if (index >= 0)
                {
                    // check if vuid is valid
                    if (main.sim != null && main.variableMgr.definitions.ContainsKey(vuid))
                    {
                        // get definition
                        VariableMgr.Definition definition = main.variableMgr.definitions[vuid];
                        // check for valid dataref
                        if (definition.drName.Length > 0)
                        {
                            // prepare message
                            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                            // add header
                            message.Write(DATA_VERSION);
                            // add message ID
                            message.Write((byte)MessageId.REQUEST_VARIABLE);
                            // add sim ID
                            message.Write((byte)index);
                            // add vuid
                            message.Write(vuid);
                            // add scalar
                            message.Write(definition.drScalar);
                            // add index
                            message.Write(definition.drIndex);
                            // add type
                            message.Write((byte)definition.type);
                            // dataref
                            message.Write(definition.drName.ToCharArray());
                            for (int i = definition.drName.Length; i < MAX_DATAREF_LENGTH; i++)
                            {
                                // write zero character
                                message.Write('\0');
                            }
                            // send message
                            localNode.Send(pluginEndPoint);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Remove non-alphanumeric characters 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        string ValidateCslName(string name)
        {
            // initialize result
            string result = "";

            // for each character
            foreach (var c in name)
            {
                // check for space
                if (c == ' ')
                {
                    // replace with underscore
                    result += '_';
                }
                // check for valid character
                else if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_')
                {
                    // add valid character
                    result += c;
                }
            }

            // return valid name
            return result;
        }

        /// <summary>
        /// Generate CSL for an X-Plane aircraft
        /// </summary>
        public void GenerateCsl(string simFolder, string subFolder, string acfFile, string type, string livery, bool first)
        {
            // validate names
            string validType = ValidateCslName(type);
            string validLivery = ValidateCslName(livery);

            // default ICAO type and variation
            string icaoType = "C172";
            string variation = "JFS " + validType + "_" + validLivery;

            // file streams
            StreamWriter xsbWriter = null;
            StreamWriter objWriter = null;
            StreamReader acfReader = null;
            StreamReader objReader = null;
            // white spaces
            char[] whiteSpaces = { ' ', '\t' };

            // number of objects processed
            int objCount = 0;

            // get CSL folder
            string cslFolder = Path.Combine(simFolder, "Resources", "plugins", "JoinFS", "Resources", "CSL");
            // get package folder
            string packageFolder = Path.Combine(cslFolder, validType);

            try
            {
                // create CSL folder
                if (Directory.Exists(cslFolder) == false) Directory.CreateDirectory(cslFolder);
                // create package folder
                if (Directory.Exists(packageFolder) == false) Directory.CreateDirectory(packageFolder);

                // check for first variation
                if (first)
                {
                    // open model file
                    xsbWriter = new StreamWriter(Path.Combine(packageFolder, "xsb_aircraft.txt"));
                    // write package name
                    xsbWriter.WriteLine("EXPORT_NAME " + validType);
                }
                else
                {
                    // append model file
                    xsbWriter = new StreamWriter(Path.Combine(packageFolder, "xsb_aircraft.txt"), true);
                }

                // aircraft header
                xsbWriter.WriteLine("");
                xsbWriter.WriteLine("OBJ8_AIRCRAFT " + validType + "_" + validLivery);

                // create reader
                acfReader = new StreamReader(acfFile);
                string line = "";
                double cogY = 0.0, cogZ = 0.0;

                // for each line the file
                while ((line = acfReader.ReadLine()) != null)
                {
                    // get words
                    string[] words = line.Split(whiteSpaces, StringSplitOptions.RemoveEmptyEntries);
                    // check for last word
                    if (words.Length > 0)
                    {
                        // check for cgy
                        if (line.Contains("_cgY "))
                        {
                            // get cogY
                            double.TryParse(words[words.Length - 1], NumberStyles.Number, CultureInfo.InvariantCulture, out cogY);
                            cogY *= Sim.METRES_PER_FOOT;
                        }
                        // check for cgy
                        else if (line.Contains("_cgZ "))
                        {
                            // get cogZ
                            double.TryParse(words[words.Length - 1], NumberStyles.Number, CultureInfo.InvariantCulture, out cogZ);
                            cogZ *= Sim.METRES_PER_FOOT;
                        }
                    }
                }

                // reset to start of file
                acfReader.BaseStream.Seek(0, SeekOrigin.Begin);
                // object data
                bool inObj = false;
                uint flags = 0;
                bool hidden = false;
                string objFile = "";
                double phi = 0.0f, psi = 0.0f, theta = 0.0f, objX = 0.0f, objY = 0.0f, objZ = 0.0f;
                // for each line the file
                while ((line = acfReader.ReadLine()) != null)
                {
                    // get words
                    string[] words = line.Split(whiteSpaces, StringSplitOptions.RemoveEmptyEntries);
                    // check for last word
                    if (words.Length >= 1)
                    {
                        // check for ICAO
                        if (line.Contains("_ICAO"))
                        {
                            // get type
                            icaoType = words[words.Length - 1];
                            // validate
                            if (icaoType.Length > 4) icaoType = icaoType.Substring(0, 4);
                        }
                        // check for object
                        else if (line.Contains("_obj_flags"))
                        {
                            // get flags
                            uint.TryParse(words[words.Length - 1], NumberStyles.Number, CultureInfo.InvariantCulture, out flags);
                            // now in object
                            inObj = true;
                            // reset hidden
                            hidden = false;
                        }
                        else if (line.Contains("hide_dataref"))
                        {
                            // object is hidden
                            hidden = true;
                        }
                        else if (line.Contains("att_file_stl"))
                        {
                            // get object file name
                            objFile = words[words.Length - 1];
                        }
                        else if (line.Contains("att_phi_ref"))
                        {
                            double.TryParse(words[words.Length - 1], NumberStyles.Number, CultureInfo.InvariantCulture, out phi);
                        }
                        else if (line.Contains("att_psi_ref"))
                        {
                            double.TryParse(words[words.Length - 1], NumberStyles.Number, CultureInfo.InvariantCulture, out psi);
                        }
                        else if (line.Contains("att_the_ref"))
                        {
                            double.TryParse(words[words.Length - 1], NumberStyles.Number, CultureInfo.InvariantCulture, out theta);
                        }
                        else if (line.Contains("att_x_acf_prt_ref"))
                        {
                            double.TryParse(words[words.Length - 1], NumberStyles.Number, CultureInfo.InvariantCulture, out objX);
                            objX *= Sim.METRES_PER_FOOT;
                        }
                        else if (line.Contains("att_y_acf_prt_ref"))
                        {
                            double.TryParse(words[words.Length - 1], NumberStyles.Number, CultureInfo.InvariantCulture, out objY);
                            objY *= Sim.METRES_PER_FOOT;
                        }
                        else if (line.Contains("att_z_acf_prt_ref"))
                        {
                            double.TryParse(words[words.Length - 1], NumberStyles.Number, CultureInfo.InvariantCulture, out objZ);
                            objZ *= Sim.METRES_PER_FOOT;
                            // interpret flags
                            bool inside = (flags & 0x4) != 0;
                            bool exterior = (flags & 0x10) != 0;
                            bool broken = (flags & 0x200) != 0;
                            // check if inside object and object is valid
                            if (inObj && objFile.StartsWith("..") == false && broken == false && (exterior || inside == false) && (hidden == false || objFile.IndexOf("pilot", StringComparison.OrdinalIgnoreCase) >= 0 || objFile.IndexOf("gear", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                // object file
                                string objectPath = Path.Combine(simFolder, subFolder, "objects", objFile);
                                // inject file
                                string injectPath = Path.Combine(packageFolder, validLivery, objFile);
                                // check for object file and not already injected
                                if (File.Exists(objectPath) && (main.settingsSkipCsl == false || File.Exists(injectPath) == false))
                                {
                                    // create reader
                                    objReader = new StreamReader(objectPath);
                                    // get inject folder
                                    string injectFolder = Path.GetDirectoryName(injectPath);
                                    // create inject folder
                                    if (Directory.Exists(injectFolder) == false) Directory.CreateDirectory(injectFolder);
                                    // open new object file
                                    objWriter = new StreamWriter(Path.Combine(packageFolder, injectPath));

                                    // read line
                                    while ((line = objReader.ReadLine()) != null)
                                    {
                                        // get words
                                        words = line.Split(whiteSpaces, StringSplitOptions.RemoveEmptyEntries);
                                        // check for valid line
                                        if (words.Length >= 2)
                                        {
                                            // get command
                                            string command = words[0].ToUpper();

                                            // check for vertex
                                            if (command == "VT" && words.Length >= 4)
                                            {
                                                // read position
                                                double.TryParse(words[1], NumberStyles.Number, CultureInfo.InvariantCulture, out double x);
                                                double.TryParse(words[2], NumberStyles.Number, CultureInfo.InvariantCulture, out double y);
                                                double.TryParse(words[3], NumberStyles.Number, CultureInfo.InvariantCulture, out double z);

                                                // modify position
                                                x += objX;
                                                y += objY - cogY;
                                                z += objZ - cogZ;
                                                // write new position
                                                objWriter.Write("VT\t" + x.ToString("F8", CultureInfo.InvariantCulture) + "\t" + y.ToString("F8", CultureInfo.InvariantCulture) + "\t" + z.ToString("F8", CultureInfo.InvariantCulture));
                                                // copy remaining line
                                                for (int i = 4; i < words.Length; i++) objWriter.Write("\t" + words[i]);
                                                // end of line
                                                objWriter.WriteLine();
                                            }
                                            // check for texture
                                            else if (command.Equals("TEXTURE") || command.Equals("TEXTURE_LIT") || command.Equals("TEXTURE_NORMAL"))
                                            {
                                                // texture filenames
                                                string texturePNG = Path.Combine(Path.GetDirectoryName(objectPath), words[1]);
                                                string liveryPNG = texturePNG.Replace(@"\objects", Path.Combine(@"\liveries", livery, "objects")).Replace(@"/objects", Path.Combine(@"/liveries", livery, "objects"));
                                                string textureDDS = texturePNG.Replace(".png", ".dds");
                                                string liveryDDS = liveryPNG.Replace(".png", ".dds");

                                                // get file name
                                                string textureFile = Path.GetFileName(words[1]);
                                                // copy texture
                                                if (File.Exists(liveryDDS)) File.Copy(liveryDDS, Path.Combine(injectFolder, textureFile), true);
                                                else if (File.Exists(liveryPNG)) File.Copy(liveryPNG, Path.Combine(injectFolder, textureFile), true);
                                                else if (File.Exists(textureDDS)) File.Copy(textureDDS, Path.Combine(injectFolder, textureFile), true);
                                                else if (File.Exists(texturePNG)) File.Copy(texturePNG, Path.Combine(injectFolder, textureFile), true);

                                                // copy line
                                                objWriter.WriteLine(command + " " + textureFile);
                                            }
                                            else
                                            {
                                                // copy line
                                                objWriter.WriteLine(line);
                                            }
                                        }
                                        else
                                        {
                                            // copy line
                                            objWriter.WriteLine(line);
                                        }
                                    }

                                    // close files
                                    objReader.Close();
                                    objWriter.Close();
                                }

                                // write object information
                                xsbWriter.WriteLine("OBJ8 SOLID YES " + validType + @"/" + validLivery + @"/" + objFile);
                                // increment count
                                objCount++;
                            }
                            // no longer in object
                            inObj = false;
                        }
                    }
                }

                // write refence names
                xsbWriter.WriteLine("LIVERY " + icaoType + " " + variation);
            }
            catch (Exception ex)
            {
                // error
                main.MonitorEvent("ERROR: Failed to inject object '" + subFolder + "'." + ex.Message);
            }
            finally
            {
                // close files
                if (xsbWriter != null) xsbWriter.Close();
                if (objWriter != null) objWriter.Close();
                if (acfReader != null) acfReader.Close();
                if (objReader != null) objReader.Close();
            }

            // check if no objects processed
            if (objCount == 0)
            {
                // remove folder
                if (Directory.Exists(packageFolder)) Directory.Delete(packageFolder, true);
            }
        }
    }
}