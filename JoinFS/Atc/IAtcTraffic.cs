using System.Collections.Generic;

namespace JoinFS
{
    /// <summary>
    /// The flight plan of an aircraft as JoinFS holds it: free text, as the pilot or SimBrief wrote it. The fields
    /// JoinFS has no value for are empty strings.
    /// </summary>
    public sealed record AtcFlightPlanData(
        string Rules, string IcaoType, string Speed, string Departure, string Altitude,
        string Destination, string Alternate, string Remarks, string Route)
    {
        public static readonly AtcFlightPlanData Empty = new("", "", "", "", "", "", "", "", "");
    }

    /// <summary>
    /// One aircraft the ATC client is told about (docs/ATC/atc-fsd-plan.md §4.7): a value copy, so the gateway never
    /// touches the simulator's objects.
    /// </summary>
    /// <param name="Owner">The owning node (its 8 wire bytes as a little-endian number); the same on every node for the same aircraft.</param>
    /// <param name="ObjectId">The owner's id for the object. (Owner, object id) is the aircraft's key.</param>
    /// <param name="FlightPlanCallsign">The callsign of the flight plan as typed; the gateway turns it into the FSD callsign (§4.6).</param>
    /// <param name="Nickname">The owner's nickname, given as the aircraft's real name.</param>
    /// <param name="Squawk">The transponder code as four decimal digits (1200 is the code 1200).</param>
    /// <param name="PitchDegrees">Nose up positive.</param>
    /// <param name="BankDegrees">Right wing down positive.</param>
    /// <param name="Transponder">The transponder mode, the same whatever simulator the aircraft flies in (§4.7, stage 9).</param>
    /// <param name="Ident">The pilot pressed IDENT.</param>
    public sealed record AtcAircraft(
        ulong Owner, ulong ObjectId, string FlightPlanCallsign, string Nickname, int Squawk,
        double Latitude, double Longitude, double AltitudeFeet, double GroundSpeedKnots,
        double PitchDegrees, double BankDegrees, double HeadingDegrees, AtcFlightPlanData Plan,
        AtcTransponder Transponder = AtcTransponder.Unknown, bool Ident = false);

    /// <summary>
    /// What an aircraft's transponder is doing, as far as a controller's scope cares. Each simulator numbers its own
    /// states differently, so <see cref="SimViewTraffic"/> turns them into this before they leave it.
    /// </summary>
    public enum AtcTransponder
    {
        /// <summary>The aircraft's node does not send a state (an older build, FSX, P3D): shown as mode C, as before stage 9.</summary>
        Unknown,
        /// <summary>Off, standby, or test: nothing is reported.</summary>
        Standby,
        /// <summary>On, mode A: the code but no altitude.</summary>
        ModeA,
        /// <summary>Alt, mode C: code and altitude. Also the Mode S states that report altitude (ground, TCAS).</summary>
        ModeC,
    }

    /// <summary>
    /// The aircraft of the session as the gateway sees them. Called on the app thread; the list is a copy that the
    /// caller may keep.
    /// </summary>
    public interface IAtcTraffic
    {
        IReadOnlyList<AtcAircraft> Aircraft { get; }
    }
}
