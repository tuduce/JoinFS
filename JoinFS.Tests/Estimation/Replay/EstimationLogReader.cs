using System.Globalization;
using System.IO.Compression;
using JoinFS.Estimation;

namespace JoinFS.Tests.Estimation.Replay
{
    /// <summary>One "sample" row of an estimation log (JoinFS/Estimation/EstimationLog.cs)</summary>
    public sealed class LoggedSample
    {
        public double NetTime, ReceivedAt;
        public double Lat, Lon, Alt, Pitch, Bank, Heading;
        public double Vx, Vy, Vz, Avx, Avy, Avz, Ax, Ay, Az;
        public bool Ground, Paused;
        /// <summary>The newest prediction made before this sample arrived (NaN before the first)</summary>
        public double PredLocal = double.NaN, PredFrom = double.NaN, PredAge = double.NaN;
        public double PredLat, PredLon, PredAlt, PredPitch, PredBank, PredHeading;

        /// <summary>The sample as the receiver's estimator was given it</summary>
        public KinematicState State() => new(
            new Sim.Pos(new Vector(Lon, Alt, Lat), new Vector(Pitch, Heading, Bank), 0.0, Ground ? 1 : 0),
            new Sim.Vel(new Vector(Vx, Vy, Vz), new Vector(Avx, Avy, Avz), new Vector(Ax, Ay, Az)));
    }

    /// <summary>One remote object as a receiver logged it</summary>
    public sealed class LoggedObject(string name)
    {
        public string Name { get; } = name;
        public List<LoggedSample> Samples { get; } = [];
    }

    /// <summary>Reads the sample rows of estimation logs, by column name</summary>
    public static class EstimationLogReader
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// The estimation logs at <paramref name="paths"/>: CSV files, folders holding
        /// estimation-*.csv, or the zips the tester package uploads. A log in several zips is read
        /// once.
        /// </summary>
        public static IEnumerable<(string name, Func<TextReader> open)> Find(IEnumerable<string> paths)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*estimation-*.csv", SearchOption.AllDirectories).Order())
                    {
                        if (seen.Add(Path.GetFileName(file)))
                        {
                            yield return (Path.GetFileName(file), () => new StreamReader(file));
                        }
                    }
                }
                else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using ZipArchive zip = ZipFile.OpenRead(path);
                    // Windows PowerShell 5.1's Compress-Archive writes backslashes
                    var names = zip.Entries.Select(e => e.FullName).Where(n => Path.GetFileName(n.Replace('\\', '/')).StartsWith("estimation-") && n.EndsWith(".csv")).Order().ToList();
                    foreach (string entry in names)
                    {
                        string name = Path.GetFileName(entry.Replace('\\', '/'));
                        if (seen.Add(name))
                        {
                            string zipPath = path;
                            yield return (name, () => new ZipEntryReader(zipPath, entry));
                        }
                    }
                }
                else if (seen.Add(Path.GetFileName(path)))
                {
                    yield return (Path.GetFileName(path), () => new StreamReader(path));
                }
            }
        }

        /// <summary>Reads one zip entry, and closes the zip with it</summary>
        sealed class ZipEntryReader : StreamReader
        {
            readonly ZipArchive zip;

            public ZipEntryReader(string path, string entry) : this(ZipFile.OpenRead(path), entry) { }

            ZipEntryReader(ZipArchive zip, string entry) : base(zip.GetEntry(entry)!.Open()) => this.zip = zip;

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    zip.Dispose();
                }
            }
        }

        /// <summary>The objects in one log, each with its samples in the order they were accepted</summary>
        public static List<LoggedObject> Read(TextReader reader)
        {
            string[] header = (reader.ReadLine() ?? "").Split(',');
            var column = header.Select((name, index) => (name, index)).ToDictionary(c => c.name, c => c.index);
            int Col(string name) => column[name];
            int kind = Col("kind"), node = Col("node"), netId = Col("netId"), callsign = Col("callsign");
            int netTime = Col("netTime"), receivedAt = Col("receivedAt");
            int lat = Col("lat"), lon = Col("lon"), alt = Col("alt"), pitch = Col("pitch"), bank = Col("bank"), heading = Col("heading");
            int vx = Col("vx"), avx = Col("avx"), ax = Col("ax"), ground = Col("ground"), paused = Col("paused");
            int predLocal = Col("predLocal"), predFrom = Col("predFrom"), predAge = Col("predAge"), predLat = Col("predLat");

            var objects = new Dictionary<string, LoggedObject>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("sample,") == false)
                {
                    continue;
                }
                string[] f = line.Split(',');
                double D(int i) => f[i].Length == 0 ? double.NaN : double.Parse(f[i], Inv);
                string key = f[node] + "/" + f[netId];
                if (objects.TryGetValue(key, out LoggedObject? obj) == false)
                {
                    obj = new LoggedObject((f[callsign].Length > 0 ? f[callsign] : "#" + f[netId]) + " from " + f[node]);
                    objects.Add(key, obj);
                }
                var s = new LoggedSample
                {
                    NetTime = D(netTime), ReceivedAt = D(receivedAt),
                    Lat = D(lat), Lon = D(lon), Alt = D(alt), Pitch = D(pitch), Bank = D(bank), Heading = D(heading),
                    Vx = D(vx), Vy = D(vx + 1), Vz = D(vx + 2),
                    Avx = D(avx), Avy = D(avx + 1), Avz = D(avx + 2),
                    Ax = D(ax), Ay = D(ax + 1), Az = D(ax + 2),
                    Ground = f[ground] != "0", Paused = f[paused] == "1",
                    PredLocal = D(predLocal), PredFrom = D(predFrom), PredAge = D(predAge),
                };
                if (double.IsNaN(s.PredLocal) == false)
                {
                    // predLat, predLon, predAlt, predPitch, predBank, predHeading
                    s.PredLat = D(predLat); s.PredLon = D(predLat + 1); s.PredAlt = D(predLat + 2);
                    s.PredPitch = D(predLat + 3); s.PredBank = D(predLat + 4); s.PredHeading = D(predLat + 5);
                }
                obj.Samples.Add(s);
            }
            return [.. objects.Values];
        }
    }
}
