using System.Globalization;

namespace JoinFS.Net
{
    /// <summary>
    /// The "NNNNN-NNNNN[-port]" obfuscated form JoinFS shows users instead of raw IPv4 addresses
    /// (hub lists, join-by-address, node names). Moved verbatim from Network.EncodeIP/DecodeIP so
    /// the protocol-neutral core doesn't depend on the legacy Network class.
    /// </summary>
    public static class AddressCodec
    {
        static uint EncodeBits(uint bits)
        {
            uint result = 0;
            for (int i = 15; i >= 0; i--)
            {
                result |= ((bits & (1 << (i * 2 + 1))) != 0) ? (uint)(1 << (i + 16)) : 0;
                result |= ((bits & (1 << (i * 2))) != 0) ? (uint)(1 << i) : 0;
            }
            return result;
        }

        static uint DecodeBits(uint bits)
        {
            uint result = 0;
            for (int i = 15; i >= 0; i--)
            {
                result |= ((bits & (1 << (i + 16))) != 0) ? (uint)(1 << (i * 2 + 1)) : 0;
                result |= ((bits & (1 << i)) != 0) ? (uint)(1 << (i * 2)) : 0;
            }
            return result;
        }

        static string FormatIP(uint ip) =>
            (ip >> 24).ToString(CultureInfo.InvariantCulture) + "." + ((ip >> 16) & 0xff).ToString(CultureInfo.InvariantCulture) + "."
            + ((ip >> 8) & 0xff).ToString(CultureInfo.InvariantCulture) + "." + (ip & 0xff).ToString(CultureInfo.InvariantCulture);

        /// <summary>"a.b.c.d[:port]" to "NNNNN-NNNNN[-port]"; returns the input unchanged if it isn't an IPv4 address.</summary>
        public static string EncodeIP(string address)
        {
            string[] colonParts = address.Split(':');
            if (colonParts.Length > 0)
            {
                string[] parts = colonParts[0].Split('.');
                if (parts.Length == 4
                    && uint.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n1)
                    && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n2)
                    && uint.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n3)
                    && uint.TryParse(parts[3], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n4)
                    && n1 <= 0xff && n2 <= 0xff && n3 <= 0xff && n4 <= 0xff)
                {
                    uint code = EncodeBits(((n1 & 0xff) << 24) + ((n2 & 0xff) << 16) + ((n3 & 0xff) << 8) + (n4 & 0xff));
                    string result = (code >> 16).ToString("D5", CultureInfo.InvariantCulture) + "-" + (code & 0xffff).ToString("D5", CultureInfo.InvariantCulture);
                    if (colonParts.Length > 1)
                    {
                        result += "-" + colonParts[1];
                    }
                    return result;
                }
            }
            return address;
        }

        /// <summary>"NNNNN-NNNNN[:port]" or "NNNNN-NNNNN-port" to "a.b.c.d[:port]"; returns the input unchanged if it isn't encoded.</summary>
        public static string DecodeIP(string address)
        {
            string[] colonParts = address.Split(':');
            if (colonParts.Length > 0)
            {
                string[] parts = colonParts[0].Split('-');
                if (parts.Length == 2
                    && uint.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n1)
                    && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n2)
                    && n1 <= 0xffff && n2 <= 0xffff)
                {
                    string result = FormatIP(DecodeBits((n1 << 16) + (n2 & 0xffff)));
                    if (colonParts.Length > 1)
                    {
                        result += ":" + colonParts[1];
                    }
                    return result;
                }
            }
            string[] dashParts = address.Split('-');
            if ((dashParts.Length == 2 || dashParts.Length == 3)
                && uint.TryParse(dashParts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint m1)
                && uint.TryParse(dashParts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint m2)
                && m1 <= 0xffff && m2 <= 0xffff)
            {
                string result = FormatIP(DecodeBits((m1 << 16) + (m2 & 0xffff)));
                if (dashParts.Length == 3)
                {
                    result += ":" + dashParts[2];
                }
                return result;
            }
            return address;
        }
    }
}
