using System.Collections.Generic;

namespace JoinFS.Fsd
{
    /// <summary>The facility types of an ATC station (the <c>%</c> line).</summary>
    public enum FsdFacility
    {
        Observer = 0,
        FlightServiceStation = 1,
        Delivery = 2,
        Ground = 3,
        Tower = 4,
        Approach = 5,
        Centre = 6,
    }

    /// <summary>The server error numbers JoinFS sends (the <c>$ER</c> line).</summary>
    public enum FsdErrorCode
    {
        CallsignInUse = 1,
        CallsignInvalid = 2,
        AlreadyRegistered = 3,
        SyntaxError = 4,
        InvalidSourceCallsign = 5,
        NoSuchCallsign = 7,
        NoFlightPlan = 8,
        InvalidProtocolRevision = 10,
        InvalidControl = 14,
    }

    /// <summary>The server's greeting, <c>$DI</c>: <c>$DISERVER:CLIENT:VATSIM FSD V3.50:76617473696d</c>.</summary>
    public sealed class FsdServerId
    {
        public string Version { get; set; } = "";
        /// <summary>The server's challenge key, hexadecimal text; may be empty.</summary>
        public string Challenge { get; set; } = "";

        public static bool TryParse(FsdLine line, out FsdServerId id)
        {
            id = null;
            if (line.Verb != FsdVerb.ServerId) return false;
            id = new FsdServerId { Version = line.Field(2), Challenge = line.Field(3) };
            return true;
        }

        public FsdLine ToLine() => new FsdLine(FsdVerb.ServerId, "SERVER", "CLIENT", Version, Challenge);
    }

    /// <summary>
    /// The client's identification, <c>$ID</c>. JoinFS reads none of it, so every value stays text exactly as the
    /// client wrote it: <c>$IDN172SP:SERVER:88e4:vPilot:3:8:123456:-582057156:6d6973746176</c>.
    /// </summary>
    public sealed class FsdClientId
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "SERVER";
        public string SoftwareId { get; set; } = "";
        public string SoftwareName { get; set; } = "";
        public string MajorVersion { get; set; } = "";
        public string MinorVersion { get; set; } = "";
        public string Cid { get; set; } = "";
        public string SystemUid { get; set; } = "";
        public string Challenge { get; set; } = "";

        public static bool TryParse(FsdLine line, out FsdClientId id)
        {
            id = null;
            if (line.Verb != FsdVerb.ClientId) return false;
            id = new FsdClientId
            {
                From = line.Field(0), To = line.Field(1), SoftwareId = line.Field(2), SoftwareName = line.Field(3),
                MajorVersion = line.Field(4), MinorVersion = line.Field(5), Cid = line.Field(6),
                SystemUid = line.Field(7), Challenge = line.Field(8),
            };
            return true;
        }

