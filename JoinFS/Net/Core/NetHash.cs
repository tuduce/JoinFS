using System.Globalization;

namespace JoinFS.Net
{
    /// <summary>
    /// The string hash used across the protocol (password hashes, uuids, variable ids, login
    /// names). Values go on the wire, so the algorithm is frozen. Moved from LocalNode.
    /// </summary>
    public static class NetHash
    {
        public static uint HashString(string str)
        {
            unchecked
            {
                uint hash1 = (5381 << 16) + 5381;
                uint hash2 = hash1;
                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }
                return hash1 + (hash2 * 1566083941);
            }
        }

        /// <summary>0 for no password; never 0 for a non-empty one.</summary>
        public static uint HashPassword(string password)
        {
            if (password.Length == 0)
            {
                return 0;
            }
            uint hash = HashString(password);
            return hash == 0 ? 1 : hash;
        }

        /// <summary>A pronounceable name derived from a string (e.g. an email), like "KDF042".</summary>
        public static string GenerateName(string str)
        {
            const string consonants = "CDFGHJKLNPRSTVXZ";
            uint hash = HashString(str);
            return "" + consonants[(int)(hash & 0xf)] + consonants[(int)((hash >> 4) & 0xf)] + consonants[(int)((hash >> 8) & 0xf)]
                + ((int)(hash >> 12) % 1000).ToString("D3", CultureInfo.InvariantCulture);
        }
    }
}
