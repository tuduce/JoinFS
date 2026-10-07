using System;
using System.Collections.Generic;
using System.Linq;
using Model = JoinFS.Substitution.Model;
using MatchAttribute = JoinFS.Substitution.MatchAttribute;
using MatchType = JoinFS.Substitution.Type;
using MatchTrace = JoinFS.Substitution.MatchTrace;

namespace JoinFS.Matching
{
    /// <summary>One installed model as the combined engine ranked it - the data behind the "Why this model" report.</summary>
    public sealed class RankedCandidate
    {
        public Model Model;
        public int BaselineScore;
        public List<string> BaselineContributions = [];
        public Dictionary<MatchAttribute, int> AttributeScores = [];
        public SimilarityResult Similarity = new();
        public int SimilarityPoints;
        /// <summary>Points for "same manufacturer, same category, similar size" (0 when the rule does not apply)</summary>
        public int FamilyBonus;
        public string FamilyReason = "";
        public int CombinedScore;
        public ResolvedSpecs CandidateSpecs = new();
    }

    /// <summary>Models of one title that were excluded as physically implausible, with the reason (rotor vs fixed wing, absurd weight ratio).</summary>
    public sealed class ExcludedGroup
    {
        public string Title = "";
        public string Reason = "";
        public int Count;
    }

    /// <summary>Everything the report needs beyond the MatchTrace: remote data, full ranking, why nothing was plausible.</summary>
    public sealed class MatchExplanation
    {
        public MatchRequest EffectiveRequest;
        public ResolvedSpecs RemoteSpecs = new();
        /// <summary>The eligible candidates, best first. Implausible models are not ranked: see <see cref="ExcludedCount"/> and <see cref="ExcludedGroups"/>.</summary>
        public List<RankedCandidate> Ranking = [];
        public int ExcludedCount;
        /// <summary>The first excluded titles (at most <see cref="MaxExcludedGroups"/>) with their reason and number of liveries</summary>
        public List<ExcludedGroup> ExcludedGroups = [];
        public const int MaxExcludedGroups = 50;
        public bool NoPlausibleMatch;
        /// <summary>Notes about the request itself (alias correction ...)</summary>
        public List<string> RequestNotes = [];
        public int ModelCount;
    }

    public sealed class CombinedResult
    {
        /// <summary>The chosen model; null when nothing plausible is installed</summary>
        public Model Model;
        public MatchType Type;
        public MatchTrace Trace = new();
        public MatchExplanation Explanation = new();
    }

    /// <summary>
    /// The proposed next-generation matcher: JoinFS' tiers and baseline signals (user substitution, exact title, ICAO type,
    /// airline, class, WTC, typerole) plus a physical-similarity score, a plausibility gate, wrong-tag correction through the
    /// alias table and a deterministic ordering. Same inputs always give the same answer, and every step is explained.
    /// </summary>
    public sealed class CombinedMatcher
    {
        /// <summary>Points the similarity adds at most (similarity x confidence x this) - between an airline match (100) and ICAO type (200).</summary>
        public const int SimilarityPointsMax = 100;

        /// <summary>A candidate must be at least this alike (when the comparison has data) unless its ICAO type is exact.</summary>
        public const double MinSimilarity = 0.5;

        /// <summary>Points for a candidate of the same manufacturer, same category and a similar size - enough to beat another make that merely looks closer.</summary>
        public const int SameManufacturerBonus = 40;

        /// <summary>Points for a model of a visually related ICAO type (same family, e.g. A320 for an A20N): between an airline match (100) and the exact type (200)</summary>
        public const int RelatedTypePoints = 120;

        /// <summary>A model without any ICAO tag whose title names the requested type (a curated title hint) earns the type/class/WTC points at this share</summary>
        public const double TitleInferredTypeFactor = 0.75;

        /// <summary>"Similar size" for the same-make rule: weights within this ratio (or spans within <see cref="FamilyMaxSpanRatio"/> when weight is unknown)</summary>
        public const double FamilyMaxMtowRatio = 2.5;
        public const double FamilyMaxSpanRatio = 1.6;

