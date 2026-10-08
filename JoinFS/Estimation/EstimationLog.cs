using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace JoinFS.Estimation
{
    /// <summary>
    /// CSV log for measuring how well remote objects are positioned (docs/position-estimation-plan.md
    /// §6). Turned on with the -estimationlog command-line option. Used on the sim thread only.
    ///
    /// Rows, told apart by the first column:
    /// - "sample": a network or playback sample as it was accepted. It carries the sender's time
    ///   and our arrival time; the RTT to the owner; the sample's state; the newest prediction
    ///   made before it arrived, and the steering law that was in force (steer); and where the
    ///   simulator last reported the object, with the simulator's own clock at that report
    ///   (simClock, MSFS) so that the drawn object can be timed without the handling jitter; and
    ///   the terrain: the sender's ground altitude under the sample (elev), and what the local
    ///   simulator reports for the object (simElev, its height above ground simAgl, and whether
    ///   it thinks the object is on the ground, simGround) - for fast dives, where the drawn
    ///   altitude has been seen to freeze.
    /// - "clock": local time against UTC, once a second. Logs from two machines whose clocks are
    ///   synchronised can then be put on one time line, which gives the true network delay.
    /// - "send": one of our own aircraft's samples was sent. local is when its message was
    ///   handled, netTime the time it was sent with, and simClock the simulator's own clock at the
    ///   sample (empty when there is none). A receiver's sample row has the same netTime, so the
    ///   two can be joined to compare ways of stamping.
    ///
    /// Units: times in seconds (local = this process's ElapsedTime, utc = Unix time), latitude
    /// and longitude in radians, altitude in metres, angles in radians, velocities in m/s,
    /// angular velocities in rad/s, accelerations in m/s².
    /// </summary>
    public sealed class EstimationLog : IDisposable
    {
        /// <summary>How often a clock row is written and the file flushed, in seconds</summary>
        public const double TickInterval = 1.0;

        public const string Header =
            "kind,utc,local,owner,node,netId,callsign,netTime,receivedAt,rtt," +
            "lat,lon,alt,pitch,bank,heading,vx,vy,vz,avx,avy,avz,ax,ay,az,ground,paused," +
            "predLocal,predFrom,predAge,predLat,predLon,predAlt,predPitch,predBank,predHeading," +
            "simTime,simLat,simLon,simAlt,simPitch,simBank,simHeading,simClock,steer,elev,simElev,simAgl,simGround";

        /// <summary>Columns in a row</summary>
        static readonly int ColumnCount = Header.Split(',').Length;

        /// <summary>Index of the simClock column, where a send row carries the simulator's clock</summary>
        static readonly int SimClockColumn = Array.IndexOf(Header.Split(','), "simClock");

        /// <summary>The newest prediction for an object, copied (the steering may reuse its objects)</summary>
        sealed class Prediction
        {
            public double local = double.NaN;
            public double from = double.NaN;
            public double age = double.NaN;
            public double lat, lon, alt, pitch, bank, heading;
            public string steer;
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly TextWriter writer;
        readonly Func<DateTime> utcNow;
        /// <summary>Per object, released with it</summary>
        readonly ConditionalWeakTable<Sim.Obj, Prediction> predictions = new();
        readonly StringBuilder line = new(512);
        double nextTick = double.NegativeInfinity;

        /// <summary>
        /// Why writing failed, or null. The log stops after a failure, so it can never get in the
        /// way of positioning.
        /// </summary>
        public string Error { get; private set; }

        public EstimationLog(TextWriter writer, Func<DateTime> utcNow = null)
        {
            this.writer = writer;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            Write(() => writer.WriteLine(Header));
        }

        /// <summary>Write, unless an earlier write failed</summary>
        void Write(Action write)
        {
            if (Error != null)
            {
                return;
            }
            try
            {
                write();
            }
            catch (IOException ex)
            {
                Error = ex.Message;
            }
        }

        /// <summary>
        /// Create a new log file in <paramref name="folder"/>, named after the port and the time, so
        /// that each session and each instance has its own
        /// </summary>
        public static EstimationLog Create(string folder, ushort port, out string path)
        {
            path = Path.Combine(folder, "estimation-" + port + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv) + ".csv");
            return new EstimationLog(new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16));
        }

        /// <summary>Once per sim tick: a clock row and a flush, every <see cref="TickInterval"/></summary>
        public void Tick(double now)
        {
            if (now < nextTick)
            {
                return;
            }
            nextTick = now + TickInterval;
            Write(() =>
            {
                writer.WriteLine("clock," + Utc() + "," + Time(now));
                writer.Flush();
            });
        }

        public void Flush() => Write(writer.Flush);

        public void Dispose() => writer.Dispose();

        /// <summary>
        /// The steering's prediction for <paramref name="obj"/> this frame, made from its newest
        /// sample (netStateTime) <paramref name="age"/> seconds on
        /// </summary>
        public void OnPrediction(Sim.Obj obj, double now, double age, in KinematicState target, string steer = null)
        {
            Prediction prediction = predictions.GetOrCreateValue(obj);
            prediction.local = now;
            prediction.from = obj.netStateTime;
            prediction.age = age;
            prediction.lat = target.Position.geo.z;
            prediction.lon = target.Position.geo.x;
            prediction.alt = target.Position.geo.y;
            prediction.pitch = target.Position.angles.x;
            prediction.bank = target.Position.angles.z;
            prediction.heading = target.Position.angles.y;
            prediction.steer = steer;
        }

        /// <summary>
        /// <paramref name="obj"/> has accepted a sample: its netPosition and netVelocity are that
        /// sample
        /// </summary>
        /// <param name="rtt">Round-trip time to the owner, NaN when it is not a network object</param>
        public void OnSample(Sim.Obj obj, double now, double netTime, double receivedAt, double rtt)
        {
            if (Error != null)
            {
                return;
            }
            Sim.Pos p = obj.netPosition;
            Sim.Vel v = obj.netVelocity;
            line.Clear();
            line.Append("sample,").Append(Utc()).Append(',').Append(Time(now)).Append(',')
                .Append(obj.owner).Append(',').Append(Text(obj.ownerNuid.ToString())).Append(',')
                .Append(obj.netId.ToString(Inv)).Append(',').Append(Text((obj as Sim.Aircraft)?.flightPlan.callsign)).Append(',')
                .Append(Time(netTime)).Append(',').Append(Time(receivedAt)).Append(',').Append(Number(rtt, "G6")).Append(',');
            AppendPosition(p.geo.z, p.geo.x, p.geo.y, p.angles.x, p.angles.z, p.angles.y);
            AppendVector(v.linear);
            AppendVector(v.angular);
            AppendVector(v.acc);
            line.Append(p.ground.ToString(Inv)).Append(',').Append(obj.paused ? '1' : '0').Append(',');

            // the newest prediction made before this sample (empty before the first)
            if (predictions.TryGetValue(obj, out Prediction prediction))
            {
                line.Append(Time(prediction.local)).Append(',').Append(Time(prediction.from)).Append(',').Append(Time(prediction.age)).Append(',');
                AppendPosition(prediction.lat, prediction.lon, prediction.alt, prediction.pitch, prediction.bank, prediction.heading);
            }
            else
            {
                line.Append(",,,,,,,,,");
            }

            // where the simulator last reported the object (empty until it has), and its own clock then
            Sim.Pos s = obj.simPosition;
            bool simulated = obj.SimValid && s != null;
            if (simulated)
            {
                line.Append(Time(obj.simTime)).Append(',');
                AppendPosition(s.geo.z, s.geo.x, s.geo.y, s.angles.x, s.angles.z, s.angles.y);
            }
            else
            {
                line.Append(",,,,,,,");
            }
            line.Append(simulated ? Time(obj.simulationTime) : "").Append(',');
            // the steering law that was in force when the prediction was made
            line.Append(Text(prediction?.steer)).Append(',');
            line.Append(Number(p.elevation, "F2")).Append(',');
            if (simulated)
            {
                line.Append(Number(s.elevation, "F2")).Append(',').Append(Number(s.radarHeight, "F2")).Append(',').Append(s.ground.ToString(Inv));
            }
            else
            {
                line.Append(",,");
            }
            Write(() => writer.WriteLine(line));
        }

        /// <summary>
        /// One of our own objects' samples was sent
        /// </summary>
        /// <param name="handled">Local time its message was handled</param>
        /// <param name="simClock">The simulator's own clock at the sample, NaN when there is none</param>
        /// <param name="netTime">The time it was sent with</param>
        public void OnSend(Sim.Obj obj, double handled, double simClock, double netTime)
        {
            if (Error != null)
            {
                return;
            }
            line.Clear();
            line.Append("send,").Append(Utc()).Append(',').Append(Time(handled)).Append(',')
                .Append(obj.owner).Append(',').Append(Text(obj.ownerNuid.ToString())).Append(',')
                .Append(obj.netId.ToString(Inv)).Append(',').Append(Text((obj as Sim.Aircraft)?.flightPlan.callsign)).Append(',')
                .Append(Time(netTime));
            // the sample columns are left empty, up to simClock
            // simClock, and the columns after it, are the only ones a send row fills in
            line.Append(',', SimClockColumn - 7).Append(Time(simClock)).Append(',', ColumnCount - 1 - SimClockColumn);
            Write(() => writer.WriteLine(line));
        }

        void AppendPosition(double lat, double lon, double alt, double pitch, double bank, double heading)
        {
            line.Append(Number(lat, "G12")).Append(',').Append(Number(lon, "G12")).Append(',').Append(Number(alt, "G10")).Append(',')
                .Append(Number(pitch, "G9")).Append(',').Append(Number(bank, "G9")).Append(',').Append(Number(heading, "G9")).Append(',');
        }

        void AppendVector(Vector vector)
        {
            line.Append(Number(vector.x, "G7")).Append(',').Append(Number(vector.y, "G7")).Append(',').Append(Number(vector.z, "G7")).Append(',');
        }

        string Utc() => ((utcNow() - DateTime.UnixEpoch).Ticks / (double)TimeSpan.TicksPerSecond).ToString("F6", Inv);

        static string Time(double seconds) => Number(seconds, "F6");

        static string Number(double value, string format) => double.IsNaN(value) ? "" : value.ToString(format, Inv);

        /// <summary>Free text, made safe for a CSV field</summary>
        static string Text(string text) => text == null ? "" : text.Replace(',', ' ').Replace('"', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
