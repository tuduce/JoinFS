using System;
using System.Collections.Generic;

namespace JoinFS.Matching
{
    /// <summary>
    /// The distinctive words of an aircraft title, to tell apart models that share a type designator ICAO folded several aircraft into
    /// (all gliders are GLID). Case and punctuation are ignored; generic words ("Glider", "Default") do not count; two neighbouring
    /// words also count as one joined word, so "ASK 21" and "ASK21" share a token.
    /// </summary>
    public static class TitleTokens
    {
        static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
        {
            "glider", "gliders", "sailplane", "segelflugzeug", "motorglider", "motor", "default", "standard", "passengers", "passenger",
            "livery", "aircraft", "plane", "version", "edition", "the", "and"
        };

        /// <summary>The single words (at least two characters, or a digit) and the joined neighbouring words of a title.</summary>
        public static IReadOnlySet<string> Of(string title)
        {
            HashSet<string> tokens = new(StringComparer.Ordinal);
            string previous = null;
            foreach (string word in Words(title))
            {
                tokens.Add(word);
                if (previous != null) tokens.Add(previous + word);
                previous = word;
            }
            return tokens;
        }

        /// <summary>How many tokens both titles have.</summary>
        public static int SharedCount(IReadOnlySet<string> first, IReadOnlySet<string> second)
        {
            int shared = 0;
            foreach (string token in first)
            {
                if (second.Contains(token)) shared++;
            }
            return shared;
        }

        static IEnumerable<string> Words(string title)
        {
            if (string.IsNullOrEmpty(title)) yield break;
            foreach (string raw in title.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string word = raw.ToLowerInvariant();
                if (GenericWords.Contains(word)) continue;
                foreach (string part in LetterAndDigitRuns(word))
                {
                    if (part.Length >= 2 || char.IsDigit(part[0])) yield return part;
                }
            }
        }

        /// <summary>"ask21" -> "ask", "21": a designation reads the same with or without a space between letters and digits.</summary>
        static IEnumerable<string> LetterAndDigitRuns(string word)
        {
            int start = 0;
            for (int i = 1; i <= word.Length; i++)
            {
                if (i == word.Length || char.IsDigit(word[i]) != char.IsDigit(word[i - 1]))
                {
                    yield return word[start..i];
                    start = i;
                }
            }
        }

        static readonly char[] Separators = BuildSeparators();

        static char[] BuildSeparators()
        {
            List<char> separators = [];
            for (char c = (char)1; c < 128; c++)
            {
                if (!char.IsLetterOrDigit(c)) separators.Add(c);
            }
            separators.Add(' ');
            return [.. separators];
        }
    }
}
