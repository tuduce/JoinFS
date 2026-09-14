using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace JoinFS
{
    public class LocalNode
    {
        #region Nodes

        /// <summary>
        /// Number of seconds before a node can expire
        /// </summary>
        const int EXPIRE_TIME = 30;
        /// <summary>
        /// Number of seconds before a link is no longer established
        /// </summary>
        const int REESTABLISH_TIME = 3;
        /// <summary>
        /// Number of seconds before a 'guaranteed in' object expires
        /// </summary>
        const int GUARANTEED_IN_EXPIRE_TIME = 240;
        /// <summary>
        /// Number of seconds before a 'guaranteed out' object expires
        /// </summary>
        const int GUARANTEED_OUT_EXPIRE_TIME = 180;
        /// <summary>
        /// Maximum nodes from any device
        /// </summary>
        const int MAX_NODES_PER_DEVICE = 32;

        /// <summary>
        /// Message header
        /// </summary>
        const short VERSION = 0x520b;

        /// <summary>
        /// Remote node
        /// </summary>
        /// <remarks>
        /// Node constructor
        /// </remarks>
        /// <param name="endPoint">Address of remote node</param>
        class Node(IPEndPoint endPoint, bool receiveEstablished)
        {
            /// <summary>
            /// Actual address of the node
            /// </summary>
            public IPEndPoint endPoint = endPoint;
            /// <summary>
            /// Route address of the node
            /// </summary>
            public IPEndPoint routeEndPoint = endPoint;
            /// <summary>
            /// Is there a direct connection to this node
            /// </summary>
            public bool Direct { get { return endPoint.Address.Equals(routeEndPoint.Address); } }
            /// <summary>
            /// Connection state of the node
            /// </summary>
            public bool sendEstablished;
            public bool receiveEstablished = receiveEstablished;
            /// <summary>
            /// Node will expire at this time
            /// </summary>
            DateTime expireTime = DateTime.UtcNow.AddSeconds(EXPIRE_TIME);
            /// <summary>
            /// Has this node expired
            /// </summary>
            public bool Expired { get { return DateTime.UtcNow > expireTime; } }
            /// <summary>
            /// This node has low bandwidth enabled
            /// </summary>
            public bool lowBandwidth;
            /// <summary>
            /// Round trip time for the node
            /// </summary>
            public float rtt;

            /// <summary>
            /// Received message from the node
            /// </summary>
            public void Received()
            {
                // update received flag
                receiveEstablished = true;
            }

            /// <summary>
            /// Sent a message from the node
            /// </summary>
            public void Responded()
            {
                // update send flag
                sendEstablished = true;
                // set new expire time
                expireTime = DateTime.UtcNow.AddSeconds(EXPIRE_TIME);
            }
        }

        /// <summary>
        /// Return the local nuid
        /// </summary>
        /// <returns>Local nuid</returns>
        public Nuid GetLocalNuid()
        {
            return localNuid;
        }

        /// <summary>
        /// Nodes
        /// </summary>
        readonly Dictionary<Nuid, Node> nodes = [];
        public int NodeCount { get { return nodes.Count; } }

        /// <summary>
        /// Get node list
        /// </summary>
        /// <returns>List of nodes</returns>
        public Nuid[] GetNodeList()
        {
            // create array of nuids
            Nuid[] nuids = new Nuid[NodeCount];
            nodes.Keys.CopyTo(nuids, 0);
            // return list
            return nuids;
        }
        
        /// <summary>
        /// Count the number of nodes from the given device
        /// </summary>
        /// <param name="nuid"></param>
        public int NodeCount_Device(Nuid nuid)
        {
            // count
            int count = Nuid.SameDevice(GetLocalNuid(), nuid) ? 1 : 0;

            // for each node
            foreach (var node in nodes)
            {
                // compare IP and local ID
                if (Nuid.SameDevice(node.Key, nuid))
                {
                    // found
                    count++;
                }
            }

            // return result
            return count;
        }

        /// <summary>
        /// Get a node address
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <param name="endPoint">End point of the node returned</param>
        /// <returns>Success</returns>
        public bool GetNodeEndPoint(Nuid nuid, out IPEndPoint endPoint)
        {
            // check for node
            if (nodes.TryGetValue(nuid, out Node value))
            {
                endPoint = value.endPoint;
                return true;
            }
            else
            {
                // invalid end point
                endPoint = new IPEndPoint(0, 0);
                // unknown nuid
                return false;
            }
        }

        /// <summary>
        /// Get a node address
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <param name="endPoint">End point of the node returned</param>
        /// <returns>Success</returns>
        public bool GetNodeRouteEndPoint(Nuid nuid, out IPEndPoint endPoint)
        {
            // check for node
            if (nodes.TryGetValue(nuid, out Node value))
            {
                endPoint = value.routeEndPoint;
                return true;
            }
            else
            {
                // invalid end point
                endPoint = new IPEndPoint(0, 0);
                // unknown nuid
                return false;
            }
        }

        /// <summary>
        /// Get a node address
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <param name="endPoint">End point of the node returned</param>
        /// <returns>Success</returns>
        public Nuid GetNodeRouteNode(Nuid nuid)
        {
            // get end point
            GetNodeEndPoint(nuid, out IPEndPoint endPoint);
            // for each node
            foreach (var node in nodes)
            {
                // check for route node
                if (node.Value.endPoint.Equals(endPoint))
                {
                    // return nuid
                    return node.Key;
                }
            }
            // not found
            return new Nuid();
        }

        /// <summary>
        /// Has the node established receiving
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <returns>Success</returns>
        public bool NodeReceiveEstablished(Nuid nuid)
        {
            // check for node
            if (nodes.TryGetValue(nuid, out Node value))
            {
                // return established
                return value.receiveEstablished;
            }
            else
            {
                // unknown nuid
                return false;
            }
        }

        /// <summary>
        /// Has the node established sending
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <returns>Success</returns>
        public bool NodeSendEstablished(Nuid nuid)
        {
            // check for node
            if (nodes.TryGetValue(nuid, out Node value))
            {
                // return established
                return value.sendEstablished;
            }
            else
            {
                // unknown nuid
                return false;
            }
        }

        /// <summary>
        /// Is the connection direct
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <returns>Success</returns>
        public bool NodeDirect(Nuid nuid)
        {
            // check for node
            if (nodes.TryGetValue(nuid, out Node value))
            {
                // return direct flag
                return value.Direct;
            }
            else
            {
                // unknown nuid
                return false;
            }
        }

        /// <summary>
        /// Get the low bandwidth flag for a node
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <returns>Low bandwidth state</returns>
        public bool NodeLowBandwidth(Nuid nuid)
        {
            // check for node
            if (nodes.TryGetValue(nuid, out Node value))
            {
                // return flag
                return value.lowBandwidth;
            }
            else
            {
                // unknown nuid
                return false;
            }
        }

        /// <summary>
        /// Get the RTT for a node
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <returns>RTT</returns>
        public float GetNodeRTT(Nuid nuid)
        {
            // check for node
            if (nodes.TryGetValue(nuid, out Node value))
            {
                // return RTT
                return value.rtt;
            }
            else
            {
                // unknown nuid
                return 9999.0f;
            }
        }

        /// <summary>
        /// Delegate for connecting to the network
        /// </summary>
        /// <param name="endPoint">Address of the new node</param>
        public delegate void ConnectComplete();
        public ConnectComplete connectComplete;

        /// <summary>
        /// Delegate for a node joining the network
        /// </summary>
        /// <param name="endPoint">Address of the new node</param>
        public delegate void NodeJoin(Nuid nuid, IPEndPoint endPoint);
        public NodeJoin nodeJoin;

        /// <summary>
        /// Delegate for a node routing
        /// </summary>
        public delegate void NodeRoute(Nuid nuid, Nuid routeNuid);
        public NodeRoute nodeRoute;

        /// <summary>
        /// Delegate for a node connection established
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        public delegate void NodeEstablished(Nuid nuid);
        public NodeEstablished nodeEstablished;

        /// <summary>
        /// Delegate for node leaving the network
        /// </summary>
        /// <param name="endPoint">Address of the node</param>
        public delegate void NodeLeave(Nuid nuid);
        public NodeLeave nodeLeave;

        /// <summary>
        /// Delegate for errors
        /// </summary>
        /// <param name="endPoint">Error message</param>
        public delegate void NodeError(string error);
        public NodeError nodeError;

        /// <summary>
        /// Delegate for debug
        /// </summary>
        /// <param name="endPoint">Debug message</param>
        public delegate void NodeDebug(string debug);
        public NodeDebug nodeDebug;

        /// <summary>
        /// Register a remote node
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <param name="endPoint">Address of the node</param>
        void RegisterNode(Nuid nuid, ushort port, bool receive, bool direct)
        {
            // check for valid nuid and that this node is not registering itself and maximum device nodes
            if (nuid.Valid() && nuid != localNuid && NodeCount_Device(nuid) < MAX_NODES_PER_DEVICE)
            {
                // check for first contact
                bool firstContact = false;

                // check if node exists
                if (nodes.ContainsKey(nuid))
                {
                    // check for direct receive
                    if (receive)
                    {
                        // check for first contact
                        if (nodes[nuid].receiveEstablished == false) firstContact = true;
                        // now received
                        nodes[nuid].Received();
                        // check if direct
                        if (direct)
                        {
                            // update port
                            nodes[nuid].endPoint.Port = port;
                        }
                    }
                    nodeDebug?.Invoke("NETWORK: RegisterNode update " + nuid + " " + port + " " + receive + " " + direct + " " + firstContact);
                }
                else
                {
                    // check for first contact
                    if (receive) firstContact = true;

                    // create new node
                    Node newNode = new(MakeEndPoint(nuid, port), receive);

                    // check if nuid is already used
                    if (nodes.ContainsKey(nuid) && nodeError != null)
                    {
                        // error message
                        nodeError?.Invoke("Duplicate network ID detected.");
                    }

                    // add node to the connected list
                    nodes[nuid] = newNode;
                    nodeDebug?.Invoke("NETWORK: RegisterNode new " + nuid + " " + port + " " + receive + " " + direct);
                }

                // check for first contact with node
                if (firstContact)
                {
                    // notify application
                    nodeJoin?.Invoke(nuid, nodes[nuid].endPoint);

                    // prepare message
                    PrepareInternalMessage(new Nuid(), true);
                    // add message ID
                    sendWriter.Write((short)MESSAGE_ID.AddNode);
                    // add suid
                    sendWriter.Write(suid);
                    // add nuid
                    nuid.Write(sendWriter);
                    sendWriter.Write((ushort)nodes[nuid].endPoint.Port);
                    // broadcast message
                    Broadcast();
                }
            }
        }

#endregion

#region Messages
        /// <summary>
        /// General purpose message buffer
        /// </summary>
        readonly MemoryStream sendBuffer;
        readonly BinaryWriter sendWriter;
        readonly MemoryStream receiveBuffer;
        readonly BinaryReader receiveReader;

        /// <summary>
        /// Message offsets
        /// </summary>
        const int VERSION_OFFSET = 0;
        const int FLAGS_OFFSET = VERSION_OFFSET + 2;
        const int GUARANTEED_ID_OFFSET = FLAGS_OFFSET + 1;
        const int GUARANTEED_INDEX_OFFSET = GUARANTEED_ID_OFFSET + 2;
        const int GUARANTEED_COUNT_OFFSET = GUARANTEED_INDEX_OFFSET + 1;
        const int SENDER_OFFSET = GUARANTEED_COUNT_OFFSET + 1;
        const int RECIPIENT_OFFSET = SENDER_OFFSET + 7;
        const int DATA_OFFSET = RECIPIENT_OFFSET + 7;

        /// <summary>
        ///  Message flag bit masks
        /// </summary>
        const byte FLAG_INTERNAL = 0x01;
        const byte FLAG_GUARANTEED = 0x02;
        const byte FLAG_FORWARD = 0x04;

        /// <summary>
        ///  Pulse flags bit masks
        /// </summary>
        const byte FLAG_LOW_BANDWIDTH = 0x01;

        /// <summary>
        /// Internal messages
        /// </summary>
        enum MESSAGE_ID
        {
            Join,
            JoinReply,
            AddNode,
            Leave,
            Pulse,
            PulseResponse,
            GuaranteedDone,
            AddNodes,
            Pathfinder,
            PathfinderResponse,
            JoinFail,
            Login,
            LoginFail,
        }

        /// <summary>
        /// Recipient of the current send
        /// </summary>
        Nuid sendRecipient;
        /// <summary>
        /// Is the current send guaranteed
        /// </summary>
        bool sendGuaranteed;
        /// <summary>
        /// Guaranteed Id of the current send
        /// </summary>
        ushort sendGuaranteedId;

        /// <summary>
        /// Start a new message writer for internal messages
        /// </summary>
        void PrepareInternalMessage(Nuid recipient, bool guaranteed)
        {
            // save current send information
            sendRecipient = recipient;
            sendGuaranteed = guaranteed;
            // check for guaranteed send
            if (guaranteed)
            {
                // set next ID
                sendGuaranteedId = nextGuaranteedId++;
            }
            else
            {
                // null ID
                sendGuaranteedId = (ushort)0;
            }

            // reset buffer
            sendBuffer.SetLength(0);

            // add header
            sendWriter.Write(VERSION);

            // add message flags
            byte flags = 0x00;
            flags |= FLAG_INTERNAL;
            if (sendGuaranteed)
            {
                flags |= FLAG_GUARANTEED;
            }
            sendWriter.Write(flags);
            // add guaranteed id
            sendWriter.Write(sendGuaranteedId);
            // add guaranteed index
            sendWriter.Write((byte)0);
            // add guaranteed count
            sendWriter.Write((byte)1);
            // add sender nuid
            localNuid.Write(sendWriter);
            // add recipient nuid
            sendRecipient.Write(sendWriter);
        }

        /// <summary>
        /// Obtain the send stream writer for writing a message to send
        /// </summary>
        /// <returns>A binary writer for building a message</returns>
        public BinaryWriter PrepareMessage(Nuid recipient, bool guaranteed)
        {
            // save current send information
            sendRecipient = recipient;
            sendGuaranteed = guaranteed;
            // check for guaranteed send
            if (guaranteed)
            {
                // set next ID
                sendGuaranteedId = nextGuaranteedId++;
            }
            else
            {
                // null ID
                sendGuaranteedId = (ushort)0;
            }

            // reset buffer
            sendBuffer.SetLength(0);

            // add header
            sendWriter.Write(VERSION);

            // add message flags
            byte flags = 0;
            if (sendGuaranteed)
            {
                flags |= FLAG_GUARANTEED;
            }
            sendWriter.Write(flags);
            // add guaranteed id
            sendWriter.Write(sendGuaranteedId);
            // add guaranteed index
            sendWriter.Write((byte)0);
            // add guaranteed count
            sendWriter.Write((byte)1);
            // add sender nuid
            localNuid.Write(sendWriter);
            // add recipient nuid
            sendRecipient.Write(sendWriter);

            // return the send writer
            return sendWriter;
        }

        /// <summary>
        /// Send data packet to an end point
        /// </summary>
        /// <param name="endPoint">Other node</param>
        /// <param name="data">Data to send</param>
        /// <param name="length">Number of bytes to send</param>
        void Send(IPEndPoint endPoint, byte[] data, int length)
        {
            // check if open and length
            if (endPoint != null && IsOpen && length >= DATA_OFFSET)
            {
                // overwrite recipient part of the header
                sendBuffer.Position = RECIPIENT_OFFSET;
                sendRecipient.Write(sendWriter);
                // check for guaranteed message
                if (sendGuaranteed)
                {
                    IPEndPoint nodeEndPoint;
                    // get node endpoint
                    if (sendRecipient.Valid() && nodes.TryGetValue(sendRecipient, out Node value))
                    {
                        // get endpoint
                        nodeEndPoint = value.routeEndPoint;
                    }
                    else
                    {
                        // use direct endpoint
                        nodeEndPoint = endPoint;
                    }
                    // add to list of guaranteed messages
                    guaranteedOutList.Add(new GuaranteedMessageOut(sendGuaranteedId, sendRecipient, nodeEndPoint, data, length));
                }
                else
                {
                    try
                    {
                        // Used for local testing
                        // check if the endPoint is the IP 192.168.1.115
                        //if (endPoint.Address.ToString() == "192.168.1.115")
                        //{
                        //    // handle specific case for IP 192.168.1.115
                        //    nodeError?.Invoke("No message to " + endPoint.ToString());
                        //    return;
                        //}

                        // send data
                        udpClient.Send(data, length, endPoint);
                    }
                    catch (Exception ex)
                    {
                        // error
                        nodeError?.Invoke(ex.Message + ", " + endPoint.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Send current message to an end point
        /// </summary>
        /// <param name="endPoint">Other node</param>
        public void Send(IPEndPoint endPoint)
        {
            Send(endPoint, sendBuffer.GetBuffer(), (int)sendBuffer.Length);
        }

        /// <summary>
        /// Send current message to another node
        /// </summary>
        /// <param name="endPoint">Other node</param>
        public void Send(Nuid nuid)
        {
            // set recipient
            sendRecipient = nuid;
            // find node
            if (nodes.TryGetValue(nuid, out Node value))
            {
                // send to end point
                Send(value.routeEndPoint);
            }
            else
            {
                // send to end point
                Send(MakeEndPoint(nuid, nuid.port));
            }
        }

        /// <summary>
        /// Broadcast message to all nodes
        /// </summary>
        /// <param name="data">Message</param>
        /// <param name="length">Number of bytes to send</param>
        void Broadcast(byte[] data, int length)
        {
            // for each node
            foreach (var node in nodes)
            {
                // set recipient
                sendRecipient = node.Key;
                // send to other node
                Send(node.Value.routeEndPoint, data, length);
            }
        }

        /// <summary>
        /// Broadcast current message to all nodes
        /// </summary>
        public void Broadcast()
        {
            Broadcast(sendBuffer.GetBuffer(), (int)sendBuffer.Length);
        }

        /// <summary>
        /// Notification of message received
        /// </summary>
        /// <param name="nuid">ID of the node</param>
        /// <param name="reader">Message reader</param>
        public delegate void ReceiveNotify(IPEndPoint endPoint, Nuid nuid, BinaryReader reader);
        public ReceiveNotify receiveNotify;

        // list of banned IP addresses
        readonly List<IPAddress> banList = [];

        /// <summary>
        /// Add to ban list
        /// </summary>
        /// <param name="ip">IP address</param>
        public void BanIP(string ip)
        {
            // check IP address
            if (IPAddress.TryParse(ip, out IPAddress address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // add to list
                banList.Add(address);
            }
        }

        /// <summary>
        /// Process a message from another node
        /// </summary>
        /// <param name="endPoint">Address of node that sent the message</param>
        void ReceiveMsg(IPEndPoint endPoint)
        {
#if DEBUG // For blocking specified ports to test re-routing
            //            if (port == 6113 && endPoint.Port == 6114)
            //                return;
#endif

            // check if address is banned
            if (banList.Find(a => a.Equals(endPoint.Address)) != null)
            {
                // ignore
                return;
            }

            try
            {
                // update stat
                Stats.Total.Record(receiveBuffer.Length);
                // check version
                if (receiveReader.ReadInt16() == VERSION)
                {
                    // extract flags
                    byte flags = receiveReader.ReadByte();
                    bool direct = (flags & FLAG_FORWARD) == 0;
                    // extract guaranteed data
                    ushort guaranteedId = receiveReader.ReadUInt16();
                    byte guaranteedIndex = receiveReader.ReadByte();
                    byte guaranteedCount = receiveReader.ReadByte();

                    // extract sender nuid
                    Nuid senderNuid = new(receiveReader);
                    // extract recipient nuid
                    Nuid recipientNuid = new(receiveReader);

                    // check for sender node
                    Node senderNode = null;
                    // check if sender is known
                    if (nodes.TryGetValue(senderNuid, out Node value))
                    {
                        // get node
                        senderNode = value;
                    }

                    // check if sending is established
                    if (senderNode != null && senderNode.sendEstablished)
                    {
                        // use the correct route
                        endPoint = senderNode.routeEndPoint;
                    }

                    // ignore messages sent from this node. It should not happen
#if DEBUG
                    if (false)
#else
                    if (localNuid.Valid() && senderNuid == localNuid)
#endif
                    {
                        // do nothing
                    }
                    // check if this node is not the recipient
                    else if (recipientNuid != localNuid && recipientNuid.Valid() && localNuid.Valid())
                    {
                        // check if message has not already been forwarded
                        if (direct)
                        {
                            // check fowarding allowed and for recipient
                            if (lowBandwidth == false && nodes.TryGetValue(recipientNuid, out Node value1) && value1.Direct && (routingNodes.Count < MAX_ROUTING_NODES || routingNodes.ContainsKey(senderNuid)))
                            {
                                // set recipient
                                sendRecipient = recipientNuid;
                                // get message data
                                byte[] data = receiveBuffer.GetBuffer();
                                // set forward flag
                                data[FLAGS_OFFSET] |= FLAG_FORWARD;
                                // forward the message to the recipient
                                Send(value1.endPoint, data, (int)receiveBuffer.Length);

                                // set routing node expiry
                                routingNodes[senderNuid] = DateTime.Now.AddSeconds(5);
                                nodeDebug?.Invoke("NETWORK: Forwarded " + senderNuid + " " + recipientNuid);
                            }
                        }
                    }
                    else
                    {
                        // check for guaranteed message
                        if ((flags & FLAG_GUARANTEED) != 0)
                        {
                            // prepare message
                            PrepareInternalMessage(senderNuid, false);

                            // add message ID
                            sendWriter.Write((short)MESSAGE_ID.GuaranteedDone);
                            // add guaranteed ID
                            sendWriter.Write(guaranteedId);
                            // add guaranteed index
                            sendWriter.Write(guaranteedIndex);

                            // send message
                            Send(endPoint);

                            // get endpoint
                            IPEndPoint nodeEndPoint;
                            if (senderNode != null)
                            {
                                // use node endpoint
                                nodeEndPoint = senderNode.endPoint;
                            }
                            else
                            {
                                // use direct endpoint
                                nodeEndPoint = endPoint;
                            }

#if DEBUG_GUARANTEED
                            if (nodeError != null)
                            {
                                nodeError("GUARANTEED IN: DONE " + guaranteedId + " " + guaranteedIndex + " " + endPoint);
                            }
#endif
                            // get in message
                            GuaranteedIn guaranteedIn = guaranteedInList.Find(g => g.nodeEndPoint.Equals(nodeEndPoint) && g.id == guaranteedId);
                            // check if not yet known
                            if (guaranteedIn == null)
                            {
                                // create incoming guaranteed message
                                guaranteedIn = new GuaranteedIn(nodeEndPoint, guaranteedId, guaranteedCount);
                                // add to list
                                guaranteedInList.Add(guaranteedIn);
                            }
                            // check if message has already been processed
                            if (guaranteedIn.Done)
                            {
                                // quit processing this message
                                return;
                            }

                            // check if only single segment
                            if (guaranteedCount == 1)
                            {
                                // finished with message
                                guaranteedIn.Finish();
#if DEBUG_GUARANTEED
                                if (nodeError != null)
                                {
                                    nodeError("GUARANTEED IN: FINISH " + guaranteedId + " " + guaranteedIndex + " " + endPoint);
                                }
#endif
                            }
                            else
                            {
                                // check if segment not received
                                if (guaranteedIndex < guaranteedIn.segments.Length && guaranteedIn.segments[guaranteedIndex] == null)
                                {
                                    // create segment
                                    guaranteedIn.segments[guaranteedIndex] = new GuaranteedIn.Segment(receiveBuffer.GetBuffer(), (int)receiveBuffer.Length);
                                }

                                // check if message complete
                                if (guaranteedIn.Complete)
                                {
                                    // clear receive buffer
                                    receiveBuffer.SetLength(0);
                                    // paste header into completed message
                                    receiveBuffer.Write(guaranteedIn.segments[0].data, 0, DATA_OFFSET);
                                    // for each segment
                                    foreach (var segment in guaranteedIn.segments)
                                    {
                                        // paste segment into completed message
                                        receiveBuffer.Write(segment.data, DATA_OFFSET, segment.data.Length - DATA_OFFSET);
                                    }
                                    // go to start of stream
                                    receiveReader.BaseStream.Seek(DATA_OFFSET, SeekOrigin.Begin);
                                    // finished with message
                                    guaranteedIn.Finish();
#if DEBUG_GUARANTEED
                                    if (nodeError != null)
                                    {
                                        nodeError("GUARANTEED IN: FINISH " + guaranteedId + " " + guaranteedIndex + " " + endPoint);
                                    }
#endif
                                }
                                else
                                {
                                    // not complete yet
                                    return;
                                }
                            }
                        }

                        // check for application message
                        if ((flags & FLAG_INTERNAL) == 0)
                        {
                            // notify application
                            receiveNotify?.Invoke(endPoint, senderNuid, receiveReader);
                        }
                        else
                        {
                            // read message ID
                            short messageId = receiveReader.ReadInt16();

                            // handle particular message
                            switch ((MESSAGE_ID)messageId)
                            {
                                case MESSAGE_ID.Join:
                                    try
                                    {
                                        // update stat
                                        Stats.Join.Record(receiveBuffer.Length);
                                        nodeDebug?.Invoke("NETWORK: Join Message - " + senderNuid + " - " + endPoint);
                                        // check if connected and maximum nodes per device
                                        if (Connected && AllowJoin)
                                        {
                                            // check device count
                                            if (NodeCount_Device(senderNuid) >= MAX_NODES_PER_DEVICE)
                                            {
                                                nodeDebug?.Invoke("NETWORK: Exceeded MAX_NODES_PER_DEVICE - " + senderNuid + " - " + endPoint);
                                            }
                                            else
                                            {
                                                // get password hash
                                                uint joinPasswordHash = 0;
                                                try
                                                {
                                                    // get password hash
                                                    joinPasswordHash = receiveReader.ReadUInt32();
                                                }
                                                catch { }
                                                // check if password enabled and password is incorrect
                                                if (passwordHash != 0 && joinPasswordHash != passwordHash)
                                                {
                                                    // prepare message
                                                    PrepareInternalMessage(new Nuid(), true);
                                                    // add message ID
                                                    sendWriter.Write((short)MESSAGE_ID.JoinFail);
                                                    // add result
                                                    sendWriter.Write((byte)JoinResult.PasswordRequired);
                                                    // send message
                                                    Send(endPoint);
                                                }
                                                else if (LoginRequired)
                                                {
                                                    // prepare message
                                                    PrepareInternalMessage(new Nuid(), true);
                                                    // add message ID
                                                    sendWriter.Write((short)MESSAGE_ID.JoinFail);
                                                    // add result
                                                    sendWriter.Write((byte)JoinResult.LoginRequired);
                                                    // send message
                                                    Send(endPoint);
                                                }
                                                else
                                                {
                                                    // prepare message
                                                    PrepareInternalMessage(new Nuid(), true);
                                                    // add message ID
                                                    sendWriter.Write((short)MESSAGE_ID.JoinReply);
                                                    // write suid
                                                    sendWriter.Write(suid);
                                                    // save buffer position
                                                    long countPosition = sendBuffer.Position;
                                                    // write placeholder count
                                                    ushort count = 0;
                                                    sendWriter.Write(count);
                                                    // for each node
                                                    foreach (var otherNode in nodes)
                                                    {
                                                        // check if other node has sent something to this node
                                                        if (otherNode.Value.receiveEstablished)
                                                        {
                                                            // add nuid
                                                            otherNode.Key.Write(sendWriter);
                                                            sendWriter.Write((ushort)otherNode.Value.endPoint.Port);
                                                            // update count
                                                            count++;
                                                        }
                                                    }
                                                    // modify count
                                                    sendBuffer.Position = countPosition;
                                                    sendWriter.Write(count);
                                                    // send message
                                                    Send(endPoint);
                                                    // register the node
                                                    RegisterNode(senderNuid, (ushort)endPoint.Port, true, direct);
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read Join message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.JoinReply:
                                    try
                                    {
                                        // update stat
                                        Stats.JoinReply.Record(receiveBuffer.Length);
                                        // check if active
                                        if (active)
                                        {
                                            // read suid
                                            uint remoteSuid = receiveReader.ReadUInt32();
                                            // check if currently not in a session
                                            if (Connected == false)
                                            {
                                                // set session ID
                                                suid = remoteSuid;
                                                // notify application
                                                connectComplete?.Invoke();
                                            }

                                            // check that message is from same session
                                            if (remoteSuid == suid)
                                            {
                                                // read number of nodes
                                                int count = receiveReader.ReadInt16();
                                                // for each node
                                                for (int i = 0; i < count; i++)
                                                {
                                                    // read nuid
                                                    Nuid nuid = new(receiveReader);
                                                    ushort port = receiveReader.ReadUInt16();
                                                    // check that the ID is not for this node
                                                    if (nuid != localNuid)
                                                    {
                                                        // register node
                                                        RegisterNode(nuid, port, false, false);
                                                    }
                                                }

                                                // register the node
                                                RegisterNode(senderNuid, (ushort)endPoint.Port, true, direct);
                                            }
                                            nodeDebug?.Invoke("NETWORK: JoinReply " + senderNuid + " " + remoteSuid);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read JoinReply message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.Leave:
                                    try
                                    {
                                        // update stat
                                        Stats.Leave.Record(receiveBuffer.Length);
                                        // check if connected
                                        if (Connected)
                                        {
                                            // read suid
                                            uint remoteSuid = receiveReader.ReadUInt32();

                                            // check session
                                            if (remoteSuid == suid)
                                            {
                                                // remove node
                                                removeList.Add(senderNuid);
                                                // remove routing node
                                                removeRoutingList.Add(senderNuid);
                                            }
                                            nodeDebug?.Invoke("NETWORK: Leave " + senderNuid + " " + remoteSuid);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read Leave message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.AddNode:
                                    try
                                    {
                                        // update stat
                                        Stats.AddNode.Record(receiveBuffer.Length);
                                        // check if connected
                                        if (Connected)
                                        {
                                            // read suid
                                            uint remoteSuid = receiveReader.ReadUInt32();

                                            // check session
                                            if (remoteSuid == suid)
                                            {
                                                // read nuid
                                                Nuid nuid = new(receiveReader);
                                                ushort port = receiveReader.ReadUInt16();
                                                // register node
                                                RegisterNode(nuid, port, false, false);
                                                nodeDebug?.Invoke("NETWORK: AddNode " + senderNuid + " " + remoteSuid + " " + nuid + " " + port);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read AddNode message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.Pulse:
                                    try
                                    {
                                        // update stat
                                        Stats.Pulse.Record(receiveBuffer.Length);
                                        // check if connected
                                        if (Connected)
                                        {
                                            // read suid
                                            uint remoteSuid = receiveReader.ReadUInt32();
                                            // read time
                                            long time = receiveReader.ReadInt64();
                                            // read pulse flags
                                            byte pulseFlags = receiveReader.ReadByte();

                                            // check for correct session
                                            if (remoteSuid == suid)
                                            {
                                                // check for valid sender
                                                if (senderNode != null)
                                                {
                                                    // set low bandwidth flag
                                                    senderNode.lowBandwidth = ((pulseFlags & FLAG_LOW_BANDWIDTH) != 0);
                                                }

                                                // register the node
                                                RegisterNode(senderNuid, (ushort)endPoint.Port, true, direct);

                                                // prepare message
                                                PrepareInternalMessage(senderNuid, false);
                                                // add message ID
                                                sendWriter.Write((short)MESSAGE_ID.PulseResponse);
                                                // add time
                                                sendWriter.Write(time);
                                                // send message
                                                Send(endPoint);
                                                nodeDebug?.Invoke("NETWORK: Pulse " + senderNuid + " " + endPoint);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read Pulse message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.PulseResponse:
                                    try
                                    {
                                        // update stat
                                        Stats.PulseResponse.Record(receiveBuffer.Length);
                                        // check if connected
                                        if (Connected)
                                        {
                                            // check if target exists
                                            if (nodes.TryGetValue(senderNuid, out Node node))
                                            {
                                                // read time
                                                long time = receiveReader.ReadInt64();
                                                // update RTT
                                                node.rtt = (Stopwatch.GetTimestamp() - time) / (float)Stopwatch.Frequency;
                                                // register the node
                                                RegisterNode(senderNuid, (ushort)endPoint.Port, true, direct);

                                                // check for initial connection
                                                if (node.sendEstablished == false)
                                                {
                                                    // call application
                                                    nodeEstablished?.Invoke(senderNuid);
                                                }
                                                // send has been acknowledged
                                                node.Responded();
                                                nodeDebug?.Invoke("NETWORK: PulseResponse " + senderNuid + " " + endPoint);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read PulseResponse message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.GuaranteedDone:
                                    try
                                    {
                                        // update stat
                                        Stats.GuaranteedDone.Record(receiveBuffer.Length);
                                        // get id
                                        ushort doneId = receiveReader.ReadUInt16();
                                        // get index
                                        byte doneIndex = receiveReader.ReadByte();

                                        // find guaranteed message
                                        int index = guaranteedOutList.FindIndex(g => g.id == doneId);
                                        // check if guaranteed message still exists
                                        if (index >= 0 && doneIndex < guaranteedOutList[index].segmentList.Count)
                                        {
                                            // now received the segment
                                            guaranteedOutList[index].segmentList[doneIndex].received = true;
                                            // check if the whole message has been received
                                            if (guaranteedOutList[index].Received)
                                            {
                                                // remove guaranteed message
                                                guaranteedOutList.RemoveAt(index);
                                            }
                                        }

#if DEBUG_GUARANTEED
                                        if (nodeError != null)
                                        {
                                            nodeError("GUARANTEED OUT: DONE " + doneId + " " + doneIndex + " " + endPoint);
                                        }
#endif
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read GuaranteedDone message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.Pathfinder:
                                    try
                                    {
                                        // update stat
                                        Stats.Pathfinder.Record(receiveBuffer.Length);
                                        // check if direct connection
                                        if (Connected && direct)
                                        {
                                            // read suid
                                            uint remoteSuid = receiveReader.ReadUInt32();

                                            // check session
                                            if (remoteSuid == suid)
                                            {
                                                // register the node
                                                RegisterNode(senderNuid, (ushort)endPoint.Port, true, direct);

                                                // read number of nodes
                                                int readCount = receiveReader.ReadInt16();

                                                // prepare message
                                                PrepareInternalMessage(new Nuid(), false);
                                                // add message ID
                                                sendWriter.Write((short)MESSAGE_ID.PathfinderResponse);
                                                // add suid
                                                sendWriter.Write(suid);
                                                // save buffer position
                                                long countPosition = sendBuffer.Position;
                                                // count number of nodes to write
                                                ushort writeCount = 0;
                                                // placeholder count
                                                sendWriter.Write(writeCount);
                                                // for each node
                                                for (int i = 0; i < readCount; i++)
                                                {
                                                    // read nuid
                                                    Nuid nuid = new(receiveReader);
                                                    // check for this node
                                                    if (nuid == localNuid)
                                                    {
                                                        // add target nuid
                                                        nuid.Write(sendWriter);
                                                        // update count
                                                        writeCount++;
                                                    }
                                                    // check for node
                                                    else if (nodes.ContainsKey(nuid))
                                                    {
                                                        // check for direct connection and not exceeded routing limit
                                                        if (nodes[nuid].sendEstablished && nodes[nuid].Direct && routingNodes.Count + writeCount < MAX_ROUTING_NODES)
                                                        {
                                                            // add target nuid
                                                            nuid.Write(sendWriter);
                                                            // update count
                                                            writeCount++;
                                                        }
                                                    }
                                                }
                                                // check connected nodes
                                                if (writeCount > 0)
                                                {
                                                    // update count
                                                    sendBuffer.Position = countPosition;
                                                    sendWriter.Write(writeCount);
                                                    // send response
                                                    Send(endPoint);
                                                }
                                                nodeDebug?.Invoke("NETWORK: PathFinder " + senderNuid + " " + endPoint + " " + readCount + " " + writeCount);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read PathFinder message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.PathfinderResponse:
                                    try
                                    {
                                        // update stat
                                        Stats.PathfinderResponse.Record(receiveBuffer.Length);
                                        // check if direct connection
                                        if (Connected && direct)
                                        {
                                            // read suid
                                            uint remoteSuid = receiveReader.ReadUInt32();

                                            // check session
                                            if (remoteSuid == suid)
                                            {
                                                // register the node
                                                RegisterNode(senderNuid, (ushort)endPoint.Port, true, direct);

                                                // read number of nodes
                                                int readCount = receiveReader.ReadInt16();

                                                // for each node
                                                for (int i = 0; i < readCount; i++)
                                                {
                                                    // read nuid
                                                    Nuid nuid = new(receiveReader);
                                                    // check for node
                                                    if (nodes.ContainsKey(nuid))
                                                    {
                                                        // check for direct connection
                                                        if (nuid == senderNuid)
                                                        {
                                                            // direct
                                                            nodes[nuid].routeEndPoint = nodes[nuid].endPoint;
                                                            // node responded
                                                            nodes[nuid].Responded();
                                                            nodeDebug?.Invoke("NETWORK: PathFinderResponse Direct " + senderNuid + " " + nodes[nuid].endPoint);
                                                        }
                                                        // check if still not established
                                                        else if (nodes[nuid].sendEstablished == false)
                                                        {
                                                            // route via sender
                                                            nodes[nuid].routeEndPoint = endPoint;
                                                            // node responded
                                                            nodes[nuid].Responded();
                                                            nodeDebug?.Invoke("NETWORK: PathFinderResponse Indirect " + senderNuid + " " + endPoint);
                                                        }
                                                    }
                                                }
                                                nodeDebug?.Invoke("NETWORK: PathFinderResponse " + senderNuid + " " + readCount);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read PathFinderResponse message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.JoinFail:
                                    try
                                    {
                                        // update stat
                                        Stats.JoinFail.Record(receiveBuffer.Length);
                                        // read result
                                        ActiveJoinResult = (JoinResult)receiveReader.ReadByte();
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read JoinFail message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.Login:
                                    try
                                    {
                                        // update stat
                                        Stats.Login.Record(receiveBuffer.Length);
                                        nodeDebug?.Invoke("NETWORK: Login Message - " + senderNuid + " - " + endPoint);
                                        // check for maximum connections from one device
                                        if (NodeCount_Device(senderNuid) >= MAX_NODES_PER_DEVICE)
                                        {
                                            nodeDebug?.Invoke("NETWORK: Exceeded MAX_NODES_PER_DEVICE - " + senderNuid + " - " + endPoint);
                                        }
                                        // check if connected and maximum nodes per device
                                        else if (Connected && Creator && LoginRequired)
                                        {
                                            // email address
                                            System.Net.Mail.MailAddress address = null;
                                            try
                                            {
                                                // read email
                                                address = new System.Net.Mail.MailAddress(receiveReader.ReadString());
                                            }
                                            catch
                                            {
                                                // add message ID
                                                sendWriter.Write((short)MESSAGE_ID.LoginFail);
                                                // add result
                                                sendWriter.Write((byte)LoginResult.InvalidAddress);
                                                // send message
                                                Send(endPoint);
                                                // finish
                                                break;
                                            }

                                            // read password hash
                                            uint hash = receiveReader.ReadUInt32();
                                            // read verify flag
                                            bool verify = receiveReader.ReadBoolean();

                                            // prepare message
                                            PrepareInternalMessage(new Nuid(), true);

                                            // check for invalid address
                                            if (credentials.ContainsKey(address) == false)
                                            {
                                                // add message ID
                                                sendWriter.Write((short)MESSAGE_ID.LoginFail);
                                                // add result
                                                sendWriter.Write((byte)LoginResult.InvalidAddress);
                                                // send message
                                                Send(endPoint);
                                            }
                                            // check for empty password
                                            else if (verify == false && credentials[address] == 0)
                                            {
                                                // add message ID
                                                sendWriter.Write((short)MESSAGE_ID.LoginFail);
                                                // add reason
                                                sendWriter.Write((byte)LoginResult.VerifyPassword);
                                                // send message
                                                Send(endPoint);
                                            }
                                            else if (verify == false && credentials[address] != hash)
                                            {
                                                // add message ID
                                                sendWriter.Write((short)MESSAGE_ID.LoginFail);
                                                // add result
                                                sendWriter.Write((byte)LoginResult.InvalidPassword);
                                                // send message
                                                Send(endPoint);
                                            }
                                            else
                                            {
                                                // check if address is verified
                                                if (verify)
                                                {
                                                    // update credentials
                                                    credentials[address] = hash;
                                                    // save credentials
                                                    SaveCredentials();
                                                }

                                                // add message ID
                                                sendWriter.Write((short)MESSAGE_ID.JoinReply);
                                                // write suid
                                                sendWriter.Write(suid);
                                                // save buffer position
                                                long countPosition = sendBuffer.Position;
                                                // write placeholder count
                                                ushort count = 0;
                                                sendWriter.Write(count);
                                                // for each node
                                                foreach (var otherNode in nodes)
                                                {
                                                    // check if other node has sent something to this node
                                                    if (otherNode.Value.receiveEstablished)
                                                    {
                                                        // add nuid
                                                        otherNode.Key.Write(sendWriter);
                                                        sendWriter.Write((ushort)otherNode.Value.endPoint.Port);
                                                        // update count
                                                        count++;
                                                    }
                                                }
                                                // modify count
                                                sendBuffer.Position = countPosition;
                                                sendWriter.Write(count);
                                                // send message
                                                Send(endPoint);
                                                // register the node
                                                RegisterNode(senderNuid, (ushort)endPoint.Port, true, direct);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read Login message. " + ex.Message);
                                    }
                                    break;

                                case MESSAGE_ID.LoginFail:
                                    try
                                    {
                                        // update stat
                                        Stats.LoginFail.Record(receiveBuffer.Length);
                                        // read result
                                        ActiveLoginResult = (LoginResult)receiveReader.ReadByte();
                                    }
                                    catch (Exception ex)
                                    {
                                        nodeError?.Invoke("ERROR: Failed to read LoginFail message. " + ex.Message);
                                    }
                                    break;
                            }
                        }
                    }
                }
                else
                {
                    // update stat
                    Stats.WrongVersion.Record(receiveBuffer.Length);
                }
            }
            catch (Exception ex)
            {
                nodeError?.Invoke("ERROR: Failed to read Node message: " + ex.Message);
            }
        }

        /// <summary>
        /// Receive any incoming messages
        /// </summary>
        public void ReceiveMessages()
        {
            // while there are messages queued
            while (IsOpen && udpClient.Available > 0)
            {
                // remote node address
                IPEndPoint endPoint = new(IPAddress.Any, 0);
                byte[] messageData = null;
                try
                {
                    messageData = udpClient.Receive(ref endPoint);

                    // copy data into message buffer
                    receiveBuffer.SetLength(0);
                    receiveBuffer.Write(messageData, 0, messageData.Length);
                    // go to start of stream
                    receiveReader.BaseStream.Seek(0, SeekOrigin.Begin);
                }
                catch (Exception ex)
                {
                    nodeError?.Invoke(ex.Message);
                }

                // JFP2 coexistence (docs/protocol-v2-design.md §3): a single byte compare on the
                // first byte of the datagram routes it to the right stack before anything else is
                // parsed. Legacy datagrams always start with the low byte of VERSION (0x520B ->
                // 0x0B on the wire); JFP2 datagrams always start with Jfp2.Envelope.Magic (0xFA),
                // which can never collide. This is the only line that touches the legacy receive
                // path (docs/protocol-v2-architecture.md §8.2) - everything else about ReceiveMsg
                // below is unmodified.
                if (messageData != null && messageData.Length > 0 && messageData[0] == Jfp2.Envelope.Magic)
                {
                    // receive the JFP2 message
                    Jfp2ReceiveMsg(endPoint, messageData);
                }
                else
                {
                    // receive the message
                    ReceiveMsg(endPoint);
                }
            }
        }

        #endregion

        #region JFP2

        // JFP2 dead-code landing (docs/protocol-v2-implementation-plan.md Phase 1): a peer already
        // known via the legacy Join/AddNode/Pulse mesh (i.e. already present in `nodes`) is offered a
        // Hello handshake. If it answers, we hold a negotiated Jfp2.PeerSession for it, but nothing
        // yet *uses* that negotiation - no application traffic (Position/Identity/...) has been
        // ported to JFP2 (that starts at Phase 2). If it never answers, it's marked AssumedLegacy and
        // left alone. Either way, the legacy path for that peer is completely unaffected.

        /// <summary>
        /// Number of Hello attempts before giving up and treating a peer as legacy-only.
        /// </summary>
        const int JFP2_HELLO_MAX_ATTEMPTS = 5;

        /// <summary>
        /// Seconds between Hello retries - mirrors the guaranteed-message resend cadence (§4 of
        /// docs/network-protocol.md) since both are "a small control message, retried until acked".
        /// </summary>
        const double JFP2_HELLO_RETRY_INTERVAL = 2.0;

        /// <summary>
        /// This build's schema offers, sent in every Hello/HelloAck. Negotiator.Resolve already
        /// handles "neither side offers this class" gracefully (the class simply never gets an
        /// agreed-version entry, docs/protocol-v2-design.md §5.3), so this list only ever needs to
        /// grow as later phases port real codecs - nothing else about negotiation changes.
        /// </summary>
        static readonly List<Jfp2.SchemaOffer> LocalJfp2Offers =
        [
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.Status, 1, 1),
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.StatusRequest, 1, 1),
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.Identity, 1, 1),
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.VariableSync, 1, 1),
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.Position, 1, 1),
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.Event, 1, 1),
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.FlightPlan, 1, 1),
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.Notes, 1, 1),
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.Weather, 1, 1),
            new Jfp2.SchemaOffer(false, Jfp2.MessageClasses.WeatherReply, 1, 1),
        ];

        /// <summary>
        /// Delegate shape for handing a decoded-but-not-yet-interpreted JFP2 application-partition
        /// message up to the application layer (Network.cs) - the JFP2 analogue of the legacy
        /// receiveNotify. `schemaVersion` is this peer's own locally-resolved
        /// PeerSession.AgreedAppVersion[messageClass] (docs/protocol-v2-design.md §5.3) - there is no
        /// per-message version field on the wire to read, unlike the legacy protocol's DataVersion.
        /// </summary>
        public delegate void Jfp2ReceiveNotify(IPEndPoint endPoint, Nuid nuid, byte messageClass, byte schemaVersion, ReadOnlySpan<byte> payload);
        public Jfp2ReceiveNotify jfp2ReceiveNotify;

        /// <summary>
        /// This build's optional capability bits. None implemented yet.
        /// </summary>
        const ulong LocalJfp2Capabilities = (ulong)Jfp2.Capability.None;

        /// <summary>
        /// Per-peer JFP2 negotiation state, keyed the same way the legacy `nodes` dictionary already
        /// is (by Nuid) so JFP2 session state rides alongside existing mesh bookkeeping rather than
        /// duplicating it (docs/protocol-v2-architecture.md §2).
        /// </summary>
        readonly Dictionary<Nuid, Jfp2.PeerSession> jfp2Sessions = [];

        /// <summary>
        /// Next PeerId this node will assign to a new JFP2 peer session. PeerId only needs to be
        /// unique from this node's own point of view (it is how a remote peer will address us going
        /// forward), so a simple wrapping counter is sufficient.
        /// </summary>
        ushort nextJfp2PeerId = 1;

        ushort NextJfp2PeerId()
        {
            // 0 is reserved as "not yet assigned" (a fresh PeerSession's RemoteAssignedId default),
            // so skip it if the counter ever wraps around.
            ushort id = nextJfp2PeerId++;
            if (nextJfp2PeerId == 0) nextJfp2PeerId = 1;
            return id;
        }

        // -- Guaranteed delivery (docs/protocol-v2-implementation-review.md Finding 1) --
        //
        // JFP2's Guaranteed flag/extension block (§4.4 of docs/protocol-v2-design.md) was defined on
        // the wire from Phase 1 but never actually implemented: nothing ever set the flag on send, and
        // ReceiveFrom never consumed the 4-byte extension on receive. That silently downgraded every
        // legacy-guaranteed message class ported since (Event, Notes, WeatherReply - see
        // Network.SendEventUpdate/SendCommsNoteMessage/SendWeatherReply) to plain unreliable UDP the
        // moment a peer negotiated JFP2 for that class.
        //
        // This implementation is deliberately scoped, not a full port of the legacy guaranteed-message
        // machinery (GuaranteedMessageOut/GuaranteedIn, segmented reassembly): every JFP2 application
        // message implemented so far comfortably fits in one UDP datagram (VariableSync, the largest,
        // chunks itself well under the segmentation threshold - see Jfp2.Codecs.VariableSyncV1Codec's
        // own chunking), so GuaranteedIndex/GuaranteedCount are always 0/1 here rather than a real
        // multi-part sequence. What this DOES implement: retransmission on a timer until acked, and an
        // ack (`GuaranteedDone`, the internal-partition class already reserved for this in
        // Jfp2.MessageClasses) that is itself never guaranteed (an ack-of-an-ack would never terminate).

        /// <summary>Seconds between guaranteed-message retries - matches JFP2_HELLO_RETRY_INTERVAL's
        /// own reasoning (mirrors the legacy guaranteed-message resend cadence).</summary>
        const double JFP2_GUARANTEED_RETRY_INTERVAL = 2.0;

        /// <summary>Retries before giving up on a guaranteed send and dropping it - matches
        /// JFP2_HELLO_MAX_ATTEMPTS. After this many attempts go unacked, the message is dropped and
        /// logged; there is no application-level notification of the failure (the legacy protocol's
        /// guaranteed delivery doesn't surface failures to the caller either).</summary>
        const int JFP2_GUARANTEED_MAX_ATTEMPTS = 5;

        /// <summary>How long a received GuaranteedId is remembered for duplicate-suppression before
        /// DoJfp2GuaranteedRetry's periodic sweep forgets it. Comfortably longer than
        /// JFP2_GUARANTEED_RETRY_INTERVAL * JFP2_GUARANTEED_MAX_ATTEMPTS (10s), so a legitimate
        /// retransmit of the same message can never be re-delivered as if it were new.</summary>
        const double JFP2_GUARANTEED_DEDUP_WINDOW = 30.0;

        /// <summary>One outstanding guaranteed send awaiting a GuaranteedDone ack. Payload is a copy
        /// (not a reused buffer) since it has to outlive the call that queued it, across however many
        /// retries it takes to get acked.</summary>
        sealed class Jfp2PendingGuaranteed
        {
            public IPEndPoint EndPoint;
            public Jfp2.EnvelopeFlags Flags;
            public byte MessageClass;
            public ushort SenderPeerId;
            public ushort RecipientPeerId;
            public byte[] Payload;
            public int Attempts;
            public double NextRetry;
        }

        readonly Dictionary<(Nuid Nuid, ushort GuaranteedId), Jfp2PendingGuaranteed> jfp2PendingGuaranteed = [];

        /// <summary>GuaranteedIds this node has already received (and acked) from a given peer recently
        /// - lets a retransmit that raced with our own ack (the ack got lost, so the sender resent) be
        /// re-acked without being re-dispatched to the application layer a second time. Value is the
        /// receive time, used by DoJfp2GuaranteedRetry's sweep to age entries out.</summary>
        readonly Dictionary<(Nuid Nuid, ushort GuaranteedId), double> jfp2RecentlySeenGuaranteed = [];

        readonly Timer jfp2GuaranteedCleanupTimer = new(JFP2_GUARANTEED_DEDUP_WINDOW);
        readonly List<(Nuid Nuid, ushort GuaranteedId)> tempJfp2GuaranteedKeys = [];

        ushort nextJfp2GuaranteedId = 1;

        ushort NextJfp2GuaranteedId()
        {
            // 0 isn't reserved for anything here (unlike PeerId), but starting at 1 and wrapping past 0
            // keeps the value pattern consistent with NextJfp2PeerId above.
            ushort id = nextJfp2GuaranteedId++;
            if (nextJfp2GuaranteedId == 0) nextJfp2GuaranteedId = 1;
            return id;
        }

        /// <summary>
        /// Send a GuaranteedDone ack for `guaranteedId` back to whoever sent us a guaranteed datagram -
        /// never itself guaranteed (see this section's own remarks on why an ack can't be acked).
        /// </summary>
        void SendJfp2GuaranteedDone(IPEndPoint endPoint, Jfp2.PeerSession session, ushort guaranteedId)
        {
            Span<byte> payload = stackalloc byte[2];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload, guaranteedId);
            SendJfp2Datagram(endPoint, Jfp2.EnvelopeFlags.Internal, Jfp2.MessageClasses.GuaranteedDone, session.LocalAssignedId, session.RemoteAssignedId, payload);
        }

        /// <summary>A GuaranteedDone ack arrived - stop retrying (and forget) the matching pending send,
        /// if we still have one (it may have already been dropped after JFP2_GUARANTEED_MAX_ATTEMPTS, or
        /// this may be a duplicate ack racing a retry that already succeeded - either way, a no-op).
        /// </summary>
        void HandleJfp2GuaranteedDone(IPEndPoint endPoint, ReadOnlySpan<byte> payload)
        {
            Nuid? nuid = FindNuidByEndPoint(endPoint);
            if (nuid == null || payload.Length < 2)
            {
                return;
            }
            ushort guaranteedId = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload);
            jfp2PendingGuaranteed.Remove((nuid.Value, guaranteedId));
        }

        /// <summary>
        /// Retry every outstanding guaranteed send whose retry interval has elapsed, drop anything past
        /// JFP2_GUARANTEED_MAX_ATTEMPTS, and periodically age out the duplicate-suppression cache.
        /// Called once per tick from DoWork(), right alongside DoJfp2Handshake() - same thread, same
        /// lock, no new concurrency (docs/protocol-v2-architecture.md §8).
        /// </summary>
        void DoJfp2GuaranteedRetry()
        {
            if (jfp2PendingGuaranteed.Count > 0)
            {
                tempJfp2GuaranteedKeys.Clear();
                foreach (var kv in jfp2PendingGuaranteed)
                {
                    Jfp2PendingGuaranteed pending = kv.Value;
                    if (main.ElapsedTime <= pending.NextRetry)
                    {
                        continue;
                    }
                    if (pending.Attempts >= JFP2_GUARANTEED_MAX_ATTEMPTS)
                    {
                        nodeError?.Invoke("JFP2: giving up on guaranteed message class " + pending.MessageClass + " to " + kv.Key.Nuid + " after " + pending.Attempts + " attempts");
                        tempJfp2GuaranteedKeys.Add(kv.Key);
                        continue;
                    }
                    pending.Attempts++;
                    pending.NextRetry = main.ElapsedTime + JFP2_GUARANTEED_RETRY_INTERVAL;
                    SendJfp2Datagram(pending.EndPoint, pending.Flags, pending.MessageClass, pending.SenderPeerId, pending.RecipientPeerId, pending.Payload, kv.Key.GuaranteedId, 0, 1);
                }
                foreach (var key in tempJfp2GuaranteedKeys)
                {
                    jfp2PendingGuaranteed.Remove(key);
                }
            }

            if (jfp2GuaranteedCleanupTimer.Elapsed(main.ElapsedTime) && jfp2RecentlySeenGuaranteed.Count > 0)
            {
                tempJfp2GuaranteedKeys.Clear();
                foreach (var kv in jfp2RecentlySeenGuaranteed)
                {
                    if (main.ElapsedTime - kv.Value >= JFP2_GUARANTEED_DEDUP_WINDOW)
                    {
                        tempJfp2GuaranteedKeys.Add(kv.Key);
                    }
                }
                foreach (var key in tempJfp2GuaranteedKeys)
                {
                    jfp2RecentlySeenGuaranteed.Remove(key);
                }
            }
        }

        /// <summary>
        /// Drop all guaranteed-delivery bookkeeping for a departed node, alongside jfp2Sessions.Remove -
        /// called from the same node-expiry pass in DoWork() that already does that (see the "remove
        /// node" block). Without this, a peer that disconnects mid-retry would leak its
        /// jfp2PendingGuaranteed entry forever (retried until JFP2_GUARANTEED_MAX_ATTEMPTS, then
        /// dropped anyway - bounded, but there's no reason to wait).
        /// </summary>
        void RemoveJfp2GuaranteedStateForNode(Nuid nuid)
        {
            tempJfp2GuaranteedKeys.Clear();
            foreach (var key in jfp2PendingGuaranteed.Keys)
            {
                if (key.Nuid == nuid) tempJfp2GuaranteedKeys.Add(key);
            }
            foreach (var key in tempJfp2GuaranteedKeys)
            {
                jfp2PendingGuaranteed.Remove(key);
            }

            tempJfp2GuaranteedKeys.Clear();
            foreach (var key in jfp2RecentlySeenGuaranteed.Keys)
            {
                if (key.Nuid == nuid) tempJfp2GuaranteedKeys.Add(key);
            }
            foreach (var key in tempJfp2GuaranteedKeys)
            {
                jfp2RecentlySeenGuaranteed.Remove(key);
            }
        }

        /// <summary>
        /// Find which known Nuid a JFP2 datagram's source endpoint belongs to. JFP2's own envelope
        /// only carries small negotiated PeerIds (docs/protocol-v2-design.md §4.1), not a Nuid, so the
        /// very first Hello from a peer - before any PeerId has been assigned - can only be matched
        /// back to the legacy mesh by its UDP source address, the same way the legacy guaranteed-
        /// delivery reassembly already matches incoming segments by endpoint.
        /// </summary>
        Nuid? FindNuidByEndPoint(IPEndPoint endPoint)
        {
            foreach (var kv in nodes)
            {
                if (kv.Value.endPoint.Equals(endPoint) || kv.Value.routeEndPoint.Equals(endPoint))
                {
                    return kv.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Send any JFP2 datagram (internal or application partition), bypassing the legacy
        /// sendBuffer/sendWriter machinery entirely - JFP2's envelope has a completely different
        /// layout, and there is no shared state to protect since the two stacks never touch the same
        /// buffer.
        ///
        /// docs/protocol-v2-implementation-review.md Finding 2: this used to allocate a fresh byte[]
        /// on every single call (including every Position send, the hot path design goal §2.1 of
        /// docs/protocol-v2-design.md explicitly asks to keep allocation-free). Fixed by renting a
        /// reusable buffer from the shared pool instead of `new byte[...]`, and sending straight off
        /// that rented span via the underlying Socket's ReadOnlySpan overload rather than
        /// UdpClient.Send(byte[], int, IPEndPoint), which requires a real array. The rented array is
        /// always returned in `finally`, so a send that throws still doesn't leak it back to the pool
        /// dirty/lost.
        /// </summary>
        void SendJfp2Datagram(IPEndPoint endPoint, Jfp2.EnvelopeFlags flags, byte rawMessageClass, ushort senderPeerId, ushort recipientPeerId, ReadOnlySpan<byte> payload, ushort guaranteedId = 0, byte guaranteedIndex = 0, byte guaranteedCount = 0)
        {
            if (!IsOpen || endPoint == null) return;

            var envelope = new Jfp2.Envelope(flags, senderPeerId, recipientPeerId, rawMessageClass, guaranteedId, guaranteedIndex, guaranteedCount);
            int length = envelope.WireSize + payload.Length;
            byte[] rented = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                int headerLength = envelope.WriteTo(rented);
                payload.CopyTo(rented.AsSpan(headerLength));
                udpClient.Client.SendTo(rented.AsSpan(0, length), SocketFlags.None, endPoint);
            }
            catch (Exception ex)
            {
                nodeError?.Invoke(ex.Message + ", " + endPoint.ToString());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        void SendJfp2Hello(Nuid nuid, Jfp2.PeerSession session)
        {
            if (!nodes.TryGetValue(nuid, out Node node)) return;

            var hello = new Jfp2.HandshakeMessage
            {
                ProtoMajorMin = Jfp2.Envelope.ProtoMajor,
                ProtoMajorMax = Jfp2.Envelope.ProtoMajor,
                Capabilities = LocalJfp2Capabilities,
                SelfAssignedId = session.LocalAssignedId,
                Offers = new List<Jfp2.SchemaOffer>(LocalJfp2Offers),
            };
            SendJfp2Datagram(node.routeEndPoint, Jfp2.EnvelopeFlags.Internal, Jfp2.MessageClasses.Hello, session.LocalAssignedId, session.RemoteAssignedId, hello.Serialize());
            nodeDebug?.Invoke("JFP2: Send Hello to " + nuid + " (attempt " + session.HelloAttempts + ")");
        }

        void SendJfp2HelloAck(IPEndPoint endPoint, Jfp2.PeerSession session, byte result)
        {
            var ack = new Jfp2.HandshakeMessage
            {
                ProtoMajorMin = Jfp2.Envelope.ProtoMajor,
                ProtoMajorMax = Jfp2.Envelope.ProtoMajor,
                Capabilities = LocalJfp2Capabilities,
                SelfAssignedId = session.LocalAssignedId,
                Result = result,
                Offers = new List<Jfp2.SchemaOffer>(LocalJfp2Offers),
            };
            SendJfp2Datagram(endPoint, Jfp2.EnvelopeFlags.Internal, Jfp2.MessageClasses.HelloAck, session.LocalAssignedId, session.RemoteAssignedId, ack.Serialize());
        }

        void HandleJfp2Hello(IPEndPoint endPoint, ReadOnlySpan<byte> payload)
        {
            Jfp2.HandshakeMessage hello = Jfp2.HandshakeMessage.Deserialize(payload);

            // Only negotiate with peers already known via the legacy mesh (Join/AddNode/Pulse) -
            // see docs/protocol-v2-architecture.md §3's sequence diagram, where the legacy
            // Join/JoinReply exchange always happens before any Hello.
            Nuid? nuid = FindNuidByEndPoint(endPoint);
            if (nuid == null)
            {
                nodeDebug?.Invoke("JFP2: Hello from unknown endpoint " + endPoint + " - ignored (not in legacy mesh yet)");
                return;
            }

            if (!jfp2Sessions.TryGetValue(nuid.Value, out Jfp2.PeerSession session))
            {
                session = new Jfp2.PeerSession { LocalAssignedId = NextJfp2PeerId() };
                jfp2Sessions[nuid.Value] = session;
            }
            session.RemoteAssignedId = hello.SelfAssignedId;

            if (hello.ProtoMajorMin > Jfp2.Envelope.ProtoMajor || hello.ProtoMajorMax < Jfp2.Envelope.ProtoMajor)
            {
                // incompatible envelope version - reply with rejection, never negotiate app versions
                SendJfp2HelloAck(endPoint, session, result: 1);
                nodeDebug?.Invoke("JFP2: Hello from " + nuid.Value + " - incompatible ProtoMajor range [" + hello.ProtoMajorMin + "," + hello.ProtoMajorMax + "]");
                return;
            }

            Jfp2.Negotiator.Resolve(session, LocalJfp2Capabilities, LocalJfp2Offers, hello.Capabilities, hello.Offers);
            session.HandshakeComplete = true;
            session.AssumedLegacy = false;

            nodeDebug?.Invoke("JFP2: Hello from " + nuid.Value + " - handshake complete, replying with HelloAck");
            SendJfp2HelloAck(endPoint, session, result: 0);
        }

        void HandleJfp2HelloAck(IPEndPoint endPoint, ReadOnlySpan<byte> payload)
        {
            Jfp2.HandshakeMessage ack = Jfp2.HandshakeMessage.Deserialize(payload);

            Nuid? nuid = FindNuidByEndPoint(endPoint);
            if (nuid == null || !jfp2Sessions.TryGetValue(nuid.Value, out Jfp2.PeerSession session))
            {
                // no outstanding Hello we recognize this reply as answering - ignore
                return;
            }

            if (ack.Result != 0)
            {
                // peer rejected our Hello (incompatible ProtoMajor) - fall back to legacy for now
                session.AssumedLegacy = true;
                nodeDebug?.Invoke("JFP2: HelloAck from " + nuid.Value + " - rejected (result=" + ack.Result + "), assuming legacy-only");
                return;
            }

            session.RemoteAssignedId = ack.SelfAssignedId;
            Jfp2.Negotiator.Resolve(session, LocalJfp2Capabilities, LocalJfp2Offers, ack.Capabilities, ack.Offers);
            session.HandshakeComplete = true;

            nodeDebug?.Invoke("JFP2: HelloAck from " + nuid.Value + " - handshake complete");
        }

        /// <summary>
        /// Entry point for every JFP2 datagram, dispatched from ReceiveMessages() by magic byte.
        /// Mirrors the shape of ReceiveMsg (parse header, then switch on message class) but is
        /// otherwise completely independent of it - no shared buffers, no shared parsing state.
        /// </summary>
        void Jfp2ReceiveMsg(IPEndPoint endPoint, byte[] messageData)
        {
            // check if address is banned (same policy as the legacy stack)
            if (banList.Find(a => a.Equals(endPoint.Address)) != null)
            {
                return;
            }

            try
            {
                Jfp2.Envelope envelope = Jfp2.Envelope.ReadFrom(messageData, out int consumed);
                ReadOnlySpan<byte> payload = messageData.AsSpan(consumed);

                if (envelope.IsGuaranteed && !AckJfp2Guaranteed(endPoint, envelope))
                {
                    // already delivered once before (this is a retransmit racing a lost ack) - the ack
                    // was already re-sent by AckJfp2Guaranteed; don't dispatch it to the application a
                    // second time.
                    return;
                }

                if (!envelope.IsInternal)
                {
                    // Application-partition message (Status/... as of Phase 2). Hand it to the
                    // application layer (Network.cs) via jfp2ReceiveNotify, the JFP2 analogue of the
                    // legacy receiveNotify - but only for a peer we actually have a completed,
                    // non-AssumedLegacy negotiation with, and only for a class it actually agreed on.
                    // A well-behaved peer never sends a class it didn't negotiate; these checks are
                    // purely defensive (e.g. against a stale PeerSession after a peer restarted).
                    Nuid? appNuid = FindNuidByEndPoint(endPoint);
                    if (appNuid == null)
                    {
                        nodeDebug?.Invoke("JFP2: application message from unknown endpoint " + endPoint + " - ignored");
                        return;
                    }
                    if (!jfp2Sessions.TryGetValue(appNuid.Value, out Jfp2.PeerSession appSession) || !appSession.HandshakeComplete)
                    {
                        nodeDebug?.Invoke("JFP2: application message from " + appNuid.Value + " with no completed handshake - ignored");
                        return;
                    }
                    byte agreedVersion = appSession.AgreedAppVersion[envelope.RawMessageClass];
                    if (agreedVersion == 0)
                    {
                        nodeDebug?.Invoke("JFP2: application message class " + envelope.RawMessageClass + " from " + appNuid.Value + " was never agreed on - ignored");
                        return;
                    }
                    jfp2ReceiveNotify?.Invoke(endPoint, appNuid.Value, envelope.RawMessageClass, agreedVersion, payload);
                    return;
                }

                switch (envelope.RawMessageClass)
                {
                    case Jfp2.MessageClasses.Hello:
                        HandleJfp2Hello(endPoint, payload);
                        break;

                    case Jfp2.MessageClasses.HelloAck:
                        HandleJfp2HelloAck(endPoint, payload);
                        break;

                    case Jfp2.MessageClasses.GuaranteedDone:
                        HandleJfp2GuaranteedDone(endPoint, payload);
                        break;

                    default:
                        // Join/Leave/Pulse/Pathfinder-equivalent JFP2 messages are out of scope for
                        // Phase 1 - the legacy mesh-management messages already do this job.
                        break;
                }
            }
            catch (Exception ex)
            {
                nodeError?.Invoke("ERROR: Failed to read JFP2 message: " + ex.Message);
            }
        }

        /// <summary>
        /// For a received datagram with Flags.Guaranteed set: ack it (always - even a duplicate gets
        /// re-acked, since seeing a duplicate means our first ack was lost) and report whether this is
        /// the first time we've seen this GuaranteedId from this peer. Returns false only for a
        /// confirmed duplicate (already-acked GuaranteedId from a known, negotiated peer) - the caller
        /// should skip dispatching in that case. Returns true for a new GuaranteedId, and also (can't
        /// safely dedup or ack) for a datagram from a peer we don't recognize yet, so it still gets
        /// dispatched rather than silently dropped - matches the "well-behaved peer" defensive style
        /// already used elsewhere in this method.
        /// </summary>
        bool AckJfp2Guaranteed(IPEndPoint endPoint, Jfp2.Envelope envelope)
        {
            Nuid? nuid = FindNuidByEndPoint(endPoint);
            if (nuid == null || !jfp2Sessions.TryGetValue(nuid.Value, out Jfp2.PeerSession session))
            {
                return true;
            }

            var key = (nuid.Value, envelope.GuaranteedId);
            bool isDuplicate = jfp2RecentlySeenGuaranteed.ContainsKey(key);
            jfp2RecentlySeenGuaranteed[key] = main.ElapsedTime;
            SendJfp2GuaranteedDone(endPoint, session, envelope.GuaranteedId);
            return !isDuplicate;
        }

        /// <summary>
        /// Offer/retry a Hello to every peer already known via the legacy mesh. Called once per tick
        /// from DoWork(), right alongside DoPulse() - same thread, same lock, no new concurrency
        /// (docs/protocol-v2-architecture.md §8).
        /// </summary>
        void DoJfp2Handshake()
        {
            foreach (var kv in nodes)
            {
                Nuid nuid = kv.Key;
                Node node = kv.Value;

                if (!jfp2Sessions.TryGetValue(nuid, out Jfp2.PeerSession session))
                {
                    // JFP2's envelope carries no routable Nuid, only small per-peer PeerIds meaningful
                    // solely to the two negotiating parties (docs/protocol-v2-design.md §4.1) - unlike
                    // every legacy message, which embeds a real Recipient Nuid a relay node can act on
                    // without understanding the payload (network-protocol.md §6.3). A Hello sent to an
                    // indirect peer's routeEndPoint therefore lands on the relay itself, which has no
                    // way to know it should forward an opaque JFP2 datagram - best case it's silently
                    // dropped, worst case a JFP2-capable relay completes a handshake *as itself*,
                    // leaving the true originator believing it negotiated with a peer it never reached.
                    // Until JFP2 has its own relay/translation mechanism (the Jfp2Bridge work), only
                    // ever attempt Hello with peers we're DIRECTLY connected to. An indirect peer is
                    // silently skipped here (no session, no AssumedLegacy) rather than given up on
                    // permanently, so it's picked up automatically the moment Pathfinder establishes a
                    // direct path - see also TryGetJfp2AppPeer, which re-checks Direct on every send so
                    // a peer that goes indirect again after a completed handshake safely falls back to
                    // legacy instead of sending JFP2 into a routeEndpoint that no longer reaches it.
                    if (!node.Direct)
                    {
                        continue;
                    }

                    // first time we've seen this peer - offer a Hello immediately
                    session = new Jfp2.PeerSession { LocalAssignedId = NextJfp2PeerId() };
                    jfp2Sessions[nuid] = session;
                    session.HelloAttempts = 1;
                    session.NextHelloAttempt = main.ElapsedTime + JFP2_HELLO_RETRY_INTERVAL;
                    SendJfp2Hello(nuid, session);
                }
                else if (!session.HandshakeComplete && !session.AssumedLegacy)
                {
                    if (!node.Direct)
                    {
                        // lost direct connectivity mid-negotiation (e.g. Pathfinder re-routed us
                        // indirect before Hello completed) - drop the session rather than keep
                        // retrying at what's now the wrong address; the branch above resumes
                        // negotiation automatically once direct connectivity returns.
                        jfp2Sessions.Remove(nuid);
                    }
                    else if (main.ElapsedTime > session.NextHelloAttempt)
                    {
                        if (session.HelloAttempts >= JFP2_HELLO_MAX_ATTEMPTS)
                        {
                            // peer never answered - treat as legacy-only for the rest of the session,
                            // exactly as if it had never offered JFP2 at all
                            session.AssumedLegacy = true;
                            nodeDebug?.Invoke("JFP2: " + nuid + " did not answer Hello after " + session.HelloAttempts + " attempts - assuming legacy-only peer");
                        }
                        else
                        {
                            session.HelloAttempts++;
                            session.NextHelloAttempt = main.ElapsedTime + JFP2_HELLO_RETRY_INTERVAL;
                            SendJfp2Hello(nuid, session);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// True if `endPoint` maps to a legacy-mesh peer (docs/protocol-v2-architecture.md §3 - JFP2
        /// negotiation only ever runs against peers already known via legacy Join/AddNode/Pulse) that
        /// has completed a JFP2 handshake, isn't AssumedLegacy, and has agreed on a version > 0 for
        /// `messageClass`. When true, `nuid`/`version` give what the caller needs to resolve a codec
        /// and call SendJfp2Application. Used by the application layer (Network.cs) to decide, per
        /// send, whether to route a given message through JFP2 or fall back to the untouched legacy
        /// Write*Message()+Send() path - see docs/protocol-v2-architecture.md §8.2.
        /// </summary>
        public bool TryGetJfp2AppPeer(IPEndPoint endPoint, byte messageClass, out Nuid nuid, out byte version)
        {
            Nuid? found = FindNuidByEndPoint(endPoint);
            if (found == null)
            {
                nuid = default;
                version = 0;
                return false;
            }
            nuid = found.Value;
            return TryGetJfp2AppPeer(nuid, messageClass, out version);
        }

        /// <summary>
        /// Same check as the IPEndPoint overload above, but for a caller that already has the peer's
        /// Nuid (e.g. from LocalNode.GetNodeList()) and would otherwise have to round-trip through an
        /// endpoint just to look it back up - added in Phase 3 for the per-node send loops
        /// Identity/VariableSync need (docs/protocol-v2-implementation-plan.md Phase 3).
        /// </summary>
        public bool TryGetJfp2AppPeer(Nuid nuid, byte messageClass, out byte version)
        {
            version = 0;
            // Re-check direct connectivity on every call, not just at handshake time: a peer that
            // completed JFP2 negotiation while direct but has since gone indirect (Pathfinder re-
            // routed it via a relay) can no longer be reached at its negotiated PeerId/routeEndpoint -
            // see DoJfp2Handshake's comment for why JFP2 can't just relay through an intermediate node
            // yet. Falling back to legacy here is silent and automatic; no session cleanup needed.
            if (!nodes.TryGetValue(nuid, out Node node) || !node.Direct)
            {
                return false;
            }
            if (!jfp2Sessions.TryGetValue(nuid, out Jfp2.PeerSession session) || !session.HandshakeComplete || session.AssumedLegacy)
            {
                return false;
            }
            version = session.AgreedAppVersion[messageClass];
            return version > 0;
        }

        /// <summary>
        /// Send an already-encoded JFP2 application-partition payload to a peer identified by Nuid. The
        /// caller is expected to have already checked TryGetJfp2AppPeer and encoded `payload` with the
        /// matching CodecRegistry-resolved codec.
        ///
        /// `guaranteed`: false (the default) sends unreliable, matching every application message class
        /// as originally ported. Pass true for a class whose legacy equivalent is sent guaranteed
        /// (currently Event, Notes, WeatherReply - see each one's own legacy Write*Message call) -
        /// docs/protocol-v2-implementation-review.md Finding 1 found these three had silently lost
        /// their guaranteed-delivery semantics versus legacy; this parameter is what closes that gap.
        /// A guaranteed send is retried by DoJfp2GuaranteedRetry until acked or
        /// JFP2_GUARANTEED_MAX_ATTEMPTS is reached, mirroring the legacy protocol's own guaranteed-
        /// message behavior (retry until acked, then give up silently).
        /// </summary>
        public void SendJfp2Application(Nuid nuid, byte messageClass, ReadOnlySpan<byte> payload, bool guaranteed = false)
        {
            if (!nodes.TryGetValue(nuid, out Node node) || !jfp2Sessions.TryGetValue(nuid, out Jfp2.PeerSession session))
            {
                return;
            }

            if (!guaranteed)
            {
                SendJfp2Datagram(node.routeEndPoint, Jfp2.EnvelopeFlags.None, messageClass, session.LocalAssignedId, session.RemoteAssignedId, payload);
                return;
            }

            ushort guaranteedId = NextJfp2GuaranteedId();
            SendJfp2Datagram(node.routeEndPoint, Jfp2.EnvelopeFlags.Guaranteed, messageClass, session.LocalAssignedId, session.RemoteAssignedId, payload, guaranteedId, 0, 1);
            jfp2PendingGuaranteed[(nuid, guaranteedId)] = new Jfp2PendingGuaranteed
            {
                EndPoint = node.routeEndPoint,
                Flags = Jfp2.EnvelopeFlags.Guaranteed,
                MessageClass = messageClass,
                SenderPeerId = session.LocalAssignedId,
                RecipientPeerId = session.RemoteAssignedId,
                Payload = payload.ToArray(),
                Attempts = 1,
                NextRetry = main.ElapsedTime + JFP2_GUARANTEED_RETRY_INTERVAL,
            };
        }

        /// <summary>
        /// A peer's JFP2 negotiation state, coarse enough for UI display (see SessionForm's Protocol
        /// column). `NotApplicable` is for the local node itself - JFP2 negotiation is peer-to-peer,
        /// so it never applies to "this node" in its own peer list.
        /// </summary>
        public enum Jfp2PeerState
        {
            /// <summary>No completed handshake yet: either a Hello was just sent and no reply has
            /// arrived, or (briefly, for well under one tick) no Hello has been attempted yet.</summary>
            Negotiating,
            /// <summary>The peer never answered Hello after JFP2_HELLO_MAX_ATTEMPTS retries and is
            /// being treated as legacy-only for the rest of the session - see DoJfp2Handshake.</summary>
            Legacy,
            /// <summary>Handshake complete; this peer negotiated at least the internal Hello/HelloAck
            /// exchange over JFP2 (individual application message classes may still each be at
            /// agreed version 0 - see TryGetJfp2AppPeer for the per-class check).</summary>
            Negotiated,
            /// <summary>The local node itself, not a peer - JFP2 negotiation doesn't apply.</summary>
            NotApplicable,
        }

        /// <summary>
        /// Coarse JFP2 negotiation state for a peer, for UI display - see Jfp2PeerState. Use
        /// TryGetJfp2AppPeer instead when the caller actually needs to know whether a specific
        /// message class can be sent over JFP2 to this peer.
        /// </summary>
        public Jfp2PeerState GetNodeJfp2State(Nuid nuid)
        {
            if (jfp2Sessions.TryGetValue(nuid, out Jfp2.PeerSession session))
            {
                if (session.HandshakeComplete && !session.AssumedLegacy) return Jfp2PeerState.Negotiated;
                if (session.AssumedLegacy) return Jfp2PeerState.Legacy;
            }
            else if (nodes.TryGetValue(nuid, out Node node) && !node.Direct)
            {
                // DoJfp2Handshake deliberately never creates a session (never even attempts a Hello)
                // for an indirect peer - JFP2 has no relay/translation mechanism yet, see that
                // method's own comment. Without this branch, a peer that is only ever reachable
                // through a relay (e.g. a hub-mediated connection between two NATed clients) would
                // report Negotiating forever, since no session will ever exist to resolve it one way
                // or the other - shown in the Sessions window as a "Pending" that never clears. Report
                // Legacy here for display purposes only: this doesn't touch jfp2Sessions, so
                // DoJfp2Handshake still transparently starts real negotiation the moment Pathfinder
                // establishes a direct path (at which point a session appears and this branch no
                // longer applies - Negotiating correctly reflects the real handshake in progress).
                return Jfp2PeerState.Legacy;
            }
            return Jfp2PeerState.Negotiating;
        }

        #endregion

        #region Guaranteed

        /// <summary>
        /// Maximum data in a guaranteed message
        /// </summary>
        const int MAX_GUARANTEED_DATA = 1000;

        /// <summary>
        /// Next available unique ID
        /// </summary>
        ushort nextGuaranteedId = 1;

        /// <summary>
        /// Message being sent by guaranteed method
        /// </summary>
        class GuaranteedMessageOut
        {
            /// <summary>
            /// Part of the message with its own header
            /// </summary>
            public class Segment
            {
                /// <summary>
                /// Segment data
                /// </summary>
                public byte[] data;
                /// <summary>
                /// Has this segment been receive by the recipient
                /// </summary>
                public bool received = false;

                /// <summary>
                /// Segment contructor
                /// </summary>
                public Segment(byte[] data, int offset, int length)
                {
                    // allocate data
                    this.data = new byte[DATA_OFFSET + length];
                    // copy message header
                    Array.Copy(data, this.data, DATA_OFFSET);
                    // set message data
                    Array.Copy(data, offset, this.data, DATA_OFFSET, length);
                }
            }

            /// <summary>
            /// Message segments
            /// </summary>
            public List<Segment> segmentList = [];

            /// <summary>
            /// Unique guaranteed ID
            /// </summary>
            public int id;
            /// <summary>
            /// Time for next resend
            /// </summary>
            public DateTime resendTime;
            /// <summary>
            /// nuid of recipient
            /// </summary>
            public Nuid nuid;
            /// <summary>
            /// direct endpoint of recipient
            /// </summary>
            public IPEndPoint endPoint;
            /// <summary>
            /// Time this will expire
            /// </summary>
            readonly DateTime expireTime;
            /// <summary>
            /// Has this expired
            /// </summary>
            public bool Expired { get { return DateTime.UtcNow > expireTime; } }
            /// <summary>
            /// All segments have been received
            /// </summary>
            public bool Received
            {
                get
                {
                    // for each segment
                    foreach (var segment in segmentList)
                    {
                        // check if segment has been received
                        if (segment.received == false)
                        {
                            // not yet received
                            return false;
                        }
                    }
                    // received
                    return true;
                }
            }

            /// <summary>
            /// Guaranteed message constructor
            /// </summary>
            public GuaranteedMessageOut(ushort id, Nuid nuid, IPEndPoint endPoint, byte[] data, int length)
            {
                // allocate unique ID
                this.id = id;
                // resend timer
                this.resendTime = DateTime.UtcNow;
                // nuid of recipient
                this.nuid = nuid;
                // set the node endpoint
                this.endPoint = endPoint;
                // initialize offset
                int offset = DATA_OFFSET;
                // while there is still data left
                while (offset < length)
                {
                    // get segment length
                    int segmentLength = Math.Min(length - offset, MAX_GUARANTEED_DATA);
                    // add segment
                    this.segmentList.Add(new Segment(data, offset, segmentLength));
                    // update offset
                    offset += segmentLength;
                }
                // check for multiple segments
                if (this.segmentList.Count > 1)
                {
                    // for each segment
                    for (int index = 0; index < this.segmentList.Count; index++)
                    {
                        // create memory stream
                        using MemoryStream stream = new(this.segmentList[index].data);
                        // create writer for segment
                        using BinaryWriter writer = new(stream);
                        // set index offset
                        stream.Position = GUARANTEED_INDEX_OFFSET;
                        // write index
                        writer.Write((byte)index);
                        // set count offset
                        stream.Position = GUARANTEED_COUNT_OFFSET;
                        // write count
                        writer.Write((byte)this.segmentList.Count);
                    }
                }
                // set expire time
                expireTime = DateTime.UtcNow.AddSeconds(GUARANTEED_OUT_EXPIRE_TIME);
            }
        }

        /// <summary>
        /// List of messages being sent by guaranteed method
        /// </summary>
        readonly List<GuaranteedMessageOut> guaranteedOutList = [];

        /// <summary>
        /// List of messages to be removed
        /// </summary>
        readonly List<GuaranteedMessageOut> removeOutList = [];

        /// <summary>
        /// Information
        /// </summary>
        public int GuaranteedOutCount { get { return guaranteedOutList.Count; } }

        /// <summary>
        /// Incoming guaranteed message
        /// </summary>
        /// <remarks>
        /// GuaranteedDone constructor
        /// </remarks>
        /// <param name="nuid">ID of node</param>
        /// <param name="id">Guaranteed ID</param>
        class GuaranteedIn(IPEndPoint nodeEndPoint, int id, int segmentCount)
        {
            /// <summary>
            /// Part of the message with its own header
            /// </summary>
            public class Segment
            {
                /// <summary>
                /// Segment data
                /// </summary>
                public byte[] data;

                /// <summary>
                /// Segment contructor
                /// </summary>
                public Segment(byte[] data, int length)
                {
                    // allocate data
                    this.data = new byte[length];
                    // copy data
                    Array.Copy(data, this.data, length);
                }
            }

            /// <summary>
            /// Segments of the message
            /// </summary>
            public Segment[] segments = new Segment[segmentCount];

            /// <summary>
            /// end point of sender
            /// </summary>
            public IPEndPoint nodeEndPoint = nodeEndPoint;
            /// <summary>
            /// Guaranteed ID
            /// </summary>
            public int id = id;

            /// <summary>
            /// Time this will expire
            /// </summary>
            readonly DateTime expireTime = DateTime.UtcNow.AddSeconds(GUARANTEED_IN_EXPIRE_TIME);

            /// <summary>
            /// Has this expired
            /// </summary>
            public bool Expired { get { return DateTime.UtcNow > expireTime; } }

            /// <summary>
            /// All segments have been received
            /// </summary>
            /// <returns>Message is complete</returns>
            public bool Complete
            {
                get
                {
                    // for each segment
                    foreach (var segment in segments)
                    {
                        // check for invalid segment
                        if (segment == null)
                        {
                            // not done
                            return false;
                        }
                    }
                    // done
                    return true;
                }
            }

            /// <summary>
            /// Message has been received
            /// </summary>
            public bool Done { get { return segments == null; } }

            /// <summary>
            /// Finish with message
            /// </summary>
            public void Finish()
            {
                // release segments
                segments = null;
            }
        }

        /// <summary>
        /// List of guaranteed already received
        /// </summary>
        readonly List<GuaranteedIn> guaranteedInList = [];

        /// <summary>
        /// Temporary list for removing expired done objects
        /// </summary>
        readonly List<GuaranteedIn> removeInList = [];

        /// <summary>
        /// Information
        /// </summary>
        public int GuaranteedInCount { get { return guaranteedInList.Count; } }

        /// <summary>
        /// Process all active guaranteed messages
        /// </summary>
        void DoGuaranteedMessages()
        {
            // for all guaranteed messages
            foreach (var message in guaranteedOutList)
            {
                // check for resend time
                if (DateTime.UtcNow >= message.resendTime)
                {
                    // get endpoint
                    IPEndPoint endPoint;
                    // check for node
                    if (nodes.TryGetValue(message.nuid, out Node value))
                    {
                        // use node endpoint
                        endPoint = value.endPoint;
                    }
                    else
                    {
                        // use original endpoint
                        endPoint = message.endPoint;
                    }

                    // for each segment
                    foreach (var segment in message.segmentList)
                    {
                        // check if segment has not yet been received
                        if (segment.received == false)
                        {
                            try
                            {
                                // Used for local testing
                                // check if the endPoint is the IP 192.168.1.115
                                //if (endPoint.Address.ToString() == "192.168.1.115")
                                //{
                                //    // handle specific case for IP 192.168.1.115
                                //    nodeError?.Invoke("No message to " + endPoint.ToString());
                                //    continue;
                                //}
                                // resend segment
                                udpClient.Send(segment.data, (int)segment.data.Length, endPoint);
                            }
                            catch (Exception ex)
                            {
                                // error
                                nodeError?.Invoke(ex.Message + ", " + endPoint.ToString());
                            }

#if DEBUG_GUARANTEED
                            nodeError("GUARANTEED OUT: SEND " + message.id + " " + endPoint);
#endif
                        }
                    }

                    // update resend time
                    message.resendTime = DateTime.UtcNow.AddSeconds(2);
                    // check if expired
                    if (message.Expired)
                    {
                        // add to remove list
                        removeOutList.Add(message);
                    }
                }
            }

            // for each message in the done list
            foreach (var message in guaranteedInList)
            {
                // check if expired
                if (message.Expired)
                {
                    // add to remove list
                    removeInList.Add(message);
                }
            }

            // for each guaranteed message to be removed
            foreach (var message in removeOutList)
            {
                // remove message
                guaranteedOutList.Remove(message);
            }
            // clear list
            removeOutList.Clear();

            // for each done to be removed
            foreach (var message in removeInList)
            {
                // remove done
                guaranteedInList.Remove(message);
            }
            // clear list
            removeInList.Clear();
        }
#endregion

        // create remove list
        readonly List<Nuid> removeList = [];

        /// <summary>
        /// Process the node
        /// </summary>
        public void DoWork()
        {
            if (IsOpen)
            {
                // check if connected
                if (CurrentState != State.Unconnected)
                {
                    // for each node
                    foreach (var node in nodes)
                    {
                        // check if node has expired
                        if (node.Value.Expired)
                        {
                            // add to remove list
                            removeList.Add(node.Key);
                        }
                    }

                    // for each node in remove list
                    foreach (var nuid in removeList)
                    {
                        // check for nuid
                        if (nodes.ContainsKey(nuid))
                        {
                            // for all nodes
                            foreach (var node in nodes)
                            {
                                // check if node is being used as a route
                                if (node.Value.routeEndPoint.Equals(nodes[nuid].endPoint))
                                {
                                    // send is no longer established
                                    node.Value.sendEstablished = false;
                                    // reset end point
                                    node.Value.routeEndPoint = node.Value.endPoint;
                                }
                            }

                            // for each guaranteed message
                            foreach (GuaranteedMessageOut message in guaranteedOutList)
                            {
                                // check end point
                                if (message.nuid.Invalid() && message.endPoint.Equals(nodes[nuid].endPoint))
                                {
                                    // add message
                                    removeOutList.Add(message);
                                }
                                // check nuid
                                else if (message.nuid == nuid)
                                {
                                    // add message
                                    removeOutList.Add(message);
                                }
                            }

                            // remove node
                            nodes.Remove(nuid);
                            // drop any JFP2 negotiation state for the departed node too
                            jfp2Sessions.Remove(nuid);
                            RemoveJfp2GuaranteedStateForNode(nuid);
                            // notify application
                            nodeLeave?.Invoke(nuid);
                        }
                    }

                    // clear list
                    removeList.Clear();

                    DoPulse();
                    DoJfp2Handshake();
                    DoJfp2GuaranteedRetry();
                    DoRouting();
                }

                DoGuaranteedMessages();
                ReceiveMessages();
            }
        }

        /// <summary>
        /// Node constructor
        /// </summary>
        public LocalNode(Main main)
        {
            this.main = main;

            // message buffers
            sendBuffer = new MemoryStream(1024);
            sendWriter = new BinaryWriter(sendBuffer);
            receiveBuffer = new MemoryStream(1024);
            receiveReader = new BinaryReader(receiveBuffer);
            // set next guaranteed ID
            this.nextGuaranteedId = (ushort)Stopwatch.GetTimestamp();

            // get local host name
            var host = Dns.GetHostEntry(Dns.GetHostName());
            // for each IP address
            foreach (var ip in host.AddressList)
            {
                // check for IP address
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    // save IP address
                    localAddress = ip;
                    localNuid.local = localAddress.GetAddressBytes()[3];
                }
            }
        }

        #region Connection

        /// <summary>
        /// UDP Client
        /// </summary>
        UdpClient udpClient = null;

        /// <summary>
        /// Is the node open
        /// </summary>
        public bool IsOpen { get { return udpClient != null; } }

        /// <summary>
        /// Open a new port
        /// </summary>
        /// <param name="port">Port</param>
        public bool Open(int port)
        {
            if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
                return false;

            // Only close and reopen if the port is different or not open
            if (udpClient != null && localNuid.port == (ushort)port)
                return true;

            Close();
            localNuid.port = (ushort)port;

            try
            {
                udpClient = new UdpClient(localNuid.port, AddressFamily.InterNetwork);
#if NET6_0_OR_GREATER
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
#endif
                {
                    uint IOC_IN = 0x80000000;
                    uint IOC_VENDOR = 0x18000000;
                    uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                    udpClient.Client.IOControl((int)SIO_UDP_CONNRESET, [Convert.ToByte(false)], null);
                }
                return true;
            }
            catch (Exception ex)
            {
                nodeError?.Invoke(ex.Message + ": port=" + port);
                return false;
            }
        }

        /// <summary>
        /// Close port
        /// </summary>
        public void Close()
        {
            // leave
            Leave();

            if (IsOpen)
            {
                // close
                udpClient.Close();
                udpClient = null;
            }
        }

        /// <summary>
        /// Option for low bandwidth
        /// </summary>
        public bool lowBandwidth = false;

        /// <summary>
        /// Hashed password
        /// </summary>
        uint passwordHash = 0;

        /// <summary>
        /// Is the session passworded
        /// </summary>
        public bool Password { get { return passwordHash != 0; } }

        /// <summary>
        /// Get a hash value from a string
        /// </summary>
        /// <param name="str"></param>
        /// <returns>Hash value</returns>
        static public uint HashString(string str)
        {
            unchecked
            {
                uint hash1 = (5381 << 16) + 5381;
                uint hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        /// <summary>
        /// Set the current password
        /// </summary>
        /// <param name="password"></param>
        static public uint HashPassword(string password)
        {
            // hashed password
            uint hash = 0;
            // check for password
            if (password.Length > 0)
            {
                // get hash value
                hash = HashString(password);
                // avoid null value
                if (hash == 0)
                {
                    hash = 1;
                }
            }
            // return hash value
            return hash;
        }

        #endregion

        /// <summary>
        /// Main interface
        /// </summary>
        readonly Main main;

        /// <summary>
        /// internal ID of the session
        /// </summary>
        uint suid = 0;

        /// <summary>
        /// ID of the session
        /// </summary>
        public uint Suid { get { return suid; } }

        /// <summary>
        /// Unique Network Identifier
        /// </summary>
        public struct Nuid
        {
            public uint ip;        // from IP address
            public ushort port;    // from port
            public byte local;     // from local network

            // constructor from values
            public Nuid(uint ip, ushort port, byte local)
            {
                this.ip = ip;
                this.port = port;
                this.local = local;
            }

            // constructor from stream reader
            public Nuid(BinaryReader reader)
            {
                ip = reader.ReadUInt32();
                port = reader.ReadUInt16();
                local = reader.ReadByte();
            }

            // Write to stream
            public readonly void Write(BinaryWriter writer)
            {
                writer.Write(ip);
                writer.Write(port);
                writer.Write(local);
            }

            // constructor from IPAddress
            public Nuid(IPAddress address, ushort port, byte local)
            {
                // get IP address
                byte[] bytes = address.GetAddressBytes();
                ip = (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
                this.port = port;
                this.local = local;
            }

            // constructor from end point
            public Nuid(IPEndPoint endPoint, byte local)
            {
                // get IP address
                byte[] bytes = endPoint.Address.GetAddressBytes();
                ip = (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
                port = (ushort)endPoint.Port;
                this.local = local;
            }

            /// <summary>
            /// Is nuid valid
            /// </summary>
            /// <returns></returns>
            public readonly bool Invalid()
            {
                return ip == 0;
            }

            public readonly bool Valid()
            {
                return Invalid() == false;
            }

            /// <summary>
            /// Convert to an end point
            /// </summary>
            /// <returns></returns>
            public readonly IPAddress ToAddress()
            {
                byte[] bytes = [(byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip];
                return new IPAddress(bytes);
            }

            /// <summary>
            /// Convert to an end point
            /// </summary>
            /// <returns></returns>
            public readonly IPEndPoint ToEndPoint(ushort port)
            {
                return new IPEndPoint(ToAddress(), port);
            }

            /// <summary>
            /// Convert nuid to a readable string
            /// </summary>
            public override readonly string ToString()
            {
                return Network.EncodeIP(ToEndPoint(port).ToString()) + "/" + local;
            }

            /// <summary>
            ///  Comparisons
            /// </summary>
            public override readonly bool Equals(Object obj) => obj is Nuid nuid && this == nuid;
            public override readonly int GetHashCode()
            {
                return ip.GetHashCode() ^ port.GetHashCode() ^ local.GetHashCode();
            }
            public static bool operator ==(Nuid x, Nuid y)
            {
                return x.ip == y.ip && x.port == y.port && x.local == y.local;
            }
            public static bool operator !=(Nuid x, Nuid y)
            {
                return !(x == y);
            }
            public static bool SameDevice(Nuid x, Nuid y)
            {
                return x.ip == y.ip && x.local == y.local;
            }
        }

        /// <summary>
        /// ID of this node
        /// </summary>
        Nuid localNuid = new();

        /// <summary>
        /// Node states
        /// </summary>
        public enum State
        {
            Unconnected,
            Connecting,
            Connected,
        };

        /// <summary>
        /// Local address
        /// </summary>
        IPAddress localAddress = IPAddress.Loopback;

        /// <summary>
        /// Accessible local address
        /// </summary>
        public IPAddress LocalAddress
        {
            get { return localAddress; }
            set
            {
                localAddress = value;
                localNuid.local = localAddress.GetAddressBytes()[3];
            }
        }

        /// <summary>
        /// Convert Unique Network Identifier to an end point
        /// </summary>
        /// <param name="nuid">Unique Network Identifier</param>
        /// <returns>Network end point</returns>
        public IPEndPoint MakeEndPoint(Nuid nuid, ushort port)
        {
            // convert
            IPEndPoint endPoint = nuid.ToEndPoint(port);
            // check for local device
            if (endPoint.Address.Equals(InternetAddress))
            {
                // make local address
                endPoint.Address = MakeLocalAddress(nuid.local);
                // use local port
                endPoint.Port = nuid.port;
            }

            return endPoint;
        }

        /// <summary>
        /// Construct an local network address
        /// </summary>
        /// <param name="id">ID</param>
        /// <returns>local address</returns>
        IPAddress MakeLocalAddress(byte id)
        {
            // construct address
            byte[] bytes = localAddress.GetAddressBytes();
            bytes[3] = id;
            // return address
            return new IPAddress(bytes);
        }

        /// <summary>
        /// Check if address is a local address
        /// </summary>
        /// <param name="address">Address to check</param>
        /// <returns>Is local address</returns>
        public bool IsLocalAddress(IPAddress address)
        {
            // compare first three bytes of address
            byte[] bytes_1 = address.GetAddressBytes();
            byte[] bytes_2 = LocalAddress.GetAddressBytes();
            return (bytes_1[0] == bytes_2[0] && bytes_1[1] == bytes_2[1] && bytes_1[2] == bytes_2[2]);
        }

        /// <summary>
        /// Internet address
        /// </summary>
        IPAddress internetAddress = IPAddress.None;

        /// <summary>
        /// Accessible local address
        /// </summary>
        public IPAddress InternetAddress
        {
            get { return internetAddress; }
            set
            {
                internetAddress = value;
                localNuid = new Nuid(internetAddress, localNuid.port, localNuid.local);
            }
        }

        /// <summary>
        /// Is the node ready for connections
        /// </summary>
        public bool Ready { get { return localNuid.Valid(); } }

        /// <summary>
        /// Is this node the creator of the session
        /// </summary>
        public bool Creator { get; private set; } = false;

        /// <summary>
        /// Does this node require authentication
        /// </summary>
        public bool LoginRequired { get; private set; } = false;

        /// <summary>
        /// Does this node allow join requests
        /// </summary>
        bool AllowJoin { get; set; } = true;

        /// <summary>
        /// Connected state
        /// </summary>
        bool active = false;
        public State CurrentState { get { return Suid != 0 ? State.Connected : active ? State.Connecting : State.Unconnected; } }
        public bool Connected { get { return Suid != 0; } }
        
        /// <summary>
        /// Is connected to global session
        /// </summary>
        public bool GlobalSession { get { return suid == 1; } }

        /// <summary>
        /// Result from a join
        /// </summary>
        public enum JoinResult : byte
        {
            Accepted,
            PasswordRequired,
            LoginRequired
        }

        /// <summary>
        /// Current join result
        /// </summary>
        public JoinResult ActiveJoinResult { get; private set; } = JoinResult.Accepted;

        /// <summary>
        /// Create new network
        /// </summary>
        public void Create(bool globalSession, uint passwordHash, bool loginRequired)
        {
            // check for valid ID
            if (IsOpen && localNuid.Valid())
            {
                // check if global
                if (globalSession)
                {
                    // global ID
                    suid = 1;
                    // no password
                    this.passwordHash = 0;
                }
                else
                {
                    // session ID
                    suid = (uint)Stopwatch.GetTimestamp();
                    // find unused suid (0 = unassigned, 1 = global)
                    while (suid <= 1)
                    {
                        // try next
                        suid++;
                    }
                    // store password
                    this.passwordHash = passwordHash;
                    // this node is the creator
                    Creator = true;
                    // login required
                    LoginRequired = loginRequired;
                    // check if login required
                    if (LoginRequired)
                    {
                        // load credentials
                        LoadCredentials();
                        // watch the password file
                        passwordWatcher = new PasswordWatcher(main);
                        // watch the for import file
                        emailWatcher = new EmailWatcher(main);
                    }
                }
                // allow joins
                AllowJoin = true;
                // now active
                active = true;
            }
        }

        /// <summary>
        /// Join an existing network
        /// </summary>
        /// <param name="endPoint">Address of a node in the network</param>
        public void Join(IPEndPoint endPoint, uint passwordHash)
        {
            // check for valid ID
            if (IsOpen && localNuid.Valid())
            {
                // prepare message
                PrepareInternalMessage(new Nuid(), true);
                // add message ID
                sendWriter.Write((short)MESSAGE_ID.Join);
                // add password hash
                sendWriter.Write(passwordHash);
                // send message
                Send(endPoint);
                // store password
                this.passwordHash = passwordHash;
                // reset password fail
                ActiveJoinResult = JoinResult.Accepted;
                // reset login fail
                ActiveLoginResult = LoginResult.Accepted;
                // not the creator
                Creator = false;
                // allow joins
                AllowJoin = true;
                // now active
                active = true;
            }
        }

        /// <summary>
        /// Login to an existing network
        /// </summary>
        /// <param name="endPoint">Address of a node in the network</param>
        public void Login(IPEndPoint endPoint, string email, uint hash, bool verify)
        {
            // check for valid ID
            if (IsOpen && localNuid.Valid())
            {
                // prepare message
                PrepareInternalMessage(new Nuid(), true);
                // add message ID
                sendWriter.Write((short)MESSAGE_ID.Login);
                // add email
                sendWriter.Write(email);
                // add password hash
                sendWriter.Write(hash);
                // add verify flag
                sendWriter.Write(verify);
                // send message
                Send(endPoint);
                // store password
                passwordHash = 0;
                // reset password fail
                ActiveJoinResult = JoinResult.Accepted;
                // reset login fail
                ActiveLoginResult = LoginResult.Accepted;
                // not the creator
                Creator = false;
                // allow joins
                AllowJoin = false;
                // now active
                active = true;
            }
        }

        /// <summary>
        /// Leave a network
        /// </summary>
        public void Leave()
        {
            if (IsOpen)
            {
                // prepare message
                PrepareInternalMessage(new Nuid(), false);

                // add message ID
                sendWriter.Write((short)MESSAGE_ID.Leave);
                // add suid
                sendWriter.Write(suid);

                // send message to all nodes
                Broadcast();

                // remove guaranteed messages
                guaranteedInList.Clear();
                guaranteedOutList.Clear();

                // notify application
                if (nodeLeave != null)
                {
                    // for all nodes
                    foreach (var node in nodes)
                    {
                        // notify application
                        nodeLeave(node.Key);
                    }
                }

                // clear node list
                nodes.Clear();

                // unallocated suid
                suid = 0;
                // no longer active
                active = false;
                // reset password
                passwordHash = 0;
                // not the creator
                Creator = false;
                // close watchers
                passwordWatcher = null;
                emailWatcher = null;
                // allow joins
                AllowJoin = true;
            }
        }

#region Pulse

        /// <summary>
        /// Number of seconds between pulses
        /// </summary>
        static readonly int PULSE_INTERVAL = 1;

        /// <summary>
        /// Time at which to issue the next pulse
        /// </summary>
        DateTime nextPulse = DateTime.UtcNow;

        /// <summary>
        /// Send pulse message to other nodes
        /// </summary>
        void DoPulse()
        {
            // check if connected
            if (Connected)
            {
                // check if time for next pulse
                if (DateTime.UtcNow > nextPulse)
                {
                    // prepare message
                    PrepareInternalMessage(new Nuid(), false);
                    // add message ID
                    sendWriter.Write((short)MESSAGE_ID.Pulse);
                    // add suid
                    sendWriter.Write(suid);
                    // add time now
                    sendWriter.Write(Stopwatch.GetTimestamp());
                    // add flags
                    byte flags = 0x00;
                    flags |= lowBandwidth ? (byte)FLAG_LOW_BANDWIDTH : (byte)0x00;
                    sendWriter.Write(flags);
                    // broadcast pulse message
                    Broadcast();

                    // next pulse
                    nextPulse = DateTime.UtcNow.AddSeconds(PULSE_INTERVAL);
                    nodeDebug?.Invoke("NETWORK: Send Pulse");
                }
            }
        }

#endregion

#region Routing

        /// <summary>
        /// Number of seconds between pathfinder message
        /// </summary>
        static readonly int PATHFINDER_INTERVAL = 5;

        /// <summary>
        /// Time at which to issue the next pathfinder message
        /// </summary>
        DateTime nextPathfinder = DateTime.UtcNow;

        /// <summary>
        /// count of pathfinder sends
        /// </summary>
        int pathfinderCount = 0;

        /// <summary>
        /// Maximum number of pathfinder nodes
        /// </summary>
        const int MAX_PATHFINDER_NODES = 100;

        /// <summary>
        /// Maximum number of routing nodes
        /// </summary>
        const int MAX_ROUTING_NODES = 10;

        /// <summary>
        /// List of current routing nodes
        /// </summary>
        readonly Dictionary<Nuid, DateTime> routingNodes = [];

        /// <summary>
        /// Remove list
        /// </summary>
        readonly List<Nuid> removeRoutingList = [];

        /// <summary>
        /// Information
        /// </summary>
        public int RoutingNodeCount { get { return routingNodes.Count; } }

        /// <summary>
        /// Process routing nodes
        /// </summary>
        void DoRouting()
        {
            // check if connected
            if (Connected)
            {
                // check if time for next pathfinder
                if (DateTime.UtcNow > nextPathfinder)
                {
                    // update count
                    pathfinderCount++;
                    // occasionally check for direct path
                    bool checkDirect = (pathfinderCount & 0x7) == 0;

                    // prepare message
                    PrepareInternalMessage(new Nuid(), false);
                    // add message ID
                    sendWriter.Write((short)MESSAGE_ID.Pathfinder);
                    // add suid
                    sendWriter.Write(suid);
                    // save buffer position
                    long countPosition = sendBuffer.Position;
                    // placeholder count
                    ushort count = 0;
                    sendWriter.Write(count);
                    // for each node
                    foreach (var node in nodes)
                    {
                        // check for reestablish
                        if (node.Value.sendEstablished == false || (checkDirect && node.Value.Direct == false))
                        {
                            // add target nuid
                            node.Key.Write(sendWriter);
                            // update count
                            count++;
                            // check for maximum count
                            if (count >= MAX_PATHFINDER_NODES) break;
                        }
                    }
                    // modify count
                    sendBuffer.Position = countPosition;
                    sendWriter.Write(count);
                    // check for any nodes not established
                    if (count > 0)
                    {
                        // for each node
                        foreach (var node in nodes)
                        {
                            // check for direct connection
                            if (checkDirect || node.Value.sendEstablished && node.Value.Direct)
                            {
                                // send to node
                                Send(node.Value.endPoint);
                                nodeDebug?.Invoke("NETWORK: Routing " + node.Key + " " + node.Value.endPoint + " " + checkDirect + " " + node.Value.sendEstablished);
                            }
                        }
                    }

                    // next pathfinder
                    nextPathfinder = DateTime.UtcNow.AddSeconds(PATHFINDER_INTERVAL);
                }

                // for each routing node
                foreach (var node in routingNodes)
                {
                    // check if expired
                    if (DateTime.Now > node.Value)
                    {
                        // add to remove list
                        removeRoutingList.Add(node.Key);
                    }
                }

                // for each remove
                foreach (var nuid in removeRoutingList)
                {
                    // check if in list
                    // remove node from routing
                    routingNodes.Remove(nuid);
                }

                // clear list
                removeRoutingList.Clear();
            }
        }

#endregion

#region Credentials

        /// <summary>
        /// Password watcher
        /// </summary>
        class PasswordWatcher : FileSystemWatcher
        {
            readonly Main main;

            public PasswordWatcher(Main main) : base(main.documentsPath)
            {
                this.main = main;
                // set up for password file
                Changed += OnChanged;
                //string sc = Program.Code("password.txt", true, 1234);
                Filter = Program.Code("jDJ~>Jj3.\\\"d", false, 1234);
                EnableRaisingEvents = true;
            }

            static void OnChanged(object sender, FileSystemEventArgs e)
            {
                Thread.Sleep(1000);
                // check for valid main
                if (sender is PasswordWatcher watcher)
                {
                    // reload credentials
                    watcher.main.network.localNode.LoadCredentials();
                }
            }
        }

        // password watched
        PasswordWatcher passwordWatcher = null;

        /// <summary>
        ///  watcher
        /// </summary>
        class EmailWatcher : FileSystemWatcher
        {
            readonly Main main;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="path"></param>
            public EmailWatcher(Main main) : base(main.documentsPath)
            {
                this.main = main;
                // set up for password file
                Changed += OnChanged;
                //string sc = Program.Code("*.csv", true, 1234);
                Filter = Program.Code("vsS\\)", false, 1234);
                EnableRaisingEvents = true;
            }

            /// <summary>
            /// Password file change event
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            static void OnChanged(object sender, FileSystemEventArgs e)
            {
                Thread.Sleep(1000);
                // check for valid main
                if (sender is EmailWatcher watcher)
                {
                    // validate emails
                    watcher.main.network.localNode.ValidateCredentials(e.FullPath);
                }
            }
        }

        // email watched
        EmailWatcher emailWatcher = null;

        /// <summary>
        /// Login result
        /// </summary>
        public enum LoginResult : byte
        {
            Accepted,
            InvalidAddress,
            VerifyPassword,
            InvalidPassword
        }

        /// <summary>
        /// Current login result
        /// </summary>
        public LoginResult ActiveLoginResult { get; private set; } = LoginResult.Accepted;

        /// <summary>
        /// List of valid credentials
        /// </summary>
        readonly Dictionary<System.Net.Mail.MailAddress, uint> credentials = [];

        /// <summary>
        /// Generate hashed name
        /// </summary>
        static public string GenerateName(string str)
        {
            // 16 letters
            const string consonants = "CDFGHJKLNPRSTVXZ";
            // create hash from email
            uint hash = HashString(str);
            // construct name
            string name = "";
            name += consonants[(int)(hash & 0xf)];
            name += consonants[(int)((hash >> 4) & 0xf)];
            name += consonants[(int)((hash >> 8) & 0xf)];
            name += ((int)(hash >> 12) % 1000).ToString("D3", CultureInfo.InvariantCulture);
            // get nickname
            return name;
        }

        /// <summary>
        /// Load all credentials
        /// </summary>
        public void LoadCredentials()
        {
            // filename
            string filename = Path.Combine(main.documentsPath, "password.txt");
            // reader
            StreamReader reader = null;
            try
            {
                // clear existing credentials
                credentials.Clear();

                // check if file exists
                if (File.Exists(filename))
                {
                    // open file
                    reader = new StreamReader(filename);

                    // password line
                    string line;
                    // for each line the file
                    while ((line = reader.ReadLine()) != null)
                    {
                        // parse line
                        string[] parts = line.Split('|');
                        // get email
                        if (parts.Length > 0)
                        {
                            try
                            {
                                // get email address
                                System.Net.Mail.MailAddress address = new(parts[0]);

                                // check for three three part entry
                                if (parts.Length == 2 && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint hash))
                                {
                                    // update password
                                    credentials[address] = hash;
                                }
                                // check for password hash
                                else if (parts.Length >= 3 && uint.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out hash))
                                {
                                    // update password
                                    credentials[address] = hash;
                                }
                                else
                                {
                                    // update without password
                                    credentials[address] = 0;
                                }
                            }
                            catch (Exception ex)
                            {
                                // error
                                nodeError?.Invoke("ERROR: Invalid credentials - " + parts[0] + " - " + ex.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // error
                nodeError?.Invoke("ERROR: Reading password file - " + ex.Message);
            }
            finally
            {
                // close file
                reader?.Close();
            }
        }

        /// <summary>
        /// Save all passwords
        /// </summary>
        void SaveCredentials()
        {
            // filename
            string filename = Path.Combine(main.documentsPath, "password.txt");
            // writer
            StreamWriter writer = null;
            try
            {
                // open file
                writer = new StreamWriter(filename);
                // for all credentials
                foreach (var entry in credentials)
                {
                    // write entry
                    writer.WriteLine(entry.Key + "|" + GenerateName(entry.Key.ToString().ToUpper()) + "|" + entry.Value);
                }
            }
            catch (Exception ex)
            {
                // error
                nodeError?.Invoke("ERROR: Writing password file - " + ex.Message);
            }
            finally
            {
                // close file
                writer?.Close();
            }
        }

        /// <summary>
        /// Validate the credentials
        /// </summary>
        void ValidateCredentials(string path)
        {
            // list of addresses to import
            List<System.Net.Mail.MailAddress> importList = [];

            // reader
            StreamReader reader = null;
            try
            {
                // check if file exists
                if (File.Exists(path))
                {
                    // open file
                    reader = new StreamReader(path);

                    // entry line
                    string line;
                    // for each line the file
                    while ((line = reader.ReadLine()) != null)
                    {
                        // parse line
                        string[] parts = line.Split(',');
                        // get email
                        if (parts.Length > 1)
                        {
                            try
                            {
                                // add to import list
                                importList.Add(new System.Net.Mail.MailAddress(parts[1]));
                            }
                            catch { }
                        }
                    }

                    // for each imported email
                    foreach (var address in importList)
                    {
                        // check if missing in credentials
                        if (credentials.ContainsKey(address) == false)
                        {
                            // add to credentials
                            credentials.Add(address, 0);
                        }
                    }

                    // remove list
                    List<System.Net.Mail.MailAddress> removeList = [];

                    // for all credentials
                    foreach (var entry in credentials)
                    {
                        // check if credentials are not in the import list
                        if (importList.Contains(entry.Key) == false)
                        {
                            // add to remove list
                            removeList.Add(entry.Key);
                        }
                    }

                    // for all addresses in the remove list
                    foreach (var address in removeList)
                    {
                        // remove from credentials
                        credentials.Remove(address);
                    }

                    // save credentials
                    SaveCredentials();
                }
            }
            catch (Exception ex)
            {
                // error
                nodeError?.Invoke("ERROR: Reading password file - " + ex.Message);
            }
            finally
            {
                // close file
                reader?.Close();
            }

            try
            {
                // delete file
                File.Delete(path);
            }
            catch { }
        }

#endregion
    }
}
