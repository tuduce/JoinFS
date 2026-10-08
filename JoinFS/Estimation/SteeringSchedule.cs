using System;
using System.Linq;

namespace JoinFS.Estimation
{
    /// <summary>
    /// Which steering law is in force at a given time. Normally one; with the selection
    /// <see cref="Alternate"/> it cycles through <see cref="EstimationRegistry.AlternatedSteering"/>, a few minutes each, so
    /// that one flight compares them under the same conditions (the log says which was in force).
    /// The laws keep no state, so switching is seamless.
    /// </summary>
    public sealed class SteeringSchedule
    {
        /// <summary>The selection that cycles through every registered law</summary>
        public const string Alternate = "alternate";

        /// <summary>Seconds each law is in force when alternating</summary>
        public const double PeriodSeconds = 120.0;

        readonly string[] names;
        readonly ISteeringLaw[] laws;
        readonly double period;

        /// <param name="create">A law by name</param>
        /// <param name="selection">The law to use, or <see cref="Alternate"/></param>
        public SteeringSchedule(Func<string, ISteeringLaw> create, string selection, double period = PeriodSeconds)
        {
            names = selection == Alternate ? EstimationRegistry.AlternatedSteering : [selection];
            laws = names.Select(create).ToArray();
            this.period = period;
        }

        /// <summary>The law in force at local time <paramref name="now"/>, and its name</summary>
        public ISteeringLaw At(double now, out string name)
        {
            int index = names.Length == 1 ? 0 : (int)(Math.Floor(Math.Max(0.0, now) / period) % names.Length);
            name = names[index];
            return laws[index];
        }
    }
}
