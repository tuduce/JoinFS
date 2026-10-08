using System.Globalization;
using System.Text;
using JoinFS.Estimation;

namespace JoinFS.Tests.Estimation.Replay
{
    /// <summary>
    /// The offline harness of docs/position-estimation-plan.md §6: replays a receiver's logged
    /// samples through estimators from <see cref="EstimationRegistry"/> and scores each prediction
    /// against the sender's own later samples, interpolated at the predicted moment (sender time).
    ///
    /// That isolates the estimator: the horizons are given, not chosen by a clock model. They are
    /// the ones the receiver's clock chose in the field ("logged": predFrom + predAge), and fixed
    /// ones standing for other network delays.
    /// </summary>
    public sealed class ReplayScorer(IReadOnlyList<string> estimators)
    {
        /// <summary>Horizons scored besides the logged ones, in seconds</summary>
        public static readonly double[] FixedHorizons = [0.05, 0.1, 0.2];
        public const string Logged = "logged";
        /// <summary>The truth is not interpolated across a longer gap between samples, in seconds</summary>
        public const double Gap = 0.15;
        /// <summary>Earth radius, as JoinFS uses it (Vector.GeodesicDistance)</summary>
        const double R = 6371009.0;

        public static readonly string[] Phases = ["all", "ground", "straight", "turning"];
        static readonly Dictionary<string, string> PhaseLabels = new()
        {
            ["all"] = "all",
            ["ground"] = "ground",
            ["straight"] = "air, bank < 10°",
            ["turning"] = "air, bank ≥ 30°",
        };

        public IReadOnlyList<string> Estimators { get; } = estimators;

        /// <summary>Errors of one estimator at one horizon in one phase</summary>
        public sealed class Errors
        {
            public readonly Distribution Along = new(), Cross = new(), Vertical = new(), Horizontal = new();
            public readonly Distribution Heading = new(), Pitch = new(), Bank = new();
        }

        readonly Dictionary<(string estimator, string horizon, string phase), Errors> errors = [];
        /// <summary>Per estimator: how far its prediction is from the one logged (which estimator ran)</summary>
        readonly Dictionary<string, Distribution> reproduction = [];
        readonly Distribution loggedHorizon = new();
        readonly List<(string name, int samples, double minutes)> objects = [];

        public Errors this[string estimator, string horizon, string phase] =>
            errors.TryGetValue((estimator, horizon, phase), out Errors? e) ? e : new Errors();

        public Distribution Reproduction(string estimator) => reproduction.TryGetValue(estimator, out var d) ? d : new Distribution();

        public static string HorizonLabel(double horizon) => (horizon * 1000.0).ToString("F0", CultureInfo.InvariantCulture) + " ms";

        static IEnumerable<string> Horizons => FixedHorizons.Select(HorizonLabel).Prepend(Logged);

        /// <summary>The sender's state, from its samples, at a moment on its clock</summary>
        readonly record struct Truth(double Lat, double Lon, double Alt, double Vx, double Vz, double Pitch, double Heading, double Bank, bool Ground);

        static double Wrap(double a) => Vector.AngleDelta(0.0, a);

        static Truth? Interpolate(List<LoggedSample> samples, double[] times, double t)
        {
            int i = Array.BinarySearch(times, t);
            i = i >= 0 ? i : ~i - 1;
            if (i < 0 || i + 1 >= samples.Count)
            {
                return null;
            }
            LoggedSample a = samples[i], b = samples[i + 1];
            double dt = times[i + 1] - times[i];
            if (dt <= 0.0 || dt > Gap || a.Paused || b.Paused)
            {
                return null;
            }
            double w = (t - times[i]) / dt;
            double L(double x, double y) => x + w * (y - x);
            double A(double x, double y) => x + w * Wrap(y - x);
            return new Truth(L(a.Lat, b.Lat), A(a.Lon, b.Lon), L(a.Alt, b.Alt), L(a.Vx, b.Vx), L(a.Vz, b.Vz),
                A(a.Pitch, b.Pitch), A(a.Heading, b.Heading), A(a.Bank, b.Bank), a.Ground);
        }

        /// <summary>Stretches of samples on one sender clock, in sender-time order</summary>
        static IEnumerable<List<LoggedSample>> Segments(LoggedObject obj)
        {
            var current = new List<LoggedSample>();
            foreach (LoggedSample s in obj.Samples)
            {
                if (current.Count > 0)
                {
                    LoggedSample last = current[^1];
                    // the sender restarted (its clock starts over), or a long break
                    if (s.NetTime < last.NetTime - 1.0 || s.ReceivedAt - last.ReceivedAt > 60.0)
                    {
                        yield return current;
                        current = [];
                    }
                    else if (s.NetTime <= last.NetTime)
                    {
                        continue;
                    }
                }
                current.Add(s);
            }
            if (current.Count > 0)
            {
                yield return current;
            }
        }

