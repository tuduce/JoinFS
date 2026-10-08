using System;

namespace JoinFS.Estimation
{
    /// <summary>
    /// The original clock model. It keeps a local estimate of the sender's clock and pulls it a
    /// little toward each sample's time, which smooths arrival jitter but absorbs the constant part
    /// of the network delay. That delay is then added back as about half the (filtered) round-trip
    /// time to the owner.
    /// </summary>
    public sealed class RttHalfClock : IClockModel
    {
        /// <summary>Share of the error between the estimate and a sample's time removed per sample</summary>
        public const double TimeErrorRate = 0.02;

        /// <summary>Sender time of the newest sample</summary>
        double stateTime = 0.0;
        /// <summary>Estimate of the sender's clock, as of <see cref="arrivalTime"/></summary>
        double realTime = 0.0;
        /// <summary>Local time the newest sample arrived</summary>
        double arrivalTime = 0.0;
        /// <summary>Low-pass filtered round-trip time. Kept across resets.</summary>
        float filteredRtt = 0.0f;

        public bool Started => realTime != 0.0;

        public void OnSample(double netTime, double receivedAt, double now)
        {
            // local time of this sample - never before the previous one, so time only moves forward
            double localTime = receivedAt > 0.0 ? Math.Max(receivedAt, arrivalTime) : now;
            stateTime = netTime;
            // check for first sample
            if (realTime == 0.0)
            {
                realTime = stateTime;
            }
            else
            {
                // update estimated sender time
                realTime += localTime - arrivalTime;
                // calculate error between the sample and the estimated time
                double error = stateTime - realTime;
                // gradually merge to remove error over time
                realTime += error * TimeErrorRate;
            }
            arrivalTime = localTime;
        }

        public double SampleAge(double now, in PeerTiming peer)
        {
            float delay = 0.0f;
            if (peer.HasLink)
            {
                // pass the round-trip time through a low-pass filter to smooth out the values and
                // avoid jittering
                float alpha = 0.75f;
                delay = alpha * peer.Rtt + (1.0f - alpha) * filteredRtt;
                filteredRtt = delay;
            }
            // time since the sample, plus the one-way delay: the round trip divided by two
            return realTime - stateTime + now - arrivalTime + 0.52 * delay;
        }

        public void Reset()
        {
            stateTime = 0.0;
            realTime = 0.0;
            arrivalTime = 0.0;
        }

        public void CopyFrom(IClockModel other)
        {
            if (other is RttHalfClock clock)
            {
                stateTime = clock.stateTime;
                realTime = clock.realTime;
                arrivalTime = clock.arrivalTime;
            }
            else
            {
                Reset();
            }
        }
    }
}
