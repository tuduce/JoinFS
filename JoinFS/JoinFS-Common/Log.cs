using System;
using System.Collections.Generic;
using System.Threading;
using System.Text;
using System.Net;
using System.IO;

namespace JoinFS
{
    public class Log
    {
        const string LOG_FILE = "log.dat";

        /// <summary>
        /// Link to main form
        /// </summary>
        Main main;

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="mainForm">Main form</param>
        public Log(Main main)
        {
            // set main form
            this.main = main;
        }

        #region Node List

        /// <summary>
        /// Generic list of nodes
        /// </summary>
        class NodeList
        {
            /// <summary>
            /// List of guids
            /// </summary>
            public Dictionary<Guid, bool> guidList = new Dictionary<Guid, bool>();
            /// <summary>
            /// List of addresses
            /// </summary>
            public Dictionary<IPAddress, bool> addressList = new Dictionary<IPAddress, bool>();

            /// <summary>
            /// Clear node list
            /// </summary>
            public void Clear()
            {
                // clear lists
                guidList.Clear();
                addressList.Clear();
            }

            /// <summary>
            /// Read list from stream
            /// </summary>
            /// <param name="reader">Binary reader</param>
            public void Read(BinaryReader reader)
            {
                // read size of guid list
                ushort guidLength = reader.ReadUInt16();
                // for each guid
                for (int count = 0; count < guidLength; count++)
                {
                    // read guid
                    byte[] guidBytes = reader.ReadBytes(16);
                    guidList.Add(new Guid(guidBytes), false);
                }

                // read size of address list
                ushort addressLength = reader.ReadUInt16();
                // for each address
                for (int count = 0; count < addressLength; count++)
                {
                    // read address
                    byte[] addressBytes = reader.ReadBytes(4);
                    addressList.Add(new IPAddress(addressBytes), false);
                }
            }

            /// <summary>
            /// Write list to stream
            /// </summary>
            /// <param name="writer">Writer</param>
            public void Write(BinaryWriter writer)
            {
                // write length
                writer.Write((ushort)guidList.Count);
                // for each guid
                foreach (var guid in guidList.Keys)
                {
                    // write guid
                    writer.Write(guid.ToByteArray(), 0, 16);
                }

                // write length
                writer.Write((ushort)addressList.Count);
                // for each address
                foreach (var address in addressList.Keys)
                {
                    // write address
                    writer.Write(address.GetAddressBytes(), 0, 4);
                }
            }
        }

        /// <summary>
        /// Add entry to node list
        /// </summary>
        /// <param name="nodeList">Node List</param>
        /// <param name="guid">Guid of node</param>
        void AddToNodeList(NodeList nodeList, LocalNode.Nuid nuid)
        {
            // get node Guid
            Guid guid = main.network.GetNodeGuid(nuid);
            // check for valid guid
            if (guid.Equals(Guid.Empty) == false && nodeList.guidList.ContainsKey(guid) == false)
            {
                // add guid to list
                nodeList.guidList.Add(guid, false);
            }
            // get address
            if (main.network.localNode.Connected && main.network.localNode.GetNodeEndPoint(nuid, out IPEndPoint endPoint) && nodeList.addressList.ContainsKey(endPoint.Address) == false)
            {
                // add address to list
                nodeList.addressList.Add(endPoint.Address, false);
            }
        }

        /// <summary>
        /// Add entry to node list
        /// </summary>
        /// <param name="nodeList">Node List</param>
        /// <param name="guid">Guid of node</param>
        void AddToNodeList(NodeList nodeList, ref Guid guid)
        {
            // check for valid guid
            if (guid.Equals(Guid.Empty) == false && nodeList.guidList.ContainsKey(guid) == false)
            {
                // add guid to list
                nodeList.guidList.Add(guid, false);
            }
        }