        readonly IMatchCatalog catalog;
        readonly ReferenceSpecs reference;
        readonly SpecResolver specResolver;
        readonly SimilarityScorer scorer;
        readonly AirlineResolver airlines;
        readonly RelatedTypes related;
        readonly ModelSpecCache specCache;

        public CombinedMatcher(IMatchCatalog catalog, ReferenceSpecs reference, SimilarityScorer scorer = null, AirlineResolver airlines = null, RelatedTypes related = null)
        {
            this.related = related ?? RelatedTypes.Empty;
            this.airlines = airlines ?? AirlineResolver.Empty;
            this.catalog = catalog;
            this.reference = reference;
            this.scorer = scorer ?? new SimilarityScorer();
            specResolver = new SpecResolver(reference, catalog.Doc8643);
            specCache = new ModelSpecCache(specResolver);
        }

        /// <summary>
        /// The request's attribute table always shows what was asked for (e.g. "MD11F", "FEDEX"), never the corrected value the
        /// matcher worked with; the corrections are listed in the steps and in the "Why" section.
        /// </summary>
        public CombinedResult Match(MatchRequest original)
        {
            CombinedResult result = MatchCorrected(original);
            foreach (var attribute in result.Trace.attributes)
            {
                if (attribute.attribute == MatchAttribute.IcaoType) attribute.requested = original.IcaoType;
                else if (attribute.attribute == MatchAttribute.IcaoAirline) attribute.requested = original.IcaoAirline;
                else if (attribute.attribute == MatchAttribute.Typerole)
                {
                    // the typerole actually used (class code overrules a stated one) with the stated one for reference - the score is for the former
                    string stated = RoleName(original.Typerole), used = RoleName(result.Explanation.EffectiveRequest?.Typerole ?? original.Typerole);
                    attribute.requested = stated == used ? used : $"{used} (stated: {stated})";
                }
            }
            return result;
        }

        string RoleName(int role) => catalog.RoleName(role);

        CombinedResult MatchCorrected(MatchRequest original)
        {
            CombinedResult result = new();
            MatchExplanation explanation = result.Explanation;
            explanation.ModelCount = catalog.Models.Count;

            // Substitute and Original come first and see the request exactly as it was sent - an explicit user choice or an exact
            // installed title is never second-guessed by the corrections below
            (string originalClass, string originalWtc) = IdentityScorer.ResolveRemoteClass(original, catalog.Doc8643);
            if (TryExplicitTiers(original, result, originalClass, originalWtc))
            {
                explanation.EffectiveRequest = original;
                explanation.RemoteSpecs = specResolver.ForRemote(original);
                return result;
            }

            // only now the request is corrected (type alias, airline, typerole) for the scoring tiers
            MatchRequest request = CorrectTyperole(CorrectAirline(CorrectIcaoTag(original, explanation), explanation), explanation);
            explanation.EffectiveRequest = request;
            explanation.RemoteSpecs = specResolver.ForRemote(request);
            AddAirlineAvailabilityNote(request, explanation);
            result.Trace.steps.AddRange(explanation.RequestNotes);

            (string remoteClass, string remoteWtc) = IdentityScorer.ResolveRemoteClass(request, catalog.Doc8643);
            explanation.Ranking = Rank(request, remoteClass, remoteWtc, explanation.RemoteSpecs.Specs, explanation);

            RankedCandidate winner = explanation.Ranking.FirstOrDefault(IsAcceptable);
            if (winner != null)
            {
                return Accept(result, winner, request, remoteClass, remoteWtc, explanation);
            }

            return NoPlausible(result, request, remoteClass, remoteWtc, explanation);
        }

