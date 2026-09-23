using System;
using System.Net;

namespace JoinFS.Net
{
    /// <summary>
    /// One wire protocol (legacy, JFP2, ...). A plugin owns its framing, handshake, reliability,
    /// same-protocol relaying and codecs, and converts between its wire format and canonical
    /// messages. It never sees the simulator, the UI or the app: everything it needs from the rest
    /// of the network stack comes through <see cref="IProtocolHost"/>.
    ///
    /// All methods are called on the network thread only.
    /// See docs/network-plugin-architecture.md §2.3.
    /// </summary>
    public interface IProtocolPlugin
    {
        string Name { get; }

        /// <summary>Higher wins when several plugins can carry a kind to a peer.</summary>
        int Preference { get; }

        void Attach(IProtocolHost host);

        /// <summary>This datagram is in this plugin's wire format (checked by magic byte).</summary>
        bool Accepts(ReadOnlySpan<byte> datagram);

        /// <summary>Handle an incoming datagram (already past the ban check).</summary>
        void OnDatagram(IPEndPoint from, ReadOnlySpan<byte> datagram);

        /// <summary>Periodic work: retries, handshakes, expiry. Called every network tick.</summary>
        void Tick();

        /// <summary>
        /// This plugin can deliver <paramref name="kind"/> to <paramref name="peer"/>. An invalid
        /// <paramref name="peer"/> asks about sending to a raw endpoint that is not a known node.
        /// The answer may change over time; the plugin calls <see cref="IProtocolHost.LinkChanged"/>
        /// when it does.
        /// </summary>
        bool CanCarry(NodeId peer, MessageKind kind);

        /// <summary>
        /// Encode <paramref name="message"/> once and send it: to <see cref="MessageMeta.EndPoint"/>
        /// if set (header recipient <see cref="MessageMeta.Recipient"/>), otherwise to each of
        /// <paramref name="recipients"/> via its route.
        /// </summary>
        void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage;

        /// <summary>A peer left the session: drop every piece of per-peer state.</summary>
        void OnPeerRemoved(Peer peer);

        /// <summary>The local node left the session: drop all session state.</summary>
        void OnSessionReset();
    }

    /// <summary>Optional: a plugin that can describe its per-peer link state for the UI.</summary>
    public interface IDescribesLinks
    {
        /// <summary>This plugin's status for the peer, or null when it has nothing to say (a plugin
        /// that never negotiates, like legacy, has no reason to implement this interface at all).</summary>
        PeerLinkState? DescribeLink(Peer peer);
    }

    /// <summary>
    /// Which protocol actually carries traffic to a peer, for display (docs/network-plugin-
    /// architecture.md §2.11 item 4: this replaces matching literal strings such as "JFP2"/"Legacy"
    /// in the UI). Reported by whichever plugin implements <see cref="IDescribesLinks"/>;
    /// <see cref="NetworkSnapshot"/> defaults untracked peers to <see cref="Legacy"/>, since with no
    /// negotiating plugin registered at all, legacy is the only thing a peer could be talking.
    /// </summary>
    public enum PeerLinkState
    {
        /// <summary>Definitely on the legacy wire (negotiation not started, gave up, or no
        /// negotiating plugin exists).</summary>
        Legacy,
        /// <summary>A newer protocol is trying to negotiate with this peer.</summary>
        Negotiating,
        /// <summary>Negotiation with a newer protocol completed.</summary>
        Negotiated,
    }

    public static class PeerLinkStateExtensions
    {
        /// <summary>The one place that decides the human-readable label for each state, so the GUI
        /// (SessionForm) and the CONSOLE monitor dump (Main.MonitorSessionDetails) can't drift.</summary>
        public static string ToDisplay(this PeerLinkState state) => state switch
        {
            PeerLinkState.Negotiated => "JFP2",
            PeerLinkState.Legacy => "Legacy",
            _ => "Pending",
        };
    }

    public enum NetLogLevel
    {
        /// <summary>Always shown in the monitor (legacy nodeError / MonitorEvent).</summary>
        Event,
        /// <summary>Network detail, shown when network monitoring is on (legacy nodeDebug / MonitorNetwork).</summary>
        Network,
    }

    /// <summary>The network core, as a protocol plugin sees it.</summary>
    public interface IProtocolHost
    {
        IDatagramTransport Transport { get; }
        IClock Clock { get; }
        LocalIdentity Identity { get; }
        PeerDirectory Peers { get; }
        ObjectStateCache Objects { get; }

        /// <summary>The local node is in a session (has a session id).</summary>
        bool Connected { get; }

        /// <summary>The local node asked for reduced traffic (it then also refuses to relay).</summary>
        bool LowBandwidth { get; }

        /// <summary>
        /// Relays are limited to a few distinct senders at once; returns true (and records the
        /// sender for a few seconds) if this node may relay a message from <paramref name="sender"/>.
        /// Shared by every plugin so the limit is per node, not per protocol.
        /// </summary>
        bool TryAcquireRelay(NodeId sender);

        /// <summary>Whether a relay slot may still be granted without evicting anyone.</summary>
        int RelayCount { get; }

        /// <summary>Hand a decoded message to the core (mesh, application, or onward relay).</summary>
        void Deliver<T>(in MessageMeta meta, in T message) where T : struct, IMessage;

        /// <summary>What this plugin can carry to <paramref name="peer"/> changed (invalidates routing).</summary>
        void LinkChanged(NodeId peer);

        void Log(NetLogLevel level, string text);
    }
}