        /// <summary>
        /// Add entry to node list
        /// </summary>
        /// <param name="nodeList">Node List</param>
        void AddToNodeList(NodeList nodeList, IPAddress address)
        {
            // check if not already in list
            if (nodeList.addressList.ContainsKey(address) == false)
            {
                // add address to list
                nodeList.addressList.Add(address, false);
            }
        }

        /// <summary>
        /// Remove entry from node list
        /// </summary>
        /// <param name="nodeList">Node list</param>
        /// <param name="guid">Guid of node</param>
        void RemoveFromNodeList(NodeList nodeList, LocalNode.Nuid nuid)
        {
            // get guid
            Guid guid = main.network.GetNodeGuid(nuid);
            // check if in list
            if (nodeList.guidList.ContainsKey(guid))
            {
                // remove from guid list
                nodeList.guidList.Remove(guid);
            }
            // get address
            if (main.network.localNode.Connected && main.network.localNode.GetNodeEndPoint(nuid, out IPEndPoint endPoint))
            {
                if (nodeList.addressList.ContainsKey(endPoint.Address))
                {
                    // remove from address list
                    nodeList.addressList.Remove(endPoint.Address);
                }
            }
        }

        /// <summary>
        /// Remove entry from node list
        /// </summary>
        /// <param name="nodeList">Node list</param>
        /// <param name="guid">Guid of node</param>
        void RemoveFromNodeList(NodeList nodeList, ref Guid guid)
        {
            // check if in list
            if (nodeList.guidList.ContainsKey(guid))
            {
                // remove from guid list
                nodeList.guidList.Remove(guid);
            }
        }

        /// <summary>
        /// Remove entry from node list
        /// </summary>
        /// <param name="nodeList">Node list</param>
        void RemoveFromNodeList(NodeList nodeList, IPAddress address)
        {
            // check if in list
            if (nodeList.addressList.ContainsKey(address))
            {
                // remove from address list
                nodeList.addressList.Remove(address);
            }
        }

        /// <summary>
        /// Check if node is in list
        /// </summary>
        /// <returns></returns>
        bool InNodeList(NodeList nodeList, LocalNode.Nuid nuid)
        {
            // check for guid
            if (nodeList.guidList.ContainsKey(main.network.GetNodeGuid(nuid)))
            {
                // is in list
                return true;
            }
            // get address
            if (main.network.localNode.Connected && main.network.localNode.GetNodeEndPoint(nuid, out IPEndPoint endPoint))
            {
                // check for address
                if (nodeList.addressList.ContainsKey(endPoint.Address))
                {
                    // is in list
                    return true;
                }
            }

            // not in list
            return false;
        }

        /// <summary>
        /// Check if node is in list
        /// </summary>
        /// <returns></returns>
        bool InNodeList(NodeList nodeList, ref Guid guid)
        {
            // check for guid
            return nodeList.guidList.ContainsKey(guid);
        }

        /// <summary>
        /// Check if node is in list
        /// </summary>
        /// <returns></returns>
        bool InNodeList(NodeList nodeList, IPAddress address)
        {
            // check for address
            return nodeList.addressList.ContainsKey(address);
        }

        #endregion

        #region Name List

        /// <summary>
        /// Generic list of names
        /// </summary>
        class NameList
        {
            /// <summary>
            /// List of names
            /// </summary>
            public Dictionary<string, bool> nameList = new Dictionary<string, bool>();

            /// <summary>
            /// Clear name list
            /// </summary>
            public void Clear()
            {
                // clear lists
                nameList.Clear();
            }

            /// <summary>
            /// Read list from stream
            /// </summary>
            /// <param name="reader">Binary reader</param>
            public void Read(BinaryReader reader)
            {
                // read size of name list
                ushort listLength = reader.ReadUInt16();
                // for each name
                for (int count = 0; count < listLength; count++)
                {
                    // add name to list
                    nameList.Add(reader.ReadString(), false);
                }
            }