        /// <summary>Replay one object's samples through every estimator</summary>
        public void Add(LoggedObject obj)
        {
            int count = 0;
            double minutes = 0.0;
            foreach (List<LoggedSample> segment in Segments(obj))
            {
                double[] times = segment.Select(s => s.NetTime).ToArray();
                count += segment.Count;
                minutes += (times[^1] - times[0]) / 60.0;
                IStateEstimator[] instances = Estimators.Select(EstimationRegistry.CreateEstimator).ToArray();
                for (int i = 0; i < segment.Count; i++)
                {
                    LoggedSample s = segment[i];
                    KinematicState state = s.State();
                    foreach (IStateEstimator estimator in instances)
                    {
                        estimator.OnSample(state, s.NetTime);
                    }
                    if (s.Paused)
                    {
                        continue;
                    }
                    // the receiver's prediction logged with the next sample was made from this one
                    if (i + 1 < segment.Count)
                    {
                        LoggedSample next = segment[i + 1];
                        if (double.IsNaN(next.PredLocal) == false && Math.Abs(next.PredFrom - s.NetTime) < 1e-6)
                        {
                            loggedHorizon.Add(next.PredAge);
                            Score(segment, times, instances, s, state, next.PredAge, Logged, next);
                        }
                    }
                    foreach (double horizon in FixedHorizons)
                    {
                        Score(segment, times, instances, s, state, horizon, HorizonLabel(horizon), null);
                    }
                }
            }
            objects.Add((obj.Name, count, minutes));
        }

        void Score(List<LoggedSample> segment, double[] times, IStateEstimator[] instances, LoggedSample s, in KinematicState state,
            double horizon, string label, LoggedSample? logged)
        {
            if (Interpolate(segment, times, s.NetTime + horizon) is not Truth truth)
            {
                return;
            }
            string phase = truth.Ground ? "ground"
                : Math.Abs(truth.Bank) < Math.PI / 18.0 ? "straight"
                : Math.Abs(truth.Bank) >= Math.PI / 6.0 ? "turning" : null!;

            for (int k = 0; k < instances.Length; k++)
            {
                KinematicState predicted = instances[k].Predict(state, horizon);
                Sim.Pos p = predicted.Position;
                double lat = p.geo.z, lon = p.geo.x, alt = p.geo.y;

                // along-track, cross-track and vertical, against the true direction of flight
                double east = Wrap(lon - truth.Lon) * R * Math.Cos(truth.Lat);
                double north = (lat - truth.Lat) * R;
                double speed = Math.Sqrt(truth.Vx * truth.Vx + truth.Vz * truth.Vz);
                double along, cross;
                if (speed < 1.0)
                {
                    along = Math.Sqrt(east * east + north * north);
                    cross = 0.0;
                }
                else
                {
                    double ue = truth.Vx / speed, un = truth.Vz / speed;
                    along = east * ue + north * un;
                    cross = -east * un + north * ue;
                }

                foreach (string bucket in phase == null ? new[] { "all" } : new[] { "all", phase })
                {
                    if (errors.TryGetValue((Estimators[k], label, bucket), out Errors? e) == false)
                    {
                        errors[(Estimators[k], label, bucket)] = e = new Errors();
                    }
                    e.Along.Add(along);
                    e.Cross.Add(cross);
                    e.Vertical.Add(alt - truth.Alt);
                    e.Horizontal.Add(Math.Sqrt(along * along + cross * cross));
                    e.Heading.Add(Wrap(p.angles.y - truth.Heading) * 180.0 / Math.PI);
                    e.Pitch.Add(Wrap(p.angles.x - truth.Pitch) * 180.0 / Math.PI);
                    e.Bank.Add(Wrap(p.angles.z - truth.Bank) * 180.0 / Math.PI);
                }

                if (logged != null)
                {
                    double de = Wrap(lon - logged.PredLon) * R * Math.Cos(logged.PredLat);
                    double dn = (lat - logged.PredLat) * R;
                    if (reproduction.TryGetValue(Estimators[k], out Distribution? r) == false)
                    {
                        reproduction[Estimators[k]] = r = new Distribution();
                    }
                    r.Add(Math.Sqrt(de * de + dn * dn) + Math.Abs(alt - logged.PredAlt));
                }
            }
        }

