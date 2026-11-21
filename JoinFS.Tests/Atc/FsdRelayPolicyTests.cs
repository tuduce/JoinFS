using JoinFS.Fsd;

namespace JoinFS.Tests.Fsd
{
    public class FsdRelayPolicyTests
    {
        [Theory]
        [InlineData("$CQSCT_S_APP:@94835:IT:ROU1887")]
        [InlineData("$CRSERVER:SAN_GND:ATC:Y:SAN_GND")]
        [InlineData("#PCSCT_APP:@94835:CCP:SC:ROU1887:ABC")]
        [InlineData("#PCSAN_APP:LAX_35_CTR:CCP:HC:ROU1887")]
        [InlineData("$HOSAN_APP:LAX_35_CTR:ROU1887")]
        [InlineData("$HALAX_35_CTR:SAN_APP:ROU1887")]
        [InlineData("#TMSAN_GND:@49999:Yo SAN_TWR")]
        [InlineData("$PIN172SP:N7938C:1736029820")]
        [InlineData("$PON7938C:N172SP:1736029820")]
        [InlineData("$AMA:B:C:I:C172:100:EDDM:::3500:EDNX:0:25:0:0::X:Y")]
        public void RelayedLines(string text)
        {
            Assert.Equal(FsdRelayRefusal.None, FsdRelayPolicy.Check(text));
        }

        [Theory]
        [InlineData("#AAEDDM_TWR:SERVER:Carla:1::4:100")]
        [InlineData("#DAEDDM_TWR:1")]
        [InlineData("%EDDM_TWR:28550:4:50:3:48.35:11.78:0")]
        [InlineData("@N:DLH4TK:2000:1:48.1:11.7:1500:120:0:0")]
        [InlineData("$FPA:B:I:C172:100:EDDM:::3500:EDNX:0:25:0:0::X:Y")]
        [InlineData("$!!ABC_SUP:N505GS:Refusing")]
        [InlineData("$ZCN7938C:SERVER:0123456789abcdef")]
        [InlineData("$ZRSERVER:N7938C:4d87482917dc9c2395bd2df151835734")]
        [InlineData("#SBN7938C:DAL625:PIR")]
        public void LinesThatAreNotRelayed(string text)
        {
            Assert.Equal(FsdRelayRefusal.VerbNotRelayed, FsdRelayPolicy.Check(text));
        }

        [Theory]
        [InlineData("IC")] [InlineData("IK")] [InlineData("IB")] [InlineData("IO")]
        [InlineData("OC")] [InlineData("OK")] [InlineData("OB")] [InlineData("OO")]
        [InlineData("MC")] [InlineData("MK")] [InlineData("MB")] [InlineData("MO")]
        public void Landline_IsDropped(string type)
        {
            Assert.Equal(FsdRelayRefusal.Landline, FsdRelayPolicy.Check("#PCSAN_TWR:SAN_APP:CCP:" + type + ":127.0.0.1:6789"));
        }

        [Theory]
        [InlineData("IH")] [InlineData("SC")] [InlineData("TA")] [InlineData("FA")] [InlineData("BC")] [InlineData("HC")]
        [InlineData("PT")] [InlineData("DP")] [InlineData("ST")] [InlineData("GD")] [InlineData("VT")] [InlineData("ID")]
        public void ProControllerData_IsNotLandline(string type)
        {
            Assert.Equal(FsdRelayRefusal.None, FsdRelayPolicy.Check("#PCSAN_TWR:SAN_APP:CCP:" + type + ":ROU1887"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("hello")]
        [InlineData("$")]
        public void Malformed(string text)
        {
            Assert.Equal(FsdRelayRefusal.Malformed, FsdRelayPolicy.Check(text));
        }

        [Theory]
        [InlineData("#TMA:B:line\rbreak")]
        [InlineData("#TMA:B:bell\a")]
        [InlineData("#TMA:B:nul\0")]
        [InlineData("#TMA:B:del\u007F")]
        [InlineData("$CQA:B:IT:X\n")]
        public void ControlCharacter(string text)
        {
            Assert.Equal(FsdRelayRefusal.ControlCharacter, FsdRelayPolicy.Check(text));
        }

        [Fact]
        public void TooLong_IsMeasuredInBytes()
        {
            string prefix = "#TMA:B:";
            Assert.Equal(FsdRelayRefusal.None, FsdRelayPolicy.Check(prefix + new string('x', FsdRelayPolicy.MaxLineBytes - prefix.Length)));
            Assert.Equal(FsdRelayRefusal.Malformed, FsdRelayPolicy.Check(prefix + new string('x', FsdRelayPolicy.MaxLineBytes - prefix.Length + 1)));
            // 400 characters of 3 bytes each are 1,200 bytes
            Assert.Equal(FsdRelayRefusal.Malformed, FsdRelayPolicy.Check(prefix + new string('€', 400)));
        }

        [Theory]
        [InlineData("SERVER", FsdAddressKind.Server)]
        [InlineData("@94835", FsdAddressKind.Group)]
        [InlineData("@49999", FsdAddressKind.Group)]
        [InlineData("*A", FsdAddressKind.Group)]
        [InlineData("*", FsdAddressKind.Group)]
        [InlineData("@22800", FsdAddressKind.Group)]
        [InlineData("@21950&@19600", FsdAddressKind.Group)]
        [InlineData("@94836", FsdAddressKind.Callsign)]
        [InlineData("*S", FsdAddressKind.Callsign)]
        [InlineData("@2280", FsdAddressKind.Callsign)]
        [InlineData("@", FsdAddressKind.Callsign)]
        [InlineData("EDDM_APP", FsdAddressKind.Callsign)]
        [InlineData("DLH4TK", FsdAddressKind.Callsign)]
        public void Classify(string to, FsdAddressKind kind)
        {
            Assert.Equal(kind, FsdRelayPolicy.Classify(to));
        }
    }
}