            /// <summary>
            /// Write list to stream
            /// </summary>
            /// <param name="writer">Writer</param>
            public void Write(BinaryWriter writer)
            {
                // write list length
                writer.Write((ushort)nameList.Count);
                // for each name
                foreach (var name in nameList.Keys)
                {
                    // write name
                    writer.Write(name);
                }
            }

            /// <summary>
            /// Add new entry
            /// </summary>
            /// <param name="name">Name</param>
            public void Add(string name)
            {
                // check if name is not already in list
                if (Contains(name) == false)
                {
                    // add to list
                    nameList.Add(name, false);
                }
            }

            /// <summary>
            /// Remove entry
            /// </summary>
            /// <param name="name">Name</param>
            public void Remove(string name)
            {
                // check if name is in list
                if (Contains(name))
                {
                    // remove from list
                    nameList.Remove(name);
                }
            }

            /// <summary>
            /// Does the list contain a name
            /// </summary>
            /// <param name="name">Name</param>
            /// <returns>Is in list</returns>
            public bool Contains(string name)
            {
                return nameList.ContainsKey(name);
            }
        }

        #endregion

        #region Ignore Node List

        /// <summary>
        /// List of nodes to ignore
        /// </summary>
        NodeList ignoreList = new NodeList();

        /// <summary>
        /// Ignore a node
        /// </summary>
        /// <param name="guid"></param>
        /// <param name="address"></param>
        public void AddIgnoreNode(LocalNode.Nuid nuid)
        {
            // add node to ignore list
            AddToNodeList(ignoreList, nuid);
            // remove objects from simulator
            main.sim ?. RemoveObjectsFromSim(nuid);
            // save log
            Save();
        }

        /// <summary>
        /// Ignore a node
        /// </summary>
        /// <param name="guid">Guid</param>
        public void AddIgnoreNode(ref Guid guid)
        {
            // add node to ignore list
            AddToNodeList(ignoreList, ref guid);
            // save log
            Save();
        }

        /// <summary>
        /// Ignore a node
        /// </summary>
        /// <param name="guid">Guid</param>
        public void AddIgnoreNode(IPAddress address)
        {
            // add node to ignore list
            AddToNodeList(ignoreList, address);
            // save log
            Save();
        }

        /// <summary>
        /// Stop ignoring a node
        /// </summary>
        /// <param name="guid"></param>
        /// <param name="address"></param>
        public void RemoveIgnoreNode(LocalNode.Nuid nuid)
        {
            // remove node from ignore list
            RemoveFromNodeList(ignoreList, nuid);
            // save log
            Save();
        }

        /// <summary>
        /// Stop ignoring a node
        /// </summary>
        /// <param name="guid"></param>
        public void RemoveIgnoreNode(ref Guid guid)
        {
            // remove node from ignore list
            RemoveFromNodeList(ignoreList, ref guid);
            // save log
            Save();
        }

        /// <summary>
        /// Stop ignoring a node
        /// </summary>
        /// <param name="guid"></param>
        /// <param name="address"></param>
        public void RemoveIgnoreNode(IPAddress address)
        {
            // remove node from ignore list
            RemoveFromNodeList(ignoreList, address);
            // save log
            Save();
        }

        /// <summary>
        /// Check if node should be ignored
        /// </summary>
        /// <param name="guid">Guid</param>
        /// <param name="address">Address</param>
        /// <returns>Should be ignored</returns>
        public bool IgnoreNode(LocalNode.Nuid nuid)
        {
            // check list
            return InNodeList(ignoreList, nuid);
        }

        /// <summary>
        /// Check if node should be ignored
        /// </summary>
        /// <param name="guid">Guid</param>
        public bool IgnoreNode(ref Guid guid)
        {
            // check list
            return InNodeList(ignoreList, ref guid);
        }

        /// <summary>
        /// Check if node should be ignored
        /// </summary>
        /// <param name="address">Address</param>
        public bool IgnoreNode(IPAddress address)
        {
            // check list
            return InNodeList(ignoreList, address);
        }

