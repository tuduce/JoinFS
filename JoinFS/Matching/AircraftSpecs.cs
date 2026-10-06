using System.Collections.Generic;

namespace JoinFS.Matching
{
    /// <summary>Where a physical value came from - drives the confidence shown in the report.</summary>
    public enum SpecSource
    {
        /// <summary>Not available</summary>
        None,
        /// <summary>Derived from Doc8643 class code / typerole only (coarse)</summary>
        Inferred,
        /// <summary>Public reference data for the ICAO type (data/aircraft-specs.json)</summary>
        Reference,
        /// <summary>Read from the model's own aircraft.cfg / flight_model.cfg / .acf</summary>
        Config,
        /// <summary>Read live from the running simulator via SimConnect</summary>
        SimConnect
    }

    public enum WingConfig { Unknown, High, Mid, Low, Biplane, Rotor }

    /// <summary>None = a pure glider (no engine). Appended last; local to this matcher, never sent over the JoinFS wire.</summary>
    public enum EngineKind { Unknown, Piston, Turboprop, Jet, Turboshaft, Electric, None }

    /// <summary>Undercarriage layout - a strong visual cue (skids vs. tailwheel vs. nosewheel).</summary>
    public enum GearKind { Unknown, Tricycle, Taildragger, Skids, Floats, Skis }

    /// <summary>
    /// Measurable aircraft features used to judge visual/behavioural similarity. Any value may be missing
    /// (null / Unknown): similarity only compares what both sides know. Units: metres, kilograms, knots.
    /// </summary>
    public class AircraftSpecs
    {
        /// <summary>True for helicopters/autogyros</summary>
        public bool? Rotor;
        public WingConfig Wing = WingConfig.Unknown;
        /// <summary>Wing span (rotor diameter for rotorcraft), metres</summary>
        public double? SpanM;
        public double? LengthM;
        /// <summary>Maximum takeoff / gross weight, kg</summary>
        public double? MtowKg;
        public int? EngineCount;
        public EngineKind Engine = EngineKind.Unknown;
        /// <summary>Typical cruise speed, knots (true airspeed)</summary>
        public double? CruiseKt;
        public GearKind Gear = GearKind.Unknown;
        /// <summary>Normalized manufacturer (see <see cref="ManufacturerKey"/>); not a weighted feature, used for the same-make rule</summary>
        public string Manufacturer = "";
        public string Name = "";

        /// <summary>Where each feature came from, by feature name (Span, Length, Mtow, Cruise, Engines, EngineType, Rotor, Wing, Gear)</summary>
        public Dictionary<string, SpecSource> Sources = [];

        public SpecSource SourceOf(string feature) => Sources.TryGetValue(feature, out var s) ? s : SpecSource.None;

        /// <summary>Set a value together with its source; ignores nulls so a better source is never overwritten by missing data.</summary>
        public void Set(string feature, SpecSource source)
        {
            Sources[feature] = source;
        }

        /// <summary>Number of the features that have a value.</summary>
        public int KnownFeatureCount
        {
            get
            {
                int n = 0;
                if (Rotor.HasValue) n++;
                if (Wing != WingConfig.Unknown) n++;
                if (SpanM.HasValue) n++;
                if (LengthM.HasValue) n++;
                if (MtowKg.HasValue) n++;
                if (EngineCount.HasValue) n++;
                if (Engine != EngineKind.Unknown) n++;
                if (CruiseKt.HasValue) n++;
                if (Gear != GearKind.Unknown) n++;
                return n;
            }
        }

        public const int FeatureCount = 9;
    }
}
