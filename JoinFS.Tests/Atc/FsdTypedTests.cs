using JoinFS.Fsd;

namespace JoinFS.Tests.Fsd
{
    public class FsdTypedTests
    {
        static FsdLine Parse(string text)
        {
            Assert.True(FsdLine.TryParse(text, out FsdLine line), text);
            return line;
        }

        // --- pitch, bank, heading

        [Fact]
        public void PitchBankHeading_OfTheDocumentExample()
        {
            FsdPitchBankHeading.Unpack(4290776072u, out double pitch, out double bank, out double heading);
            Assert.Equal(0.35, pitch, 2);
            Assert.Equal(0.0, bank, 2);
            Assert.Equal(270.7, heading, 1);
            Assert.Equal(4290776072u, FsdPitchBankHeading.Pack(pitch, bank, heading));
        }

        [Fact]
        public void PitchBankHeading_EveryStep_RoundTrips()
        {
            for (uint pitch = 0; pitch < 1024; pitch++)
            for (uint bank = 0; bank < 1024; bank += 31)
            for (uint heading = 0; heading < 1024; heading += 29)
            {
                uint packed = pitch << 22 | bank << 12 | heading << 2;
                FsdPitchBankHeading.Unpack(packed, out double p, out double b, out double h);
                Assert.Equal(packed, FsdPitchBankHeading.Pack(p, b, h));
            }
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(10, -20, 90)]
        [InlineData(-5, 45, 359)]
        [InlineData(0, 0, 360)]
        [InlineData(0, 0, -90)]
        public void PitchBankHeading_AnglesComeBackWithinAStep(double pitch, double bank, double heading)
        {
            FsdPitchBankHeading.Unpack(FsdPitchBankHeading.Pack(pitch, bank, heading), out double p, out double b, out double h);
            Assert.InRange(p, pitch - 0.36, pitch + 0.36);
            Assert.InRange(b, bank - 0.36, bank + 0.36);
            double headingWrapped = ((heading % 360) + 360) % 360;
            double difference = Math.Abs(h - headingWrapped);
            Assert.True(difference < 0.36 || difference > 359.64, $"{h} vs {headingWrapped}");
        }

        [Fact]
        public void PitchBankHeading_TheTwoLowestBitsAreZero()
        {
            Assert.Equal(0u, FsdPitchBankHeading.Pack(12.3, -4.5, 200.1) & 3u);
        }

        // --- frequency

        [Theory]
        [InlineData("28550", 128550)]
        [InlineData("18700", 118700)]
        [InlineData("8500", 108500)]
        [InlineData("99998", 199998)]
        public void Frequency_RoundTrips(string field, int khz)
        {
            Assert.True(FsdFrequency.TryParse(field, out int parsed));
            Assert.Equal(khz, parsed);
            Assert.Equal(field, FsdFrequency.ToField(khz));
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("-1")]
        [InlineData("123456")]
        public void Frequency_NotAFrequency(string field)
        {
            Assert.False(FsdFrequency.TryParse(field, out _));
        }