        #endregion

        #region Share Cockpit List

        /// <summary>
        /// List of nodes to share cockpit
        /// </summary>
        NodeList shareCockpitList = new NodeList();

        /// <summary>
        /// Share cockpit with a node
        /// </summary>
        public void AddShareCockpit(LocalNode.Nuid nuid)
        {
            // add node to cockpit list
            AddToNodeList(shareCockpitList, nuid);
            // save log
            Save();
        }

        /// <summary>
        /// Stop sharing cockpit with a node
        /// </summary>
        public void RemoveShareCockpit(LocalNode.Nuid nuid)
        {
            // remove node from cockpit list
            RemoveFromNodeList(shareCockpitList, nuid);
            // save log
            Save();
        }

        /// <summary>
        /// Check whether to share cockpit with node
        /// </summary>
        /// <param name="guid">Guid</param>
        public bool ShareCockpit(LocalNode.Nuid nuid)
        {
            // check list
            return InNodeList(shareCockpitList, nuid);
        }

        #endregion

        #region Ignore Names

        /// <summary>
        /// List of names to ignore
        /// </summary>
        NameList ignoreNameList = new NameList();

        /// <summary>
        /// Ignore name
        /// </summary>
        /// <param name="name">Name to ignore</param>
        public void AddIgnoreName(string name)
        {
            // add name to list
            ignoreNameList.Add(name);
            // remove objects from simulator
            main.sim ?. RemoveObjectsFromSim(name);
            // save log
            Save();
        }

        /// <summary>
        /// Stop ignoring name
        /// </summary>
        /// <param name="name">Name to stop ignoring</param>
        public void RemoveIgnoreName(string name)
        {
            // remove name from list
            ignoreNameList.Remove(name);
            // save log
            Save();
        }

        /// <summary>
        /// Check whether to share cockpit with node
        /// </summary>
        /// <param name="guid">Guid</param>
        /// <returns>Name is ignored</returns>
        public bool IgnoreName(string name)
        {
            // check list
            return ignoreNameList.Contains(name);
        }

        #endregion

        #region Broadcast Names

        /// <summary>
        /// List of names to broadcast
        /// </summary>
        NameList broadcastNameList = new NameList();

        /// <summary>
        /// Broadcast name
        /// </summary>
        /// <param name="name">Name to broadcast</param>
        public void AddBroadcastName(string name)
        {
            // add name to list
            broadcastNameList.Add(name);
            // save log
            Save();
        }

        /// <summary>
        /// Stop broadcasting name
        /// </summary>
        /// <param name="name">Name to stop broadcasting</param>
        public void RemoveBroadcastName(string name)
        {
            // remove name from list
            broadcastNameList.Remove(name);
            // save log
            Save();
        }

        /// <summary>
        /// Check whether to broadcast name
        /// </summary>
        /// <param name="name">Name</param>
        /// <returns>Name is broadcast</returns>
        public bool BroadcastName(string name)
        {
            // check list
            return broadcastNameList.Contains(name);
        }

        #endregion

        #region Multiple Objects List

        /// <summary>
        /// List of nodes to allow multiple objects
        /// </summary>
        NodeList multipleObjectsList = new NodeList();

        /// <summary>
        /// Allow multiple objects with a node
        /// </summary>
        public void AddMultipleObjects(LocalNode.Nuid nuid)
        {
            // add node to multiple objects
            AddToNodeList(multipleObjectsList, nuid);
            // save log
            Save();
        }

        /// <summary>
        /// Stop allowing multiple objects with a node
        /// </summary>
        public void RemoveMultipleObjects(LocalNode.Nuid nuid)
        {
            // remove node from multiple objects list
            RemoveFromNodeList(multipleObjectsList, nuid);
            // save log
            Save();
        }

        /// <summary>
        /// Check whether to allow multiple objects with node
        /// </summary>
        /// <param name="guid">Guid</param>
        public bool MultipleObjects(LocalNode.Nuid nuid)
        {
            // check list
            return InNodeList(multipleObjectsList, nuid);
        }

