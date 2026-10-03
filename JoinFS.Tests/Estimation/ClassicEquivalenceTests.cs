using JoinFS.Estimation;

namespace JoinFS.Tests.Estimation
{
    /// <summary>
    /// The Classic clock, estimator and steering must behave exactly like the code they were moved
    /// out of (Sim.Steering.cs and Sim.UpdateObject before the Estimation/ refactor). The Original*
    /// classes below are verbatim copies of that code, with the SimConnect calls recorded instead
    /// of made. They are the frozen reference: do not change them to make a test pass.
    /// </summary>
    public class ClassicEquivalenceTests
    {
        // ---- reference: the code as it was ----

        /// <summary>Sim.UpdateObject(obj, netTime, receivedAt) and the delay part of UpdateSimObjectVelocity</summary>
        sealed class OriginalClock
        {
            public double netStateTime, netRealTime, netSimTime;
            public float prevDelay;

            public bool Started => netRealTime != 0.0;

            public void Update(double netTime, double receivedAt, double now)
            {
                double localTime = receivedAt > 0.0 ? Math.Max(receivedAt, netSimTime) : now;
                netStateTime = netTime;
                if (netRealTime == 0.0)
                {
                    netRealTime = netStateTime;
                }
                else
                {
                    netRealTime += localTime - netSimTime;
                    double error = netStateTime - netRealTime;
                    netRealTime += error * 0.02;
                }
                netSimTime = localTime;
            }

            public double NetDeltaTime(double now, bool network, float rtt)
            {
                float delay = 0.0f;
                if (network)
                {
                    float prev = prevDelay;
                    delay = rtt;
                    float alpha = 0.75f;
                    delay = alpha * delay + (1.0f - alpha) * prev;
                    prevDelay = delay;
                }
                return netRealTime - netStateTime + now - netSimTime + 0.52 * delay;
            }

            public void Reset()
            {
                netStateTime = 0.0;
                netRealTime = 0.0;
                netSimTime = 0.0;
            }

            public void CopyTimesFrom(OriginalClock other)
            {
                netRealTime = other.netRealTime;
                netStateTime = other.netStateTime;
                netSimTime = other.netSimTime;
            }
        }

        /// <summary>One SimConnect call, with the values it was given at the time</summary>
        record Call(string Kind, double[] Values)
        {
            public static Call Position(Sim.Pos p) => new("position", [p.geo.x, p.geo.y, p.geo.z, p.angles.x, p.angles.y, p.angles.z]);
            public static Call Velocity(Sim.ObjectVelocity v) => new("velocity", [v.velocityX, v.velocityY, v.velocityZ, v.angularVelocityX, v.angularVelocityY, v.angularVelocityZ, v.accelerationX, v.accelerationY, v.accelerationZ]);
            public static Call Euler(Vector a) => new("euler", [a.x, a.y, a.z]);
        }

        /// <summary>UpdateSimObjectVelocity's body, with msfs standing for FS2020 || FS2024</summary>
        static List<Call> OriginalFrame(Sim.Pos objSimPosition, Sim.Pos objNetPosition, Sim.Vel objNetVelocity, bool paused, double simDeltaTime, double netDeltaTime, bool msfs)
        {
            var calls = new List<Call>();
            if (paused)
            {
                calls.Add(Call.Position(objNetPosition));
                calls.Add(Call.Velocity(new Sim.ObjectVelocity()));
                if (msfs) calls.Add(Call.Euler(objNetPosition.angles));
                return calls;
            }

            simDeltaTime = Math.Min(2.0, Math.Max(-2.0, simDeltaTime));
            netDeltaTime = Math.Min(2.0, Math.Max(-2.0, netDeltaTime));
            Sim.Pos simPosition = objSimPosition.Extrapolate(objNetVelocity, simDeltaTime);
            Sim.Pos netPosition = objNetPosition.Extrapolate(objNetVelocity, netDeltaTime);
            Sim.Vel netVelocity = objNetVelocity.Extrapolate(netDeltaTime);

            double distance = Vector.GeodesicDistance(simPosition.geo.x, simPosition.geo.z, netPosition.geo.x, netPosition.geo.z);
            double bearing = Vector.GeodesicBearing(simPosition.geo.x, simPosition.geo.z, netPosition.geo.x, netPosition.geo.z);

            double altitudeDeltaLimit = 50.0;
            if (msfs && simPosition.ground != 0) altitudeDeltaLimit = 0.2;

            if (distance > 50.0 || Math.Abs(simPosition.geo.y - netPosition.geo.y) > altitudeDeltaLimit)
            {
                calls.Add(Call.Position(netPosition));
                calls.Add(Call.Velocity(new Sim.ObjectVelocity(netVelocity.linear, netVelocity.angular, netVelocity.acc)));
                if (msfs) calls.Add(Call.Euler(netPosition.angles));
            }
            else
            {
                Vector deltaGeo = new(distance * Math.Sin(bearing), netPosition.geo.y - simPosition.geo.y, distance * Math.Cos(bearing));
                Vector deltaAngles = Vector.AnglesDelta(simPosition.angles, netPosition.angles);

                netVelocity.linear += deltaGeo * 1.5;

                if (Math.Abs(simPosition.angles.x) < Math.PI * 0.25 && Math.Abs(simPosition.angles.z) < Math.PI * 0.5)
                {
                    if (Math.Abs(netVelocity.angular.x) < 0.2 && Math.Abs(netVelocity.angular.y) < 0.2 && Math.Abs(netVelocity.angular.z) < 0.2)
                    {
                        netVelocity.angular += deltaAngles * 1.5;
                    }
                    if (msfs) calls.Add(Call.Euler(netPosition.angles));
                }
                else
                {
                    calls.Add(Call.Euler(netPosition.angles));
                }

                calls.Add(Call.Velocity(new Sim.ObjectVelocity(netVelocity.linear.InvRotate(simPosition.angles), netVelocity.angular * 0.3, netVelocity.acc.InvRotate(simPosition.angles))));
            }
            return calls;
        }

