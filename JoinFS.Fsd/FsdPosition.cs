using System;

namespace JoinFS.Fsd
{
    /// <summary>
    /// The pitch, bank and heading of an aircraft packed into one unsigned 32-bit number, as the <c>@</c> line
    /// carries it: each angle is a 10-bit number (360 degrees = 1024 steps), pitch above bit 22, bank above bit 12,
    /// heading above bit 2, the two lowest bits zero.
    ///
    /// The document does not give the signs. As in FSD's own code, pitch and bank are written negated: the
    /// caller passes the usual angles (nose up and right wing down positive, heading true) and a nose-up pitch
    /// of 0.35 degrees is the step 1023. The example <c>4290776072</c> of <c>FSD-protocol.md</c> (a pitch of one
    /// step up, level, heading 270.7) fits this reading.
    /// </summary>
    public static class FsdPitchBankHeading
    {
        const double Step = 360.0 / 1024.0;

        /// <summary>Pack the angles, in degrees. Any angle is wrapped into one turn and rounded to a step.</summary>
        public static uint Pack(double pitchDegrees, double bankDegrees, double headingDegrees)
        {
            uint pitch = ToSteps(-pitchDegrees);
            uint bank = ToSteps(-bankDegrees);
            uint heading = ToSteps(headingDegrees);
            return pitch << 22 | bank << 12 | heading << 2;
        }

        /// <summary>Unpack. Pitch and bank are in (-180, 180], heading in [0, 360).</summary>
        public static void Unpack(uint packed, out double pitchDegrees, out double bankDegrees, out double headingDegrees)
        {
            pitchDegrees = Normalise(-(int)(packed >> 22 & 0x3FF) * Step);
            bankDegrees = Normalise(-(int)(packed >> 12 & 0x3FF) * Step);
            headingDegrees = (int)(packed >> 2 & 0x3FF) * Step;
        }

        static uint ToSteps(double degrees)
        {
            long steps = (long)Math.Round(degrees / Step);
            return (uint)(((steps % 1024) + 1024) % 1024);
        }

        static double Normalise(double degrees)
        {
            degrees %= 360.0;
            if (degrees > 180.0) degrees -= 360.0;
            else if (degrees <= -180.0) degrees += 360.0;
            return degrees;
        }
    }

    /// <summary>
    /// A pilot's position, the <c>@</c> line: <c>@S:GTI8197:2000:1:40.65906:-73.79891:26:0:4290776072:359</c>.
    /// </summary>
    public sealed class FsdPosition
    {
        /// <summary>Transponder mode: <c>Y</c> ident, <c>N</c> mode C, <c>S</c> standby.</summary>
        public char Mode { get; set; } = 'N';
        public string Callsign { get; set; } = "";
        /// <summary>The squawk as four decimal digits that are each 0 to 7: 2000 is the code 2000.</summary>
        public int Squawk { get; set; }
        public int Rating { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        /// <summary>True altitude, feet.</summary>
        public int AltitudeFeet { get; set; }
        /// <summary>Ground speed, knots.</summary>
        public int GroundSpeedKnots { get; set; }
        /// <summary>See <see cref="FsdPitchBankHeading"/>.</summary>
        public uint PitchBankHeading { get; set; }
        /// <summary>Pressure altitude minus true altitude, feet.</summary>
        public int AltitudeCorrection { get; set; }

        public static bool TryParse(FsdLine line, out FsdPosition position)
        {
            position = null;
            if (line.Verb != FsdVerb.PilotPosition) return false;
            var p = new FsdPosition { Callsign = line.Field(1) };
            if (!FsdText.TryChar(line.Field(0), out char mode)) return false;
            p.Mode = mode;
            if (!FsdText.TryInt(line.Field(2), out int squawk) || squawk < 0 || squawk > 7777) return false;
            p.Squawk = squawk;
            if (!FsdText.TryInt(line.Field(3), out int rating)) return false;
            p.Rating = rating;
            if (!FsdText.TryDegrees(line.Field(4), out double lat)) return false;
            if (!FsdText.TryDegrees(line.Field(5), out double lon)) return false;
            p.Latitude = lat;
            p.Longitude = lon;
            if (!FsdText.TryInt(line.Field(6), out int altitude)) return false;
            if (!FsdText.TryInt(line.Field(7), out int speed)) return false;
            if (!FsdText.TryUInt(line.Field(8), out uint pbh)) return false;
            if (!FsdText.TryInt(line.Field(9), out int correction)) return false;
            p.AltitudeFeet = altitude;
            p.GroundSpeedKnots = speed;
            p.PitchBankHeading = pbh;
            p.AltitudeCorrection = correction;
            position = p;
            return true;
        }

        public FsdLine ToLine() => new FsdLine(FsdVerb.PilotPosition,
            FsdText.Char(Mode), Callsign, Squawk.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            FsdText.Int(Rating), FsdText.Degrees(Latitude), FsdText.Degrees(Longitude),
            FsdText.Int(AltitudeFeet), FsdText.Int(GroundSpeedKnots), FsdText.Int(PitchBankHeading),
            FsdText.Int(AltitudeCorrection));
    }
}
