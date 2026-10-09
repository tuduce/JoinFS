namespace JoinFS
{
    /// <summary>Which model-matching engine resolves remote aircraft. Append new members only.</summary>
    public enum MatchingEngine
    {
        /// <summary>The scorer JoinFS has always used (identity signals only)</summary>
        Classic = 0,
        /// <summary>Identity signals + related types + physical similarity + plausibility gate (see JoinFS.Matching.CombinedMatcher)</summary>
        New = 1
    }
}
