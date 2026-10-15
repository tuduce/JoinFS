using System;

namespace JoinFS.Recording
{
    /// <summary>
    /// The time base of one recorded object: the recording seconds its records are stamped with, so a track's positions, variables,
    /// events and identities are in one order and the capture never has to hold a record back (spec 5: a track's time may not go back).
    /// </summary>
    /// <remarks>
    /// Positions carry the time they were sampled (for a network object, its sender's clock). The first position of the object fixes
    /// the offset to recording time, and later ones keep their sample spacing, so arrival jitter does not move them. Variables, events
    /// and identities carry no sample time: they take the time of the latest position, which is what they arrived with. Only when the
    /// positions stop (a parked object, a lost stream) does that time move on, <see cref="OverdueSeconds"/> behind the recorder's.
    /// Before the first position everything takes the recorder's time.
    /// </remarks>
    public sealed class TrackTimeBase
    {
        /// <summary>
        /// How long after the latest position arrived records keep its time. Far above the spacing of positions (20 Hz for aircraft)
        /// and their arrival jitter, so a record is never stamped after the next position.
        /// </summary>
        public const double OverdueSeconds = 0.5;

        double offset;
        bool hasPosition;
        double latestPosition;
        double latestArrival;

        /// <summary>The time of a position sampled at <paramref name="sampleTime"/> that the recorder gets at <paramref name="now"/>.</summary>
        public double Position(double now, double sampleTime)
        {
            if (!hasPosition)
            {
                offset = now - sampleTime;
                hasPosition = true;
            }
            latestPosition = sampleTime + offset;
            latestArrival = now;
            return latestPosition;
        }

        /// <summary>The time of a variables, event or identity record that the recorder gets at <paramref name="now"/>.</summary>
        public double Other(double now) => hasPosition ? latestPosition + Math.Max(0.0, now - latestArrival - OverdueSeconds) : now;
    }
}
