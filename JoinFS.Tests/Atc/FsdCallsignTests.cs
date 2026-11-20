using JoinFS.Fsd;

namespace JoinFS.Tests.Fsd
{
    public class FsdCallsignTests
    {
        [Theory]
        [InlineData("EDDM_TWR")]
        [InlineData("EDDM_N_TWR")]
        [InlineData("EDMM_ALB_CTR")]
        [InlineData("EDDM_ATIS")]
        [InlineData("ABC")]
        [InlineData("EDDM_N_S_TWR")]
        [InlineData("EDDM__TWR")]
        public void StationCallsign_Valid(string callsign)
        {
            Assert.True(FsdCallsign.IsValidStation(callsign));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("AB")]
        [InlineData("EDDM_N_S_TWRS")]
        [InlineData("EDMM_ALB_CTRX")]
        [InlineData("eddm_twr")]
        [InlineData("EDDM TWR")]
        [InlineData("EDDM-TWR")]
        [InlineData("EDDM_TWR:")]
        [InlineData("EDDM_TÜR")]
        public void StationCallsign_Invalid(string callsign)
        {
            Assert.False(FsdCallsign.IsValidStation(callsign));
        }

        [Fact]
        public void StationCallsign_TwelveCharactersIsTheLimit()
        {
            Assert.True(FsdCallsign.IsValidStation("EDDM_N_S_TWR"));
            Assert.False(FsdCallsign.IsValidStation("EDDM_N_S_TWR1"));
        }

        static string[] Names(params FsdAircraft[] aircraft) => FsdCallsign.Aircraft(aircraft);

        [Fact]
        public void Aircraft_TheFlightPlanCallsign_UpperCaseAndCleaned()
        {
            string[] names = Names(
                new FsdAircraft(1, 1, "dlh4tk"),
                new FsdAircraft(1, 2, "D-ABC 1_2"),
                new FsdAircraft(1, 3, "ABCDEFGHIJKL"));
            Assert.Equal(new[] { "DLH4TK", "D-ABC12", "ABCDEFGHIJ" }, names);
        }

        [Fact]
        public void Aircraft_EmptyCallsign_GetsJfsAndFourHexDigits()
        {
            string[] names = Names(new FsdAircraft(7, 42, null), new FsdAircraft(7, 43, ""), new FsdAircraft(8, 42, "  !! "));
            foreach (string name in names)
            {
                Assert.Matches("^JFS[0-9A-F]{4}$", name);
            }
            Assert.Equal(3, names.Distinct().Count());
        }

        [Fact]
        public void Aircraft_EmptyCallsign_IsStable()
        {
            // every node must compute the same name: the value is fixed here so a change of the hash is noticed
            Assert.Equal(Names(new FsdAircraft(7, 42, "")), Names(new FsdAircraft(7, 42, null)));
            Assert.Equal("JFS3EEC", Names(new FsdAircraft(7, 42, ""))[0]);
        }

        [Fact]
        public void Aircraft_Duplicate_TheLowestKeepsItsName()
        {
            string[] names = Names(
                new FsdAircraft(9, 5, "DLH4TK"),
                new FsdAircraft(2, 8, "DLH4TK"),
                new FsdAircraft(2, 3, "DLH4TK"));
            Assert.Equal("DLH4TK", names[2]);
            Assert.NotEqual("DLH4TK", names[0]);
            Assert.NotEqual("DLH4TK", names[1]);
            Assert.Matches("^DLH4TK[0-9A-F]{2}$", names[0]);
            Assert.Matches("^DLH4TK[0-9A-F]{2}$", names[1]);
            Assert.Equal(3, names.Distinct().Count());
        }

        [Fact]
        public void Aircraft_Duplicate_LongNameIsCutToEightCharacters()
        {
            string[] names = Names(new FsdAircraft(1, 1, "ABCDEFGHIJ"), new FsdAircraft(2, 1, "ABCDEFGHIJ"));
            Assert.Equal("ABCDEFGHIJ", names[0]);
            Assert.Matches("^ABCDEFGH[0-9A-F]{2}$", names[1]);
        }

        [Fact]
        public void Aircraft_SameResultForAnyOrderOfTheInput()
        {
            var input = new List<FsdAircraft>
            {
                new FsdAircraft(5, 1, "DLH4TK"), new FsdAircraft(5, 2, "dlh4tk"), new FsdAircraft(3, 9, "DLH4TK"),
                new FsdAircraft(3, 1, ""), new FsdAircraft(4, 1, ""), new FsdAircraft(4, 2, "BAW1"), new FsdAircraft(6, 1, "BAW1"),
                new FsdAircraft(7, 1, "ABCDEFGHIJKL"), new FsdAircraft(8, 1, "ABCDEFGHIJ"), new FsdAircraft(9, 1, null),
            };
            var expected = new Dictionary<(ulong, ulong), string>();
            string[] reference = FsdCallsign.Aircraft(input);
            for (int i = 0; i < input.Count; i++) expected[(input[i].Owner, input[i].ObjectId)] = reference[i];

            var random = new Random(12345);
            for (int round = 0; round < 200; round++)
            {
                var shuffled = input.OrderBy(_ => random.Next()).ToList();
                string[] names = FsdCallsign.Aircraft(shuffled);
                for (int i = 0; i < shuffled.Count; i++)
                    Assert.Equal(expected[(shuffled[i].Owner, shuffled[i].ObjectId)], names[i]);
                Assert.Equal(shuffled.Count, names.Distinct().Count());
            }
        }

        [Fact]
        public void Aircraft_ASuffixedNameNeverEqualsAnotherAircraftsName()
        {
            // a real aircraft that already has the cut-and-suffixed name of the second DLH4TK keeps its name
            string suffixed = Names(new FsdAircraft(1, 1, "DLH4TK"), new FsdAircraft(2, 1, "DLH4TK"))[1];
            string[] names = Names(new FsdAircraft(1, 1, "DLH4TK"), new FsdAircraft(2, 1, "DLH4TK"), new FsdAircraft(3, 1, suffixed));
            Assert.Equal(3, names.Distinct().Count());
            Assert.Equal(suffixed, names[2]);
        }

        [Fact]
        public void Aircraft_NoAircraft()
        {
            Assert.Empty(FsdCallsign.Aircraft(new List<FsdAircraft>()));
        }
    }
}
