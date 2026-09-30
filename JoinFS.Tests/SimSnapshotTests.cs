using JoinFS.Net;

namespace JoinFS.Tests
{
    /// <summary>
    /// Snapshot copies (Sim.Obj.CloneView): other threads read them while the sim thread goes on
    /// changing the live objects, so a copy must share nothing the sim thread changes.
    /// </summary>
    public class SimSnapshotTests
    {
        static Sim.Plane LivePlane()
        {
            var plane = new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 7);
            plane.flightPlan.callsign = "DLH1";
            plane.simPosition.geo.y = 1000;
            plane.netPosition.geo.y = 2000;
            plane.netPosition.staticCgToGround = 5.5;
            plane.netVelocity.linear.x = 3;
            return plane;
        }

        [Fact]
        public void Copy_PointsBackToTheLiveObject()
        {
            Sim.Plane live = LivePlane();
            var copy = (Sim.Plane)live.CloneView();

            Assert.Same(live, copy.Source);
            Assert.Null(live.Source);
            Assert.Equal(7u, copy.netId);
            Assert.Equal("DLH1", copy.flightPlan.callsign);
            Assert.Equal(5.5, copy.netPosition.staticCgToGround);
        }

        [Fact]
        public void Copy_IsUnaffectedByLaterChangesToTheLiveObject()
        {
            Sim.Plane live = LivePlane();
            var copy = (Sim.Plane)live.CloneView();

            live.flightPlan.callsign = "BAW2";
            live.simPosition.geo.y = 1;
            live.netPosition.geo.y = 2;
            live.netPosition.angles.y = 1.5;
            live.netVelocity.linear.x = 4;
            live.simPosition = new Sim.Pos();

            Assert.Equal("DLH1", copy.flightPlan.callsign);
            Assert.Equal(1000, copy.simPosition.geo.y);
            Assert.Equal(2000, copy.netPosition.geo.y);
            Assert.Equal(0, copy.netPosition.angles.y);
            Assert.Equal(3, copy.netVelocity.linear.x);
        }

        [Fact]
        public void FlightPlanClone_CopiesEveryField()
        {
            var plan = new Sim.FlightPlan { callsign = "A", departure = "EDDF", destination = "EGLL", route = "R", callsignSetByUser = true };
            Sim.FlightPlan copy = plan.Clone();
            plan.route = "changed";

            Assert.NotSame(plan, copy);
            Assert.Equal("R", copy.route);
            Assert.Equal("EGLL", copy.destination);
            Assert.True(copy.callsignSetByUser);
        }
    }
}
