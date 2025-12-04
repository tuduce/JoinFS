using System;
using System.Text;
using System.Security.Cryptography;

namespace JoinFS.Tests
{
    /// <summary>
    /// Copy of the Program.Code method from JoinFS for testing purposes.
    /// This allows us to test the encoding/decoding logic independently.
    /// </summary>
    public static class ProgramCode
    {
        /// <summary>
        /// Cryptographically secure random number generator seeded with a key
        /// </summary>
        private class SeededCryptoRandom
        {
            private readonly byte[] seed;
            private int counter;

            public SeededCryptoRandom(int key)
            {
                seed = BitConverter.GetBytes(key);
                counter = 0;
            }

            /// <summary>
            /// Generate a deterministic but cryptographically derived random number in range [0, maxValue)
            /// </summary>
            public int Next(int maxValue)
            {
                if (maxValue <= 0)
                    throw new ArgumentOutOfRangeException(nameof(maxValue));

                // Use HMACSHA256 to derive random value from seed and counter
                using (var hmac = new HMACSHA256(seed))
                {
                    byte[] counterBytes = BitConverter.GetBytes(counter++);
                    byte[] hash = hmac.ComputeHash(counterBytes);
                    uint randomValue = BitConverter.ToUInt32(hash, 0);
                    return (int)(randomValue % (uint)maxValue);
                }
            }
        }

        public static string? Code(string? s, bool bToCode, int nKey)
        {
            if (s == null)
                return null;

            const char cLow = '!';
            const char cHigh = '~';

            const int nRange = (int)cHigh - cLow + 1;
            const string csCaesar = @"THEFIVBOXNGWZARDSJUMPQCKLY" + // Low to High, rearranged (will be nRange in length)
                                    @"mywaftvexdzoquipsblchngjrk" +
                                    @"1407329685" +
                                    @"!""#$%&'()*+,-./:;<=>?@" +
                                    @"[\]^_`{|}~";

            if (s.Length == 0)
                return s;

            StringBuilder sb = new StringBuilder(s);

            // find last valid char & trim ignored ones
            int nLast = sb.Length - 1;
            int nEnd = nLast;
            while (nEnd >= 0 && (sb[nEnd] < cLow || sb[nEnd] > cHigh))
                --nEnd;
            if (nEnd != nLast)
                sb.Remove(nEnd + 1, sb.Length - nEnd - 1);

            // find first valid char & trim ignored ones
            nLast = sb.Length - 1;
            int nStart = 0;
            while (nStart <= nLast && (sb[nStart] < cLow || sb[nStart] > cHigh))
                ++nStart;
            if (nStart != 0)
                sb.Remove(0, nStart);

            if (sb.Length == 0)
                return sb.ToString();

            nLast = sb.Length - 1;

            // if decoding, reverse-substitute
            int nChar;
            if (!bToCode)
            {
                for (int i = 0; i <= nLast; ++i)
                {
                    if (sb[i] < cLow || sb[i] > cHigh)
                        continue;

                    nChar = csCaesar.IndexOf(sb[i]);

                    if (nChar >= 0)
                        sb[i] = (char)((int)cLow + nChar);
                }
            }

            // mangle
            int nInc = 0;
            int k;
            const int nPasses = 11;
            SeededCryptoRandom rnd = new SeededCryptoRandom(nKey);
            for (int j = 0; j < nPasses; ++j)
            {
                k = (bToCode) ? j : nPasses - j - 1;
                if ((k & 1) == 0)
                { nStart = nLast; nEnd = -1; nInc = -1; }   // from last downto first
                else
                { nStart = 0; nEnd = nLast + 1; nInc = 1; }   // from first upto last

                for (int i = nStart; i != nEnd; i += nInc)
                {
                    if (sb[i] < cLow || sb[i] > cHigh)
                        continue;

                    nChar = sb[i] - cLow;

                    if (bToCode)
                        nChar = nRange - 1 - nChar;

                    if (bToCode)
                        nChar += rnd.Next(nRange - 1);
                    else
                        nChar -= rnd.Next(nRange - 1);

                    if (nChar >= nRange)
                        nChar -= nRange;
                    else if (nChar < 0)
                        nChar += nRange;

                    if (!bToCode)
                        nChar = nRange - 1 - nChar;

                    sb[i] = (char)(nChar + cLow);
                }
            }

            // if encoding, substitute
            if (bToCode)
            {
                for (int i = 0; i <= nLast; ++i)
                {
                    if (sb[i] < cLow || sb[i] > cHigh)
                        continue;

                    nChar = sb[i] - cLow;

                    sb[i] = csCaesar[nChar];
                }
            }

            return sb.ToString();
        }
    }
}