        /// <summary>
        /// An airline that is not a valid ICAO code (atc_airline "FDX" or a name "FEDEX" in the wrong field, or only the title
        /// "FedEx N234234") is resolved through ICAO_Airlines.dat; the result counts as guessed.
        /// </summary>
        MatchRequest CorrectAirline(MatchRequest request, MatchExplanation explanation)
        {
            AirlineResolution resolution = airlines.Resolve(request.IcaoAirline, request.AtcAirline, request.Title, request.Livery);
            if (!resolution.Changed || resolution.Icao.Equals(request.IcaoAirline, StringComparison.OrdinalIgnoreCase)) return request;

            explanation.RequestNotes.Add($"Airline: {resolution.Note}; '{resolution.Icao}' is used for matching{(resolution.Guessed ? " (guessed - airline points count a fifth)" : "")}.");
            return Copy(request, request.IcaoType, resolution.Icao, resolution.Guessed);
        }

        /// <summary>When the requested airline is valid but no installed model carries it, say so - the airline signal cannot score then.</summary>
        void AddAirlineAvailabilityNote(MatchRequest request, MatchExplanation explanation)
        {
            if (request.IcaoAirline.Length == 0 || !airlines.IsKnownCode(request.IcaoAirline)) return;
            if (catalog.Models.Any(m => m.icaoAirline.Equals(request.IcaoAirline, StringComparison.OrdinalIgnoreCase))) return;

            string name = airlines.NameOf(request.IcaoAirline);
            explanation.RequestNotes.Add($"Airline: '{request.IcaoAirline}'{(name.Length > 0 ? " (" + name + ")" : "")} is a valid ICAO airline code, but no installed model carries it - the airline signal cannot score, the choice rests on type, class, WTC and similarity.");
        }

        /// <summary>
        /// The class code overrules the typerole: not every aircraft has a fitting typerole (JoinFS has none for a tri-motor), while the
        /// class code (given, or from the ICAO type in Doc8643) always describes the airframe. A role derived from it replaces a differing one.
        /// </summary>
        MatchRequest CorrectTyperole(MatchRequest request, MatchExplanation explanation)
        {
            (string classCode, string wtc) = IdentityScorer.ResolveRemoteClass(request, catalog.Doc8643);
            int derived = TypeRole.FromClassCode(request.IcaoType, classCode, wtc);
            if (derived == 0 || derived == request.Typerole) return request;

            string Name(int role) => RoleName(role);
            explanation.RequestNotes.Add($"Typerole: the class code '{classCode}' implies '{Name(derived)}', which overrules the stated '{Name(request.Typerole)}' (the class code is more reliable than the typerole).");
            MatchRequest corrected = Copy(request, request.IcaoType, request.IcaoAirline, false);
            corrected.IcaoAirlineGuessed = request.IcaoAirlineGuessed;
            corrected.Typerole = derived;
            return corrected;
        }

        /// <summary>
        /// Tiers 1 and 2, exactly as Substitution.Match runs them: the substitution the user configured for the title, then an installed model
        /// with exactly the requested title (and livery on builds that have one). True when one of them decided.
        /// </summary>
        bool TryExplicitTiers(MatchRequest request, CombinedResult result, string remoteClass, string remoteWtc)
        {
            string title = request.Title;
            var steps = result.Trace.steps;
            MatchAttribute[] decisive = request.LiveryAware ? [MatchAttribute.Title, MatchAttribute.Livery] : [MatchAttribute.Title];

            if (catalog.Matches.TryGetValue(title, out Model overrideModel))
            {
                Model target = request.LiveryAware ? catalog.GetModel(overrideModel.title, overrideModel.variation) : catalog.GetModel(overrideModel.title);
                if (target != null)
                {
                    steps.Add($"Substitute: found a user-defined override for title '{title}' -> '{target.title}' (Settings > Model Matching), and the target model is currently installed.");
                    Finish(result, target, MatchType.Substitute, request, remoteClass, remoteWtc, null, decisive);
                    return true;
                }
                steps.Add($"Substitute: a user-defined override exists for title '{title}' -> '{overrideModel.title}', but that target model is not currently installed/scanned. Falling through to the next matching tier.");
            }
            else
            {
                steps.Add("Substitute: no user-defined override configured for this title.");
            }

            Model original = request.LiveryAware ? catalog.GetModel(title, request.Livery) : catalog.GetModel(title);
            if (original != null)
            {
                steps.Add(request.LiveryAware
                    ? $"Original: an installed model exactly matches the requested title '{title}' and livery '{request.Livery}'."
                    : $"Original: an installed model exactly matches the requested title '{title}'.");
                Finish(result, original, MatchType.Original, request, remoteClass, remoteWtc, null, decisive);
                return true;
            }

            var siblings = request.LiveryAware ? catalog.Models.Where(m => m.title.Equals(title, StringComparison.Ordinal)).ToList() : [];
            steps.Add(siblings.Count > 0
                ? $"Original: title '{title}' is installed, but not with livery/variation '{request.Livery}'. Installed liveries for this title: {string.Join(", ", siblings.Select(m => "'" + m.variation + "'"))}."
                : $"Original: no installed model has title '{title}'.");
            return false;
        }

