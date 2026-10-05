using System;

namespace JoinFS.Estimation
{
    /// <summary>
    /// A clock model that reads the sender's clock offset off the fastest recent samples, instead of
    /// averaging it (docs/position-estimation-plan.md §3 item 2, the passive fallback, F1/F6/F7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sample's arrival time minus its stamp is the clock offset plus its one-way delay. The
    /// smallest value over the last ten seconds is the offset plus the least delay the path gave,
    /// which half the smallest round trip over the last minute estimates. So the age of a sample is
    /// the time since it arrived, plus how much later than the fastest one it arrived, plus that
    /// least delay.
    /// </para>
    /// <para>
    /// It needs honest stamps (sim-time stamping, <see cref="SimClockStamper"/>): dispatch jitter in
    /// the stamp would pull the minimum down. It assumes the two ways of the path take the same
    /// time, like <see cref="RttHalfClock"/> does.
    /// </para>
    /// </remarks>
    public sealed class MinOffsetClock : IClockModel
    {
        public const string Name = "MinOffset";

        /// <summary>Seconds a bucket of the offset window covers; the window is <see cref="OffsetBuckets"/> of them</summary>
        public const double OffsetBucketSeconds = 1.0;
        public const int OffsetBuckets = 10;
        /// <summary>Seconds a bucket of the round-trip window covers; the window is <see cref="RttBuckets"/> of them</summary>
        public const double RttBucketSeconds = 5.0;
        public const int RttBuckets = 12;

        /// <summary>
        /// A sample this much later than the fastest of the window is the new normal when it keeps
        /// happening (the sender's clock stepped, or its simulator restarted): start over
        /// </summary>
        public const double RestartExcess = 0.3;
        /// <summary>Consecutive samples later than <see cref="RestartExcess"/> that make it start over</summary>
        public const int RestartCount = 10;

        /// <summary>Round-trip times at or above this, in seconds, mean "not known" (the network side gives 9999)</summary>
        public const float MaxRtt = 5.0f;

        readonly MinWindow offsets = new(OffsetBuckets, OffsetBucketSeconds);
        readonly MinWindow rtts = new(RttBuckets, RttBucketSeconds);
        bool started;
        /// <summary>Local time the newest sample arrived</summary>
        double arrivalTime;
        /// <summary>Arrival minus stamp of the newest sample</summary>
        double sampleOffset;
        int lateSamples;

        public bool Started => started;

        public void OnSample(double netTime, double receivedAt, double now)
        {
            // local time of this sample - never before the previous one, so time only moves forward
            double localTime = receivedAt > 0.0 ? Math.Max(receivedAt, arrivalTime) : now;
            double offset = localTime - netTime;
            if (started && offsets.Min(localTime) is double fastest && offset - fastest > RestartExcess)
            {
                if (++lateSamples >= RestartCount)
                {
                    offsets.Clear();
                    lateSamples = 0;
                }
            }
            else
            {
                lateSamples = 0;
            }
            offsets.Add(localTime, offset);
            sampleOffset = offset;
            arrivalTime = localTime;
            started = true;
        }

        public double SampleAge(double now, in PeerTiming peer)
        {
            if (started == false)
            {
                return 0.0;
            }
            double oneWay = 0.0;
            if (peer.HasLink)
            {
                // the network side keeps one pulse answer per second, or none yet
                if (peer.Rtt > 0.0f && peer.Rtt < MaxRtt)
                {
                    rtts.Add(now, peer.Rtt);
                }
                oneWay = (rtts.Min(now) ?? 0.0) * 0.5;
            }
            // time since it arrived, how late it was against the fastest, and the least delay
            return now - arrivalTime + (sampleOffset - (offsets.Min(now) ?? sampleOffset)) + oneWay;
        }

        public void Reset()
        {
            started = false;
            offsets.Clear();
            lateSamples = 0;
            arrivalTime = 0.0;
            sampleOffset = 0.0;
        }

        public void CopyFrom(IClockModel other)
        {
            if (other is MinOffsetClock clock)
            {
                offsets.CopyFrom(clock.offsets);
                rtts.CopyFrom(clock.rtts);
                started = clock.started;
                arrivalTime = clock.arrivalTime;
                sampleOffset = clock.sampleOffset;
                lateSamples = clock.lateSamples;
            }
            else
            {
                Reset();
            }
        }

        /// <summary>
        /// The smallest value over the last few buckets of time. A few numbers per window, so it is
        /// cheap to ask every frame and to copy.
        /// </summary>
        sealed class MinWindow(int buckets, double bucketSeconds)
        {
            readonly double[] values = new double[buckets];
            readonly long[] ids = NewIds(buckets);

            static long[] NewIds(int count)
            {
                long[] ids = new long[count];
                Array.Fill(ids, long.MinValue);
                return ids;
            }

            long Bucket(double time) => (long)Math.Floor(time / bucketSeconds);

            public void Add(double time, double value)
            {
                long id = Bucket(time);
                int slot = (int)(((id % buckets) + buckets) % buckets);
                if (ids[slot] != id)
                {
                    ids[slot] = id;
                    values[slot] = value;
                }
                else if (value < values[slot])
                {
                    values[slot] = value;
                }
            }

            /// <summary>The smallest value of the window that ends at <paramref name="time"/>, null when it is empty</summary>
            public double? Min(double time)
            {
                long newest = Bucket(time);
                double? min = null;
                for (int slot = 0; slot < ids.Length; slot++)
                {
                    if (ids[slot] > newest - buckets && ids[slot] <= newest && (min == null || values[slot] < min))
                    {
                        min = values[slot];
                    }
                }
                return min;
            }

            public void Clear() => Array.Fill(ids, long.MinValue);

            public void CopyFrom(MinWindow other)
            {
                Array.Copy(other.values, values, values.Length);
                Array.Copy(other.ids, ids, ids.Length);
            }
        }
    }
}
