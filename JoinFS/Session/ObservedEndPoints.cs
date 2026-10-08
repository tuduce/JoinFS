using JoinFS.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace JoinFS
{
    /// <summary>How the network between this node and the internet treats its UDP port, as the observations show it.</summary>
    public enum NatClass
    {
        /// <summary>No public observation yet.</summary>
        Unknown,
        /// <summary>Peers see our own interface address at the bound port: no NAT.</summary>
        Public,
        /// <summary>Peers see another address, at the bound port (a NAT that keeps the port, or a port forward).</summary>
        PortPreserving,
        /// <summary>Peers see another port, the same for every peer.</summary>
        Translated,
        /// <summary>Different peers see different ports or addresses: this node depends on relays (typical of CGNAT).</summary>
        EndpointDependent,
    }

    /// <summary>
    /// Where neighbors say this node's datagrams come from (the JFP2 HelloAck's ObservedEndPoint,
    /// docs/jfp2-wire-design.md §7.3), and what that says about the NAT in front of it. Plain C# on
    /// the app thread, fed by <see cref="NetBootstrap"/> from <see cref="NetworkEventKind.EndPointObserved"/>.
    ///
    /// Keeps the latest observation of every neighbor (bounded by the peer count: the plugin reports an
    /// observation only when it changes, so one dropped here would never come back), classifies from
    /// the <see cref="MaxReporters"/> most recent, and ignores what a
    /// neighbor on our own network sees (a private or local address, or another address in our /24),
    /// since that says nothing about the way to the internet. The result is only logged: nothing uses
    /// it yet, and the public address stays the one the HTTP lookup gives.
    /// </summary>
    public sealed class ObservedEndPoints
    {
        public const int MaxReporters = 8;

        /// <summary>The latest public observation of each reporter, the least recently updated first; the classification rests on the last <see cref="MaxReporters"/>.</summary>
        readonly List<(NodeId Reporter, IPEndPoint EndPoint)> latest = [];

        int reportersLogged;

        public NatClass Class { get; private set; }

        /// <summary>The distinct endpoints peers see us at, in order.</summary>
        public IReadOnlyList<IPEndPoint> EndPoints { get; private set; } = [];

        /// <summary>How many reporters the classification rests on.</summary>
        public int Reporters => Math.Min(latest.Count, MaxReporters);

        /// <summary>
        /// <paramref name="reporter"/> sees us at <paramref name="observed"/>. True when the class or the
        /// endpoints seen changed (what is worth a log line). <paramref name="localAddress"/> is our
        /// interface address and <paramref name="boundPort"/> the port our socket is bound to.
        /// </summary>
        public bool Observe(NodeId reporter, IPEndPoint observed, IPAddress localAddress, ushort boundPort)
        {
            Remove(reporter);
            if (observed != null)
            {
                observed = Normalize(observed);
                if (!IsLan(observed.Address, localAddress))
                {
                    latest.Add((reporter, observed));
                }
            }
            return Classify(localAddress, boundPort);
        }

        /// <summary><paramref name="reporter"/> left: its observation no longer counts. True when that changed the result.</summary>
        public bool Forget(NodeId reporter, IPAddress localAddress, ushort boundPort) =>
            Remove(reporter) && Classify(localAddress, boundPort);

        /// <summary>The log line: the class and the endpoints seen, next to the address the HTTP lookup gave.</summary>
        public string Describe(IPAddress httpAddress, ushort boundPort) =>
            "NAT: " + Class + " - " + (EndPoints.Count == 0 ? "no public observation" : "peers see this node at " + string.Join(", ", EndPoints))
            + " (" + Reporters + " of up to " + MaxReporters + " peers); HTTP address " + httpAddress + ", bound port " + boundPort;

        bool Remove(NodeId reporter)
        {
            int index = latest.FindIndex(o => o.Reporter == reporter);
            if (index < 0)
            {
                return false;
            }
            latest.RemoveAt(index);
            return true;
        }

        bool Classify(IPAddress localAddress, ushort boundPort)
        {
            List<IPEndPoint> endPoints = latest.Skip(latest.Count - Reporters).Select(o => o.EndPoint).Distinct()
                .OrderBy(e => e.Address.ToString()).ThenBy(e => e.Port).ToList();
            NatClass natClass;
            if (endPoints.Count == 0)
            {
                natClass = NatClass.Unknown;
            }
            else if (endPoints.Count > 1)
            {
                natClass = NatClass.EndpointDependent;
            }
            else if (endPoints[0].Port != boundPort)
            {
                natClass = NatClass.Translated;
            }
            else
            {
                natClass = endPoints[0].Address.Equals(Normalize(localAddress)) ? NatClass.Public : NatClass.PortPreserving;
            }
            bool changed = natClass != Class || !endPoints.SequenceEqual(EndPoints) || Reporters != reportersLogged;
            reportersLogged = Reporters;
            Class = natClass;
            EndPoints = endPoints;
            return changed;
        }

        static IPEndPoint Normalize(IPEndPoint endPoint) =>
            endPoint.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(endPoint.Address.MapToIPv4(), endPoint.Port) : endPoint;

        static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        /// <summary>
        /// An address only a neighbor on our own network would see: private, shared (CGNAT), loopback,
        /// link-local, unspecified; or another address in our /24. Our own interface address counts
        /// as public: a node whose interface has a public address is seen there from everywhere.
        /// </summary>
        public static bool IsLan(IPAddress address, IPAddress localAddress)
        {
            address = Normalize(address);
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal
                    || address.Equals(IPAddress.IPv6Any);
            }
            byte[] b = address.GetAddressBytes();
            if (b[0] == 10 || b[0] == 127 || b[0] == 0
                || (b[0] == 172 && (b[1] & 0xF0) == 16)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && (b[1] & 0xC0) == 64))
            {
                return true;
            }
            localAddress = localAddress == null ? null : Normalize(localAddress);
            if (localAddress == null || localAddress.AddressFamily != AddressFamily.InterNetwork || address.Equals(localAddress))
            {
                return false;
            }
            byte[] l = localAddress.GetAddressBytes();
            return b[0] == l[0] && b[1] == l[1] && b[2] == l[2];
        }
    }
}
