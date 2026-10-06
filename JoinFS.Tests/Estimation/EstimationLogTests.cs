using System.Globalization;
using System.Text;
using JoinFS.Estimation;
using JoinFS.Net;

namespace JoinFS.Tests.Estimation
{
    public class EstimationLogTests
    {
        static readonly DateTime Utc = new(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc);

        static readonly int Columns = EstimationLog.Header.Split(',').Length;

        static Sim.Plane Plane(string callsign = "JFS123")
        {
            var plane = new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 7);
            plane.flightPlan.callsign = callsign;
            plane.netPosition = new Sim.Pos(new Vector(0.1, 1500.0, 0.8), new Vector(0.05, 1.2, -0.3), 0.0, 0);
            plane.netVelocity = new Sim.Vel(new Vector(100.0, -2.0, 50.0), new Vector(0.01, 0.02, 0.03), new Vector(0.5, 0.0, -0.5));
            return plane;
        }

        static string[] Rows(StringWriter writer) => writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        static Dictionary<string, string> Fields(string row) =>
            EstimationLog.Header.Split(',').Zip(row.Split(',')).ToDictionary(pair => pair.First, pair => pair.Second);

        [Fact]
        public void StartsWithTheHeader()
        {
            var writer = new StringWriter();
            _ = new EstimationLog(writer, () => Utc);
            Assert.Equal([EstimationLog.Header], Rows(writer));
        }

        [Fact]
        public void SampleRows_HaveEveryColumn_WithOrWithoutPredictionAndSimPosition()
        {
            var writer = new StringWriter();
            var log = new EstimationLog(writer, () => Utc);
            var plane = Plane();

            // no prediction yet, no sim position
            log.OnSample(plane, 100.0, 50.0, 99.99, 0.08);
            // with both
            plane.netStateTime = 50.0;
            log.OnPrediction(plane, 100.02, 0.1, new KinematicState(plane.netPosition.Extrapolate(plane.netVelocity, 0.1), plane.netVelocity));
            plane.simPosition = new Sim.Pos(new Vector(0.1000001, 1499.0, 0.8000001), new Vector(0.0, 1.0, 0.0), 0.0, 0);
            plane.simTime = 100.01;
            log.OnSample(plane, 100.05, 50.05, 100.04, double.NaN);

            string[] rows = Rows(writer);
            Assert.Equal(3, rows.Length);
            Assert.All(rows, row => Assert.Equal(Columns, row.Split(',').Length));

            var first = Fields(rows[1]);
            Assert.Equal("sample", first["kind"]);
            Assert.Equal("", first["predLocal"]);
            Assert.Equal("", first["simTime"]);
            Assert.Equal("0.08", first["rtt"]);

            var second = Fields(rows[2]);
            Assert.Equal("100.020000", second["predLocal"]);
            Assert.Equal("50.000000", second["predFrom"]);
            Assert.Equal("0.100000", second["predAge"]);
            Assert.Equal("100.010000", second["simTime"]);
            // not a network object: no round trip
            Assert.Equal("", second["rtt"]);
        }

        [Fact]
        public void SampleRows_SayWhichSteeringWasInForce_AndTheSimulatorsClockForTheDrawnObject()
        {
            var writer = new StringWriter();
            var log = new EstimationLog(writer, () => Utc);
            var plane = Plane();

            plane.netStateTime = 50.0;
            log.OnPrediction(plane, 100.02, 0.1, new KinematicState(plane.netPosition, plane.netVelocity), "Gain4");
            plane.netPosition.elevation = 1480.25;
            plane.simPosition = new Sim.Pos(new Vector(0.1000001, 1499.0, 0.8000001), new Vector(0.0, 1.0, 0.0), 1612.5, 1);
            plane.simTime = 100.01;
            plane.simulationTime = 4321.25;
            log.OnSample(plane, 100.05, 50.05, 100.04, 0.08);
            // no simulator clock on this one, and no steering named
            log.OnPrediction(plane, 100.07, 0.1, new KinematicState(plane.netPosition, plane.netVelocity));
            plane.simulationTime = double.NaN;
            log.OnSample(plane, 100.10, 50.10, 100.09, 0.08);

            string[] rows = Rows(writer);
            Assert.All(rows, row => Assert.Equal(Columns, row.Split(',').Length));
            var first = Fields(rows[1]);
            Assert.Equal("4321.250000", first["simClock"]);
            Assert.Equal("Gain4", first["steer"]);
            // the terrain: the sender's ground altitude, the local simulator's, and whether it has the object on the ground
            Assert.Equal("1480.25", first["elev"]);
            Assert.Equal("1612.50", first["simElev"]);
            Assert.Equal("", first["simAgl"]);
            Assert.Equal("1", first["simGround"]);
            var second = Fields(rows[2]);
            Assert.Equal("", second["simClock"]);
            Assert.Equal("", second["steer"]);
            // send rows have the same width, and no steering
            log.OnSend(plane, 100.2, 4321.5, 100.19);
            string send = Rows(writer)[3];
            Assert.Equal(Columns, send.Split(',').Length);
            Assert.Equal("", Fields(send)["steer"]);
            Assert.Equal("4321.500000", Fields(send)["simClock"]);
        }

