using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;

namespace JoinFS
{
    /// <summary>
    /// The addresses that took no conversation over JFP2 (a join or a request got no answer), kept across runs so that the next run joins and asks
    /// them over legacy at once instead of waiting 2 s each time (docs/network/jfp2/protocol.md §5.12, §5.16). One line per address, in
    /// <c>legacyonly.txt</c>: the endpoint, a tab and the UTC ticks until which the memory lasts. It covers hubs, address book entries and typed
    /// addresses alike, as the network thread keeps one memory for all of them (<see cref="Net.NetworkEventKind.LegacyOnly"/>).
    /// </summary>
    public sealed class LegacyOnlyStore(string storagePath, ISessionLog log)
    {
        const string FILE = "legacyonly.txt";

        readonly string path = Path.Combine(storagePath, FILE);
        readonly Dictionary<IPEndPoint, DateTime> until = [];

        /// <summary>The addresses remembered and not yet expired, with the seconds of the memory that are left.</summary>
        public List<(IPEndPoint EndPoint, double Seconds)> Load()
        {
            var result = new List<(IPEndPoint, double)>();
            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string[] parts = line.Split('\t');
                        if (parts.Length == 2 && IPEndPoint.TryParse(parts[0], out IPEndPoint endPoint) && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                        {
                            var end = new DateTime(ticks, DateTimeKind.Utc);
                            if (end > DateTime.UtcNow)
                            {
                                until[endPoint] = end;
                                result.Add((endPoint, (end - DateTime.UtcNow).TotalSeconds));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Event("Cannot read " + FILE + ": " + ex.Message);
            }
            return result;
        }

        /// <summary>The network thread learned that <paramref name="endPoint"/> takes no conversation over JFP2 for <paramref name="seconds"/>.</summary>
        public void Remember(IPEndPoint endPoint, double seconds)
        {
            until[endPoint] = DateTime.UtcNow.AddSeconds(seconds);
            DateTime now = DateTime.UtcNow;
            foreach (IPEndPoint expired in new List<IPEndPoint>(until.Keys))
            {
                if (until[expired] <= now) until.Remove(expired);
            }
            try
            {
                var lines = new List<string>(until.Count);
                foreach (var (address, end) in until)
                {
                    lines.Add(address + "\t" + end.Ticks.ToString(CultureInfo.InvariantCulture));
                }
                File.WriteAllLines(path, lines);
            }
            catch (Exception ex)
            {
                log.Event("Cannot write " + FILE + ": " + ex.Message);
            }
        }
    }
}