        /// <summary>Record the outcome and fill the requested-vs-matched attribute grid.</summary>
        void Finish(CombinedResult result, Model matched, MatchType type, MatchRequest request, string remoteClass, string remoteWtc,
            Dictionary<MatchAttribute, int> attributeScores, params MatchAttribute[] decisiveAttrs)
        {
            result.Model = matched;
            result.Type = type;
            IdentityScorer.FillAttributes(result.Trace, matched, request, remoteClass, remoteWtc, RoleName(request.Typerole),
                matched != null ? RoleName(matched.typerole) : "", attributeScores, decisiveAttrs);
        }

        static MatchRequest Copy(MatchRequest r, string icaoType, string icaoAirline, bool airlineGuessed) => new()
        {
            Callsign = r.Callsign, LiveryAware = r.LiveryAware, Title = r.Title, Livery = r.Livery, IcaoType = icaoType, IcaoAirline = icaoAirline,
            AtcAirline = r.AtcAirline, IcaoAirlineGuessed = airlineGuessed || r.IcaoAirlineGuessed, ClassCode = r.ClassCode, Wtc = r.Wtc,
            ClassCodeConfirmed = r.ClassCodeConfirmed, Typerole = r.Typerole, TyperoleDerived = r.TyperoleDerived, Registration = r.Registration
        };

        /// <summary>A type tag with a curated alias ("500E" -> "H500") is replaced, with a note.</summary>
        MatchRequest CorrectIcaoTag(MatchRequest request, MatchExplanation explanation)
        {
            string icao = request.IcaoType;
            if (icao.Length == 0) return request;

            // an official ICAO designator always rules: the alias table only repairs tags that are not designators
            if (catalog.Doc8643.IsRecognized(icao)) return request;

            string resolved = reference.ResolveAlias(icao);
            if (resolved == icao) return request;

            explanation.RequestNotes.Add($"ICAO type '{icao}': it is not a Doc8643 designator and the alias table maps it to '{resolved}', which is used for matching.");
            return Copy(request, resolved, request.IcaoAirline, false);
        }

        /// <summary>What depends only on a candidate's physical data - computed once per distinct spec set, not once per model.</summary>
        sealed class GroupEvaluation
        {
            public SimilarityResult Similarity;
            public int Points;
            public int FamilyBonus;
            public string FamilyReason = "";
        }

