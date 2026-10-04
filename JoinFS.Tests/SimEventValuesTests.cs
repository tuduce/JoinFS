namespace JoinFS.Tests
{
    /// <summary>
    /// Sim.Event values travel as raw numbers over the legacy wire, inside .jfs recordings and to the
    /// X-Plane plugin, so existing members must never be renumbered - new ones are only appended.
    /// </summary>
    public class SimEventValuesTests
    {
        [Theory]
        [InlineData(Sim.Event.OBJECT_ADDED, 0)]
        [InlineData(Sim.Event.OBJECT_REMOVED, 1)]
        [InlineData(Sim.Event.FRAME, 2)]
        [InlineData(Sim.Event.PAUSE, 3)]
        [InlineData(Sim.Event.RUDDER_SET, 4)]
        [InlineData(Sim.Event.ELEVATOR_SET, 5)]
        [InlineData(Sim.Event.AILERON_SET, 6)]
        [InlineData(Sim.Event.SMOKE_ON, 7)]
        [InlineData(Sim.Event.SMOKE_OFF, 8)]
        [InlineData(Sim.Event.AP_HEADING_VAR, 9)]
        [InlineData(Sim.Event.EVENT_00011000, 10)]
        [InlineData(Sim.Event.EVENT_00011001, 11)]
        [InlineData(Sim.Event.EVENT_00011002, 12)]
        [InlineData(Sim.Event.EVENT_00011003, 13)]
        [InlineData(Sim.Event.EVENT_00011004, 14)]
        [InlineData(Sim.Event.EVENT_00011005, 15)]
        [InlineData(Sim.Event.EVENT_00011006, 16)]
        [InlineData(Sim.Event.EVENT_00011007, 17)]
        [InlineData(Sim.Event.EVENT_00011008, 18)]
        [InlineData(Sim.Event.EVENT_00011009, 19)]
        [InlineData(Sim.Event.EVENT_0001100A, 20)]
        public void ExistingEvents_KeepTheirWireValue(Sim.Event simEvent, int expected)
        {
            Assert.Equal(expected, (int)simEvent);
        }

        [Fact]
        public void SimStartAndStop_AreAppendedAfterAllWireEvents()
        {
            Assert.Equal(21, (int)Sim.Event.SIM_START);
            Assert.Equal(22, (int)Sim.Event.SIM_STOP);
        }
    }
}
