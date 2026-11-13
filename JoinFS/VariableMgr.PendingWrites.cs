using System;
using System.Collections.Generic;

namespace JoinFS
{
    public partial class VariableMgr
    {
        /// <summary>
        /// Values JoinFS wrote to the simulator and has not read back yet. A read that returns
        /// one of them is our own echo, not a change made in the simulator, so it must not start
        /// a hold; and a network update repeating the latest write while it is still on its way
        /// must not be written again (a toggle event sent twice flips the switch back).
        /// </summary>
        public sealed class PendingWrites<T>
        {
            /// <summary>
            /// How long a write waits for its echo. A value the simulator does not take (or changes)
            /// is written again after this, as long as the network still asks for it.
            /// </summary>
            public const double TIMEOUT = 2.0;

            /// <summary>
            /// Most writes remembered per variable (a lever moving on the sender gives one per update)
            /// </summary>
            const int MAX_WRITES = 16;

            readonly Dictionary<uint, List<(T value, double expiry)>> writes = [];
            readonly Func<uint, T, T, bool> match;

            /// <param name="match">Whether two values of a variable count as the same</param>
            public PendingWrites(Func<uint, T, T, bool> match)
            {
                this.match = match;
            }

            /// <summary>
            /// We wrote this value to the simulator
            /// </summary>
            public void Written(uint vuid, T value, double now)
            {
                if (writes.TryGetValue(vuid, out var list) == false)
                {
                    writes[vuid] = list = [];
                }
                list.RemoveAll(write => write.expiry < now);
                if (list.Count >= MAX_WRITES) list.RemoveAt(0);
                list.Add((value, now + TIMEOUT));
            }

            /// <summary>
            /// Is our latest write to this variable this value, and still on its way to the simulator
            /// </summary>
            public bool InFlight(uint vuid, T value, double now)
            {
                if (writes.TryGetValue(vuid, out var list) == false || list.Count == 0)
                {
                    return false;
                }
                var latest = list[^1];
                if (latest.expiry < now)
                {
                    // no echo in time: let the network write it again
                    writes.Remove(vuid);
                    return false;
                }
                return match(vuid, latest.value, value);
            }

            /// <summary>
            /// Is this read the echo of one of our writes. An echo ends that write and the ones before it.
            /// </summary>
            public bool IsEcho(uint vuid, T value)
            {
                if (writes.TryGetValue(vuid, out var list))
                {
                    for (int index = list.Count - 1; index >= 0; index--)
                    {
                        if (match(vuid, list[index].value, value))
                        {
                            list.RemoveRange(0, index + 1);
                            if (list.Count == 0) writes.Remove(vuid);
                            return true;
                        }
                    }
                }
                return false;
            }
        }
    }
}