        List<RankedCandidate> Rank(MatchRequest request, string remoteClass, string remoteWtc, AircraftSpecs remoteSpecs, MatchExplanation explanation)
        {
            string registrationAlnum = TextTokens.AlnumOnly(request.Registration);
            IReadOnlyList<Model> models = catalog.Models;
            specCache.Validate(models);

            // thousands of models share a handful of types (and liveries): judge each distinct spec set once
            Dictionary<ResolvedSpecs, GroupEvaluation> groups = new(ReferenceEqualityComparer.Instance);
            Dictionary<string, ExcludedGroup> excludedByTitle = new(StringComparer.Ordinal);
            List<RankedCandidate> ranked = [];

            foreach (var model in models)
            {
                ResolvedSpecs candidateSpecs = specCache.For(model);
                if (!groups.TryGetValue(candidateSpecs, out GroupEvaluation group))
                {
                    group = Evaluate(remoteSpecs, candidateSpecs);
                    groups[candidateSpecs] = group;
                }

                if (group.Similarity.Gated)
                {
                    NoteExcluded(explanation, excludedByTitle, model, group.Similarity.GateReason);
                    continue;
                }

                CandidateScore baselineScore = IdentityScorer.Score(model, request, remoteClass, remoteWtc, registrationAlnum, typeroleIsWeakHint: true);
                AddRelatedTypePoints(baselineScore, model, request);
                AddTitleInferredIdentity(baselineScore, model, candidateSpecs, request, remoteClass, remoteWtc);

                ranked.Add(new RankedCandidate
                {
                    Model = model,
                    BaselineScore = baselineScore.Score,
                    BaselineContributions = baselineScore.Contributions,
                    AttributeScores = baselineScore.AttributeScores,
                    CandidateSpecs = candidateSpecs,
                    Similarity = group.Similarity,
                    SimilarityPoints = group.Points,
                    FamilyBonus = group.FamilyBonus,
                    FamilyReason = group.FamilyReason,
                    CombinedScore = baselineScore.Score + group.Points + group.FamilyBonus
                });
            }

            // deterministic order: best score, then most alike, then title/variation alphabetically
            ranked.Sort(CompareCandidates);
            return ranked;
        }

        GroupEvaluation Evaluate(AircraftSpecs remoteSpecs, ResolvedSpecs candidateSpecs)
        {
            SimilarityResult similarity = scorer.Compare(remoteSpecs, candidateSpecs.Specs);
            (int familyBonus, string familyReason) = similarity.Gated ? (0, "") : SameManufacturerRule(remoteSpecs, candidateSpecs.Specs);
            return new GroupEvaluation
            {
                Similarity = similarity,
                Points = (int)Math.Round(SimilarityPointsMax * similarity.Score * similarity.Confidence),
                FamilyBonus = familyBonus,
                FamilyReason = familyReason
            };
        }

        static int CompareCandidates(RankedCandidate a, RankedCandidate b)
        {
            int byScore = b.CombinedScore.CompareTo(a.CombinedScore);
            if (byScore != 0) return byScore;
            int bySimilarity = b.Similarity.Score.CompareTo(a.Similarity.Score);
            if (bySimilarity != 0) return bySimilarity;
            int byTitle = string.CompareOrdinal(a.Model.title, b.Model.title);
            return byTitle != 0 ? byTitle : string.CompareOrdinal(a.Model.variation, b.Model.variation);
        }

        static void NoteExcluded(MatchExplanation explanation, Dictionary<string, ExcludedGroup> byTitle, Model model, string reason)
        {
            explanation.ExcludedCount++;
            if (byTitle.TryGetValue(model.title, out ExcludedGroup group))
            {
                group.Count++;
            }
            else if (explanation.ExcludedGroups.Count < MatchExplanation.MaxExcludedGroups)
            {
                group = new ExcludedGroup { Title = model.title, Reason = reason, Count = 1 };
                byTitle[model.title] = group;
                explanation.ExcludedGroups.Add(group);
            }
        }

        /// <summary>
        /// Same manufacturer + same category (rotorcraft or fixed wing) + a similar size (the size window keeps a Cessna 172 from borrowing a Citation): a sibling of the
        /// requested aircraft (Robinson R22 for an R44, Boeing 747-400 for a 747-8) wins over another make that only looks close.
        /// </summary>
        /// <summary>A related type (same family in related.dat) is worth <see cref="RelatedTypePoints"/>, scaled down when the model's type was guessed.</summary>
        void AddRelatedTypePoints(CandidateScore score, Model model, MatchRequest request)
        {
            if (score.AttributeScores.ContainsKey(MatchAttribute.IcaoType) || !related.AreRelated(request.IcaoType, model.icaoType)) return;

            double factor = model.icaoGuessed ? Substitution.GuessedSignalMultiplier : 1.0;
            int points = (int)Math.Round(RelatedTypePoints * factor);
            score.Score += points;
            score.AttributeScores[MatchAttribute.IcaoType] = points;
            score.Contributions.Add($"related ICAO type '{model.icaoType}' ~ '{request.IcaoType}' ({(factor < 1 ? "guessed, " : "")}+{points})");
        }

