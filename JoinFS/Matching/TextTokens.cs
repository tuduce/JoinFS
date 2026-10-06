namespace JoinFS.Matching
{
    /// <summary>The text helpers of <see cref="Substitution"/> (token match, letters-and-digits), shared with the new matcher.</summary>
    public static class TextTokens
    {
        /// <summary>True if needle appears in haystack as a standalone token (not adjacent to another letter/digit).</summary>
        public static bool ContainsToken(string haystack, string needle) => Substitution.ContainsToken(haystack, needle);

        /// <summary>Strip everything but letters and digits.</summary>
        public static string AlnumOnly(string s) => Substitution.AlnumOnly(s);
    }
}
