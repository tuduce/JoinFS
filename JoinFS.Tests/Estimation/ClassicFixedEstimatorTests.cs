using JoinFS.Estimation;

namespace JoinFS.Tests.Estimation
{
    public class ClassicFixedEstimatorTests
    {
        static Sim.Pos Position(Vector angles) => new(new Vector(0.1, 1500.0, 0.8), angles, 12.0, 0, 30.0);

        [Fact]
        public void WithoutRotationOrAcceleration_IsClassic()
        {
            var sample = new KinematicState(Position(new Vector(0.05, 1.2, -0.3)), new Sim.Vel(new Vector(100.0, -2.0, 50.0), new Vector(), new Vector()));
            foreach (double age in new[] { -0.5, 0.0, 0.06, 0.3, 5.0 })
            {
                KinematicState classic = new ClassicEstimator().Predict(sample, age);
                KinematicState fixedState = new ClassicFixedEstimator().Predict(sample, age);
                Assert.Equal(classic.Position.geo.x, fixedState.Position.geo.x, 1e-15);
                Assert.Equal(classic.Position.geo.y, fixedState.Position.geo.y, 1e-9);
                Assert.Equal(classic.Position.geo.z, fixedState.Position.geo.z, 1e-15);
                Assert.Equal(classic.Position.angles.x, fixedState.Position.angles.x, 1e-12);
                Assert.Equal(classic.Position.angles.y, fixedState.Position.angles.y, 1e-12);
                Assert.Equal(classic.Position.angles.z, fixedState.Position.angles.z, 1e-12);
                Assert.Equal(classic.Velocity.linear.x, fixedState.Velocity.linear.x);
            }
        }

        [Fact]
        public void Acceleration_MovesHalfAtSquared()
        {
            var sample = new KinematicState(Position(new Vector()), new Sim.Vel(new Vector(), new Vector(), new Vector(0.0, 4.0, 0.0)));
            KinematicState state = new ClassicFixedEstimator().Predict(sample, 0.5);
            Assert.Equal(1500.0 + 0.5 * 4.0 * 0.25, state.Position.geo.y, 1e-9);
            // the velocity grows by a t
            Assert.Equal(2.0, state.Velocity.linear.y, 1e-12);
        }

        [Fact]
        public void BankedTurn_HeadingFollowsTheTurn()
        {
            double bank = Math.PI / 3, turnRate = 0.05;
            var sample = new KinematicState(Position(new Vector(0.0, 1.0, bank)),
                new Sim.Vel(new Vector(100.0, 0.0, 0.0), new Vector(turnRate * Math.Sin(bank), turnRate * Math.Cos(bank), 0.0), new Vector()));
            KinematicState state = new ClassicFixedEstimator().Predict(sample, 0.2);
            Assert.Equal(1.0 + turnRate * 0.2, state.Position.angles.y, 1e-9);
            Assert.Equal(bank, state.Position.angles.z, 1e-9);
            Assert.Equal(0.0, state.Position.angles.x, 1e-9);
        }

        [Fact]
        public void KeepsTheSamplesGroundData_AndTheTwoSecondLimit()
        {
            var sample = new KinematicState(Position(new Vector()), new Sim.Vel(new Vector(10.0, 0.0, 0.0), new Vector(), new Vector()));
            KinematicState far = new ClassicFixedEstimator().Predict(sample, 10.0);
            KinematicState limit = new ClassicFixedEstimator().Predict(sample, ClassicEstimator.MaxAge);
            Assert.Equal(limit.Position.geo.x, far.Position.geo.x);
            Assert.Equal(12.0, far.Position.elevation);
            Assert.Equal(30.0, far.Position.radarHeight);
        }

        [Fact]
        public void Registry_KnowsIt_AndRefusesUnknownNames()
        {
            Assert.IsType<ClassicFixedEstimator>(EstimationRegistry.CreateEstimator(ClassicFixedEstimator.Name));
            Assert.Contains(ClassicFixedEstimator.Name, EstimationRegistry.EstimatorNames);
            string selected = EstimationRegistry.SelectedEstimator;
            Assert.False(EstimationRegistry.SelectEstimator("NoSuchEstimator"));
            Assert.False(EstimationRegistry.SelectEstimator(null));
            Assert.Equal(selected, EstimationRegistry.SelectedEstimator);
        }
    }
}
