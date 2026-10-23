using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using JoinFS.Net.Jfp2;

namespace JoinFS.Net
{
    // Asking a node that is not a member (a hub, an address book entry): over JFP2 first, through a non-member session, and over legacy
    // after a short silence, the way a join is (docs/network/jfp2/protocol.md §5.12, §5.16; plan D4). An address that took no JFP2
    // conversation is remembered with the joins' memory (legacyOnly), and joined, polled and asked over legacy at once.
    public sealed partial class MeshManager
    {
        /// <summary>How long a node has to open the conversation before the request goes over legacy (the join's 2 s).</summary>
        const double AskSilence = 2.0;

        /// <summary>How long a request to a node that legacy cannot reach (an IPv6 endpoint) is held for the conversation to open.</summary>
        const double AskGiveUp = 7.0;

        /// <summary>The requests to one node waiting for its conversation to open: the newest of each kind.</summary>
        sealed class PendingAsk(IPEndPoint endPoint, double since)
        {
            public readonly IPEndPoint EndPoint = endPoint;
            public readonly double Since = since;
            public readonly Dictionary<MessageKind, (Func<IDirectoryLink, bool> Send, Action Legacy)> Messages = [];
        }

        readonly Dictionary<IPEndPoint, PendingAsk> asks = [];

        /// <summary>
        /// A request to the node at <paramref name="endPoint"/>, which may not be a member: sent over an open directory link to it, else held
        /// while the link opens, else over legacy. A member, an address that took no JFP2 conversation before and a node that cannot be
        /// reached over JFP2 at all are asked as <see cref="NetworkCore.SendToEndPoint"/> asks.
        /// </summary>
        public void Ask<T>(IPEndPoint endPoint, in T message, bool guaranteed) where T : struct, IMessage
        {
            endPoint = WireEndPoint.Normalize(endPoint);
            IDirectoryLink link = core.Plugins.OfType<IDirectoryLink>().FirstOrDefault();
            bool v6 = endPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            if (link == null || !core.IsOpen || !core.Identity.Ready || core.Peers.FindByEndPoint(endPoint) != null || (!v6 && IsLegacyOnly(endPoint)))
            {
                core.SendToEndPoint(endPoint, message, guaranteed);
                return;
            }
            if (v6 ? !core.Identity.HasIPv6 : core.Identity.NoIPv4)
            {
                return; // this node has no address in that family: there is no way to send
            }
            if (link.TrySend(endPoint, message))
            {
                return;
            }
            T copy = message;
            if (!asks.TryGetValue(endPoint, out PendingAsk pending))
            {
                if (asks.Count >= 64)
                {
                    return; // bounded: a hub list can name many hubs
                }
                asks[endPoint] = pending = new PendingAsk(endPoint, Now);
            }
            pending.Messages[T.Kind] = (l => l.TrySend(endPoint, copy), () => core.SendToEndPoint(endPoint, copy, guaranteed));
            if (link.Introduce(endPoint))
            {
                core.Log(NetLogLevel.Network, "NETWORK: asking " + endPoint + " over JFP2 (" + T.Kind + "), legacy if it does not answer within " + AskSilence + " s");
            }
        }

        /// <summary>Called every tick: send what waited for a conversation that opened, and hand on what waited too long.</summary>
        internal void DoAsks()
        {
            if (asks.Count == 0)
            {
                return;
            }
            IDirectoryLink link = core.Plugins.OfType<IDirectoryLink>().FirstOrDefault();
            double now = Now;
            foreach (PendingAsk pending in asks.Values.ToList())
            {
                bool v6 = pending.EndPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
                if (link != null && link.HasLink(pending.EndPoint))
                {
                    asks.Remove(pending.EndPoint);
                    foreach (var (send, legacy) in pending.Messages.Values)
                    {
                        if (!send(link))
                        {
                            legacy(); // the node did not agree the class: it is an older build
                        }
                    }
                }
                else if (!v6 && now - pending.Since >= AskSilence)
                {
                    asks.Remove(pending.EndPoint);
                    RememberLegacyOnly(pending.EndPoint, LegacyOnlyMemory);
                    core.Log(NetLogLevel.Event, "NETWORK: " + pending.EndPoint + " takes no conversation over JFP2 - asking it over legacy, and remembering that for " + LegacyOnlyMemory / 3600 + " h");
                    foreach (var (_, legacy) in pending.Messages.Values)
                    {
                        legacy();
                    }
                }
                else if (v6 && now - pending.Since >= AskGiveUp)
                {
                    asks.Remove(pending.EndPoint);
                    core.Log(NetLogLevel.Network, "NETWORK: " + pending.EndPoint + " did not answer over JFP2 - legacy cannot reach an IPv6 address, so nothing is sent");
                }
            }
        }

        bool IsLegacyOnly(IPEndPoint endPoint) => legacyOnly.TryGetValue(endPoint, out double until) && Now < until;

        /// <summary>
        /// The address takes no conversation over JFP2 for <paramref name="seconds"/>: joins and requests to it go over legacy at once. The app
        /// keeps the memory across runs (<see cref="NetworkEventKind.LegacyOnly"/>) and gives it back with <see cref="RestoreLegacyOnly"/>.
        /// </summary>
        void RememberLegacyOnly(IPEndPoint endPoint, double seconds)
        {
            endPoint = new IPEndPoint(endPoint.Address, endPoint.Port);
            legacyOnly[endPoint] = Now + seconds;
            core.Raise(new NetworkEvent { Kind = NetworkEventKind.LegacyOnly, EndPoint = endPoint, Seconds = seconds });
        }

        /// <summary>The memory of an earlier run (an address and the seconds of it that are left), put back at start.</summary>
        public void RestoreLegacyOnly(IPEndPoint endPoint, double seconds)
        {
            if (seconds > 0)
            {
                legacyOnly[WireEndPoint.Normalize(new IPEndPoint(endPoint.Address, endPoint.Port))] = Now + seconds;
            }
        }
    }
}
