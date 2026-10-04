using System.Collections.Generic;
using System.Linq;
using JoinFS.Properties;
using JoinFS.UI.Services;
using UiEntry = JoinFS.UI.Models.AddressBookEntry;

namespace JoinFS.Live
{
    /// <summary>
    /// The address book of the forms (bookmarks2.txt) as the new UI's list of hubs. The built-in Global entry is added in front,
    /// since joining Global is its own button in the old window. Which entry is picked is the old "join address".
    /// </summary>
    class LiveAddressBookStore : IAddressBookStore
    {
        public const string GLOBAL_NAME = "Global";
        const string GLOBAL_ADDRESS = "Global hubs mesh";

        readonly Main main;

        public LiveAddressBookStore(Main main)
        {
            this.main = main;
        }

        /// <summary>
        /// Global first, then the bookmarks as they are. A picked name that is not there falls back to the first entry.
        /// </summary>
        public static (IReadOnlyList<UiEntry> Entries, string SelectedName) Compose(IEnumerable<(string Name, string Address)> bookmarks, string pickedName)
        {
            List<UiEntry> entries = [new UiEntry(GLOBAL_NAME, GLOBAL_ADDRESS, BuiltIn: true)];
            entries.AddRange(bookmarks.Select(b => new UiEntry(b.Name, b.Address)));
            string selected = entries.Any(e => e.Name == pickedName) ? pickedName : entries[0].Name;
            return (entries, selected);
        }

        public (IReadOnlyList<UiEntry> Entries, string SelectedName) Load()
        {
            (string, string)[] bookmarks;
            lock (main.conch)
            {
                bookmarks = main.addressBook.entries.Select(e => (e.name, e.address)).ToArray();
            }
            return Compose(bookmarks, Settings.Default.JoinAddress);
        }

        public void Save(IReadOnlyList<UiEntry> entries, string selectedName)
        {
            lock (main.conch)
            {
                // the same entries the old form kept; Save() writes them out and Load() resolves their end points again
                main.addressBook.entries.Clear();
                foreach (UiEntry entry in entries.Where(e => !e.BuiltIn))
                {
                    main.addressBook.entries.Add(new AddressBook.AddressBookEntry { name = entry.Name, address = entry.Address });
                }
                main.addressBook.Save();
                main.addressBook.Load();
            }

            if (selectedName != null)
            {
                Settings.Default.JoinAddress = selectedName;
                Settings.Default.Save();
            }
        }
    }
}
