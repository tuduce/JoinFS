// Canonical messages of the ATC clients on the mesh (docs/ATC/atc-fsd-plan.md §4.5). JFP2 only: the legacy wire is
// frozen and carries neither (LegacyPlugin.CanCarry says so).

namespace JoinFS.Net
{
    /// <summary>
    /// The presence of one controller station on its node (not guaranteed; the origin is the owner; one per
    /// <see cref="Slot"/>). Sent on change, to a member that just joined and every 10 s; one with
    /// <see cref="Online"/> clear says the station logged off. The receiver drops a station it has not heard
    /// for 35 s and every station of a member that left.
    /// </summary>
    public struct AtcStation : IMessage
    {
        public static MessageKind Kind => MessageKind.AtcStation;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        /// <summary>0 the main station, 1 its ATIS.</summary>
        public byte Slot;
        /// <summary>False means "logged off": the receiver drops the station at once.</summary>
        public bool Online;
        /// <summary>The FSD value: 0 OBS, 1 FSS, 2 DEL, 3 GND, 4 TWR, 5 APP, 6 CTR.</summary>
        public byte Facility;
        /// <summary>As the client sent it.</summary>
        public byte Rating;
        /// <summary>Nautical miles.</summary>
        public ushort Visibility;
        /// <summary>kHz (128550 is 128.550 MHz); 0 none.</summary>
        public uint Frequency;
        public double Latitude;
        public double Longitude;
        /// <summary>The whole station callsign the client sent (<c>EDDM_N_TWR</c>).</summary>
        public string Callsign;
        public string Name;
    }

    /// <summary>
    /// One FSD line a controller's client sent to a station on another node (guaranteed, unicast): the line without
    /// its line ending. The sender checks the verb and the size; the receiver checks them again before it passes the
    /// line to its client (§4.4). A line longer than <c>AtcRelayV1Codec.LineLimit</c> is not sent.
    /// </summary>
    public struct AtcRelay : IMessage
    {
        public static MessageKind Kind => MessageKind.AtcRelay;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public string Line;
    }
}
