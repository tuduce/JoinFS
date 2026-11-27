using JoinFS.Fsd;

namespace JoinFS.Tests.Fsd
{
    /// <summary>
    /// Every example line of docs/ATC/FSD-protocol.md is parsed and rebuilt byte for byte, as a plain line and,
    /// where the library has a typed form of its verb, through that form.
    /// </summary>
    public class FsdLineTests
    {
        /// <summary>The example lines of the document: the lines of its code blocks that start with a verb.</summary>
        static List<string> DocumentExamples()
        {
            string dir = AppContext.BaseDirectory;
            string path = null;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "docs", "ATC", "FSD-protocol.md");
                if (File.Exists(candidate)) { path = candidate; break; }
                dir = Path.GetDirectoryName(dir);
            }
            Assert.True(path != null, "docs/ATC/FSD-protocol.md not found above " + AppContext.BaseDirectory);

            var lines = new List<string>();
            bool inBlock = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("```")) { inBlock = !inBlock; continue; }
                if (inBlock && line.Length > 0 && "$#%@^".IndexOf(line[0]) >= 0) lines.Add(line);
            }
            return lines;
        }

        [Fact]
        public void TheDocumentHasExamples()
        {
            Assert.True(DocumentExamples().Count > 60);
        }

        [Fact]
        public void EveryExampleLine_ParsesAndIsRebuiltByteForByte()
        {
            foreach (string text in DocumentExamples())
            {
                Assert.True(FsdLine.TryParse(text, out FsdLine line), text);
                Assert.Equal(text, line.ToString());
            }
        }

        [Fact]
        public void EveryExampleLine_OfATypedVerb_IsRebuiltByteForByteFromItsTypedForm()
        {
            int typed = 0;
            foreach (string text in DocumentExamples())
            {
                FsdLine line = Parse(text);
                FsdLine rebuilt = TypedRoundTrip(line);
                if (rebuilt == null) continue;
                typed++;
                Assert.Equal(text, rebuilt.ToString());
            }
            Assert.True(typed > 40, "only " + typed + " typed examples");
        }

        /// <summary>Through the typed form of the line's verb; null for a verb that has none.</summary>
        static FsdLine TypedRoundTrip(FsdLine line)
        {
            switch (line.Verb)
            {
                case FsdVerb.ServerId: Assert.True(FsdServerId.TryParse(line, out var di)); return di.ToLine();
                case FsdVerb.ClientId: Assert.True(FsdClientId.TryParse(line, out var id)); return id.ToLine();
                case FsdVerb.AddAtc: Assert.True(FsdAddAtc.TryParse(line, out var aa)); return aa.ToLine();
                case FsdVerb.AtcPosition: Assert.True(FsdAtcPosition.TryParse(line, out var pos)); return pos.ToLine();
                case FsdVerb.DeleteAtc:
                case FsdVerb.DeletePilot: Assert.True(FsdDelete.TryParse(line, out var del)); return del.ToLine();
                case FsdVerb.PilotPosition: Assert.True(FsdPosition.TryParse(line, out var at)); return at.ToLine();
                case FsdVerb.FlightPlan:
                case FsdVerb.FlightPlanAmendment: Assert.True(FsdFlightPlan.TryParse(line, out var fp)); return fp.ToLine();
                case FsdVerb.Ping:
                case FsdVerb.Pong: Assert.True(FsdPing.TryParse(line, out var ping)); return ping.ToLine();
                case FsdVerb.ClientQuery:
                case FsdVerb.ClientResponse: Assert.True(FsdQuery.TryParse(line, out var q)); return q.ToLine();
                case FsdVerb.Handoff:
                case FsdVerb.HandoffAccept: Assert.True(FsdHandoff.TryParse(line, out var ho)); return ho.ToLine();
                case FsdVerb.ProController: Assert.True(FsdProController.TryParse(line, out var pc)); return pc.ToLine();
                case FsdVerb.TextMessage: Assert.True(FsdTextMessage.TryParse(line, out var tm)); return tm.ToLine();
                case FsdVerb.Error: Assert.True(FsdError.TryParse(line, out var er)); return er.ToLine();
                default: return null;
            }
        }

        static FsdLine Parse(string text)
        {
            Assert.True(FsdLine.TryParse(text, out FsdLine line), text);
            return line;
        }

        [Theory]
        [InlineData("@S:GTI8197:2000:1:40.65906:-73.79891:26:0:4290776072:359", "@", "GTI8197", "")]
        [InlineData("%EWR_P_APP:28550:5:150:4:40.67317:-74.18533:0", "%", "EWR_P_APP", "")]
        [InlineData("$CQSAN_GND:SERVER:ATC:SAN_GND", "$CQ", "SAN_GND", "SERVER")]
        [InlineData("#DASAN_TWR:1555555", "#DA", "SAN_TWR", "")]
        [InlineData("#TMSAN_GND:@49999:Yo SAN_TWR", "#TM", "SAN_GND", "@49999")]
        [InlineData("$!!ABC_SUP:N505GS:Refusing", "$!!", "ABC_SUP", "N505GS")]
        public void VerbFromAndTo(string text, string verb, string from, string to)
        {
            FsdLine line = Parse(text);
            Assert.Equal(verb, line.Verb);
            Assert.Equal(from, line.From);
            Assert.Equal(to, line.To);
        }

        [Fact]
        public void TheLastFieldOfATextMessage_KeepsItsColons()
        {
            FsdLine line = Parse("#TMSAN_GND:SAN_TWR:Meet at 12:30: gate 4");
            Assert.Equal(3, line.Fields.Count);
            Assert.Equal("Meet at 12:30: gate 4", line.Field(2));
            Assert.True(FsdTextMessage.TryParse(line, out var tm));
            Assert.Equal("Meet at 12:30: gate 4", tm.Message);
        }

        [Fact]
        public void TheRouteOfAFlightPlan_KeepsItsColons()
        {
            FsdLine line = Parse("$FPAAL152:SAN_TWR:I:B772:487:KLAX:1200:1200:35000:KDFW:2:40:4:5:KOKC:RMK/X:DCT A:B C");
            Assert.True(FsdFlightPlan.TryParse(line, out var plan));
            Assert.Equal("DCT A:B C", plan.Route);
        }

        [Fact]
        public void EmptyFieldsAreKept()
        {
            FsdLine line = Parse("$CRJBU325:JBU617:RN:John Doe::1");
            Assert.Equal(new[] { "JBU325", "JBU617", "RN", "John Doe", "", "1" }, line.Fields);
        }

        [Theory]
        [InlineData("")]
        [InlineData("hello")]
        [InlineData("$")]
        [InlineData("#A")]
        [InlineData("$CQA:B\r")]
        [InlineData("$CQA:B\n")]
        [InlineData("#TMA:B:bell\a")]
        public void NotALine_DoesNotParse(string text)
        {
            Assert.False(FsdLine.TryParse(text, out _));
        }

        [Fact]
        public void BuildingALine_GivesTheText()
        {
            Assert.Equal("$PIA:B:1", new FsdLine(FsdVerb.Ping, "A", "B", "1").ToString());
            Assert.Equal("%A", new FsdLine(FsdVerb.AtcPosition, "A").ToString());
        }

        [Fact]
        public void TypedLines_OfAnotherVerb_DoNotParse()
        {
            FsdLine ping = Parse("$PIA:B:1");
            Assert.False(FsdQuery.TryParse(ping, out _));
            Assert.False(FsdPosition.TryParse(ping, out _));
            Assert.False(FsdAtcPosition.TryParse(ping, out _));
            Assert.False(FsdProController.TryParse(Parse("#PCA:B:XXX:SC:T"), out _));
        }

        [Fact]
        public void ANumberThatIsNoNumber_DoesNotParse()
        {
            Assert.False(FsdAtcPosition.TryParse(Parse("%A:28550:five:150:4:40.1:-74.1:0"), out _));
            Assert.False(FsdPosition.TryParse(Parse("@N:A:9999:1:40.1:-74.1:26:0:1:0"), out _));
            Assert.False(FsdPosition.TryParse(Parse("@N:A:2000:1:north:-74.1:26:0:1:0"), out _));
        }
    }
}