        /// <summary>
        /// A model the simulator left untagged (no ICAO type) but whose title names the requested type - "S12-G: Passengers" for a Stemme S12 - is the
        /// aircraft itself; without this it would score nothing next to models that merely carry a tag. Type, class and WTC count at
        /// <see cref="TitleInferredTypeFactor"/>.
        /// </summary>
        void AddTitleInferredIdentity(CandidateScore score, Model model, ResolvedSpecs modelSpecs, MatchRequest request, string remoteClass, string remoteWtc)
        {
            if (model.icaoType.Length > 0 || request.IcaoType.Length == 0) return;
            if (!modelSpecs.EffectiveIcao.Equals(request.IcaoType, StringComparison.OrdinalIgnoreCase)) return;

            int type = (int)Math.Round(200 * TitleInferredTypeFactor);
            score.Score += type;
            score.AttributeScores[MatchAttribute.IcaoType] = type;
            score.Contributions.Add($"title names ICAO type '{request.IcaoType}' (title hint, untagged model, +{type})");

            if (catalog.Doc8643.TryGet(request.IcaoType, out var row))
            {
                if (remoteClass.Length == 3 && row.ClassCode == remoteClass && model.classCode.Length == 0)
                {
                    int cls = (int)Math.Round(100 * TitleInferredTypeFactor);
                    score.Score += cls;
                    score.AttributeScores[MatchAttribute.ClassCode] = cls;
                    score.Contributions.Add($"class code '{remoteClass}' via the title-named type (+{cls})");
                }
                if (remoteWtc.Length > 0 && row.Wtc == remoteWtc && model.wtc.Length == 0)
                {
                    int wtc = (int)Math.Round(40 * TitleInferredTypeFactor);
                    score.Score += wtc;
                    score.AttributeScores[MatchAttribute.Wtc] = wtc;
                    score.Contributions.Add($"WTC '{remoteWtc}' via the title-named type (+{wtc})");
                }
            }
        }

        static (int points, string reason) SameManufacturerRule(AircraftSpecs remote, AircraftSpecs candidate)
        {
            if (remote.Manufacturer.Length == 0 || remote.Manufacturer != candidate.Manufacturer) return (0, "");
            if (!remote.Rotor.HasValue || !candidate.Rotor.HasValue || remote.Rotor != candidate.Rotor) return (0, "");

            if (remote.MtowKg.HasValue && candidate.MtowKg.HasValue)
            {
                double ratio = Ratio(remote.MtowKg.Value, candidate.MtowKg.Value);
                return ratio <= FamilyMaxMtowRatio
                    ? (SameManufacturerBonus, $"same manufacturer ({remote.Manufacturer}), same category, weight within {ratio:0.0}x")
                    : (0, "");
            }
            if (remote.SpanM.HasValue && candidate.SpanM.HasValue)
            {
                double ratio = Ratio(remote.SpanM.Value, candidate.SpanM.Value);
                return ratio <= FamilyMaxSpanRatio
                    ? (SameManufacturerBonus, $"same manufacturer ({remote.Manufacturer}), same category, span within {ratio:0.0}x")
                    : (0, "");
            }
            return (0, "");
        }

        static double Ratio(double a, double b) => Math.Max(a, b) / Math.Min(a, b);

        static bool IsExactIcao(RankedCandidate candidate, MatchRequest request) =>
            request.IcaoType.Length > 0 && candidate.Model.icaoType.Equals(request.IcaoType, StringComparison.OrdinalIgnoreCase);

        bool IsAcceptable(RankedCandidate candidate)
        {
            if (candidate.CombinedScore < Substitution.MinMatchScore) return false;
            bool comparable = candidate.Similarity.Confidence > 0;
            return !comparable || candidate.Similarity.Score >= MinSimilarity || candidate.AttributeScores.ContainsKey(MatchAttribute.IcaoType);
        }

