using JoinFS.Fsd;

namespace JoinFS
{
    /// <summary>
    /// This node's ATC presence (docs/ATC/atc-fsd-plan.md §4.1, D10): what a logged-in station says (airport, level, frequency;
    /// the old manual ATC mode that used to set them went in stage 12). Immutable: <c>Main.LocalAtc</c> is replaced, so the sim
    /// thread and the UI read it without a lock. Every reader of "are we a controller" reads this, never the settings.
    /// </summary>
    /// <param name="Active">This node announces itself as a controller (a station is logged in and is not an observer).</param>
    /// <param name="Callsign">The whole callsign shown for this node: <c>EDDM_N_TWR</c>.</param>
    /// <param name="Airport">The airport code <c>PeerInfo</c> carries: the callsign up to its first <c>_</c> for a station.</param>
    /// <param name="Level">0 delivery, 1 ground, 2 tower, 3 approach, 4 centre.</param>
    /// <param name="Frequency">In the form <c>PeerInfo</c> uses: 28550 is 128.550 MHz; 0 for none.</param>
    /// <param name="HasPosition">The position is the station's own (from its <c>%</c>); otherwise it is the airport's.</param>
    /// <param name="StationOnline">A main station is logged in, an observer or one without its first <c>%</c> included.</param>
    public sealed record LocalAtc(
        bool Active, string Callsign, string Airport, int Level, int Frequency,
        bool HasPosition, double Latitude, double Longitude, bool StationOnline)
    {
        /// <summary>No controller and no station.</summary>
        public static readonly LocalAtc Inactive = new(false, "", "", 2, 0, false, 0, 0, false);

        /// <summary>
        /// The online main station if there is one, otherwise inactive (plan stage 3; stage 12 removed the manual ATC settings).
        /// </summary>
        public static LocalAtc Resolve(AtcStationInfo station) => station != null ? FromStation(station) : Inactive;

        /// <summary>
        /// A station is a controller once its client has sent its first <c>%</c> (before that the facility is unknown) and
        /// unless it is an observer. The level comes from the facility, never from the callsign's suffix.
        /// </summary>
        public static LocalAtc FromStation(AtcStationInfo station)
        {
            if (!station.PositionKnown || station.Facility == FsdFacility.Observer)
                return Inactive with { StationOnline = true };

            int split = station.Callsign.IndexOf('_');
            string airport = (split < 0 ? station.Callsign : station.Callsign[..split]).ToUpperInvariant();
            int khz = station.FrequencyKhz;
            int frequency = khz >= FsdFrequency.MinKhz && khz <= FsdFrequency.MaxKhz ? khz - FsdFrequency.MinKhz : 0;
            return new LocalAtc(true, station.Callsign, airport, LevelOf(station.Facility), frequency,
                true, station.Latitude, station.Longitude, true);
        }

        /// <summary>The level of a facility: delivery 0, ground 1, tower 2, approach 3, centre and flight service 4.</summary>
        public static int LevelOf(FsdFacility facility) => facility switch
        {
            FsdFacility.Delivery => 0,
            FsdFacility.Ground => 1,
            FsdFacility.Tower => 2,
            FsdFacility.Approach => 3,
            FsdFacility.Centre => 4,
            FsdFacility.FlightServiceStation => 4,
            _ => 2,
        };

        /// <summary>
        /// Would a peer see a difference in the <c>PeerInfo</c> this node sends? The station's position alone is not one,
        /// so a <c>%</c> every 15 s does not trigger a message.
        /// </summary>
        public bool SamePresenceAs(LocalAtc other) =>
            other != null && Active == other.Active && Airport == other.Airport && Level == other.Level
            && Frequency == other.Frequency && StationOnline == other.StationOnline;
    }
}