        [Fact]
        public void SampleRows_CarryTheSampleExactlyEnough()
        {
            var writer = new StringWriter();
            var log = new EstimationLog(writer, () => Utc);
            var plane = Plane();
            log.OnSample(plane, 100.0, 50.0, 99.99, 0.08);

            var f = Fields(Rows(writer)[1]);
            double Get(string name) => double.Parse(f[name], CultureInfo.InvariantCulture);
            Assert.Equal("Network", f["owner"]);
            Assert.Equal("7", f["netId"]);
            Assert.Equal("JFS123", f["callsign"]);
            Assert.Equal(50.0, Get("netTime"));
            Assert.Equal(99.99, Get("receivedAt"));
            // to well under a millimetre
            Assert.Equal(0.8, Get("lat"), 1e-11);
            Assert.Equal(0.1, Get("lon"), 1e-11);
            Assert.Equal(1500.0, Get("alt"), 1e-6);
            Assert.Equal(0.05, Get("pitch"), 1e-9);
            Assert.Equal(-0.3, Get("bank"), 1e-9);
            Assert.Equal(1.2, Get("heading"), 1e-9);
            Assert.Equal(100.0, Get("vx"));
            Assert.Equal(0.03, Get("avz"), 1e-7);
            Assert.Equal(-0.5, Get("az"));
            Assert.Equal((Utc - DateTime.UnixEpoch).TotalSeconds, Get("utc"));
        }

        [Fact]
        public void SendRows_HaveEveryColumn_AndBothClocks()
        {
            var writer = new StringWriter();
            var log = new EstimationLog(writer, () => Utc);
            var plane = Plane();

            log.OnSend(plane, 100.016, 4321.5, 100.004);
            log.OnSend(plane, 100.066, double.NaN, 100.066);

            string[] rows = Rows(writer);
            Assert.Equal(3, rows.Length);
            Assert.All(rows, row => Assert.Equal(Columns, row.Split(',').Length));
            var f = Fields(rows[1]);
            Assert.Equal("send", f["kind"]);
            Assert.Equal("100.016000", f["local"]);
            Assert.Equal("100.004000", f["netTime"]);
            Assert.Equal("4321.500000", f["simClock"]);
            Assert.Equal("JFS123", f["callsign"]);
            Assert.Equal("", f["lat"]);
            // no simulator clock
            Assert.Equal("", Fields(rows[2])["simClock"]);
        }

        [Fact]
        public void Callsigns_CannotBreakTheColumns()
        {
            var writer = new StringWriter();
            var log = new EstimationLog(writer, () => Utc);
            log.OnSample(Plane("A,B\"C\nD"), 100.0, 50.0, 99.99, 0.08);
            Assert.Equal(Columns, Rows(writer)[1].Split(',').Length);
        }

        [Fact]
        public void Tick_WritesAClockRowOncePerInterval()
        {
            var writer = new StringWriter();
            var log = new EstimationLog(writer, () => Utc);
            log.Tick(10.0);
            log.Tick(10.5);
            log.Tick(10.99);
            log.Tick(11.0);
            string[] clocks = Rows(writer).Where(row => row.StartsWith("clock,")).ToArray();
            Assert.Equal(["clock," + (Utc - DateTime.UnixEpoch).TotalSeconds.ToString("F6", CultureInfo.InvariantCulture) + ",10.000000", "clock," + (Utc - DateTime.UnixEpoch).TotalSeconds.ToString("F6", CultureInfo.InvariantCulture) + ",11.000000"], clocks);
        }

        /// <summary>A disk that fills up after the header</summary>
        sealed class FailingWriter : StringWriter
        {
            public bool Fail;
            public override void WriteLine(StringBuilder value) { if (Fail) throw new IOException("disk full"); base.WriteLine(value); }
            public override void WriteLine(string value) { if (Fail) throw new IOException("disk full"); base.WriteLine(value); }
        }

        [Fact]
        public void AFailedWrite_StopsTheLogWithoutThrowing()
        {
            var writer = new FailingWriter();
            var log = new EstimationLog(writer, () => Utc);
            writer.Fail = true;
            log.OnSample(Plane(), 100.0, 50.0, 99.99, 0.08);
            Assert.Equal("disk full", log.Error);

            // nothing more is attempted
            writer.Fail = false;
            log.OnSample(Plane(), 100.1, 50.1, 100.09, 0.08);
            log.Tick(200.0);
            Assert.Equal([EstimationLog.Header], Rows(writer));
        }

        [Fact]
        public void Create_MakesAFilePerPortAndSession()
        {
            string folder = Path.Combine(Path.GetTempPath(), "joinfs-estimationlog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path;
                using (EstimationLog.Create(folder, 6112, out path))
                {
                }
                Assert.StartsWith(Path.Combine(folder, "estimation-6112-"), path);
                Assert.EndsWith(".csv", path);
                Assert.Equal(EstimationLog.Header, File.ReadAllText(path).TrimEnd());
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