        CombinedResult Accept(CombinedResult result, RankedCandidate winner, MatchRequest request, string remoteClass, string remoteWtc, MatchExplanation explanation)
        {
            Model model = winner.Model;
            MatchType type = model.icaoType.Length > 0 && request.IcaoType.Length > 0 && model.icaoType.Equals(request.IcaoType, StringComparison.OrdinalIgnoreCase) ? MatchType.Icao
                : model.classCode.Length == 3 && model.classCode == remoteClass ? MatchType.Category
                : MatchType.Auto;

            result.Trace.steps.Add($"Scoring (combined): {explanation.ModelCount} installed model(s) compared on JoinFS signals plus physical similarity; {explanation.ExcludedCount} excluded as physically implausible. " +
                $"Winner '{model.title}' / '{model.variation}' scored {winner.CombinedScore} = {winner.BaselineScore} (JoinFS signals) + {winner.SimilarityPoints} (similarity {winner.Similarity.Score:0.00} x confidence {winner.Similarity.Confidence:0.00})" +
                (winner.FamilyBonus > 0 ? $" + {winner.FamilyBonus} ({winner.FamilyReason})." : "."));

            result.Trace.topCandidates = explanation.Ranking.Take(5).Select(r => new MatchTrace.Candidate
            {
                title = r.Model.title,
                variation = r.Model.variation,
                totalScore = r.CombinedScore,
                contributions = [.. r.BaselineContributions, $"similarity {r.Similarity.Score:0.00} x confidence {r.Similarity.Confidence:0.00} (+{r.SimilarityPoints})",
                    .. (r.FamilyBonus > 0 ? new[] { $"{r.FamilyReason} (+{r.FamilyBonus})" } : [])]
            }).ToList();

            Finish(result, model, type, request, remoteClass, remoteWtc, winner.AttributeScores);
            return result;
        }

        CombinedResult NoPlausible(CombinedResult result, MatchRequest request, string remoteClass, string remoteWtc, MatchExplanation explanation)
        {
            explanation.NoPlausibleMatch = true;
            RankedCandidate best = explanation.Ranking.FirstOrDefault();
            string why = explanation.ModelCount == 0
                ? "no models are installed/scanned at all"
                : best == null
                    ? $"every installed model was excluded as physically implausible ({string.Join("; ", explanation.ExcludedGroups.Take(3).Select(g => "'" + g.Title + "': " + g.Reason))}{(explanation.ExcludedGroups.Count > 3 || explanation.ExcludedCount > 3 ? "; ..." : "")})"
                    : $"the best candidate '{best.Model.title}' reached {best.CombinedScore} (similarity {best.Similarity.Score:0.00}), below the acceptance rules";
            result.Trace.steps.Add($"Scoring (combined): no physically plausible installed model - {why}.");

            // the user's configured default for the typerole is an explicit choice: use it when it is plausible
            string requestedRole = RoleName(request.Typerole);
            if (catalog.DefaultModels.TryGetValue(request.Typerole, out string defaultKey) && catalog.Matches.TryGetValue(defaultKey, out var match))
            {
                RankedCandidate defaultRank = explanation.Ranking.FirstOrDefault(r => r.Model.title == match.title);
                if (defaultRank != null)
                {
                    result.Trace.steps.Add($"Default: using the configured default model for typerole '{requestedRole}' -> '{defaultRank.Model.title}'.");
                    Finish(result, defaultRank.Model, MatchType.Default, request, remoteClass, remoteWtc, null, MatchAttribute.Typerole);
                    return result;
                }
                result.Trace.steps.Add($"Default: the configured default for typerole '{requestedRole}' ('{match.title}') is not installed or not plausible for this aircraft.");
            }
            else
            {
                result.Trace.steps.Add($"Default: no default model is configured for typerole '{requestedRole}'.");
            }

            result.Trace.steps.Add("Last resort: refused - substituting an implausible model (for example a light single for a wide-body) is worse than showing none; JoinFS' baseline would take the first installed model.");
            Finish(result, null, MatchType.Default, request, remoteClass, remoteWtc, null);
            return result;
        }
    }
}
