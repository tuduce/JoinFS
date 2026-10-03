using System;
using System.Collections.Generic;

namespace JoinFS.Estimation
{
    /// <summary>
    /// The position estimation parts by name, so that they can be swapped to try other ideas
    /// (docs/position-estimation-plan.md §4). Register a new clock model or estimator here.
    /// </summary>
    public static class EstimationRegistry
    {
        public const string DefaultClock = "RttHalf";
        public const string DefaultEstimator = "Classic";

        static readonly Dictionary<string, Func<IClockModel>> clocks = new()
        {
            [DefaultClock] = () => new RttHalfClock(),
        };

        static readonly Dictionary<string, Func<IStateEstimator>> estimators = new()
        {
            [DefaultEstimator] = () => new ClassicEstimator(),
        };

        public static IEnumerable<string> ClockNames => clocks.Keys;
        public static IEnumerable<string> EstimatorNames => estimators.Keys;

        /// <summary>A new clock model by name - the default one when the name is unknown</summary>
        public static IClockModel CreateClock(string name = DefaultClock) =>
            (clocks.TryGetValue(name, out var create) ? create : clocks[DefaultClock])();

        /// <summary>A new estimator by name - the default one when the name is unknown</summary>
        public static IStateEstimator CreateEstimator(string name = DefaultEstimator) =>
            (estimators.TryGetValue(name, out var create) ? create : estimators[DefaultEstimator])();
    }
}
