using System.Text;
using JoinFS.Estimation;
using JoinFS.Net;
using Xunit.Abstractions;

namespace JoinFS.Tests.Estimation.Replay
{
    /// <summary>
    /// The replay harness on made-up flights with known answers, and - when
    /// JOINFS_ESTIMATION_LOGS names field logs - on real ones (<see cref="ReplayFieldLogs"/>).
    /// </summary>
    public class ReplayHarnessTests(ITestOutputHelper output)
    {
        const double R = 6371009.0;
        static readonly string[] Both = [EstimationRegistry.ClassicName, ClassicFixedEstimator.Name];

        /// <summary>
        /// A level coordinated turn at 150 m/s and <paramref name="bankDegrees"/> of bank, sampled
        /// at 20 Hz, exactly. Each sample carries the Classic prediction a receiver would have made
        /// from the one before, 60 ms on.
        /// </summary>
        static LoggedObject CoordinatedTurn(double bankDegrees, double seconds = 60.0)
        {
            double v = 150.0, bank = bankDegrees * Math.PI / 180.0;
            double rate = 9.80665 * Math.Tan(bank) / v;
            double lat0 = 0.8, lon0 = 0.1, heading0 = 0.3;
            var obj = new LoggedObject("turn");
            LoggedSample? previous = null;
            for (int i = 0; i * 0.05 <= seconds; i++)
            {
                double t = i * 0.05, heading = heading0 + rate * t;
                double east = rate == 0.0 ? v * t * Math.Sin(heading0) : -(v / rate) * (Math.Cos(heading) - Math.Cos(heading0));
                double north = rate == 0.0 ? v * t * Math.Cos(heading0) : (v / rate) * (Math.Sin(heading) - Math.Sin(heading0));
                var s = new LoggedSample
                {
                    NetTime = 100.0 + t, ReceivedAt = 500.0 + t,
                    Lat = lat0 + north / R, Lon = lon0 + east / (R * Math.Cos(lat0)), Alt = 3000.0,
                    Pitch = 0.0, Bank = bank, Heading = heading % (2.0 * Math.PI),
                    Vx = v * Math.Sin(heading), Vz = v * Math.Cos(heading),
                    Avx = rate * Math.Sin(bank), Avy = rate * Math.Cos(bank),
                    Ax = v * rate * Math.Cos(heading), Az = -v * rate * Math.Sin(heading),
                };
                if (previous != null)
                {
                    Sim.Pos p = new ClassicEstimator().Predict(previous.State(), 0.06).Position;
                    s.PredLocal = s.ReceivedAt; s.PredFrom = previous.NetTime; s.PredAge = 0.06;
                    s.PredLat = p.geo.z; s.PredLon = p.geo.x; s.PredAlt = p.geo.y;
                }
                obj.Samples.Add(s);
                previous = s;
            }
            return obj;
        }

        [Fact]
        public void Distribution_GivesPercentilesToAboutTwoPercent()
        {
            var d = new Distribution();
            for (int i = 1; i <= 1000; i++)
            {
                d.Add(i % 2 == 0 ? i * 0.001 : -i * 0.001);
            }
            Assert.Equal(1000, d.Count);
            Assert.InRange(d.Percentile(0.5), 0.5, 0.5 * 1.024);
            Assert.InRange(d.Percentile(0.95), 0.95, 0.95 * 1.024);
            Assert.Equal(1.0, d.Percentile(1.0), 1e-12);
            Assert.True(double.IsNaN(new Distribution().Percentile(0.5)));
        }

        [Fact]
        public void Reader_ReadsWhatTheLogWrites()
        {
            var writer = new StringWriter();
            var log = new EstimationLog(writer, () => new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc));
            var plane = new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 7);
            plane.flightPlan.callsign = "JFS123";
            plane.netPosition = new Sim.Pos(new Vector(0.1, 1500.0, 0.8), new Vector(0.05, 1.2, -0.3), 0.0, 0);
            plane.netVelocity = new Sim.Vel(new Vector(100.0, -2.0, 50.0), new Vector(0.01, 0.02, 0.03), new Vector(0.5, 0.25, -0.5));
            log.OnSample(plane, 100.0, 50.0, 99.99, 0.08);
            plane.netStateTime = 50.0;
            log.OnPrediction(plane, 100.02, 0.07, new KinematicState(plane.netPosition.Extrapolate(plane.netVelocity, 0.07), plane.netVelocity));
            log.OnSend(plane, 100.03, 12.5, 100.02);
            log.OnSample(plane, 100.05, 50.05, 100.04, 0.08);

