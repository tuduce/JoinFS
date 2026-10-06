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
        /// <summary>The original clock model, kept as the frozen reference (-clock RttHalf)</summary>
        public const string RttHalfName = "RttHalf";
        public const string DefaultClock = MinOffsetClock.Name;
        /// <summary>The original estimator, kept as the frozen reference (-estimator Classic)</summary>
        public const string ClassicName = "Classic";
        public const string DefaultEstimator = ClassicFixedEstimator.Name;
        public const string DefaultSteering = "Classic";

        static readonly Dictionary<string, Func<IClockModel>> clocks = new()
        {
            [RttHalfName] = () => new RttHalfClock(),
            [MinOffsetClock.Name] = () => new MinOffsetClock(),
        };

        /// <summary>The catch-up rate of each steering law, per second of error</summary>
        static readonly Dictionary<string, double> steeringGains = new()
        {
            [DefaultSteering] = ClassicSteering.CatchUpRate,
            ["Gain4"] = 4.0,
            ["Gain8"] = 8.0,
            ["Gain16"] = 16.0,
        };

        static readonly Dictionary<string, Func<IStateEstimator>> estimators = new()
        {
            [ClassicName] = () => new ClassicEstimator(),
            [ClassicFixedEstimator.Name] = () => new ClassicFixedEstimator(),
        };

        public static IEnumerable<string> ClockNames => clocks.Keys;
        public static IEnumerable<string> EstimatorNames => estimators.Keys;
        public static IEnumerable<string> SteeringNames => steeringGains.Keys;

        /// <summary>The laws <see cref="SteeringSchedule.Alternate"/> cycles through: the original and the stiff candidates (Gain4 was in the 2026-10-06 flight and sits between)</summary>
        public static readonly string[] AlternatedSteering = [DefaultSteering, "Gain8", "Gain16"];

        /// <summary>
        /// The steering law new objects get (the -steering command-line option): a name, or
        /// <see cref="SteeringSchedule.Alternate"/>. Set once at start-up.
        /// </summary>
        public static string SelectedSteering { get; private set; } = DefaultSteering;

        /// <summary>Choose the steering law; false, and no change, when the name is unknown</summary>
        public static bool SelectSteering(string name)
        {
            if (name == null || (name != SteeringSchedule.Alternate && steeringGains.ContainsKey(name) == false))
            {
                return false;
            }
            SelectedSteering = name;
            return true;
        }

        /// <summary>A steering law by name - the default one when the name is unknown</summary>
        public static ISteeringLaw CreateSteering(string name, bool setAttitudeEveryFrame, double groundAltitudeLimit) =>
            new ClassicSteering(setAttitudeEveryFrame, groundAltitudeLimit, steeringGains.TryGetValue(name, out double gain) ? gain : steeringGains[DefaultSteering]);

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
