using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace JoinFS.Diagnostics
{
#if LATENCY_TRACE
    /// <summary>
    /// Analyzes latency tracer data and generates CSV and JSON reports
    /// </summary>
    public static class LatencyReport
    {
        /// <summary>
        /// Dump the current trace buffer to CSV and JSON files
        /// </summary>
        /// <param name="basePath">Base path for output files (without extension)</param>
        /// <returns>Paths to the generated files</returns>
        public static (string csvPath, string jsonPath) Dump(string basePath)
        {
            // Get snapshot of the buffer
            TraceEntry[] snapshot = LatencyTracer.GetBufferSnapshot();
            int writeIndex = LatencyTracer.GetWriteIndex();
            long totalEntries = LatencyTracer.TotalEntries;

            // Determine how many valid entries we have
            int validEntries = (int)Math.Min(totalEntries, snapshot.Length);

            // Sort entries by timestamp to get chronological order
            var sortedEntries = new List<TraceEntry>(validEntries);
            for (int i = 0; i < validEntries; i++)
            {
                if (snapshot[i].TimestampTicks != 0)
                    sortedEntries.Add(snapshot[i]);
            }
            sortedEntries.Sort((a, b) => a.TimestampTicks.CompareTo(b.TimestampTicks));

            // Generate CSV
            string csvPath = basePath + ".csv";
            WriteCsv(csvPath, sortedEntries);

            // Generate JSON summary
            string jsonPath = basePath + "_summary.json";
            WriteJsonSummary(jsonPath, sortedEntries);

            return (csvPath, jsonPath);
        }

        /// <summary>
        /// Write raw trace entries to CSV
        /// </summary>
        private static void WriteCsv(string path, List<TraceEntry> entries)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);

            // Header
            writer.WriteLine("TimestampMs,ElapsedSinceLastUs,TracePoint,ObjectId,PayloadBytes,ThreadId");

            long previousTicks = 0;
            foreach (var entry in entries)
            {
                double timestampMs = LatencyTracer.TicksToMilliseconds(entry.TimestampTicks);
                long elapsedTicks = entry.TimestampTicks - previousTicks;
                double elapsedUs = LatencyTracer.TicksToMicroseconds(elapsedTicks);

                writer.WriteLine($"{timestampMs:F3},{elapsedUs:F2},{entry.Point},{entry.ObjectId},{entry.PayloadBytes},{entry.ThreadId}");

                previousTicks = entry.TimestampTicks;
            }
        }

        /// <summary>
        /// Write JSON summary with per-hop latency statistics
        /// </summary>
        private static void WriteJsonSummary(string path, List<TraceEntry> entries)
        {
            var summary = new
            {
                Metadata = new
                {
                    GeneratedAt = DateTime.Now.ToString("o"),
                    TotalEntries = LatencyTracer.TotalEntries,
                    AnalyzedEntries = entries.Count,
                    StopwatchFrequency = LatencyTracer.StopwatchFrequency,
                },
                PerHopLatencyUs = CalculatePerHopLatencies(entries),
                TracePointCounts = CalculateTracePointCounts(entries),
                TopDelays = CalculateTopDelays(entries),
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            string json = JsonSerializer.Serialize(summary, options);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        /// <summary>
        /// Calculate latency statistics for common trace point pairs (hops)
        /// </summary>
        private static Dictionary<string, LatencyStats> CalculatePerHopLatencies(List<TraceEntry> entries)
        {
            var hopLatencies = new Dictionary<string, List<double>>();

            // Define interesting hop pairs
            var hopPairs = new[]
            {
                (TracePoint.TickStart, TracePoint.TickEnd),
                (TracePoint.SimConnectRequestSent, TracePoint.SimConnectDataReceived),
                (TracePoint.PositionReadFromSim, TracePoint.PositionWrittenToSendBuffer),
                (TracePoint.PositionWrittenToSendBuffer, TracePoint.UdpSendCalled),
                (TracePoint.UdpSendCalled, TracePoint.UdpSendCompleted),
                (TracePoint.UdpPacketReceived, TracePoint.PacketProcessed),
                (TracePoint.HubRelayIn, TracePoint.HubRelayOut),
            };

            // Track the most recent occurrence of each trace point (by object ID)
            var lastSeen = new Dictionary<(TracePoint, uint), long>();

            foreach (var entry in entries)
            {
                var key = (entry.Point, entry.ObjectId);
                lastSeen[key] = entry.TimestampTicks;

                // Check if this completes any hop pairs
                foreach (var (start, end) in hopPairs)
                {
                    if (entry.Point == end)
                    {
                        var startKey = (start, entry.ObjectId);
                        if (lastSeen.TryGetValue(startKey, out long startTicks))
                        {
                            long deltaTicks = entry.TimestampTicks - startTicks;
                            if (deltaTicks > 0 && deltaTicks < LatencyTracer.StopwatchFrequency * 10) // Sanity check: < 10 seconds
                            {
                                double deltaUs = LatencyTracer.TicksToMicroseconds(deltaTicks);
                                string hopName = $"{start} → {end}";

                                if (!hopLatencies.ContainsKey(hopName))
                                    hopLatencies[hopName] = new List<double>();

                                hopLatencies[hopName].Add(deltaUs);
                            }
                        }
                    }
                }
            }

            // Calculate percentiles for each hop
            var result = new Dictionary<string, LatencyStats>();
            foreach (var kvp in hopLatencies)
            {
                result[kvp.Key] = CalculateStats(kvp.Value);
            }

            return result;
        }

        /// <summary>
        /// Calculate statistics from a list of latency values
        /// </summary>
        private static LatencyStats CalculateStats(List<double> values)
        {
            if (values.Count == 0)
                return new LatencyStats { Count = 0 };

            values.Sort();

            return new LatencyStats
            {
                Count = values.Count,
                Min = values[0],
                P50 = Percentile(values, 0.50),
                P95 = Percentile(values, 0.95),
                P99 = Percentile(values, 0.99),
                Max = values[values.Count - 1],
                Mean = values.Average(),
            };
        }

        /// <summary>
        /// Calculate percentile value from sorted list
        /// </summary>
        private static double Percentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0)
                return 0;

            double index = percentile * (sortedValues.Count - 1);
            int lowerIndex = (int)Math.Floor(index);
            int upperIndex = (int)Math.Ceiling(index);

            if (lowerIndex == upperIndex)
                return sortedValues[lowerIndex];

            double weight = index - lowerIndex;
            return sortedValues[lowerIndex] * (1 - weight) + sortedValues[upperIndex] * weight;
        }

        /// <summary>
        /// Count occurrences of each trace point
        /// </summary>
        private static Dictionary<string, int> CalculateTracePointCounts(List<TraceEntry> entries)
        {
            var counts = new Dictionary<string, int>();

            foreach (var entry in entries)
            {
                string pointName = entry.Point.ToString();
                if (!counts.ContainsKey(pointName))
                    counts[pointName] = 0;
                counts[pointName]++;
            }

            return counts;
        }

        /// <summary>
        /// Find the top 10 largest delays between consecutive entries
        /// </summary>
        private static List<DelayRecord> CalculateTopDelays(List<TraceEntry> entries)
        {
            var delays = new List<DelayRecord>();

            for (int i = 1; i < entries.Count; i++)
            {
                long deltaTicks = entries[i].TimestampTicks - entries[i - 1].TimestampTicks;
                double deltaUs = LatencyTracer.TicksToMicroseconds(deltaTicks);

                delays.Add(new DelayRecord
                {
                    FromPoint = entries[i - 1].Point.ToString(),
                    ToPoint = entries[i].Point.ToString(),
                    DelayUs = deltaUs,
                    TimestampMs = LatencyTracer.TicksToMilliseconds(entries[i - 1].TimestampTicks)
                });
            }

            return delays.OrderByDescending(d => d.DelayUs).Take(10).ToList();
        }

        /// <summary>
        /// Latency statistics for a hop
        /// </summary>
        public class LatencyStats
        {
            public int Count { get; set; }
            public double Min { get; set; }
            public double P50 { get; set; }
            public double P95 { get; set; }
            public double P99 { get; set; }
            public double Max { get; set; }
            public double Mean { get; set; }
        }

        /// <summary>
        /// Record of a single delay
        /// </summary>
        public class DelayRecord
        {
            public string FromPoint { get; set; }
            public string ToPoint { get; set; }
            public double DelayUs { get; set; }
            public double TimestampMs { get; set; }
        }
    }
#endif
}
