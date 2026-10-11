using System;
using System.Collections.Generic;
using System.Linq;
using JoinFS.Net;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using UiPeer = JoinFS.UI.Models.PeerInfo;

namespace JoinFS.Live
{
    /// <summary>
    /// The Session tab on the real session: the users the old SessionForm listed, and what its permissions dialog, Save and Ignore did.
    /// Everything is read and changed under the app's lock, as the form did, and a peer is named to the UI by its node id.
    /// </summary>
    class LiveSessionSource : ISessionSource
    {
        readonly Main main;

        public LiveSessionSource(Main main)
        {
            this.main = main;
        }

        public IReadOnlyList<UiPeer> GetPeers()
        {
            List<UiPeer> peers = [];

            lock (main.conch)
            {
                // this node first, while there is a session to be in
                if (main.network.Connected)
                {
                    peers.Add(Me());
                }

                List<UiPeer> others = [];
                foreach (var node in main.network.Peers.Nodes)
                {
                    others.Add(Other(node.Key, node.Value.nickname));
                }

                // the old window's default order
                others.Sort((a, b) => string.Compare(a.Nick, b.Nick, StringComparison.CurrentCulture));
                peers.AddRange(others);
            }

            return peers;
        }

        UiPeer Me()
        {
            int aircraft = 0;
            int objects = 0;
            string simulator = "";
            if (main.sim != null)
            {
                aircraft = main.sim.View.FindAll(o => o is Sim.Aircraft && main.sim.IsBroadcast(o)).Count;
                objects = main.sim.View.FindAll(o => (o is Sim.Aircraft) == false && o.owner == Sim.Obj.Owner.Sim).Count;
                simulator = main.sim.View.SimulatorName;
            }

            NodeId local = main.network.LocalId;
            return new UiPeer(
                local.ToString(), main.settingsNickname, main.network.Peers.GetLocalCallsign(), "Yes", 0, aircraft, objects,
                simulator, Main.Version, "", local.port, IsMe: true);
        }

        UiPeer Other(NodeId nuid, string nickname)
        {
            int aircraft = 0;
            int objects = 0;
            if (main.sim != null)
            {
                aircraft = main.sim.View.FindAll(o => o.ownerNuid == nuid && o is Sim.Aircraft).Count;
                objects = main.sim.View.FindAll(o => o.ownerNuid == nuid && (o is Sim.Aircraft) == false).Count;
            }

            PeerSnapshot peer = main.network.Snapshot.Peer(nuid);

            // "Yes" when the link is up and direct, "Route" through another user, "No" before it is up
            string connected = "No";
            if (peer?.SendEstablished ?? false)
            {
                connected = (peer?.Direct ?? false) ? "Yes" : "Route";
            }

            // a user the app's list knows of but the snapshot does not yet (it is a tick behind) is still negotiating, not blank
            PeerLinkState link = peer?.LinkState ?? PeerLinkState.Negotiating;

            return new UiPeer(
                nuid.ToString(), nickname, main.network.Peers.GetNodeCallsign(nuid), connected,
                (int)Math.Round(main.network.GetNodeRTT(nuid) * 1000.0f), aircraft, objects,
                main.network.Peers.GetNodeSimulator(nuid), main.network.Peers.GetNodeVersion(nuid),
                link.ToDisplay(), peer?.EndPoint?.Port ?? 0);
        }

        public PeerSettings GetSettings(string peerId)
        {
            lock (main.conch)
            {
                NodeId? found = Find(peerId);
                if (found == null)
                {
                    return new PeerSettings(false, false, false, false, false);
                }
                NodeId nuid = found.Value;

                uint uuid = UserDirectory.MakeUuid(main.network.Peers.GetNodeGuid(nuid));
                return new PeerSettings(
                    CockpitEntry: main.log.ShareCockpit(nuid),
                    HandOverControls: main.network.Peers.shareFlightControls == nuid,
                    MultipleObjects: main.log.MultipleObjects(nuid),
                    IsSaved: main.addressBook.entries.Find(e => e.uuid == uuid) != null,
                    IsIgnored: main.log.IgnoreNode(nuid));
            }
        }

        public void SetCockpitEntry(string peerId, bool allowed) => Change(peerId, nuid =>
        {
            if (allowed)
            {
                main.log.AddShareCockpit(nuid);
            }
            else
            {
                main.log.RemoveShareCockpit(nuid);
            }
        });

        public void SetHandOverControls(string peerId, bool handedOver) => Change(peerId, nuid =>
        {
            // only the flight controls, as the old dialog's one box did; the engine and other controls stay as they were
            if (handedOver)
            {
                main.network.Peers.shareFlightControls = nuid;
            }
            else if (main.network.Peers.shareFlightControls == nuid)
            {
                main.network.Peers.shareFlightControls = new NodeId();
            }
        });

        public void SetMultipleObjects(string peerId, bool allowed) => Change(peerId, nuid =>
        {
            if (allowed)
            {
                main.log.AddMultipleObjects(nuid);
            }
            else
            {
                main.log.RemoveMultipleObjects(nuid);
            }
        });

        public void SetIgnored(string peerId, bool ignored)
        {
            lock (main.conch)
            {
                NodeId? nuid = Find(peerId);
                // nobody ignores themselves
                if (nuid == null || nuid.Value == main.network.LocalId)
                {
                    return;
                }

                if (ignored)
                {
                    main.log.AddIgnoreNode(nuid.Value);
                }
                else
                {
                    main.log.RemoveIgnoreNode(nuid.Value);
                }
            }
        }

        public void SetSaved(string peerId, bool saved)
        {
            lock (main.conch)
            {
                NodeId? found = Find(peerId);
                // nobody saves themselves
                if (found == null || found.Value == main.network.LocalId)
                {
                    return;
                }
                NodeId nuid = found.Value;

                Guid guid = main.network.Peers.GetNodeGuid(nuid);
                uint uuid = UserDirectory.MakeUuid(guid);
                AddressBook.AddressBookEntry entry = main.addressBook.entries.Find(e => e.uuid == uuid);

                if (entry != null && !saved)
                {
                    main.addressBook.entries.Remove(entry);
                    main.addressBook.Save();
                }
                else if (entry == null && saved)
                {
                    string nickname = main.network.Peers.Nodes.TryGetValue(nuid, out var node) ? node.nickname : "";
                    main.addressBook.entries.Add(new AddressBook.AddressBookEntry
                    {
                        name = nickname.Length > 0 ? nickname : UserDirectory.UuidToString(uuid),
                        uuid = uuid,
                        address = UserDirectory.UuidToString(uuid),
                    });
                    main.addressBook.Save();
                }
            }
        }

        /// <summary>
        /// Change something about a user, under the lock, and tell them what they now share.
        /// </summary>
        void Change(string peerId, Action<NodeId> change)
        {
            lock (main.conch)
            {
                NodeId? nuid = Find(peerId);
                if (nuid == null || nuid.Value == main.network.LocalId)
                {
                    return;
                }
                change(nuid.Value);
                // shared data
                main.network.Peers.SchedulePeerInfo(nuid.Value);
            }
        }

        /// <summary>
        /// The node a row's id names, or null if the user has gone. The caller holds the lock.
        /// </summary>
        NodeId? Find(string peerId)
        {
            foreach (NodeId nuid in main.network.Peers.Nodes.Keys)
            {
                if (nuid.ToString() == peerId)
                {
                    return nuid;
                }
            }
            return null;
        }
    }
}
