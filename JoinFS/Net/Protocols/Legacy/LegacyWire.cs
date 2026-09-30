namespace JoinFS.Net.Legacy
{
    /// <summary>
    /// The frozen legacy wire format (docs/network-protocol.md). Every datagram starts with a
    /// 21-byte header:
    /// <code>
    ///   0  int16  version (0x520B, so byte 0 is 0x0B - never JFP2's 0xFA magic)
    ///   2  byte   flags (Internal | Guaranteed | Forward)
    ///   3  uint16 guaranteed id (0 when not guaranteed)
    ///   5  byte   segment index
    ///   6  byte   segment count
    ///   7  Nuid   sender (7 bytes)
    ///  14  Nuid   recipient (7 bytes, all zero = unaddressed)
    ///  21  payload
    /// </code>
    /// Internal (mesh) payloads start with an int16 <see cref="InternalId"/>; application payloads
    /// with int16 data version then int16 <see cref="AppId"/>. Also the framing of the X-Plane
    /// plugin link (JoinFS-XP's Link.h), which must stay byte-identical with it.
    /// </summary>
    public static class LegacyWire
    {
        public const short Version = 0x520b;
        public const byte VersionLowByte = 0x0b;

        public const int FlagsOffset = 2;
        public const int GuaranteedIdOffset = 3;
        public const int GuaranteedIndexOffset = 5;
        public const int GuaranteedCountOffset = 6;
        public const int SenderOffset = 7;
        public const int RecipientOffset = 14;
        public const int DataOffset = 21;

        public const byte FlagInternal = 0x01;
        public const byte FlagGuaranteed = 0x02;
        public const byte FlagForward = 0x04;

        public const byte PulseFlagLowBandwidth = 0x01;

        /// <summary>
        /// Application data-model version written into every application message (legacy
        /// Sim.VERSION). Frozen with the legacy protocol: new fields go into JFP2 instead.
        /// </summary>
        public const short DataVersion = 21008;

        /// <summary>Oldest data version whose application messages are accepted.</summary>
        public const short MinDataVersion = 10014;

        /// <summary>
        /// The FlightPlan message's own version byte (distinct from <see cref="DataVersion"/>).
        /// Frozen at 1 since the field was introduced; no reader (this codebase's or, as far as
        /// known, any released build's) branches on it - see
        /// docs/network-plugin-architecture.md §2.11 item 3. Kept only for byte-for-byte wire
        /// fidelity; the canonical FlightPlanUpdate no longer carries it.
        /// </summary>
        public const byte FlightPlanFormatVersion = 1;

        /// <summary>Largest payload per guaranteed segment.</summary>
        public const int MaxGuaranteedData = 1000;

        public const int MaxIntegerVariables = 100;
        public const int MaxFloatVariables = 100;
        public const int MaxString8Variables = 80;
        public const int MaxHubListEntries = 25;
        public const int MaxUserPositions = 20;

        /// <summary>Notes type and expiry written into comms notes (Notes.Type.Comms, Notes.COMMS_EXPIRE).</summary>
        public const ushort NoteTypeComms = 0;
        public const ushort CommsExpire = 10;

        public enum InternalId : short
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

        public enum AppId : short
        {
            ObjectPosition,
            AircraftPosition,
            // 2-9: obsolete, never sent or read
            PlaneState,
            HelicopterState,
            AircraftState,
            PistonEngineState,
            TurbineEngineState,
            AircraftFuel,
            AircraftPayload,
            ObjectSmoke,
            WeatherRequest,
            WeatherReply,
            WeatherUpdate,
            SharedData,
            StatusRequest,
            Status,
            HubList,
            RemoveObject,
            UserListRequest,
            UserList,
            UsageLog,
            SimEvent,
            KeyLog,
            Shutdown,
            SessionCommsRequest,
            GlobalCommsRequest,
            CommsListenRequest,
            AllNotesRequest,
            Notes,
            UserNuidRequest,
            UserNuid,
            Online,
            FlightPlanRequest,
            FlightPlan,
            UserList2,
            UserPositionsRequest,
            UserPositions,
            IntegerVariables,
            FloatVariables,
            String8Variables,
            ShowOnRadar,
        }

        public static short ConvertToAxis(float input) => (short)(input * 16384.0);
        public static float ConvertFromAxis(short input) => (float)(int)input * (1.0f / 16384.0f);
    }
}
