using Xunit;

namespace JoinFS.Tests
{
    /// <summary>
    /// A bitfield variable (LIGHT STATES) applied to an aircraft takes only the bits its
    /// variable files declare; the other bits stay as the simulator has them.
    /// </summary>
    public class MergeDeclaredBitsTests
    {
        // Plane.txt declares LIGHT STATES bits 0-3 and 5-9; bit 4 (strobe) belongs to LIGHT STROBE
        const int PlaneDeclared = 0b11_1110_1111;
        const int Strobe = 1 << 4;
        const int Beacon = 1 << 1;

        [Fact]
        public void UndeclaredStrobeBit_FromNetwork_DoesNotTurnStrobeOn()
        {
            int merged = JoinFS.VariableMgr.Set.MergeDeclaredBits(Strobe | Beacon, 0, PlaneDeclared);

            Assert.Equal(Beacon, merged);
        }

        [Fact]
        public void UndeclaredStrobeBit_InSim_IsKept()
        {
            int merged = JoinFS.VariableMgr.Set.MergeDeclaredBits(0, Strobe | Beacon, PlaneDeclared);

            Assert.Equal(Strobe, merged);
        }

        [Fact]
        public void OnlyUndeclaredBitsDiffer_MergeEqualsCurrent_SoNothingIsWritten()
        {
            int current = Beacon;
            int merged = JoinFS.VariableMgr.Set.MergeDeclaredBits(Strobe | Beacon, current, PlaneDeclared);

            Assert.Equal(current, merged);
        }

        [Fact]
        public void AllBitsDeclared_TakesIncomingWhole()
        {
            int merged = JoinFS.VariableMgr.Set.MergeDeclaredBits(Strobe, Beacon, 0b11_1111_1111);

            Assert.Equal(Strobe, merged);
        }
    }
}