        #endregion

        #region Passwords

        /// <summary>
        /// Generic list of passwords
        /// </summary>
        class PasswordList
        {
            /// <summary>
            /// List of passwords
            /// </summary>
            public Dictionary<IPEndPoint, uint> passwordList = new Dictionary<IPEndPoint, uint>();

            /// <summary>
            /// Clear password list
            /// </summary>
            public void Clear()
            {
                // clear lists
                passwordList.Clear();
            }

            /// <summary>
            /// Read list from stream
            /// </summary>
            /// <param name="reader">Binary reader</param>
            public void Read(BinaryReader reader)
            {
                // read size of name list
                ushort listLength = reader.ReadUInt16();
                // for each name
                for (int count = 0; count < listLength; count++)
                {
                    // read address
                    byte[] addressBytes = reader.ReadBytes(4);
                    // read port
                    ushort port = reader.ReadUInt16();
                    // add name to list
                    passwordList.Add(new IPEndPoint(new IPAddress(addressBytes), port), reader.ReadUInt32());
                }
            }

            /// <summary>
            /// Write list to stream
            /// </summary>
            /// <param name="writer">Writer</param>
            public void Write(BinaryWriter writer)
            {
                // write list length
                writer.Write((ushort)passwordList.Count);
                // for each password
                foreach (var password in passwordList)
                {
                    // write address
                    writer.Write(password.Key.Address.GetAddressBytes(), 0, 4);
                    // write port
                    writer.Write((ushort)password.Key.Port);
                    // write password
                    writer.Write(password.Value);
                }
            }

            /// <summary>
            /// Add new entry
            /// </summary>
            /// <param name="endPoint">end point</param>
            /// <param name="passwordHash">password</param>
            public void Add(IPEndPoint endPoint, uint passwordHash)
            {
                // check if name is not already in list
                if (Contains(endPoint) == false)
                {
                    // add to list
                    passwordList.Add(endPoint, passwordHash);
                }
            }

            /// <summary>
            /// Remove entry
            /// </summary>
            /// <param name="nuid">Nuid</param>
            public void Remove(IPEndPoint endPoint)
            {
                // check if nuid is in list
                if (Contains(endPoint))
                {
                    // remove from list
                    passwordList.Remove(endPoint);
                }
            }

            /// <summary>
            /// Does the list contain a name
            /// </summary>
            /// <param name="nuid">Nuid</param>
            /// <returns>Is in list</returns>
            public bool Contains(IPEndPoint endPoint)
            {
                return passwordList.ContainsKey(endPoint);
            }
        }

        /// <summary>
        /// List of used passwords
        /// </summary>
        PasswordList usedPasswords = new PasswordList();

        /// <summary>
        /// Add password
        /// </summary>
        /// <param name="name">Name to broadcast</param>
        public void AddPassword(IPEndPoint endPoint, uint passwordHash)
        {
            // remove old password
            usedPasswords.Remove(endPoint);
            // add password to list
            usedPasswords.Add(endPoint, passwordHash);
            // save log
            Save();
        }

        /// <summary>
        /// Stop broadcasting name
        /// </summary>
        /// <param name="nuid">Nuid to remove</param>
        public void RemovePassword(IPEndPoint endPoint)
        {
            // remove password from list
            usedPasswords.Remove(endPoint);
            // save log
            Save();
        }

        /// <summary>
        /// Check whether to broadcast name
        /// </summary>
        /// <param name="name">Name</param>
        /// <returns>Name is broadcast</returns>
        public uint GetUsedPassword(IPEndPoint endPoint)
        {
            // check if nuid is known
            if (usedPasswords.Contains(endPoint))
            {
                // check list
                return usedPasswords.passwordList[endPoint];
            }

            // not found
            return 0;
        }

        #endregion

