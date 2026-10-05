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
        /// <summary>The original estimator, kept as the frozen reference (-estimator Classic)</summary>
        public const string ClassicName = "Classic";
        public const string DefaultEstimator = ClassicFixedEstimator.Name;

        static readonly Dictionary<string, Func<IClockModel>> clocks = new()
        {
            [DefaultClock] = () => new RttHalfClock(),
            [MinOffsetClock.Name] = () => new MinOffsetClock(),
        };

        static readonly Dictionary<string, Func<IStateEstimator>> estimators = new()
        {
            [ClassicName] = () => new ClassicEstimator(),
            [ClassicFixedEstimator.Name] = () => new ClassicFixedEstimator(),
        };

        public static IEnumerable<string> ClockNames => clocks.Keys;
        public static IEnumerable<string> EstimatorNames => estimators.Keys;

        /// <summary>
        /// The clock model new objects get (the -clock command-line option). Set once at start-up,
        /// before any object exists.
        /// </summary>
        public static string SelectedClock { get; private set; } = DefaultClock;

        /// <summary>Choose the clock model new objects get; false, and no change, when the name is unknown</summary>
        public static bool SelectClock(string name)
        {
            if (name == null || clocks.ContainsKey(name) == false)
            {
                return false;
            }
            SelectedClock = name;
            return true;
        }

        /// <summary>A new clock model by name - the default one when the name is unknown</summary>
        public static IClockModel CreateClock(string name = DefaultClock) =>
            (clocks.TryGetValue(name, out var create) ? create : clocks[DefaultClock])();

        /// <summary>
        /// The estimator new objects get (the -estimator command-line option). Set once at start-up,
        /// before any object exists.
        /// </summary>
        public static string SelectedEstimator { get; private set; } = DefaultEstimator;

        /// <summary>Choose the estimator new objects get; false, and no change, when the name is unknown</summary>
        public static bool SelectEstimator(string name)
        {
            if (name == null || estimators.ContainsKey(name) == false)
            {
                return false;
            }
            SelectedEstimator = name;
            return true;
        }

        /// <summary>A new estimator by name - the default one when the name is unknown</summary>
        public static IStateEstimator CreateEstimator(string name = DefaultEstimator) =>
            (estimators.TryGetValue(name, out var create) ? create : estimators[DefaultEstimator])();
    }
}
