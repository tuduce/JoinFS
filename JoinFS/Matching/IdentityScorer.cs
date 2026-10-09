using System;
using System.Collections.Generic;
using JoinFS.Matching;
using Model = JoinFS.Substitution.Model;
using MatchAttribute = JoinFS.Substitution.MatchAttribute;
using MatchType = JoinFS.Substitution.Type;
using MatchTrace = JoinFS.Substitution.MatchTrace;

namespace JoinFS.Matching
{
    /// <summary>One candidate's identity score with the per-attribute breakdown Explain Match shows.</summary>
    public sealed class CandidateScore
    {
        public int Score;
        public Dictionary<MatchAttribute, int> AttributeScores = [];
        public List<string> Contributions = [];
    }

    /// <summary>
    /// JoinFS' identity signals (ICAO type, airline, class code, WTC, typerole, registration, livery, title prefix) for the new matcher.
    /// The formula is <c>Substitution.ScoreCandidate</c> itself - there is one copy of the weights - called with the typerole as a weak hint.
    /// </summary>
    public static class IdentityScorer
    {
        public static CandidateScore Score(Model candidate, MatchRequest request, string remoteClassCode, string remoteWtc, string registrationAlnum, bool typeroleIsWeakHint)
        {
            Substitution.ScoreCandidate(candidate, request.IcaoType, remoteClassCode, remoteWtc, request.IcaoAirline, request.Registration, registrationAlnum,
                request.Livery, request.Typerole, request.Title, out int score, out var attributeScores, out var contributions,
                request.IcaoAirlineGuessed, typeroleIsWeakHint);
            return new CandidateScore { Score = score, AttributeScores = attributeScores, Contributions = contributions };
        }

        /// <summary>The remote's class code/WTC: the sender's own, else looked up from the ICAO type (as <c>Substitution.Match</c> does).</summary>
        public static (string classCode, string wtc) ResolveRemoteClass(MatchRequest request, Doc8643 doc8643)
        {
            string classCode = request.ClassCode, wtc = request.Wtc;
            if (classCode.Length == 0 && request.IcaoType.Length > 0 && doc8643.TryGet(request.IcaoType, out var row))
            {
                classCode = row.ClassCode;
                wtc = row.Wtc;
            }
            return (classCode, wtc);
        }

        /// <summary>Fill the requested-vs-matched attribute grid of the trace (the same rows as <c>Substitution.Match</c>' Finalize).</summary>
        public static void FillAttributes(MatchTrace trace, Model matched, MatchRequest request, string remoteClassCode, string remoteWtc, string requestedRoleName,
            string matchedRoleName, Dictionary<MatchAttribute, int> attributeScores, params MatchAttribute[] decisiveAttrs)
        {
            bool matchedIsGuessed = matched != null && matched.icaoGuessed;
            bool matchedAirlineIsGuessed = matched != null && (matched.icaoAirlineGuessed || request.IcaoAirlineGuessed);

            void Add(MatchAttribute attr, string requested, string matchedValue)
            {
                int contribution = attributeScores != null ? attributeScores.GetValueOrDefault(attr) : 0;
                trace.attributes.Add(new MatchTrace.AttributeComparison
                {
                    attribute = attr,
                    requested = requested,
                    matched = matchedValue,
                    decisive = attributeScores != null ? contribution > 0 : Array.IndexOf(decisiveAttrs, attr) >= 0,
                    scoreContribution = contribution,
                    wasDownweighted = contribution > 0 && (
                        (matchedIsGuessed && attr is MatchAttribute.IcaoType or MatchAttribute.ClassCode or MatchAttribute.Wtc) ||
                        (matchedAirlineIsGuessed && attr == MatchAttribute.IcaoAirline))
                });
            }

            Add(MatchAttribute.Title, request.Title, matched?.title ?? "");
            Add(MatchAttribute.Livery, request.Livery, matched?.variation ?? "");
            Add(MatchAttribute.Registration, request.Registration, matched?.atcId ?? "");
            Add(MatchAttribute.IcaoType, request.IcaoType, matched?.icaoType ?? "");
            Add(MatchAttribute.IcaoAirline, request.IcaoAirline, matched?.icaoAirline ?? "");
            Add(MatchAttribute.ClassCode, remoteClassCode, matched?.classCode ?? "");
            Add(MatchAttribute.Wtc, remoteWtc, matched?.wtc ?? "");
            Add(MatchAttribute.Typerole, requestedRoleName, matchedRoleName);
            Add(MatchAttribute.Folder, "", matched?.folder ?? "");
        }
    }
}