        /// <summary>The scores as a Markdown section</summary>
        public string Report(string title)
        {
            static string F(double x, int digits = 2) => double.IsNaN(x) ? "-" : x.ToString("F" + digits, CultureInfo.InvariantCulture);
            var w = new StringBuilder();
            w.Append("## ").Append(title).Append("\n\n");
            foreach (var (name, samples, minutes) in objects)
            {
                w.Append("- ").Append(name).Append(": ").Append(samples).Append(" samples, ").Append(F(minutes, 0)).Append(" min\n");
            }
            if (loggedHorizon.Count > 0)
            {
                w.Append("\nLogged horizons: p50 ").Append(F(loggedHorizon.Percentile(0.5) * 1000.0, 0)).Append(" ms, p95 ")
                    .Append(F(loggedHorizon.Percentile(0.95) * 1000.0, 0)).Append(" ms. Distance from the logged predictions (which estimator ran):");
                foreach (string estimator in Estimators)
                {
                    Distribution r = Reproduction(estimator);
                    w.Append(' ').Append(estimator).Append(" p50 ").Append(F(r.Percentile(0.5) * 1000.0, 3)).Append(" mm;");
                }
                w.Length--;
                w.Append(".\n");
            }
            foreach (string horizon in Horizons)
            {
                w.Append("\n### Horizon: ").Append(horizon).Append("\n\n");
                w.Append("| phase | estimator | n | along p50 / p95 / p99 m | cross p95 m | vert p95 m | horiz p95 / p99 m | heading p95 / p99 deg | pitch p95 deg | bank p95 deg |\n");
                w.Append("|---|---|---|---|---|---|---|---|---|---|\n");
                foreach (string phase in Phases)
                {
                    foreach (string estimator in Estimators)
                    {
                        Errors e = this[estimator, horizon, phase];
                        if (e.Along.Count == 0)
                        {
                            continue;
                        }
                        w.Append("| ").Append(PhaseLabels[phase]).Append(" | ").Append(estimator).Append(" | ").Append(e.Along.Count).Append(" | ")
                            .Append(F(e.Along.Percentile(0.5))).Append(" / ").Append(F(e.Along.Percentile(0.95))).Append(" / ").Append(F(e.Along.Percentile(0.99))).Append(" | ")
                            .Append(F(e.Cross.Percentile(0.95))).Append(" | ").Append(F(e.Vertical.Percentile(0.95))).Append(" | ")
                            .Append(F(e.Horizontal.Percentile(0.95))).Append(" / ").Append(F(e.Horizontal.Percentile(0.99))).Append(" | ")
                            .Append(F(e.Heading.Percentile(0.95), 3)).Append(" / ").Append(F(e.Heading.Percentile(0.99), 3)).Append(" | ")
                            .Append(F(e.Pitch.Percentile(0.95), 3)).Append(" | ").Append(F(e.Bank.Percentile(0.95), 3)).Append(" |\n");
                    }
                }
            }
            return w.ToString();
        }
    }

    /// <summary>
    /// Percentiles of |value| in constant memory: log-spaced bins, 100 per decade (about 2%
    /// resolution) from 1e-6 to 1e4. Field logs have millions of predictions.
    /// </summary>
    public sealed class Distribution
    {
        const double Min = 1e-6;
        const int PerDecade = 100;
        readonly long[] bins = new long[PerDecade * 10 + 2];

        public long Count { get; private set; }
        public double Max { get; private set; }

        public void Add(double value)
        {
            if (double.IsNaN(value))
            {
                return;
            }
            double v = Math.Abs(value);
            Count++;
            Max = Math.Max(Max, v);
            int i = v < Min ? 0 : Math.Min(bins.Length - 1, 1 + (int)Math.Floor(Math.Log10(v / Min) * PerDecade));
            bins[i]++;
        }

        /// <summary>The q-quantile of |value| (the upper edge of its bin), NaN when empty</summary>
        public double Percentile(double q)
        {
            if (Count == 0)
            {
                return double.NaN;
            }
            long target = Math.Max(1, (long)Math.Ceiling(q * Count));
            long seen = 0;
            for (int i = 0; i < bins.Length; i++)
            {
                seen += bins[i];
                if (seen >= target)
                {
                    return Math.Min(Max, Min * Math.Pow(10.0, i / (double)PerDecade));
                }
            }
            return Max;
        }
    }
}