        // ---- the new parts, turned into the same calls ----

        /// <summary>The calls Sim.ApplySteering makes for a command, in the order SteeringCommand documents</summary>
        static List<Call> Calls(in SteeringCommand command)
        {
            var calls = new List<Call>();
            if (command.ResetTo != null)
            {
                calls.Add(Call.Position(command.ResetTo));
                calls.Add(Call.Velocity(command.Velocity));
                if (command.Attitude != null) calls.Add(Call.Euler(command.Attitude));
            }
            else
            {
                if (command.Attitude != null) calls.Add(Call.Euler(command.Attitude));
                calls.Add(Call.Velocity(command.Velocity));
            }
            return calls;
        }

        static ClassicSteering Steering(bool msfs) => msfs ? new ClassicSteering(true, 0.2) : new ClassicSteering(false, ClassicSteering.ResetDistance);

        static void AssertSameCalls(List<Call> expected, List<Call> actual, string context)
        {
            Assert.True(expected.Count == actual.Count, $"{context}: {expected.Count} calls expected, {actual.Count} made");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.True(expected[i].Kind == actual[i].Kind, $"{context}: call {i} is {actual[i].Kind}, expected {expected[i].Kind}");
                Assert.True(expected[i].Values.SequenceEqual(actual[i].Values), $"{context}: call {i} ({expected[i].Kind}) values differ: [{string.Join(", ", expected[i].Values)}] vs [{string.Join(", ", actual[i].Values)}]");
            }
        }

        // ---- random states that reach every branch ----

        const double MetresPerRadian = 6371009.0;

        static double Range(Random random, double min, double max) => min + random.NextDouble() * (max - min);

        static Vector RandomVector(Random random, double size) => new(Range(random, -size, size), Range(random, -size, size), Range(random, -size, size));

