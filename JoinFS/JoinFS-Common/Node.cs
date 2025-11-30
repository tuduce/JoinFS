using System;
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
        class Node
        {
            /// <summary>
            /// Actual address of the node
            /// </summary>
            public IPEndPoint endPoint;
            /// <summary>
            /// Route address of the node
            /// </summary>
            public IPEndPoint routeEndPoint;
            /// <summary>
            /// Is there a direct connection to this node
            /// </summary>
            public bool Direct { get { return endPoint.Address.Equals(routeEndPoint.Address); } }
            /// <summary>
            /// Connection state of the node
            /// </summary>
            public bool sendEstablished;
            public bool receiveEstablished;
            /// <summary>
            /// Node will expire at this time
            /// </summary>
            DateTime expireTime;
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
            /// Node constructor
            /// </summary>
            /// <param name="endPoint">Address of remote node</param>
            public Node(IPEndPoint endPoint, bool receiveEstablished)
            {
                // Address of remote node
                this.endPoint = endPoint;
                // Address of remote node
                this.routeEndPoint = endPoint;
                // set receive state
                this.receiveEstablished = receiveEstablished;
                // set expire time
                expireTime = DateTime.UtcNow.AddSeconds(EXPIRE_TIME);
            }

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
        Dictionary<Nuid, Node> nodes = new Dictionary<Nuid, Node>();
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
            if (nodes.ContainsKey(nuid))
            {
                endPoint = nodes[nuid].endPoint;
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
            if (nodes.ContainsKey(nuid))
            {
                endPoint = nodes[nuid].routeEndPoint;
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
            if (nodes.ContainsKey(nuid))
            {
                // return established
                return nodes[nuid].receiveEstablished;
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
            if (nodes.ContainsKey(nuid))
            {
                // return established
                return nodes[nuid].sendEstablished;
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
            if (nodes.ContainsKey(nuid))
            {
                // return direct flag
                return nodes[nuid].Direct;
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
            if (nodes.ContainsKey(nuid))
            {
                // return flag
                return nodes[nuid].lowBandwidth;
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
            if (nodes.ContainsKey(nuid))
            {
                // return RTT
                return nodes[nuid].rtt;
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
                    Node newNode = new Node(MakeEndPoint(nuid, port), receive);

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
        MemoryStream sendBuffer;
        BinaryWriter sendWriter;
        MemoryStream receiveBuffer;
        BinaryReader receiveReader;

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
                    if (sendRecipient.Valid() && nodes.ContainsKey(sendRecipient))
                    {
                        // get endpoint
                        nodeEndPoint = nodes[sendRecipient].routeEndPoint;
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
                        // TODO: remove IP restriction
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
            if (nodes.ContainsKey(nuid))
            {
                // send to end point
                Send(nodes[nuid].routeEndPoint);
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
        List<IPAddress> banList = new List<IPAddress>();

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
                    Nuid senderNuid = new LocalNode.Nuid(receiveReader);
                    // extract recipient nuid
                    Nuid recipientNuid = new LocalNode.Nuid(receiveReader);

                    // check for sender node
                    Node senderNode = null;
                    // check if sender is known
                    if (nodes.ContainsKey(senderNuid))
                    {
                        // get node
                        senderNode = nodes[senderNuid];
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
                            if (lowBandwidth == false && nodes.ContainsKey(recipientNuid) && nodes[recipientNuid].Direct && (routingNodes.Count < MAX_ROUTING_NODES || routingNodes.ContainsKey(senderNuid)))
                            {
                                // set recipient
                                sendRecipient = recipientNuid;
                                // get message data
                                byte[] data = receiveBuffer.GetBuffer();
                                // set forward flag
                                data[FLAGS_OFFSET] |= FLAG_FORWARD;
                                // forward the message to the recipient
                                Send(nodes[recipientNuid].endPoint, data, (int)receiveBuffer.Length);

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
                                                    Nuid nuid = new Nuid(receiveReader);
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
                                                Nuid nuid = new Nuid(receiveReader);
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
                                            if (nodes.ContainsKey(senderNuid))
                                            {
                                                // read time
                                                long time = receiveReader.ReadInt64();
                                                // get target
                                                Node node = nodes[senderNuid];
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
                                                    Nuid nuid = new Nuid(receiveReader);
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
                                                    Nuid nuid = new Nuid(receiveReader);
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
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    byte[] messageData = udpClient.Receive(ref endPoint);

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

                // receive the message
                ReceiveMsg(endPoint);
            }
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
            public List<Segment> segmentList = new List<Segment>();

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
                        using (MemoryStream stream = new MemoryStream(this.segmentList[index].data))
                        {
                            // create writer for segment
                            using (BinaryWriter writer = new BinaryWriter(stream))
                            {
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
                    }
                }
                // set expire time
                expireTime = DateTime.UtcNow.AddSeconds(GUARANTEED_OUT_EXPIRE_TIME);
            }
        }

        /// <summary>
        /// List of messages being sent by guaranteed method
        /// </summary>
        List<GuaranteedMessageOut> guaranteedOutList = new List<GuaranteedMessageOut>();

        /// <summary>
        /// List of messages to be removed
        /// </summary>
        List<GuaranteedMessageOut> removeOutList = new List<GuaranteedMessageOut>();

        /// <summary>
        /// Information
        /// </summary>
        public int GuaranteedOutCount { get { return guaranteedOutList.Count; } }

        /// <summary>
        /// Incoming guaranteed message
        /// </summary>
        class GuaranteedIn
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
            public Segment[] segments;

            /// <summary>
            /// end point of sender
            /// </summary>
            public IPEndPoint nodeEndPoint;
            /// <summary>
            /// Guaranteed ID
            /// </summary>
            public int id;

            /// <summary>
            /// Time this will expire
            /// </summary>
            readonly DateTime expireTime;

            /// <summary>
            /// Has this expired
            /// </summary>
            public bool Expired { get { return DateTime.UtcNow > expireTime; } }

            /// <summary>
            /// GuaranteedDone constructor
            /// </summary>
            /// <param name="nuid">ID of node</param>
            /// <param name="id">Guaranteed ID</param>
            public GuaranteedIn(IPEndPoint nodeEndPoint, int id, int segmentCount)
            {
                // address
                this.nodeEndPoint = nodeEndPoint;
                // guaranteed ID
                this.id = id;
                // segments
                this.segments = new Segment[segmentCount];
                // set expire time
                expireTime = DateTime.UtcNow.AddSeconds(GUARANTEED_IN_EXPIRE_TIME);
            }

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
        List<GuaranteedIn> guaranteedInList = new List<GuaranteedIn>();

        /// <summary>
        /// Temporary list for removing expired done objects
        /// </summary>
        List<GuaranteedIn> removeInList = new List<GuaranteedIn>();

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
                    if (nodes.ContainsKey(message.nuid))
                    {
                        // use node endpoint
                        endPoint = nodes[message.nuid].endPoint;
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
                                // TODO: remove IP restriction
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
        List<Nuid> removeList = new List<Nuid>();

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
                            // notify application
                            nodeLeave?.Invoke(nuid);
                        }
                    }

                    // clear list
                    removeList.Clear();

                    DoPulse();
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
            // close
            Close();

            // update port
            localNuid.port = (ushort)port;

            // create UDP client
            try
            {
                udpClient = new UdpClient(localNuid.port, AddressFamily.InterNetwork);
                // check for windows
#if NET6_0_OR_GREATER
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
#endif
                {
                    // reset socket
                    uint IOC_IN = 0x80000000;
                    uint IOC_VENDOR = 0x18000000;
                    uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                    udpClient.Client.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
                }
                return true;
            }
            catch (Exception ex)
            {
                // error
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
        Main main;

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
            public void Write(BinaryWriter writer)
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
            public bool Invalid()
            {
                return ip == 0;
            }

            public bool Valid()
            {
                return Invalid() == false;
            }

            /// <summary>
            /// Convert to an end point
            /// </summary>
            /// <returns></returns>
            public IPAddress ToAddress()
            {
                byte[] bytes = new byte[4];
                bytes[0] = (byte)(ip >> 24);
                bytes[1] = (byte)(ip >> 16);
                bytes[2] = (byte)(ip >> 8);
                bytes[3] = (byte)ip;

                return new IPAddress(bytes);
            }

            /// <summary>
            /// Convert to an end point
            /// </summary>
            /// <returns></returns>
            public IPEndPoint ToEndPoint(ushort port)
            {
                return new IPEndPoint(ToAddress(), port);
            }

            /// <summary>
            /// Convert nuid to a readable string
            /// </summary>
            public override string ToString()
            {
                return Network.EncodeIP(ToEndPoint(port).ToString()) + "/" + local;
            }

            /// <summary>
            ///  Comparisons
            /// </summary>
            public override bool Equals(Object obj)
            {
                return obj is Nuid && this == (Nuid)obj;
            }
            public override int GetHashCode()
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
        Nuid localNuid = new Nuid();

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
                    passwordHash = 0;
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
        Dictionary<Nuid, DateTime> routingNodes = new Dictionary<Nuid, DateTime>();

        /// <summary>
        /// Remove list
        /// </summary>
        List<Nuid> removeRoutingList = new List<Nuid>();

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
                    if (routingNodes.ContainsKey(nuid))
                    {
                        // remove node from routing
                        routingNodes.Remove(nuid);
                    }
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
            Main main;

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
            Main main;

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
        Dictionary<System.Net.Mail.MailAddress, uint> credentials = new Dictionary<System.Net.Mail.MailAddress, uint>();

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
                                System.Net.Mail.MailAddress address = new System.Net.Mail.MailAddress(parts[0]);

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
                if (reader != null) reader.Close();
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
                if (writer != null) writer.Close();
            }
        }

        /// <summary>
        /// Validate the credentials
        /// </summary>
        void ValidateCredentials(string path)
        {
            // list of addresses to import
            List<System.Net.Mail.MailAddress> importList = new List<System.Net.Mail.MailAddress>();

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
                    List<System.Net.Mail.MailAddress> removeList = new List<System.Net.Mail.MailAddress>();

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
                if (reader != null) reader.Close();
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
