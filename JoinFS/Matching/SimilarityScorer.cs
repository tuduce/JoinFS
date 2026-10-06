using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JoinFS.Matching
{
    /// <summary>One feature compared between the remote aircraft and a candidate model - a row of the "Why this model" table.</summary>
    public sealed class FeatureComparison
    {
        public string Feature = "";
        public string Remote = "";
        public string Candidate = "";
        public SpecSource RemoteSource;
        public SpecSource CandidateSource;
        /// <summary>0 = identical, 1 = completely different</summary>
        public double Distance;
        public double Weight;
        /// <summary>Share of the similarity this feature supplies (weight * (1 - distance) / total weight)</summary>
        public double Contribution;
    }

    public sealed class SimilarityResult
    {
        /// <summary>0..1; how alike the two aircraft are over the features both sides know</summary>
        public double Score;
        /// <summary>0..1; how much of the total feature weight could be compared - low means "judged on little data"</summary>
        public double Confidence;
        /// <summary>True when the pair is implausible regardless of score (rotor vs fixed wing, absurd weight ratio)</summary>
        public bool Gated;
        public string GateReason = "";
        public List<FeatureComparison> Features = [];
    }

    /// <summary>Feature weights (their ratio is what matters) and the plausibility gate limits.</summary>
    public sealed class SimilarityWeights
    {
        public double Mtow = 3.0;
        public double Span = 2.0;
        public double Length = 1.5;
        public double Cruise = 2.0;
        public double EngineCount = 2.0;
        public double EngineType = 2.0;
        public double Wing = 1.0;
        public double Gear = 1.5;

        /// <summary>Weight ratio beyond which two aircraft are never considered stand-ins for each other.</summary>
        public double GateMtowRatio = 8.0;
    }

    /// <summary>
    /// Judges how alike two aircraft are from measurable features (size, weight, speeds, engines, wing and gear layout) -
    /// so a missing B747-8 is replaced by a B747-400 and never by a Cessna. Pure and deterministic: same inputs, same score,
    /// every contribution listed. A feature is only compared when both sides know it.
    /// </summary>
    public sealed class SimilarityScorer
    {
        readonly SimilarityWeights weights;

        public SimilarityScorer(SimilarityWeights weights = null)
        {
            this.weights = weights ?? new SimilarityWeights();
        }

        public SimilarityResult Compare(AircraftSpecs remote, AircraftSpecs candidate)
        {
            SimilarityResult result = new();
            if (remote == null || candidate == null) return result;

            ApplyGate(result, remote, candidate);

            AddLog(result, "Mtow", "weight", remote.MtowKg, candidate.MtowKg, weights.Mtow, Math.Log(4), remote, candidate, "kg");
            AddLog(result, "Span", "span", remote.SpanM, candidate.SpanM, weights.Span, Math.Log(2), remote, candidate, "m");
            AddLog(result, "Length", "length", remote.LengthM, candidate.LengthM, weights.Length, Math.Log(2), remote, candidate, "m");
            AddLog(result, "Cruise", "cruise", remote.CruiseKt, candidate.CruiseKt, weights.Cruise, Math.Log(2), remote, candidate, "kt");
            AddEngineCount(result, remote, candidate);
            AddEngineType(result, remote, candidate);
            AddWing(result, remote, candidate);
            AddGear(result, remote, candidate);

            Finish(result);
            return result;
        }

        void ApplyGate(SimilarityResult result, AircraftSpecs remote, AircraftSpecs candidate)
        {
            if (remote.Rotor.HasValue && candidate.Rotor.HasValue && remote.Rotor != candidate.Rotor)
            {
                result.Gated = true;
                result.GateReason = remote.Rotor == true ? "rotorcraft requested, fixed-wing candidate" : "fixed-wing requested, rotorcraft candidate";
                return;
            }
            if (remote.MtowKg.HasValue && candidate.MtowKg.HasValue)
            {
                double ratio = Math.Max(remote.MtowKg.Value, candidate.MtowKg.Value) / Math.Min(remote.MtowKg.Value, candidate.MtowKg.Value);
                if (ratio > weights.GateMtowRatio)
                {
                    result.Gated = true;
                    result.GateReason = $"weight differs by {ratio:0}x ({Format(remote.MtowKg.Value, "kg")} vs {Format(candidate.MtowKg.Value, "kg")}), more than the {weights.GateMtowRatio:0}x plausibility limit";
                }
            }
        }

        static void Add(SimilarityResult result, string feature, string remoteText, string candidateText, SpecSource remoteSource, SpecSource candidateSource, double distance, double weight)
        {
            result.Features.Add(new FeatureComparison
            {
                Feature = feature, Remote = remoteText, Candidate = candidateText,
                RemoteSource = remoteSource, CandidateSource = candidateSource, Distance = distance, Weight = weight
            });
        }

        void AddLog(SimilarityResult result, string feature, string label, double? remoteValue, double? candidateValue, double weight, double scale,
            AircraftSpecs remote, AircraftSpecs candidate, string unit)
        {
            if (!remoteValue.HasValue || !candidateValue.HasValue || remoteValue <= 0 || candidateValue <= 0) return;
            double distance = Math.Min(1.0, Math.Abs(Math.Log(remoteValue.Value / candidateValue.Value)) / scale);
            Add(result, feature, Format(remoteValue.Value, unit), Format(candidateValue.Value, unit), remote.SourceOf(feature), candidate.SourceOf(feature), distance, weight);
        }

        void AddEngineCount(SimilarityResult result, AircraftSpecs remote, AircraftSpecs candidate)
        {
            if (!remote.EngineCount.HasValue || !candidate.EngineCount.HasValue) return;
            int difference = Math.Abs(remote.EngineCount.Value - candidate.EngineCount.Value);
            double distance = difference == 0 ? 0 : difference == 1 ? 0.6 : 1.0;
            Add(result, "Engines", remote.EngineCount.Value.ToString(CultureInfo.InvariantCulture), candidate.EngineCount.Value.ToString(CultureInfo.InvariantCulture),
                remote.SourceOf("Engines"), candidate.SourceOf("Engines"), distance, weights.EngineCount);
        }

        void AddEngineType(SimilarityResult result, AircraftSpecs remote, AircraftSpecs candidate)
        {
            if (remote.Engine == EngineKind.Unknown || candidate.Engine == EngineKind.Unknown) return;
            Add(result, "EngineType", remote.Engine.ToString().ToLowerInvariant(), candidate.Engine.ToString().ToLowerInvariant(),
                remote.SourceOf("EngineType"), candidate.SourceOf("EngineType"), EngineDistance(remote.Engine, candidate.Engine), weights.EngineType);
        }

        void AddWing(SimilarityResult result, AircraftSpecs remote, AircraftSpecs candidate)
        {
            if (remote.Wing == WingConfig.Unknown || candidate.Wing == WingConfig.Unknown) return;
            Add(result, "Wing", remote.Wing.ToString().ToLowerInvariant(), candidate.Wing.ToString().ToLowerInvariant(),
                remote.SourceOf("Wing"), candidate.SourceOf("Wing"), WingDistance(remote.Wing, candidate.Wing), weights.Wing);
        }

        void AddGear(SimilarityResult result, AircraftSpecs remote, AircraftSpecs candidate)
        {
            if (remote.Gear == GearKind.Unknown || candidate.Gear == GearKind.Unknown) return;
            Add(result, "Gear", remote.Gear.ToString().ToLowerInvariant(), candidate.Gear.ToString().ToLowerInvariant(),
                remote.SourceOf("Gear"), candidate.SourceOf("Gear"), GearDistance(remote.Gear, candidate.Gear), weights.Gear);
        }

        /// <summary>Same technology 0; neighbouring technologies partly alike (piston/turboprop), jet vs. piston entirely different.</summary>
        static double EngineDistance(EngineKind a, EngineKind b)
        {
            if (a == b) return 0;
            return Pair(a, b, EngineKind.Piston, EngineKind.Turboprop) ? 0.5
                : Pair(a, b, EngineKind.Turboprop, EngineKind.Turboshaft) ? 0.4
                : Pair(a, b, EngineKind.Turboprop, EngineKind.Jet) ? 0.6
                : Pair(a, b, EngineKind.Piston, EngineKind.Turboshaft) ? 0.7
                : Pair(a, b, EngineKind.Piston, EngineKind.Electric) ? 0.4
                : Pair(a, b, EngineKind.Jet, EngineKind.Turboshaft) ? 0.9
                : 1.0;
        }

        static double WingDistance(WingConfig a, WingConfig b)
        {
            if (a == b) return 0;
            if (a == WingConfig.Rotor || b == WingConfig.Rotor) return 1.0;
            if (a == WingConfig.Biplane || b == WingConfig.Biplane) return 1.0;
            if (a == WingConfig.Mid || b == WingConfig.Mid) return 0.5;
            return 1.0;
        }

        static double GearDistance(GearKind a, GearKind b)
        {
            if (a == b) return 0;
            if (Pair(a, b, GearKind.Tricycle, GearKind.Taildragger)) return 0.6;
            if (Pair(a, b, GearKind.Floats, GearKind.Tricycle) || Pair(a, b, GearKind.Floats, GearKind.Taildragger)) return 0.8;
            return 1.0;
        }

        static bool Pair<T>(T a, T b, T x, T y) where T : struct
        {
            var comparer = EqualityComparer<T>.Default;
            return (comparer.Equals(a, x) && comparer.Equals(b, y)) || (comparer.Equals(a, y) && comparer.Equals(b, x));
        }

        void Finish(SimilarityResult result)
        {
            double totalWeight = result.Features.Sum(f => f.Weight);
            double allWeights = weights.Mtow + weights.Span + weights.Length + weights.Cruise +
                weights.EngineCount + weights.EngineType + weights.Wing + weights.Gear;

            if (totalWeight <= 0)
            {
                result.Score = 0;
                result.Confidence = 0;
                return;
            }

            foreach (var feature in result.Features)
            {
                feature.Contribution = feature.Weight * (1 - feature.Distance) / totalWeight;
            }
            result.Score = result.Features.Sum(f => f.Contribution);

            // without any size data (weight, span, length on both sides) the airframe is judged on what is left: engine count, engine type
            // and gear are then the reference for "enough to decide", so confidence is measured against them instead of all features
            bool sizeKnown = result.Features.Any(f => f.Feature is "Mtow" or "Span" or "Length");
            double reference = sizeKnown ? allWeights : weights.EngineCount + weights.EngineType + weights.Gear;
            result.Confidence = Math.Min(1.0, totalWeight / reference);
        }

        static string Format(double value, string unit)
        {
            string number = value >= 1000 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);
            return number + " " + unit;
        }
    }
}