            List<LoggedObject> objects = EstimationLogReader.Read(new StringReader(writer.ToString()));
            LoggedObject obj = Assert.Single(objects);
            Assert.StartsWith("JFS123", obj.Name);
            Assert.Equal(2, obj.Samples.Count);
            LoggedSample first = obj.Samples[0], second = obj.Samples[1];
            Assert.Equal(50.0, first.NetTime);
            Assert.True(double.IsNaN(first.PredLocal));
            Assert.Equal(0.8, first.Lat, 1e-11);
            Assert.Equal(1.2, first.Heading, 1e-9);
            Assert.Equal(-0.3, first.Bank, 1e-9);
            Assert.Equal(-2.0, first.Vy);
            Assert.Equal(0.03, first.Avz, 1e-7);
            Assert.Equal(0.25, first.Ay);
            Assert.Equal(50.0, second.PredFrom);
            Assert.Equal(0.07, second.PredAge);
            Assert.Equal(plane.netPosition.Extrapolate(plane.netVelocity, 0.07).geo.z, second.PredLat, 1e-11);
        }

        [Fact]
        public void CoordinatedTurn_ClassicFixedFollowsTheHeading_AndClassicIsWhatRan()
        {
            var scorer = new ReplayScorer(Both);
            scorer.Add(CoordinatedTurn(60.0));
            output.WriteLine(scorer.Report("Coordinated turn, 60° bank"));

            // the logged predictions were Classic's
            Assert.InRange(scorer.Reproduction(EstimationRegistry.ClassicName).Percentile(0.99), 0.0, 1e-5);
            Assert.InRange(scorer.Reproduction(ClassicFixedEstimator.Name).Percentile(0.5), 1e-3, double.MaxValue);

            foreach (string horizon in new[] { ReplayScorer.Logged, ReplayScorer.HorizonLabel(0.1), ReplayScorer.HorizonLabel(0.2) })
            {
                var classic = scorer[EstimationRegistry.ClassicName, horizon, "turning"];
                var fixedErrors = scorer[ClassicFixedEstimator.Name, horizon, "turning"];
                Assert.True(classic.Heading.Count > 1000);
                // Classic turns the heading at only cos(60°) = half the rate
                Assert.True(classic.Heading.Percentile(0.95) > 0.1);
                Assert.InRange(fixedErrors.Heading.Percentile(0.95), 0.0, 0.002);
                Assert.InRange(fixedErrors.Bank.Percentile(0.95), 0.0, 0.002);
                // and half the acceleration term is the right one
                Assert.True(fixedErrors.Horizontal.Percentile(0.95) < classic.Horizontal.Percentile(0.95));
            }
        }

        [Fact]
        public void StraightFlight_BothAgree()
        {
            var scorer = new ReplayScorer(Both);
            scorer.Add(CoordinatedTurn(0.0));
            foreach (string estimator in Both)
            {
                var e = scorer[estimator, ReplayScorer.HorizonLabel(0.2), "straight"];
                Assert.True(e.Along.Count > 1000);
                // a centimetre or two: the made-up flight and Sim.Pos.GeoPerMetre turn metres into degrees slightly differently
                Assert.InRange(e.Horizontal.Percentile(0.99), 0.0, 0.05);
                Assert.InRange(e.Heading.Percentile(0.99), 0.0, 1e-4);
            }
        }

        /// <summary>
        /// Replays field logs through every registered estimator and writes the scores. Runs only
        /// when JOINFS_ESTIMATION_LOGS names logs: estimation-*.csv files, folders holding them, or
        /// the tester package's zips, separated by ';'. The report goes to JOINFS_REPLAY_REPORT
        /// (default: estimation-replay.md in the temp folder). For example:
        ///   $env:JOINFS_ESTIMATION_LOGS = "D:\logs"; dotnet test JoinFS.Tests/JoinFS.Tests.csproj -c FS2024-Debug -p:Platform=x64 --filter "FullyQualifiedName~ReplayFieldLogs"
        /// </summary>
        [Fact]
        public void ReplayFieldLogs()
        {
            string? paths = Environment.GetEnvironmentVariable("JOINFS_ESTIMATION_LOGS");
            if (string.IsNullOrWhiteSpace(paths))
            {
                return;
            }
            string reportPath = Environment.GetEnvironmentVariable("JOINFS_REPLAY_REPORT") ?? Path.Combine(Path.GetTempPath(), "estimation-replay.md");
            string[] estimators = [.. EstimationRegistry.EstimatorNames];

            var report = new StringBuilder("# Estimation replay\n\nEach receiver's logged samples, replayed through " + string.Join(", ", estimators) +
                ". Errors are against the sender's own samples at the predicted moment; the horizon is given, so this scores the estimator alone." +
                " Percentiles are of |error|, to about 2%.\n\n");
            int logs = 0;
            foreach (var (name, open) in EstimationLogReader.Find(paths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                List<LoggedObject> objects;
                using (TextReader reader = open())
                {
                    objects = EstimationLogReader.Read(reader);
                }
                var scorer = new ReplayScorer(estimators);
                foreach (LoggedObject obj in objects.Where(o => o.Samples.Count >= 1000))
                {
                    scorer.Add(obj);
                }
                report.Append(scorer.Report(name)).Append('\n');
                logs++;
            }
            File.WriteAllText(reportPath, report.ToString());
            output.WriteLine($"{logs} logs replayed, report: {reportPath}");
            Assert.True(logs > 0, "no estimation logs found in " + paths);
        }
    }
}
