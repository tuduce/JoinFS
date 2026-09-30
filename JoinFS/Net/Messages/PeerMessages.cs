using System;

// Canonical messages describing peers, hubs and shared session state. StatusRequestUpdate and
// StatusUpdate moved here from JoinFS/Jfp2/Codecs/StatusCodecs.cs; the weather structs replace
// JoinFS.Jfp2.Codecs.WeatherReport (one type per kind, same shape).

namespace JoinFS.Net
{
    [Flags]
    public enum ShareCockpitFlags : byte
    {
        None = 0,
        /// <summary>The sender shares its cockpit with the recipient.</summary>
        Cockpit = 0x01,
        /// <summary>The recipient holds the sender's flight controls.</summary>
        FlightControls = 0x02,
        AncillaryControls = 0x04,
        NavControls = 0x08,
    }

    /// <summary>
    /// A peer's own description of itself, sent to each node when it connects and whenever it
    /// changes (legacy "SharedData"). Includes the cockpit-sharing state the sender holds
    /// towards the specific recipient.
    /// </summary>
    public struct PeerInfo : IMessage
    {
        public static MessageKind Kind => MessageKind.PeerInfo;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public ShareCockpitFlags Share;
        public string Nickname;
        public Guid Guid;
        public bool Hub;
        public bool Atc;
        public bool SimulatorConnected;
        public string AtcAirport;
        public byte AtcLevel;
        public short AtcFrequency;
        public byte ActivityCircle;
        /// <summary>Application version string ("" from peers too old to send it).</summary>
        public string Version;
        /// <summary>Simulator name ("" from peers too old to send it).</summary>
        public string Simulator;
    }

    public struct StatusRequestUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.StatusRequest;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public bool HubEnabled;
        public bool HubListRequested;
        public uint Uuid;
    }

    public struct StatusUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.Status;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public Guid Guid;
        public string AppVersion;
        public ushort Users;
        public ushort AtcCount;
        /// <summary>Meaningful only when AtcCount > 0.</summary>
        public string AtcAirport;
        public int AtcLevel;
        public ushort Planes;
        public ushort Helicopters;
        public ushort Boats;
        public ushort Vehicles;
        public bool HubEnabled;
        public string Address;
        public string Name;
        public string About;
        public string Voip;
        public string NextEvent;
        public string Airport;
        public int ActivityCircle;
        public bool GlobalSession;
        public bool PasswordRequired;
    }

    /// <summary>Ask the recipient for its current weather (it replies with <see cref="WeatherReply"/>).</summary>
    public struct WeatherRequest : IMessage
    {
        public static MessageKind Kind => MessageKind.WeatherRequest;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint ObjectId;
    }

    /// <summary>The weather the recipient should apply to its own simulator.</summary>
    public struct WeatherReply : IMessage
    {
        public static MessageKind Kind => MessageKind.WeatherReply;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public string Metar;
    }

    /// <summary>The weather at the sender's aircraft.</summary>
    public struct WeatherUpdate : IMessage
    {
        public static MessageKind Kind => MessageKind.WeatherUpdate;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public string Metar;
    }
}
