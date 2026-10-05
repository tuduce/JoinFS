using JoinFS.Estimation;
using JoinFS.Net;

namespace JoinFS.Tests.Estimation
{
    public class EstimationWiringTests
    {
        [Fact]
        public void Registry_GivesTheDefaults()
        {
            Assert.IsType<MinOffsetClock>(EstimationRegistry.CreateClock());
            Assert.IsType<RttHalfClock>(EstimationRegistry.CreateClock(EstimationRegistry.RttHalfName));
            Assert.IsType<ClassicFixedEstimator>(EstimationRegistry.CreateEstimator());
            Assert.IsType<ClassicEstimator>(EstimationRegistry.CreateEstimator(EstimationRegistry.ClassicName));
            Assert.Contains(EstimationRegistry.DefaultClock, EstimationRegistry.ClockNames);
            Assert.Contains(EstimationRegistry.DefaultEstimator, EstimationRegistry.EstimatorNames);
        }

        [Fact]
        public void Registry_FallsBackToTheDefaultForUnknownNames()
        {
            // names will come from settings, which may name an estimator this build does not have
            Assert.IsType<MinOffsetClock>(EstimationRegistry.CreateClock("NoSuchClock"));
            Assert.IsType<ClassicFixedEstimator>(EstimationRegistry.CreateEstimator("NoSuchEstimator"));
        }

        [Fact]
        public void EachObject_HasItsOwnClockAndEstimator()
        {
            var a = new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 1);
            var b = new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 2);
            Assert.Same(a.Clock, a.Clock);
            Assert.NotSame(a.Clock, b.Clock);
            Assert.NotSame(a.Estimator, b.Estimator);
        }

        [Fact]
        public void SnapshotCopies_DoNotShareTheSimThreadsClock()
        {
            var plane = new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 1);
            plane.Clock.OnSample(10.0, 0.0, 100.0);
            Sim.Obj view = plane.CloneView();
            Assert.NotSame(plane.Clock, view.Clock);
            Assert.NotSame(plane.Estimator, view.Estimator);
            Assert.True(plane.Clock.Started);
        }
    }
}
