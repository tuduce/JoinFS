using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using JoinFS.Net;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The Network Hubs tab on the real hub directory: the hubs the old HubsForm listed, and what its Address Book and Ignore columns did.
    /// The app keeps the list up to date from the network; this only reads it, under the app's lock, as the form did.
    /// </summary>
    class LiveHubDirectory : IHubDirectory
    {
        const string OWN_HUB_ID = "own";

        readonly Main main;

        public LiveHubDirectory(Main main)
        {
            this.main = main;
        }

        public IReadOnlyList<HubInfo> GetHubs()
        {
            List<HubInfo> hubs = [];

            lock (main.conch)
            {
                // this node's own hub, while it is one
                if (main.settingsHub)
                {
                    int aircraft = 0;
                    if (main.sim != null)
                    {
                        aircraft = main.sim.View.Objects.Count(o => o is Sim.Aircraft && (main.sim.IsBroadcast(o) || o.owner == Sim.Obj.Owner.Network));
                    }

                    HubStatus status = main.network.Snapshot.PasswordProtected ? HubStatus.Password : main.network.Snapshot.GlobalSession ? HubStatus.Global : HubStatus.Online;
                    hubs.Add(new HubInfo(
                        OWN_HUB_ID, main.settingsHubName, status, main.network.HubHost.LocalUsers.Count, aircraft, Main.Version,
                        main.settingsHubAbout, main.settingsHubVoip, main.settingsHubEvent, "",
                        Ignored: false, Saved: false, CanJoin: false, CanIgnore: false));
                }

                foreach (var hub in main.network.Hubs.List)
                {
                    // a hub that has not answered yet has no identity
                    if (!hub.nuid.Valid())
                    {
                        continue;
                    }

                    HubStatus status = !hub.online ? HubStatus.Offline : hub.password ? HubStatus.Password : hub.globalSession ? HubStatus.Global : HubStatus.Online;
                    hubs.Add(new HubInfo(
                        IdOf(hub), hub.name, status, hub.users, hub.planes + hub.helicopters, hub.appVersion,
                        hub.about, hub.voip, hub.nextEvent, hub.endPoint.ToString(),
                        Ignored: IsIgnored(hub), Saved: SavedEntries(hub).Any(), CanJoin: hub.online, CanIgnore: true));
                }
            }

            return hubs;
        }

        /// <summary>
        /// What names a hub to the UI: its guid, or its node id before it has told us one.
        /// </summary>
        static string IdOf(HubDirectory.Hub hub) => hub.guid != Guid.Empty ? "hub:" + hub.guid : "node:" + hub.nuid;

        HubDirectory.Hub Find(string hubId) => main.network.Hubs.List.Find(h => h.nuid.Valid() && IdOf(h) == hubId);

        bool IsIgnored(HubDirectory.Hub hub) => main.log.IgnoreNode(ref hub.guid) || main.log.IgnoreNode(hub.endPoint.Address);

        /// <summary>
        /// The address book entries that are this hub: the one saved from its guid, or any at its address.
        /// </summary>
        IEnumerable<AddressBook.AddressBookEntry> SavedEntries(HubDirectory.Hub hub)
        {
            uint uuid = UserDirectory.MakeUuid(hub.guid);
            return main.addressBook.entries.Where(e =>
                uuid != 0 && e.uuid == uuid
                || !hub.endPoint.Address.Equals(IPAddress.Any) && e.endPoint.Address.Equals(hub.endPoint.Address));
        }

        public void SetIgnored(string hubId, bool ignored)
        {
            lock (main.conch)
            {
                HubDirectory.Hub hub = Find(hubId);
                if (hub == null)
                {
                    return;
                }

                // by guid and by address, as the old window did, so the hub stays ignored if it moves or changes identity
                if (ignored)
                {
                    main.log.AddIgnoreNode(ref hub.guid);
                    main.log.AddIgnoreNode(hub.endPoint.Address);
                }
                else
                {
                    main.log.RemoveIgnoreNode(ref hub.guid);
                    main.log.RemoveIgnoreNode(hub.endPoint.Address);
                }
            }
        }

        public void SetSaved(string hubId, bool saved)
        {
            lock (main.conch)
            {
                HubDirectory.Hub hub = Find(hubId);
                if (hub == null)
                {
                    return;
                }

                List<AddressBook.AddressBookEntry> existing = SavedEntries(hub).ToList();
                if (!saved && existing.Count > 0)
                {
                    foreach (var entry in existing)
                    {
                        main.addressBook.entries.Remove(entry);
                    }
                    main.addressBook.Save();
                }
                else if (saved && existing.Count == 0 && hub.name.Length > 0)
                {
                    uint uuid = UserDirectory.MakeUuid(hub.guid);
                    main.addressBook.entries.Add(new AddressBook.AddressBookEntry
                    {
                        name = hub.name,
                        uuid = uuid,
                        address = UserDirectory.UuidToString(uuid),
                        endPoint = hub.endPoint,
                    });
                    main.addressBook.Save();
                }
            }
        }
    }
}
