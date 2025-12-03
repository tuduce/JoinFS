using System;
using System.Collections.Generic;
using System.Net;
#if !CONSOLE
using System.Windows.Forms;
#endif
using System.IO;
using System.Text;


namespace JoinFS
{
    public class AddressBook
    {
        /// <summary>
        /// Address book file name
        /// </summary>
        const string ADDRESSBOOK_FILE = "bookmarks.txt";
        const string ADDRESSBOOK2_FILE = "bookmarks2.txt";

        /// <summary>
        /// Link to main form
        /// </summary>
        Main main;

        /// <summary>
        /// Address
        /// </summary>
        public class AddressBookEntry
        {
            /// <summary>
            /// Name of the address
            /// </summary>
            public string name = "";
            /// <summary>
            /// Address
            /// </summary>
            public string address = "";
            /// <summary>
            /// User ID
            /// </summary>
            public uint uuid = 0;
            /// <summary>
            /// Endpoint of address
            /// </summary>
            public IPEndPoint endPoint = new IPEndPoint(0, 0);

            /// <summary>
            /// Node details
            /// </summary>
            public bool online = false;
            public double offlineTime = 0.0;
        }

        /// <summary>
        /// List of entries
        /// </summary>
        public List<AddressBookEntry> entries = new List<AddressBookEntry>();

        /// <summary>
        /// Constructor
        /// </summary>
        public AddressBook(Main main)
        {
            // set main form
            this.main = main;

            Load();
        }

        /// <summary>
        /// Load address book
        /// </summary>
        public void Load()
        {
            try
            {
                // open file
                StreamReader reader = null;
                // check for new entries
                if (File.Exists(Path.Combine(main.storagePath, ADDRESSBOOK2_FILE)))
                {
                    // open file
                    reader = File.OpenText(Path.Combine(main.storagePath, ADDRESSBOOK2_FILE));
                }
                else if (File.Exists(Path.Combine(main.storagePath, ADDRESSBOOK_FILE)))
                {
                    // open file
                    reader = File.OpenText(Path.Combine(main.storagePath, ADDRESSBOOK_FILE));
                }

                // check for opened file
                if (reader != null)
                {
                    // clear list
                    entries.Clear();

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // successful read
                        bool success = true;
                        // address details
                        string name = "";
                        string address = "";
                        // parse line
                        string[] lineParts = line.Split('=');
                        // check for two parts
                        if (lineParts.Length == 2 && lineParts[1].Length > 0)
                        {
                            // get address name
                            name = lineParts[0];
                            // get address
                            address = lineParts[1];
                        }
                        else
                        {
                            success = false;
                        }

                        // check for success
                        if (success)
                        {
                            // new entry
                            AddressBookEntry entry = new AddressBookEntry();
                            // set name
                            entry.name = name;
                            // set address
                            entry.address = address;
#if NO_HUBS
                            // use entry address
                            main.network.MakeEndPoint(Network.DecodeIP(entry.address), Network.DEFAULT_PORT, out entry.endPoint);
#else
                            // set uuid
                            entry.uuid = Network.MakeUuid(address);
                            // check if not uuid
                            if (entry.uuid == 0 && entry.address.Contains("."))
                            {
                                // attempt to make endpoint
                                main.network.MakeEndPoint(address, Network.DEFAULT_PORT, out entry.endPoint);
                            }
#endif
                            // add to list
                            entries.Add(entry);
                        }
                    }
                    reader.Close();

                    main.MonitorEvent("Loaded " + entries.Count + " entries from the address book");
                }

#if !CONSOLE
                // update combo list
                main.mainForm ?. RefreshComboList();
#endif
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }

        /// <summary>
        /// Save address book
        /// </summary>
        public void Save()
        {
            try
            {
                // open file
                StreamWriter writer = new StreamWriter(Path.Combine(main.storagePath, ADDRESSBOOK2_FILE));
                if (writer != null)
                {
                    // for each entry
                    foreach (var entry in entries)
                    {
                        // write address
                        writer.WriteLine(entry.name + "=" + entry.address);
                    }
                }
                writer.Close();

                main.MonitorEvent("Saved " + entries.Count + " entries in the address book");
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }
    }
}
