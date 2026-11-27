using System;

namespace JoinFS.Fsd
{
    /// <summary>A ping, <c>$PI</c>, or its answer, <c>$PO</c>: <c>$PIN172SP:N7938C:1736029820</c>.</summary>
    public sealed class FsdPing
    {
        public bool IsPong { get; set; }
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        /// <summary>Echoed unchanged in the answer.</summary>
        public string Timestamp { get; set; } = "";

        /// <summary>The answer to this ping: from and to swapped, the same timestamp.</summary>
        public FsdPing Answer() => new FsdPing { IsPong = true, From = To, To = From, Timestamp = Timestamp };

        public static bool TryParse(FsdLine line, out FsdPing ping)
        {
            ping = null;
            if (line.Verb != FsdVerb.Ping && line.Verb != FsdVerb.Pong) return false;
            ping = new FsdPing { IsPong = line.Verb == FsdVerb.Pong, From = line.Field(0), To = line.Field(1), Timestamp = line.Field(2) };
            return true;
        }

        public FsdLine ToLine() => new FsdLine(IsPong ? FsdVerb.Pong : FsdVerb.Ping, From, To, Timestamp);
    }

    /// <summary>
    /// A client query, <c>$CQ</c>, or its response, <c>$CR</c>: <c>$CQSAN_GND:SERVER:ATC:SAN_GND</c>. The payload is
    /// the fields after the query type (<c>ATC</c>, <c>IP</c>, <c>FP</c>, <c>RN</c>, <c>IT</c>, <c>WH</c>, ...).
    /// </summary>
    public sealed class FsdQuery
    {
        public bool IsResponse { get; set; }
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Type { get; set; } = "";
        public string[] Payload { get; set; } = Array.Empty<string>();

        public static FsdQuery Query(string from, string to, string type, params string[] payload) =>
            new FsdQuery { From = from, To = to, Type = type, Payload = payload };

        public static FsdQuery Response(string from, string to, string type, params string[] payload) =>
            new FsdQuery { IsResponse = true, From = from, To = to, Type = type, Payload = payload };

        public static bool TryParse(FsdLine line, out FsdQuery query)
        {
            query = null;
            if (line.Verb != FsdVerb.ClientQuery && line.Verb != FsdVerb.ClientResponse) return false;
            var payload = new string[Math.Max(0, line.Fields.Count - 3)];
            for (int i = 0; i < payload.Length; i++) payload[i] = line.Fields[3 + i];
            query = new FsdQuery
            {
                IsResponse = line.Verb == FsdVerb.ClientResponse,
                From = line.Field(0), To = line.Field(1), Type = line.Field(2), Payload = payload,
            };
            return true;
        }

        public FsdLine ToLine()
        {
            var fields = new string[3 + Payload.Length];
            fields[0] = From;
            fields[1] = To;
            fields[2] = Type;
            Payload.CopyTo(fields, 3);
            return new FsdLine(IsResponse ? FsdVerb.ClientResponse : FsdVerb.ClientQuery, fields);
        }
    }

    /// <summary>A handoff of an aircraft, <c>$HO</c>, or its acceptance, <c>$HA</c>: <c>$HOSAN_APP:LAX_35_CTR:ROU1887</c>.</summary>
    public sealed class FsdHandoff
    {
        public bool IsAccept { get; set; }
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Target { get; set; } = "";

        public static bool TryParse(FsdLine line, out FsdHandoff handoff)
        {
            handoff = null;
            if (line.Verb != FsdVerb.Handoff && line.Verb != FsdVerb.HandoffAccept) return false;
            handoff = new FsdHandoff { IsAccept = line.Verb == FsdVerb.HandoffAccept, From = line.Field(0), To = line.Field(1), Target = line.Field(2) };
            return true;
        }

        public FsdLine ToLine() => new FsdLine(IsAccept ? FsdVerb.HandoffAccept : FsdVerb.Handoff, From, To, Target);
    }

    /// <summary>
    /// Controller to controller data, <c>#PC</c>: <c>#PCSCT_APP:@94835:CCP:SC:ROU1887:ABC</c>. The static field
    /// <c>CCP</c> is checked; the type (<c>IH</c>, <c>SC</c>, <c>TA</c>, <c>HC</c>, ...) and its payload follow.
    /// </summary>
    public sealed class FsdProController
    {
        public const string Ccp = "CCP";

        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Type { get; set; } = "";
        public string[] Payload { get; set; } = Array.Empty<string>();

        public static bool TryParse(FsdLine line, out FsdProController pc)
        {
            pc = null;
            if (line.Verb != FsdVerb.ProController || line.Field(2) != Ccp) return false;
            var payload = new string[Math.Max(0, line.Fields.Count - 4)];
            for (int i = 0; i < payload.Length; i++) payload[i] = line.Fields[4 + i];
            pc = new FsdProController { From = line.Field(0), To = line.Field(1), Type = line.Field(3), Payload = payload };
            return true;
        }

        public FsdLine ToLine()
        {
            var fields = new string[4 + Payload.Length];
            fields[0] = From;
            fields[1] = To;
            fields[2] = Ccp;
            fields[3] = Type;
            Payload.CopyTo(fields, 4);
            return new FsdLine(FsdVerb.ProController, fields);
        }
    }

    /// <summary>A text message, <c>#TM</c>: <c>#TMSAN_GND:@49999:Yo SAN_TWR</c>. The message keeps its colons.</summary>
    public sealed class FsdTextMessage
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Message { get; set; } = "";

        public static bool TryParse(FsdLine line, out FsdTextMessage message)
        {
            message = null;
            if (line.Verb != FsdVerb.TextMessage) return false;
            message = new FsdTextMessage { From = line.Field(0), To = line.Field(1), Message = line.Field(2) };
            return true;
        }

        public FsdLine ToLine() => new FsdLine(FsdVerb.TextMessage, From, To, Message);
    }
}
