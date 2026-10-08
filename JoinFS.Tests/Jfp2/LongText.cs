using System.Text;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>
    /// Texts longer than a JFP2 field's limit (docs/jfp2/protocol.md §9.7), and what the sender
    /// must make of them, worked out here independently of WireText.
    /// </summary>
    static class LongText
    {
        /// <summary>
        /// Longer than <paramref name="limit"/> bytes: an ASCII letter, then two-byte characters, so
        /// that an even limit falls inside a character.
        /// </summary>
        public static string Over(int limit) => "a" + new string('é', limit);

        /// <summary>ASCII, longer than <paramref name="limit"/>: cut, it fills the limit exactly.</summary>
        public static string Ascii(int limit) => new('x', limit + 10);

        /// <summary>The longest run of whole characters from the start of <paramref name="s"/> that fits <paramref name="limit"/> UTF-8 bytes.</summary>
        public static string Cut(string s, int limit)
        {
            var cut = new StringBuilder();
            int bytes = 0;
            foreach (Rune rune in s.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > limit)
                {
                    break;
                }
                bytes += rune.Utf8SequenceLength;
                cut.Append(rune.ToString());
            }
            return cut.ToString();
        }

        /// <summary><paramref name="received"/> is <paramref name="sent"/> cut to <paramref name="limit"/>, and shorter than it was sent (the test text was too long).</summary>
        public static void AssertCut(string sent, int limit, string received)
        {
            Xunit.Assert.Equal(Cut(sent, limit), received);
            Xunit.Assert.True(Encoding.UTF8.GetByteCount(received) <= limit);
            Xunit.Assert.True(received.Length < sent.Length);
        }
    }
}