        [Fact]
        public void Frequency_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FsdFrequency.ToField(99999));
            Assert.Throws<ArgumentOutOfRangeException>(() => FsdFrequency.ToField(200000));
        }

        [Fact]
        public void Frequency_ToMegahertz()
        {
            Assert.Equal("128.550", FsdFrequency.ToMegahertz(128550));
            Assert.Equal("118.005", FsdFrequency.ToMegahertz(118005));
        }

        // --- typed lines

        [Fact]
        public void Position_Fields()
        {
            Assert.True(FsdPosition.TryParse(Parse("@S:GTI8197:2000:1:40.65906:-73.79891:26:0:4290776072:359"), out var p));
            Assert.Equal('S', p.Mode);
            Assert.Equal("GTI8197", p.Callsign);
            Assert.Equal(2000, p.Squawk);
            Assert.Equal(40.65906, p.Latitude, 5);
            Assert.Equal(-73.79891, p.Longitude, 5);
            Assert.Equal(26, p.AltitudeFeet);
            Assert.Equal(0, p.GroundSpeedKnots);
            Assert.Equal(4290776072u, p.PitchBankHeading);
            Assert.Equal(359, p.AltitudeCorrection);
        }

        [Fact]
        public void Position_Built_UsesFiveDecimalsAndFourSquawkDigits()
        {
            var p = new FsdPosition
            {
                Mode = 'Y', Callsign = "DLH4TK", Squawk = 7, Rating = 1, Latitude = 48.1, Longitude = -11.75,
                AltitudeFeet = 1500, GroundSpeedKnots = 120, PitchBankHeading = FsdPitchBankHeading.Pack(2, 0, 90),
            };
            Assert.Equal("@Y:DLH4TK:0007:1:48.10000:-11.75000:1500:120:" + p.PitchBankHeading + ":0", p.ToLine().ToString());
        }

        [Fact]
        public void AtcPosition_SeveralFrequencies()
        {
            Assert.True(FsdAtcPosition.TryParse(Parse("%EDDM_TWR:28550&20950:4:50:3:48.35300:11.78600:0"), out var p));
            Assert.Equal(new[] { 128550, 120950 }, p.FrequenciesKhz);
            Assert.Equal(FsdFacility.Tower, p.Facility);
            Assert.Equal(50, p.VisibilityRange);
            Assert.Equal(3, p.Rating);
            Assert.Equal("%EDDM_TWR:28550&20950:4:50:3:48.35300:11.78600:0", p.ToLine().ToString());
        }

        [Fact]
        public void AtcPosition_NoFrequency()
        {
            Assert.True(FsdAtcPosition.TryParse(Parse("%EDDM_OBS::0:50:1:48.35300:11.78600:0"), out var p));
            Assert.Empty(p.FrequenciesKhz);
            Assert.Equal("%EDDM_OBS::0:50:1:48.35300:11.78600:0", p.ToLine().ToString());
        }

        [Fact]
        public void AddAtc_AsTheServerPassesItOn_HasNoToken()
        {
            var add = new FsdAddAtc { Callsign = "EDDM_APP", To = "CLIENT", RealName = "Ben", Cid = "1", Rating = 4, ProtocolRevision = "100" };
            Assert.Equal("#AAEDDM_APP:CLIENT:Ben:1::4:100", add.ToLine().ToString());
        }

        [Fact]
        public void Error_UsesThreeDigits()
        {
            var error = FsdError.Create("EDDM_TWR", FsdErrorCode.CallsignInUse, "EDDM_TWR", "Callsign already in use");
            Assert.Equal("$ERSERVER:EDDM_TWR:001:EDDM_TWR:Callsign already in use", error.ToLine().ToString());
            Assert.True(FsdError.TryParse(Parse("$ERSERVER:unknown:006::Invalid CID/password."), out var e));
            Assert.Equal(6, e.Code);
            Assert.Equal("Invalid CID/password.", e.Description);
        }

        [Fact]
        public void ServerId_Built()
        {
            var id = new FsdServerId { Version = "JoinFS", Challenge = "" };
            Assert.Equal("$DISERVER:CLIENT:JoinFS:", id.ToLine().ToString());
        }

        [Fact]
        public void Delete_AtcAndPilot()
        {
            Assert.Equal("#DAEDDM_TWR:0", FsdDelete.Atc("EDDM_TWR", "0").ToLine().ToString());
            Assert.Equal("#DPDLH4TK:", FsdDelete.Pilot("DLH4TK").ToLine().ToString());
            Assert.True(FsdDelete.TryParse(Parse("#DAEDDM_TWR:123:abcdef"), out var d));
            Assert.False(d.IsPilot);
            Assert.Equal("EDDM_TWR", d.Callsign);
        }

        [Fact]
        public void Ping_AnswerSwapsTheEndsAndEchoesTheTimestamp()
        {
            Assert.True(FsdPing.TryParse(Parse("$PIN172SP:N7938C:1736029820"), out var ping));
            Assert.Equal("$PON7938C:N172SP:1736029820", ping.Answer().ToLine().ToString());
        }

        [Fact]
        public void Query_Payload()
        {
            Assert.True(FsdQuery.TryParse(Parse("$CQSCT_S_APP:@94835:TA:ROU1887:23000"), out var q));
            Assert.False(q.IsResponse);
            Assert.Equal("TA", q.Type);
            Assert.Equal(new[] { "ROU1887", "23000" }, q.Payload);
            Assert.Equal("$CRSERVER:SAN_GND:ATC:Y:SAN_GND",
                FsdQuery.Response("SERVER", "SAN_GND", "ATC", "Y", "SAN_GND").ToLine().ToString());
            Assert.Equal("$CQA:SERVER:IP", FsdQuery.Query("A", "SERVER", "IP").ToLine().ToString());
        }

        [Fact]
        public void ProController_Payload()
        {
            Assert.True(FsdProController.TryParse(Parse("#PCSCT_APP:@94835:CCP:SC:ROU1887:ABC"), out var pc));
            Assert.Equal("SC", pc.Type);
            Assert.Equal(new[] { "ROU1887", "ABC" }, pc.Payload);
            Assert.True(FsdProController.TryParse(Parse("#PCSCT_APP:SAN_APP:CCP:VER"), out var ver));
            Assert.Empty(ver.Payload);
        }

        [Fact]
        public void Handoff_AndAccept()
        {
            Assert.True(FsdHandoff.TryParse(Parse("$HOSAN_APP:LAX_35_CTR:ROU1887"), out var ho));
            Assert.False(ho.IsAccept);
            Assert.Equal("ROU1887", ho.Target);
            Assert.True(FsdHandoff.TryParse(Parse("$HALAX_35_CTR:SAN_APP:ROU1887"), out var ha));
            Assert.True(ha.IsAccept);
        }

        [Fact]
        public void FlightPlan_Fields()
        {
            FsdLine line = Parse("$FPAAL152:SAN_TWR:I:H/B772/L:487:KLAX:250:250:35000:KDFW:2:40:4:5:KOKC:PBN/A1B1 DOF/250111:DOTSS2 CNERY BLH");
            Assert.True(FsdFlightPlan.TryParse(line, out var plan));
            Assert.False(plan.IsAmendment);
            Assert.Equal("AAL152", plan.Aircraft);
            Assert.Equal('I', plan.Rules);
            Assert.Equal("H/B772/L", plan.Equipment);
            Assert.Equal(487, plan.TrueAirspeedKnots);
            Assert.Equal("KLAX", plan.Departure);
            Assert.Equal("35000", plan.CruiseAltitude);
            Assert.Equal("KDFW", plan.Destination);
            Assert.Equal(2, plan.HoursEnroute);
            Assert.Equal(40, plan.MinutesEnroute);
            Assert.Equal(4, plan.HoursFuel);
            Assert.Equal(5, plan.MinutesFuel);
            Assert.Equal("KOKC", plan.Alternate);
            Assert.Equal("PBN/A1B1 DOF/250111", plan.Remarks);
            Assert.Equal("DOTSS2 CNERY BLH", plan.Route);
        }

        [Fact]
        public void FlightPlan_Amendment_NamesTheAircraftInItsOwnField()
        {
            // the document's own $AM example has five extra fields after the alternate; this one has none
            string text = "$AMSAN_TWR:@94835:AAL123:I:H/B737/L:420:KLAX:230:240:35000:KORD:3:15:3:30:KDEN:RMK/TCAS:DOTSS2 CNERY J169";
            Assert.True(FsdFlightPlan.TryParse(Parse(text), out var plan));
            Assert.True(plan.IsAmendment);
            Assert.Equal("SAN_TWR", plan.From);
            Assert.Equal("AAL123", plan.Aircraft);
            Assert.Equal('I', plan.Rules);
            Assert.Equal(420, plan.TrueAirspeedKnots);
            Assert.Equal("KDEN", plan.Alternate);
            Assert.Equal("RMK/TCAS", plan.Remarks);
            Assert.Equal("DOTSS2 CNERY J169", plan.Route);
            Assert.Equal(text, plan.ToLine().ToString());
        }

        [Fact]
        public void FlightPlan_BuiltFromValues()
        {
            var plan = new FsdFlightPlan
            {
                From = "DLH4TK", To = "EDDM_TWR", Rules = 'V', Equipment = "C172", TrueAirspeedKnots = 110, Departure = "EDDM",
                CruiseAltitude = "3500", Destination = "EDNX", HoursEnroute = 0, MinutesEnroute = 25, Remarks = "TEST", Route = "DCT",
            };
            Assert.Equal("$FPDLH4TK:EDDM_TWR:V:C172:110:EDDM:::3500:EDNX:0:25:0:0::TEST:DCT", plan.ToLine().ToString());
            Assert.True(FsdFlightPlan.TryParse(plan.ToLine(), out var back));
            Assert.Equal(plan.ToLine().ToString(), back.ToLine().ToString());
        }

        [Fact]
        public void ClientId_KeepsEverythingAsText()
        {
            Assert.True(FsdClientId.TryParse(Parse("$IDEDDM_TWR:SERVER:ffff:EuroScope:3.2:1.0:1234567:-9:abc"), out var id));
            Assert.Equal("EDDM_TWR", id.From);
            Assert.Equal("EuroScope", id.SoftwareName);
            Assert.Equal("3.2", id.MajorVersion);
        }
    }
}
