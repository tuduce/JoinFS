using JoinFS.Net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace JoinFS
{
    /// <summary>
    /// Users by user id (uuid, derived from each installation's GUID and independent of network
    /// address): announcing ourselves to hubs, hubs' registry of who is online where, finding a
    /// user's node through the hubs ("join by user id"), and the address book's online checks.
    /// </summary>
    public sealed class UserDirectory
    {
        /// <summary>Ask at most this many hubs where a user is.</summary>
        public const int REQUEST_NUID_NUM_SAMPLES = 5;

        // ------------------------------------------------------------------ uuid

        /// <summary>A user id from a GUID (never 0).</summary>
        public static uint MakeUuid(Guid guid)
        {
            uint uuid = NetHash.HashString(guid.ToString());
            return uuid == 0 ? 1 : uuid;
        }

        /// <summary>A user id from its text form ("12345 67890"), or 0.</summary>
        public static uint MakeUuid(string str)
        {
            string[] parts = str.Split(' ');
            if (parts.Length == 2 && parts[0].Length == 5 && parts[1].Length == 5
                && uint.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n1)
                && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n2))
            {
                return (n1 << 16) + (n2 & 0xffff);
            }
            return 0;
        }

        public static string UuidToString(uint uuid) =>
            (uuid >> 16).ToString("D5", CultureInfo.InvariantCulture) + " " + (uuid & 0xffff).ToString("D5", CultureInfo.InvariantCulture);

        public static bool IsUuidFormat(string str)
        {
            string[] parts = str.Split(' ');
            return parts.Length == 2 && parts[0].Length == 5 && parts[1].Length == 5
                && uint.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint _)
                && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint _);
        }

        // ------------------------------------------------------------------ online users

        /// <summary>Where a user was last seen.</summary>
        sealed class OnlineUser
        {
            public NodeId nuid;
            public ushort port;
            public double expireTime;
        }

        readonly Dictionary<uint, OnlineUser> onlineUsers = [];
        readonly List<uint> tempUuids = [];

        readonly INetworkOutbox outbox;
        readonly ISessionState session;
        readonly ILocalProfile profile;
        readonly HubDirectory hubs;
        readonly IEndPointResolver endPoints;
        readonly ISessionUi ui;
        readonly IClock clock;
        readonly ISessionLog log;

        readonly Timer onlineTimer = new(60.0);
        readonly Timer addressBookTimer = new(60.0);
        readonly Random randomHubIndex = new();

        /// <summary>Check the address book faster for the first few times.</summary>
        int addressBookFastCount = 4;

        public UserDirectory(INetworkOutbox outbox, ISessionState session, ILocalProfile profile, HubDirectory hubs, IEndPointResolver endPoints, ISessionUi ui, IClock clock, ISessionLog log)
        {
            this.outbox = outbox;
            this.session = session;
            this.profile = profile;
            this.hubs = hubs;
            this.endPoints = endPoints;
            this.ui = ui;
            this.clock = clock;
            this.log = log;
            addressBookTimer.Set(clock.Now + 4.0);
        }

        public int OnlineUserCount => onlineUsers.Count;

        public bool IsUserOnline(uint uuid) => onlineUsers.ContainsKey(uuid);

        /// <summary>Where to reach a user, if we know.</summary>
        public bool TryGetEndPoint(uint uuid, out IPEndPoint endPoint)
        {
            if (onlineUsers.TryGetValue(uuid, out OnlineUser user))
            {
                endPoint = endPoints.MakeEndPoint(user.nuid, user.port);
                return true;
            }
            endPoint = null;
            return false;
        }

        public void RegisterOnlineUser(uint uuid, NodeId nuid, ushort port)
        {
            if (!onlineUsers.TryGetValue(uuid, out OnlineUser user))
            {
                user = new OnlineUser();
                onlineUsers.Add(uuid, user);
                log.Network("Added Online User '" + UuidToString(uuid) + "'");
            }
            user.nuid = nuid;
            user.port = port;
            user.expireTime = clock.Now + HubDirectory.OFFLINE_TIME;
        }

        /// <summary>Ask a few online hubs (from a random start) where a user is.</summary>
        public void RequestNuid(uint uuid)
        {
            log.Network("RequestNuid '" + UuidToString(uuid) + "'");
            List<HubDirectory.Hub> list = hubs.List;
            int startIndex = randomHubIndex.Next(list.Count);
            int requestCount = 0;
            for (int i = 0; i < list.Count && requestCount < REQUEST_NUID_NUM_SAMPLES; i++)
            {
                HubDirectory.Hub hub = list[(startIndex + i) % list.Count];
                if (hub.online)
                {
                    // one guaranteed request per hub
                    outbox.SendToEndPoint(hub.endPoint, new UserNuidRequest { Uuid = uuid }, guaranteed: true);
                    requestCount++;
                }
            }
        }

        /// <summary>Every minute: tell the online hubs we're here, and expire users not heard of.</summary>
        public void DoOnlineUsers()
        {
            if (!session.Ready || !onlineTimer.Elapsed(clock.Now))
            {
                return;
            }
            foreach (var hub in hubs.List)
            {
                if (hub.online)
                {
                    outbox.SendToEndPoint(hub.endPoint, new OnlineAnnouncement { Uuid = profile.Uuid });
                }
            }
            double now = clock.Now;
            foreach (var user in onlineUsers)
            {
                if (user.Value.expireTime < now) tempUuids.Add(user.Key);
            }
            foreach (var uuid in tempUuids)
            {
                onlineUsers.Remove(uuid);
                log.Network("Removed Online User '" + UuidToString(uuid) + "'");
            }
            tempUuids.Clear();
        }

        public void Handle(in MessageMeta meta, in OnlineAnnouncement online) =>
            RegisterOnlineUser(online.Uuid, meta.Sender, (ushort)meta.EndPoint.Port);

        public void Handle(in MessageMeta meta, in UserNuidRequest request)
        {
            if (onlineUsers.TryGetValue(request.Uuid, out OnlineUser value))
            {
                outbox.SendToEndPoint(meta.EndPoint, new UserNuidReply { Uuid = request.Uuid, Node = value.nuid, Port = value.port }, guaranteed: true);
                log.Network("UserNuidRequest '" + meta.EndPoint + "' - '" + UuidToString(request.Uuid) + "' - '" + value.nuid + ":" + value.port + "'");
            }
            else
            {
                log.Network("UserNuidRequest '" + meta.EndPoint + "' - '" + UuidToString(request.Uuid) + "' NOT ONLINE");
            }
        }

        public void Handle(in MessageMeta meta, in UserNuidReply reply)
        {
            RegisterOnlineUser(reply.Uuid, reply.Node, reply.Port);
            log.Network("UserNuid '" + meta.EndPoint + "' - '" + UuidToString(reply.Uuid) + "' - '" + reply.Node + ":" + reply.Port + "'");
        }

        // ------------------------------------------------------------------ address book

        /// <summary>While the address book is open: ask each entry for its status (finding it by uuid if needed).</summary>
        public void DoAddressBook()
        {
            if (!session.Ready || !ui.AddressBookVisible || !addressBookTimer.Elapsed(clock.Now))
            {
                return;
            }
            double now = clock.Now;
            if (addressBookFastCount > 0)
            {
                addressBookTimer.Set(now + 3.0);
                addressBookFastCount--;
            }

            foreach (var entry in profile.AddressBook)
            {
                if (entry.endPoint.Port != 0)
                {
                    hubs.SendStatusRequest(entry.endPoint, false);
                }
                else if (TryGetEndPoint(entry.uuid, out IPEndPoint endPoint))
                {
                    entry.endPoint = endPoint;
                    hubs.SendStatusRequest(entry.endPoint, false);
                }
                else if (entry.uuid != 0)
                {
                    RequestNuid(entry.uuid);
                }

                if (now > entry.offlineTime)
                {
                    entry.online = false;
                }
            }
        }

        /// <summary>Any status reply marks the address book entry at that address online.</summary>
        public void Handle(in MessageMeta meta, in StatusUpdate status)
        {
            IPAddress address = meta.EndPoint.Address;
            AddressBook.AddressBookEntry entry = profile.AddressBook.Find(f => f.endPoint.Address.Equals(address));
            if (entry != null)
            {
                entry.online = true;
                entry.offlineTime = clock.Now + HubDirectory.OFFLINE_TIME;
            }
        }
    }
}
