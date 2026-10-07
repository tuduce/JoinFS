using System.Buffers.Binary;
using System.Text;
using JoinFS.Net.Jfp2.Codecs;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>
    /// WireText, the u16-prefixed UTF-8 strings of the JFP2 codecs: a text longer than its field's
    /// limit is cut at a character boundary (docs/jfp2-wire-design.md §4.6).
    /// </summary>
    public class WireTextTests
    {
        static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        static (int Written, ushort Prefix, byte[] Text) Write(string s, int limit)
        {
            byte[] buffer = new byte[2 + 4096];
            int written = WireText.WriteString(buffer, s, limit);
            ushort prefix = BinaryPrimitives.ReadUInt16LittleEndian(buffer);
            return (written, prefix, buffer.AsSpan(2, written - 2).ToArray());
        }

        /// <summary>Characters of 2, 3 and 4 UTF-8 bytes (the last a surrogate pair in .NET) straddling the limit.</summary>
        [Theory]
        [InlineData("abcdefg" + "é", 8, "abcdefg")]           // é is 2 bytes: 9 in all, the limit falls inside it
        [InlineData("abcdef" + "€€", 8, "abcdef")]            // € is 3 bytes
        [InlineData("abc" + "\U0001F600\U0001F600", 8, "abc\U0001F600")] // 4 bytes each: 3 + 4 fit, the second does not
        [InlineData("ab" + "\U0001F600\U0001F600", 8, "ab\U0001F600")]
        [InlineData("éééé" + "é", 8, "éééé")]                 // exactly at the limit, then one more
        public void LongText_IsCutAtACharacterBoundary(string text, int limit, string expected)
        {
            var (written, prefix, bytes) = Write(text, limit);

            Assert.True(prefix <= limit);
            Assert.Equal(2 + prefix, written);
            Assert.Equal(expected, StrictUtf8.GetString(bytes)); // valid UTF-8: throws otherwise
            Assert.Equal(LongText.Cut(text, limit), expected);
            Assert.Equal(written, WireText.MeasureString(text, limit));
        }

        [Fact]
        public void LongText_OfManyCharacters_IsCutToTheLongestWholePrefix()
        {
            string text = LongText.Over(256);

            var (written, prefix, bytes) = Write(text, 256);

            Assert.Equal(255, prefix); // "a" and 127 two-byte characters; the 128th would need byte 257
            Assert.Equal(LongText.Cut(text, 256), StrictUtf8.GetString(bytes));
            Assert.Equal(written, WireText.MeasureString(text, 256));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("KSEA")]
        [InlineData("12345678")] // exactly the limit
        public void TextWithinTheLimit_IsWrittenWhole(string? text)
        {
            var (written, prefix, bytes) = Write(text!, 8);

            Assert.Equal(Encoding.UTF8.GetByteCount(text ?? ""), prefix);
            Assert.Equal(text ?? "", Encoding.UTF8.GetString(bytes));
            Assert.Equal(written, WireText.MeasureString(text!, 8));
        }

        /// <summary>Before the limits a text over 65,535 bytes wrapped the u16 length and desynchronised the rest of the message.</summary>
        [Fact]
        public void TextLongerThanTheLengthPrefixCanSay_IsCutToTheLimit()
        {
            string text = new('x', 70_000);

            var (written, prefix, _) = Write(text, WireText.MaxLimit);

            Assert.Equal(WireText.MaxLimit, prefix);
            Assert.Equal(2 + WireText.MaxLimit, written);
        }

        [Fact]
        public void ListAndSpanOverloads_WriteTheSameBytes()
        {
            string text = LongText.Over(512);
            var list = new List<byte>();

            WireText.WriteString(list, text, 512);

            byte[] span = new byte[1024];
            int written = WireText.WriteString(span, text, 512);
            Assert.Equal(span.AsSpan(0, written).ToArray(), list.ToArray());
        }

        [Fact]
        public void LimitAboveTheLargestPayload_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WireText.WriteString(new byte[4096], "x", WireText.MaxLimit + 1));
        }

        [Fact]
        public void DestinationTooSmallForTheText_Throws()
        {
            Assert.Throws<ArgumentException>(() => WireText.WriteString(new byte[2 + 4], "abcdef", 8));
            Assert.Equal(2 + 3, WireText.WriteString(new byte[2 + 4], "abc", 8)); // a short text fits a small destination
        }

        /// <summary>A lone surrogate is not text: it goes out as U+FFFD, as Encoding.UTF8 writes it, and counts as 3 bytes.</summary>
        [Fact]
        public void LoneSurrogate_IsWrittenAsTheReplacementCharacter()
        {
            var (written, _, bytes) = Write("ab\uD83D", 8);

            Assert.Equal("ab�", StrictUtf8.GetString(bytes));
            Assert.Equal(written, WireText.MeasureString("ab\uD83D", 8));
        }
    }
}
