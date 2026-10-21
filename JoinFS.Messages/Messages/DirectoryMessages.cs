using System;
using System.Collections.Generic;

// Canonical messages for the hub directory: hub discovery, the per-hub user lists shown in the
// hubs/ATC windows, and looking users up by their uuid.

namespace JoinFS.Net
{
    public struct HubAddress
    {
        /// <summary>The hub's legacy id when it has one, else the name of its key.</summary>
        public NodeId Node;
        public ushort Port;

        /// <summary>JFP2 only: where the hub can be reached, best first (global IPv6 first); null or empty for a legacy entry, which says it through <see cref="Node"/> and <see cref="Port"/>.</summary>
        public List<System.Net.IPEndPoint> Candidates;
    }

    /// <summary>Hubs the sender knows about.</summary>
    public struct HubList : IMessage
    {
        public static MessageKind Kind => MessageKind.HubList;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public List<HubAddress> Hubs;
    }

    /// <summary>Ask a hub for the users connected to it (answered with one <see cref="HubUserUpdate"/> per user).</summary>
    public struct UserListRequest : IMessage
    {
        public static MessageKind Kind => MessageKind.UserListRequest;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);
    }

    /// <summary>One user connected to the sending hub, with their flight plan.</summary>
    public struct HubUserUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.HubUser;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public Guid Guid;
        public bool Atc;
        public bool Ifr;
        public string Callsign;
        public string Nickname;
        public ushort Frequency;
        public float Latitude;
        public float Longitude;
        public ushort Altitude;
        public ushort Speed;
        public ushort Squawk;
        public byte Level;
        public byte Range;
        public ushort Heading;
        public string IcaoType;
        public string Departure;
        public string Destination;
        public string Rules;
        public string Route;
        public string Remarks;
        public string Alternate;
        public string FlightSpeed;
        public string FlightAltitude;
        public string Registration;
        public string IcaoAirline;
        public string FlightNumber;
    }

    /// <summary>Ask a hub for its users' current positions (answered with <see cref="UserPositions"/>).</summary>
    public struct UserPositionsRequest : IMessage
    {
        public static MessageKind Kind => MessageKind.UserPositionsRequest;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);
    }

    public struct UserPosition
    {
        public Guid Guid;
        public float Latitude;
        public float Longitude;
        public ushort Altitude;
        public ushort Speed;
        public ushort Squawk;
        public ushort Heading;
    }

    public struct UserPositions : IMessage
    {
        public static MessageKind Kind => MessageKind.UserPositions;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public List<UserPosition> Users;
    }

    /// <summary>Tells a hub the sender is online under <see cref="Uuid"/> (so others can find it by user id).</summary>
    public struct OnlineAnnouncement : IMessage
    {
        public static MessageKind Kind => MessageKind.Online;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint Uuid;
    }

    /// <summary>Ask a hub where the user with <see cref="Uuid"/> is.</summary>
    public struct UserNuidRequest : IMessage
    {
        public static MessageKind Kind => MessageKind.UserNuidRequest;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint Uuid;
    }

    /// <summary>Where the user with <see cref="Uuid"/> can be reached.</summary>
    public struct UserNuidReply : IMessage
    {
        public static MessageKind Kind => MessageKind.UserNuid;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint Uuid;
        public NodeId Node;
        public ushort Port;

        /// <summary>JFP2 only: where the hub saw the user, or null (a legacy reply says it through <see cref="Node"/> and <see cref="Port"/>).</summary>
        public List<System.Net.IPEndPoint> Candidates;
    }
}
