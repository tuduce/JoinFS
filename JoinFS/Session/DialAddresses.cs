using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace JoinFS
{
    /// <summary>
    /// Turns what the user typed (a hostname, an IPv4 or IPv6 literal, with or without a port) into the
    /// ordered address list a Join dials (docs/jfp2/design-future.md §4.1, "Dial"). A hostname gives all
    /// its addresses, not just the first (an AAAA record cannot hide the A record); the IPv6 ones come
    /// first when this node has IPv6, and are left out when it has not. Pure: the resolver is passed in.
    /// </summary>
    public static class DialAddresses
    {
        public enum Failure
        {
            None,
            /// <summary>The text after the colon is not a port (<see cref="Result.BadPort"/>).</summary>
            InvalidPort,
            /// <summary>No address this node can dial.</summary>
            NoAddress,
        }

        public readonly record struct Result(List<IPEndPoint> EndPoints, Failure Failure, string BadPort);

        /// <param name="text">A hostname or literal; <c>[2001:db8::1]:6112</c>, <c>2001:db8::1</c>, <c>203.0.113.9:6112</c>, <c>hub.example</c>.</param>
        /// <param name="defaultPort">The port when the text has none.</param>
        /// <param name="hasIpv6">This node's socket serves IPv6.</param>
        /// <param name="resolve">All addresses of a hostname (empty when it does not resolve).</param>
        public static Result Parse(string text, int defaultPort, bool hasIpv6, Func<string, IReadOnlyList<IPAddress>> resolve)
        {
            text = text.Trim();
            if (!Split(text, out string host, out string portText))
            {
                return Fail(Failure.NoAddress);
            }
            int port = defaultPort;
            if (portText != null && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > ushort.MaxValue))
            {
                return new Result([], Failure.InvalidPort, portText);
            }

            List<IPAddress> addresses = [];
            if (IPAddress.TryParse(host, out IPAddress literal))
            {
                addresses.Add(literal);
            }
            else if (host.Length > 0 && !host.Contains(':'))
            {
                IReadOnlyList<IPAddress> resolved = resolve(host);
                if (resolved != null) addresses.AddRange(resolved);
            }

            List<IPEndPoint> v6 = [];
            List<IPEndPoint> v4 = [];
            foreach (IPAddress address in addresses)
            {
                IPAddress a = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
                var endPoint = new IPEndPoint(a, port);
                if (a.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    if (hasIpv6 && !v6.Contains(endPoint)) v6.Add(endPoint);
                }
                else if (a.AddressFamily == AddressFamily.InterNetwork && !v4.Contains(endPoint))
                {
                    v4.Add(endPoint);
                }
            }
            v6.AddRange(v4);
            return v6.Count > 0 ? new Result(v6, Failure.None, null) : Fail(Failure.NoAddress);
        }

        static Result Fail(Failure failure) => new([], failure, null);

        /// <summary>Host and optional port text: <c>[v6]:port</c>, <c>[v6]</c>, a bare IPv6 literal (several colons: no port), <c>host:port</c> or <c>host</c>.</summary>
        static bool Split(string text, out string host, out string port)
        {
            host = text;
            port = null;
            if (text.StartsWith('['))
            {
                int close = text.IndexOf(']');
                if (close < 0)
                {
                    return false;
                }
                host = text[1..close];
                string rest = text[(close + 1)..];
                if (rest.Length > 0)
                {
                    if (rest[0] != ':')
                    {
                        return false;
                    }
                    port = rest[1..];
                }
                // only an address literal goes in brackets
                return IPAddress.TryParse(host, out _);
            }
            int colons = text.Split(':').Length - 1;
            if (colons == 1)
            {
                int colon = text.IndexOf(':');
                host = text[..colon];
                port = text[(colon + 1)..];
            }
            return true;
        }
    }
}
