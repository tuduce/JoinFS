using System.Diagnostics;

namespace JoinFS.Net
{
    /// <summary>
    /// Monotonic time source for the network core. Everything timer-driven (pulses, expiry,
    /// retries, handshakes) reads this instead of DateTime/Stopwatch directly, so tests can drive
    /// the whole stack deterministically with a manual clock.
    /// </summary>
    public interface IClock
    {
        /// <summary>Seconds since an arbitrary fixed origin.</summary>
        double Now { get; }

        /// <summary>High-resolution tick count, as carried in legacy Pulse messages for RTT.</summary>
        long Timestamp { get; }

        /// <summary>Ticks per second of <see cref="Timestamp"/>.</summary>
        long Frequency { get; }
    }

    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new();

        readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public double Now => stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
        public long Timestamp => Stopwatch.GetTimestamp();
        public long Frequency => Stopwatch.Frequency;
    }

    /// <summary>Clock advanced explicitly by tests.</summary>
    public sealed class ManualClock : IClock
    {
        public double Now { get; private set; }
        public long Frequency => 10_000_000;
        public long Timestamp => (long)(Now * Frequency);

        public void Advance(double seconds) => Now += seconds;
    }
}
