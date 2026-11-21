using JoinFS.Fsd;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// Whether an ATC client is connected, for the ATC card of Settings (docs/ATC/atc-fsd-plan.md, stage 5). It reads the listener and the
    /// gateway under the app's lock, as the other live sources read their state; it changes nothing. The switch itself goes through
    /// <see cref="LivePreferencesStore"/>.
    /// </summary>
    class LiveAtcStatusSource : IAtcStatusSource
    {
        readonly Main main;

        public LiveAtcStatusSource(Main main)
        {
            this.main = main;
        }

        public AtcClientStatus Read()
        {
            lock (main.conch)
            {
                if (main.AtcFsdFailed)
                {
                    return new AtcClientStatus(AtcClientState.Failed, main.settingsAtcFsdPort);
                }

                int port = main.AtcFsdPort;
                if (port == 0)
                {
                    return AtcClientStatus.Off;
                }

                AtcStationInfo station = main.atcGateway?.Station;
                if (station == null)
                {
                    return new AtcClientStatus(AtcClientState.Listening, port);
                }

                // the frequency is known once the client has sent its first position
                string frequency = station.FrequencyKhz > 0 ? FsdFrequency.ToMegahertz(station.FrequencyKhz) : "";
                return new AtcClientStatus(AtcClientState.Connected, port, station.Callsign, frequency);
            }
        }
    }
}