        public FsdLine ToLine() => new FsdLine(FsdVerb.ClientId,
            From, To, SoftwareId, SoftwareName, MajorVersion, MinorVersion, Cid, SystemUid, Challenge);
    }

    /// <summary>
    /// A controller logging in, <c>#AA</c>:
    /// <c>#AARN_OBS:SERVER:John Doe:100000:token:2:100</c>. The server passes it on to the other clients with the
    /// token left empty (<see cref="Token"/> is then ""). CID, token and revision are opaque text to JoinFS.
    /// </summary>
    public sealed class FsdAddAtc
    {
        public string Callsign { get; set; } = "";
        public string To { get; set; } = "SERVER";
        public string RealName { get; set; } = "";
        public string Cid { get; set; } = "";
        public string Token { get; set; } = "";
        public int Rating { get; set; }
        public string ProtocolRevision { get; set; } = "";

        public static bool TryParse(FsdLine line, out FsdAddAtc add)
        {
            add = null;
            if (line.Verb != FsdVerb.AddAtc) return false;
            if (!FsdText.TryInt(line.Field(5), out int rating)) return false;
            add = new FsdAddAtc
            {
                Callsign = line.Field(0), To = line.Field(1), RealName = line.Field(2), Cid = line.Field(3),
                Token = line.Field(4), Rating = rating, ProtocolRevision = line.Field(6),
            };
            return true;
        }

        public FsdLine ToLine() => new FsdLine(FsdVerb.AddAtc,
            Callsign, To, RealName, Cid, Token, FsdText.Int(Rating), ProtocolRevision);
    }

    /// <summary>
    /// A controller's position, <c>%</c>: <c>%EWR_P_APP:28550:5:150:4:40.67317:-74.18533:0</c>. The frequencies are
    /// in kHz here (28550 in the text is 128550).
    /// </summary>
    public sealed class FsdAtcPosition
    {
        public string Callsign { get; set; } = "";
        /// <summary>The tuned frequencies, kHz; the text joins them with <c>&amp;</c>.</summary>
        public List<int> FrequenciesKhz { get; set; } = new List<int>();
        public FsdFacility Facility { get; set; }
        /// <summary>Nautical miles.</summary>
        public int VisibilityRange { get; set; }
        public int Rating { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public static bool TryParse(FsdLine line, out FsdAtcPosition position)
        {
            position = null;
            if (line.Verb != FsdVerb.AtcPosition) return false;
            var p = new FsdAtcPosition { Callsign = line.Field(0) };
            string frequencies = line.Field(1);
            if (frequencies.Length > 0)
            {
                foreach (string part in frequencies.Split('&'))
                {
                    if (!FsdFrequency.TryParse(part, out int khz)) return false;
                    p.FrequenciesKhz.Add(khz);
                }
            }
            if (!FsdText.TryInt(line.Field(2), out int facility)) return false;
            if (!FsdText.TryInt(line.Field(3), out int visibility)) return false;
            if (!FsdText.TryInt(line.Field(4), out int rating)) return false;
            if (!FsdText.TryDegrees(line.Field(5), out double lat)) return false;
            if (!FsdText.TryDegrees(line.Field(6), out double lon)) return false;
            p.Facility = (FsdFacility)facility;
            p.VisibilityRange = visibility;
            p.Rating = rating;
            p.Latitude = lat;
            p.Longitude = lon;
            position = p;
            return true;
        }

        public FsdLine ToLine()
        {
            var frequencies = new string[FrequenciesKhz.Count];
            for (int i = 0; i < frequencies.Length; i++) frequencies[i] = FsdFrequency.ToField(FrequenciesKhz[i]);
            return new FsdLine(FsdVerb.AtcPosition,
                Callsign, string.Join("&", frequencies), FsdText.Int((int)Facility), FsdText.Int(VisibilityRange),
                FsdText.Int(Rating), FsdText.Degrees(Latitude), FsdText.Degrees(Longitude), "0");
        }
    }

    /// <summary>A client logging off, <c>#DA</c> (a controller) or <c>#DP</c> (a pilot): <c>#DASAN_TWR:1555555</c>.</summary>
    public sealed class FsdDelete
    {
        public bool IsPilot { get; set; }
        public string Callsign { get; set; } = "";
        public string Cid { get; set; } = "";

        public static FsdDelete Atc(string callsign, string cid = "") => new FsdDelete { Callsign = callsign, Cid = cid };
        public static FsdDelete Pilot(string callsign, string cid = "") => new FsdDelete { IsPilot = true, Callsign = callsign, Cid = cid };

        public static bool TryParse(FsdLine line, out FsdDelete delete)
        {
            delete = null;
            if (line.Verb != FsdVerb.DeleteAtc && line.Verb != FsdVerb.DeletePilot) return false;
            delete = new FsdDelete { IsPilot = line.Verb == FsdVerb.DeletePilot, Callsign = line.Field(0), Cid = line.Field(1) };
            return true;
        }

        /// <summary>A <c>#DA</c> may end with an auth challenge, which is not carried here: it is not written back.</summary>
        public FsdLine ToLine() => new FsdLine(IsPilot ? FsdVerb.DeletePilot : FsdVerb.DeleteAtc, Callsign, Cid);
    }

    /// <summary>A server error, <c>$ER</c>: <c>$ERSERVER:unknown:006::Invalid CID/password.</c></summary>
    public sealed class FsdError
    {
        public string To { get; set; } = "unknown";
        /// <summary>The number, three digits on the wire. Not only the values of <see cref="FsdErrorCode"/>.</summary>
        public int Code { get; set; }
        /// <summary>The field that caused it, empty when none.</summary>
        public string Parameter { get; set; } = "";
        public string Description { get; set; } = "";

        public static FsdError Create(string to, FsdErrorCode code, string parameter, string description) =>
            new FsdError { To = to, Code = (int)code, Parameter = parameter, Description = description };

        public static bool TryParse(FsdLine line, out FsdError error)
        {
            error = null;
            if (line.Verb != FsdVerb.Error) return false;
            if (!FsdText.TryInt(line.Field(2), out int code)) return false;
            error = new FsdError { To = line.Field(1), Code = code, Parameter = line.Field(3), Description = line.Field(4) };
            return true;
        }

        public FsdLine ToLine() => new FsdLine(FsdVerb.Error,
            "SERVER", To, Code.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), Parameter, Description);
    }
}
