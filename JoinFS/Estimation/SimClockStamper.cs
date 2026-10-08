using System;
using System.Collections.Generic;

namespace JoinFS.Estimation
{
    /// <summary>
    /// Times an own-aircraft sample by the simulator's clock instead of by when its message was
    /// handled (finding F2 in docs/position-estimation-plan.md). The handling time carries the
    /// jitter of SimConnect's queue and of the sim thread's wake-up, which goes straight into every
    /// receiver's extrapolation; the simulator's clock is the time the state was computed.
    ///
    /// The two clocks are tied together by the least-delayed samples: the smallest
    /// (handled - simulator time) over the last <see cref="Window"/>. A stamp is then the
    /// simulator time plus that offset: on our clock, never later than the handling time, and
    /// only ever moving forward.
    ///
    /// It falls back to the handling time when the simulator's clock cannot be used: when there
    /// is none (X-Plane, FSX, P3D), while it stands still (paused), and when it jumps back or falls
    /// behind (a reload, a hitch, the end of a pause), from where it starts over.
    /// One per object, sim thread only.
    /// </summary>
    public sealed class SimClockStamper
    {
        /// <summary>How far back the least-delayed sample is looked for, in seconds</summary>
        public const double Window = 1.0;
        /// <summary>
        /// A sample this much later than the window's least-delayed one, in seconds, means the
        /// simulator's clock fell behind ours: start over
        /// </summary>
        public const double StepLimit = 0.04;

        /// <summary>The last <see cref="Window"/> of samples: handling time and (handled - simulator time)</summary>
        readonly Queue<(double handled, double offset)> recent = new();
        double lastSimTime = double.NaN;
        double lastStamp = double.NegativeInfinity;

        /// <summary>
        /// The time, on our clock, at which a sample was taken
        /// </summary>
        /// <param name="simTime">The simulator's clock at the sample, in seconds; NaN when unknown</param>
        /// <param name="handled">Our clock (ElapsedTime) when its message was handled</param>
        public double Stamp(double simTime, double handled)
        {
            // only a clock that moved on since the last sample says when this one was taken
            bool advanced = simTime > lastSimTime;
            if (double.IsNaN(simTime) == false)
            {
                lastSimTime = simTime;
            }
            if (advanced == false)
            {
                return Keep(handled);
            }

            // the least-delayed sample still in the window
            while (recent.Count > 0 && recent.Peek().handled < handled - Window)
            {
                recent.Dequeue();
            }
            double offset = handled - simTime;
            double floor = double.PositiveInfinity;
            foreach (var sample in recent)
            {
                floor = Math.Min(floor, sample.offset);
            }

            // the simulator's clock fell behind ours, or jumped back: start over from this sample
            if (offset > floor + StepLimit)
            {
                recent.Clear();
                floor = double.PositiveInfinity;
            }
            recent.Enqueue((handled, offset));
            double stamp = simTime + Math.Min(floor, offset);

            // receivers drop a sample that is not newer than the one before
            return stamp > lastStamp ? Keep(stamp) : Keep(handled);
        }

        double Keep(double stamp)
        {
            lastStamp = Math.Max(lastStamp, stamp);
            return stamp;
        }
    }
}