        /// <summary>A sample, and the object's sim position somewhere near it (or far, for a reset)</summary>
        static (Sim.Pos net, Sim.Vel velocity, Sim.Pos sim) RandomState(Random random)
        {
            double lat = Range(random, -1.2, 1.2);
            double lon = Range(random, -3.0, 3.0);
            double alt = Range(random, 0.0, 10000.0);
            // gentle or aerobatic attitude
            double attitudeSize = random.Next(3) == 0 ? Math.PI : 0.3;
            Sim.Pos net = new(new Vector(lon, alt, lat), RandomVector(random, attitudeSize), 0.0, random.Next(4) == 0 ? 1 : 0);
            // slow or fast rotation
            Sim.Vel velocity = new(RandomVector(random, 150.0), RandomVector(random, random.Next(2) == 0 ? 0.15 : 1.0), RandomVector(random, 20.0));

            // near (track) or far (reset); small or large altitude error (ground reset)
            double offset = random.Next(4) == 0 ? 120.0 : 20.0;
            double altitudeError = random.Next(2) == 0 ? 0.5 : 80.0;
            Sim.Pos sim = new(
                new Vector(lon + Range(random, -offset, offset) / MetresPerRadian, alt + Range(random, -altitudeError, altitudeError), lat + Range(random, -offset, offset) / MetresPerRadian),
                RandomVector(random, attitudeSize), 0.0, random.Next(3) == 0 ? 1 : 0);
            return (net, velocity, sim);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Steering_MatchesTheOriginalFrame(bool msfs)
        {
            var random = new Random(msfs ? 2024 : 2004);
            var estimator = new ClassicEstimator();
            var steering = Steering(msfs);
            // how often each branch was reached, so the comparison is known to cover them all
            int holds = 0, resets = 0, tracks = 0, tracksWithAttitude = 0;
            for (int i = 0; i < 20000; i++)
            {
                var (net, velocity, sim) = RandomState(random);
                // ages beyond the two-second limit too
                double simDeltaTime = Range(random, -0.5, 3.0);
                double netDeltaTime = Range(random, -0.5, 3.0);
                bool paused = random.Next(20) == 0;

                List<Call> expected = OriginalFrame(sim.CloneAll(), net.CloneAll(), velocity.Clone(), paused, simDeltaTime, netDeltaTime, msfs);

                KinematicState sample = new(net, velocity);
                SteeringCommand command = paused
                    ? steering.Hold(sample)
                    : steering.Steer(estimator.Predict(sample, netDeltaTime), sample, sim, simDeltaTime);

                AssertSameCalls(expected, Calls(command), $"state {i}");

                if (paused) holds++;
                else if (expected[0].Kind == "position") resets++;
                else if (expected[0].Kind == "euler") tracksWithAttitude++;
                else tracks++;
            }

            Assert.True(holds > 100 && resets > 1000, $"holds {holds}, resets {resets}");
            // MSFS sets the attitude on every tracking frame; the others only in aerobatic attitudes
            if (msfs) Assert.True(tracks == 0 && tracksWithAttitude > 1000, $"tracks {tracks}, with attitude {tracksWithAttitude}");
            else Assert.True(tracks > 1000 && tracksWithAttitude > 1000, $"tracks {tracks}, with attitude {tracksWithAttitude}");
        }

        [Fact]
        public void Steering_LeavesTheSampleAlone()
        {
            var random = new Random(7);
            var estimator = new ClassicEstimator();
            var steering = Steering(true);
            for (int i = 0; i < 2000; i++)
            {
                var (net, velocity, sim) = RandomState(random);
                Sim.Pos netBefore = net.CloneAll();
                Sim.Vel velocityBefore = velocity.Clone();
                Sim.Pos simBefore = sim.CloneAll();

                KinematicState sample = new(net, velocity);
                steering.Steer(estimator.Predict(sample, 0.1), sample, sim, 0.02);

                Assert.Equal([netBefore.geo.x, netBefore.geo.y, netBefore.geo.z, netBefore.angles.x, netBefore.angles.y, netBefore.angles.z],
                    new[] { net.geo.x, net.geo.y, net.geo.z, net.angles.x, net.angles.y, net.angles.z });
                Assert.Equal([velocityBefore.linear.x, velocityBefore.linear.y, velocityBefore.linear.z, velocityBefore.angular.x, velocityBefore.angular.y, velocityBefore.angular.z, velocityBefore.acc.x, velocityBefore.acc.y, velocityBefore.acc.z],
                    new[] { velocity.linear.x, velocity.linear.y, velocity.linear.z, velocity.angular.x, velocity.angular.y, velocity.angular.z, velocity.acc.x, velocity.acc.y, velocity.acc.z });
                Assert.Equal([simBefore.geo.x, simBefore.geo.y, simBefore.geo.z, simBefore.angles.x, simBefore.angles.y, simBefore.angles.z],
                    new[] { sim.geo.x, sim.geo.y, sim.geo.z, sim.angles.x, sim.angles.y, sim.angles.z });
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Clock_MatchesTheOriginalTimeKeeping(int seed)
        {
            var random = new Random(seed);
            // two objects, so entering a cockpit (copying times) can be exercised
            var original = new[] { new OriginalClock(), new OriginalClock() };
            var clocks = new[] { new RttHalfClock(), new RttHalfClock() };
            double now = 100.0;
            // the first sample can be at sender time 0 (a recording's start): not yet "started" after it
            double[] netTime = [seed == 1 ? 0.0 : 5000.0, 7000.0];
            float rtt = 0.08f;

            for (int step = 0; step < 50000; step++)
            {
                now += Range(random, 0.001, 0.02);
                int n = random.Next(2);
                int roll = random.Next(100);
                if (roll < 30)
                {
                    // a sample: from the network (sometimes out of arrival order), or local (receivedAt 0)
                    double receivedAt = random.Next(10) == 0 ? 0.0 : now - Range(random, 0.0, 0.03);
                    Assert.Equal(original[n].Started, clocks[n].Started);
                    original[n].Update(netTime[n], receivedAt, now);
                    clocks[n].OnSample(netTime[n], receivedAt, now);
                    netTime[n] += Range(random, 0.03, 0.07);
                }
                else if (roll < 95)
                {
                    // a frame
                    bool network = random.Next(5) != 0;
                    if (random.Next(50) == 0) rtt = (float)Range(random, 0.0, 0.4);
                    double expected = original[n].NetDeltaTime(now, network, rtt);
                    double actual = clocks[n].SampleAge(now, new PeerTiming(network, rtt));
                    Assert.Equal(expected, actual);
                }
                else if (roll < 98)
                {
                    original[n].Reset();
                    clocks[n].Reset();
                }
                else
                {
                    // enter the other object's cockpit: reset, then take over its times
                    original[n].Reset();
                    clocks[n].Reset();
                    original[n].CopyTimesFrom(original[1 - n]);
                    clocks[n].CopyFrom(clocks[1 - n]);
                }
                Assert.Equal(original[n].Started, clocks[n].Started);
            }
        }
    }
}
