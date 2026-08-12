using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace JoinFS.Diagnostics
{
#if LATENCY_TRACE
    /// <summary>
    /// Trace points for latency measurement throughout the position update pipeline
    /// </summary>
    public enum TracePoint : byte
    {
        // Tick lifecycle
        TickStart = 0,
        TickEnd = 1,

        // SimConnect path
        SimConnectRequestSent = 10,
        SimConnectDataReceived = 11,

        // Position update path (local → network)
        PositionReadFromSim = 20,
        PositionWrittenToSendBuffer = 21,
        UdpSendCalled = 22,
        UdpSendCompleted = 23,

        // Receive path (network → local)
        UdpPacketReceivedInOsBuffer = 30,
        UdpPacketReceived = 31,
        PacketProcessed = 32,

        // Hub relay (CONSOLE only)
        HubRelayIn = 40,
        HubRelayOut = 41,
    }

    /// <summary>
    /// Single trace entry in the ring buffer
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TraceEntry
    {
        /// <summary>
        /// High-resolution timestamp from Stopwatch.GetTimestamp()
        /// </summary>
        public long TimestampTicks;

        /// <summary>
        /// Trace point identifier
        /// </summary>
        public TracePoint Point;

        /// <summary>
        /// Object identifier (netId or nuid hash) for correlation
        /// </summary>
        public uint ObjectId;

        /// <summary>
        /// Payload size in bytes (for network operations)
        /// </summary>
        public ushort PayloadBytes;

        /// <summary>
        /// Thread ID for multi-threading analysis (Phase 2)
        /// </summary>
        public byte ThreadId;

        /// <summary>
        /// Reserved for future use
        /// </summary>
        public byte Reserved;
    }

    /// <summary>
    /// Lock-free ring buffer tracer for high-performance latency measurement.
    /// Pre-allocated buffer with Interlocked-based write index to avoid allocations and locks on the hot path.
    /// </summary>
    public static class LatencyTracer
    {
        /// <summary>
        /// Ring buffer capacity (must be power of 2 for efficient modulo via bitwise AND)
        /// 65536 entries × 24 bytes = ~1.5 MB
        /// </summary>
        private const int BufferSize = 65536;
        private const int BufferMask = BufferSize - 1;

        /// <summary>
        /// Pre-allocated ring buffer
        /// </summary>
        private static readonly TraceEntry[] Buffer = new TraceEntry[BufferSize];

        /// <summary>
        /// Current write index (wraps around at BufferSize)
        /// </summary>
        private static int writeIndex = 0;

        /// <summary>
        /// Stopwatch frequency for microsecond conversion
        /// </summary>
        public static readonly long StopwatchFrequency = Stopwatch.Frequency;

        /// <summary>
        /// Total number of entries written (for statistics)
        /// </summary>
        private static long totalEntries = 0;

        /// <summary>
        /// Whether tracing is currently enabled
        /// </summary>
        private static volatile bool isEnabled = true;

        /// <summary>
        /// Enable or disable tracing
        /// </summary>
        public static bool IsEnabled
        {
            get => isEnabled;
            set => isEnabled = value;
        }

        /// <summary>
        /// Get total entries written since startup
        /// </summary>
        public static long TotalEntries => Interlocked.Read(ref totalEntries);

        /// <summary>
        /// Record a trace point (hot path - must be fast)
        /// </summary>
        /// <param name="point">Trace point identifier</param>
        /// <param name="objectId">Object identifier for correlation (optional)</param>
        /// <param name="bytes">Payload size in bytes (optional)</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Record(TracePoint point, uint objectId = 0, ushort bytes = 0)
        {
            if (!isEnabled)
                return;

            // Get high-resolution timestamp first (minimize latency to measurement)
            long timestamp = Stopwatch.GetTimestamp();

            // Atomically increment write index and get our slot
            int index = Interlocked.Increment(ref writeIndex) - 1;
            int slot = index & BufferMask;

            // Write entry (may overwrite old data if buffer is full - intentional)
            ref TraceEntry entry = ref Buffer[slot];
            entry.TimestampTicks = timestamp;
            entry.Point = point;
            entry.ObjectId = objectId;
            entry.PayloadBytes = bytes;
            entry.ThreadId = (byte)(Environment.CurrentManagedThreadId & 0xFF);
            entry.Reserved = 0;

            // Track total entries for statistics
            Interlocked.Increment(ref totalEntries);
        }

        /// <summary>
        /// Get a snapshot of the current buffer for analysis.
        /// Returns a copy to avoid race conditions during dump.
        /// </summary>
        public static TraceEntry[] GetBufferSnapshot()
        {
            // Create a copy of the buffer
            TraceEntry[] snapshot = new TraceEntry[BufferSize];
            Array.Copy(Buffer, snapshot, BufferSize);
            return snapshot;
        }

        /// <summary>
        /// Get the current write index (for determining how much of the buffer is valid)
        /// </summary>
        public static int GetWriteIndex()
        {
            return Interlocked.CompareExchange(ref writeIndex, 0, 0);
        }

        /// <summary>
        /// Reset the tracer (clear all entries)
        /// </summary>
        public static void Reset()
        {
            Interlocked.Exchange(ref writeIndex, 0);
            Interlocked.Exchange(ref totalEntries, 0);
            Array.Clear(Buffer, 0, BufferSize);
        }

        /// <summary>
        /// Convert Stopwatch ticks to microseconds
        /// </summary>
        public static double TicksToMicroseconds(long ticks)
        {
            return (ticks * 1_000_000.0) / StopwatchFrequency;
        }

        /// <summary>
        /// Convert Stopwatch ticks to milliseconds
        /// </summary>
        public static double TicksToMilliseconds(long ticks)
        {
            return (ticks * 1000.0) / StopwatchFrequency;
        }
    }
#endif
}