        /// <summary>
        /// Load lists from file
        /// </summary>
        public void Load()
        {
            BinaryReader reader = null;

            // try ten times
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    // check for matching file
                    if (File.Exists(main.storagePath + Path.DirectorySeparatorChar + LOG_FILE))
                    {
                        // clear lists
                        ignoreList.Clear();
                        shareCockpitList.Clear();
                        ignoreNameList.Clear();
                        broadcastNameList.Clear();
                        multipleObjectsList.Clear();
                        usedPasswords.Clear();

                        // open file
                        reader = new BinaryReader(File.Open(main.storagePath + Path.DirectorySeparatorChar + LOG_FILE, FileMode.Open));

                        // monitor
                        if (attempt == 1)
                        {
                            main.MonitorEvent("Opened 'log.dat'");
                        }
                        else
                        {
                            main.MonitorEvent("Opened 'log.dat' on attempt " + attempt);
                        }

                        // read ignore list
                        ignoreList.Read(reader);
                        // monitor
                        main.MonitorEvent("Loaded " + ignoreList.addressList.Count + " address(es) to be ignored");
                        main.MonitorEvent("Loaded " + ignoreList.guidList.Count + " user(s) to be ignored");

                        // read cockpit list
                        shareCockpitList.Read(reader);
                        // monitor
                        main.MonitorEvent("Loaded " + shareCockpitList.guidList.Count + " user(s) to share cockpit");
                        
                        // check for ignore name list
                        if (reader.PeekChar() != -1)
                        {
                            // read ignore name list
                            ignoreNameList.Read(reader);
                            // monitor
                            main.MonitorEvent("Loaded " + ignoreNameList.nameList.Count + " object(s) to be ignored");
                        }
                        // check for broadcast name list
                        if (reader.PeekChar() != -1)
                        {
                            // read broadcast name list
                            broadcastNameList.Read(reader);
                            // monitor
                            main.MonitorEvent("Loaded " + broadcastNameList.nameList.Count + " object(s) for broadcast");
                        }
                        // check for multiple objects list
                        if (reader.PeekChar() != -1)
                        {
                            // read multiple objects list
                            multipleObjectsList.Read(reader);
                            // monitor
                            main.MonitorEvent("Loaded " + multipleObjectsList.guidList.Count + " user(s) for multiple objects");
                        }
                        // check for used password
                        if (reader.PeekChar() != -1)
                        {
                            // read passwords
                            usedPasswords.Read(reader);
                            // monitor
                            main.MonitorEvent("Loaded " + usedPasswords.passwordList.Count + " password(s)");
                        }

                        // close file
                        reader.Close();
                        // finished
                        return;
                    }
                }
                catch (Exception ex)
                {
                    main.MonitorEvent(ex.Message);
                }
                finally
                {
                    // check for reader
                    if (reader != null)
                    {
                        // close file
                        reader.Close();
                    }
                }

                // sleep for random time
                Random random = new Random((int)DateTime.Now.Ticks);
                Thread.Sleep(random.Next(10, 100));
            }
        }

        /// <summary>
        /// Save lists to file
        /// </summary>
        public void Save()
        {
            try
            {
                // open file
                BinaryWriter writer = new BinaryWriter(File.Create(main.storagePath + Path.DirectorySeparatorChar + LOG_FILE));
                if (writer != null)
                {
                    // write ignore list
                    ignoreList.Write(writer);
                    // write cockpit list
                    shareCockpitList.Write(writer);
                    // write ignore name list
                    ignoreNameList.Write(writer);
                    // write broadcast name list
                    broadcastNameList.Write(writer);
                    // write multiple objects list
                    multipleObjectsList.Write(writer);
                    // write used passwords
                    usedPasswords.Write(writer);
                    // finished
                    writer.Close();
                }

                main.MonitorEvent("Saved data log");
            }
            catch (Exception ex)
            {
                main.MonitorEvent(ex.Message);
            }
        }
    }
}
