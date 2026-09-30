using System;
using System.Net;
using BenchmarkDotNet.Attributes;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Legacy;

namespace JoinFS.Benchmarks
{
    /// <summary>
    /// The send/receive path's hot path (docs/network-plugin-architecture.md §5): encode, route and
    /// decode of a Position update, for both protocols. Each [Benchmark] exercises exactly one plugin
    /// call - no transport I/O (SwitchableTransport.Silent), no app-layer work (NullSink) - so
    /// [MemoryDiagnoser]'s Allocated column is the plugin/core layer's own allocation, matching the
    /// design's "zero allocations per datagram in steady state" target.
    ///
    /// Two independent two-node pairs are built once in [GlobalSetup]: a legacy-only pair (no
    /// negotiation needed) and a JFP2 pair (real Hello/HelloAck handshake, bypassing MeshManager
    /// entirely via Mesh.Create() + Peers.Add() directly - JFP2 negotiation only needs
    /// a known peer, not an established mesh). Both sides call Mesh.Create() so
    /// NetworkCore.Deliver's session gate (RequiresSession) doesn't silently drop the decoded
    /// Position - see the same gate's effect in JoinFS.Tests/Legacy/LegacyPluginGoldenTests.cs's
    /// ForceSession() calls, which this mirrors via the public Create() API instead of the internal
    /// test hook, so this project needs no InternalsVisibleTo entry.
    /// </summary>
    [MemoryDiagnoser]
    public class PositionBenchmarks
    {
        PositionUpdate position;
        IdentityUpdate identity;

        NetworkCore legacyPeerCore = null!;
        LegacyPlugin legacyHubPlugin = null!;
        NodeId legacyPeerId;
        byte[] legacyPositionDatagram = null!;
        IPEndPoint legacyHubEndPoint = null!;
        // Calling a plugin's Send() directly (bypassing NetworkCore.Send) skips its
        // "meta.Sender defaults to Identity.Id when unset" auto-fill - which the encoder needs to
        // find the identity cached under host.Objects.SetIdentity(hub's own id, ...). Build it once
        // with Sender set explicitly instead of a fresh `new MessageMeta()` per call.
        MessageMeta legacySendMeta;

        NetworkCore jfp2HubCore = null!;
        NetworkCore jfp2PeerCore = null!;
        Jfp2Plugin jfp2HubPlugin = null!;
        NodeId jfp2PeerId;
        byte[] jfp2PositionDatagram = null!;
        IPEndPoint jfp2HubEndPoint = null!;
        MessageMeta jfp2SendMeta;

        [GlobalSetup]
        public void Setup()
        {
            position = new PositionUpdate
            {
                ObjectId = 1, NetTime = 12345.678, Latitude = 51.4775, Longitude = -0.4614, Altitude = 1234.5,
                Pitch = -2.5f, Bank = 10.25f, Heading = 271.75f,
                VelocityX = 1.5f, VelocityY = -0.25f, VelocityZ = 120.125f,
                AngularVelocityX = 0.01f, AngularVelocityY = -0.02f, AngularVelocityZ = 0.03f,
                AccelerationX = 0.1f, AccelerationY = 0.2f, AccelerationZ = -0.3f,
                Rudder = 0.5f, Elevator = -0.25f, Aileron = 0.125f, BrakeLeft = 1f, BrakeRight = 0f,
                Elevation = 25.5f, StaticCgToGround = 4.75f,
                StateFlags = PositionStateFlags.UserControlled,
            };
            identity = new IdentityUpdate
            {
                ObjectId = 1, IsAircraft = true, IsPlane = true, Callsign = "BAW123", Model = "Airbus A320 Neo Asobo",
                Livery = "British Airways", IcaoType = "A320", IcaoAirline = "BAW", Registration = "G-ABCD", FlightNumber = "123",
                ClassCode = "L2J", Wtc = "M", ClassCodeConfirmed = true, TypeRole = 1,
            };

            SetupLegacy();
            SetupJfp2();
        }

        static NetworkCore MakeCore(SwitchableTransport transport, IPEndPoint endPoint, IClock clock)
        {
            var core = new NetworkCore(transport, clock, NullSink.Instance) { IsOpen = true };
            core.Identity.LocalAddress = endPoint.Address;
            core.Identity.InternetAddress = endPoint.Address;
            core.Identity.Port = (ushort)endPoint.Port;
            return core;
        }

        void SetupLegacy()
        {
            legacyHubEndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6112);
            var peerEndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 6112);
            var hubTransport = new SwitchableTransport(legacyHubEndPoint);
            var peerTransport = new SwitchableTransport(peerEndPoint);
            var clock = SystemClock.Instance;

            NetworkCore legacyHubCore = MakeCore(hubTransport, legacyHubEndPoint, clock);
            legacyPeerCore = MakeCore(peerTransport, peerEndPoint, clock);
            hubTransport.Peer = legacyPeerCore;
            peerTransport.Peer = legacyHubCore;

            legacyHubPlugin = new LegacyPlugin();
            legacyHubCore.AddPlugin(legacyHubPlugin);
            legacyPeerCore.AddPlugin(new LegacyPlugin());

            legacyHubCore.Mesh.Create(false, 0, false, "");
            legacyPeerCore.Mesh.Create(false, 0, false, "");

            legacyPeerId = legacyPeerCore.Identity.Id;
            legacyHubCore.Peers.Add(legacyPeerId, peerEndPoint, receiveEstablished: true);
            legacyPeerCore.Peers.Add(legacyHubCore.Identity.Id, legacyHubEndPoint, receiveEstablished: true);

            legacyHubCore.Objects.SetIdentity(legacyHubCore.Identity.Id, identity);
            legacySendMeta = new MessageMeta { Sender = legacyHubCore.Identity.Id };

            byte[]? captured = null;
            hubTransport.Capture = data => captured = data;
            legacyHubPlugin.Send(legacySendMeta, position, new ReadOnlySpan<NodeId>(in legacyPeerId));
            legacyPositionDatagram = captured ?? throw new InvalidOperationException("legacy capture failed");
            hubTransport.Capture = null;

            // the timed EncodeLegacy benchmark must not also pay for legacyPeerCore's receive path
            hubTransport.Silent = true;
        }

        void SetupJfp2()
        {
            jfp2HubEndPoint = new IPEndPoint(IPAddress.Parse("10.0.1.1"), 6112);
            var peerEndPoint = new IPEndPoint(IPAddress.Parse("10.0.1.2"), 6112);
            var hubTransport = new SwitchableTransport(jfp2HubEndPoint);
            var peerTransport = new SwitchableTransport(peerEndPoint);
            var clock = new ManualClock();

            jfp2HubCore = MakeCore(hubTransport, jfp2HubEndPoint, clock);
            jfp2PeerCore = MakeCore(peerTransport, peerEndPoint, clock);
            hubTransport.Peer = jfp2PeerCore;
            peerTransport.Peer = jfp2HubCore;

            jfp2HubPlugin = new Jfp2Plugin();
            jfp2HubCore.AddPlugin(jfp2HubPlugin);
            jfp2PeerCore.AddPlugin(new Jfp2Plugin());

            jfp2HubCore.Mesh.Create(false, 0, false, "");
            jfp2PeerCore.Mesh.Create(false, 0, false, "");

            jfp2PeerId = jfp2PeerCore.Identity.Id;
            // JFP2 negotiation only needs a peer the mesh knows (so its Hellos are answered; RouteEndPoint
            // defaults to EndPoint) - no Join/Pulse round trip required. ExpireTime defaults to 0,
            // so without setting it MeshManager.Tick() (run below to drive JFP2's handshake) would
            // expire this hand-added peer on its very first tick - same fix LegacyPluginGoldenTests'
            // Stack.AddPeer applies.
            jfp2HubCore.Peers.Add(jfp2PeerId, peerEndPoint, receiveEstablished: true).ExpireTime = 1e9;
            jfp2PeerCore.Peers.Add(jfp2HubCore.Identity.Id, jfp2HubEndPoint, receiveEstablished: true).ExpireTime = 1e9;

            for (int i = 0; i < 400 && !jfp2HubPlugin.IsNegotiated(jfp2PeerId); i++)
            {
                clock.Advance(0.05);
                jfp2HubCore.Tick();
                jfp2PeerCore.Tick();
            }
            if (!jfp2HubPlugin.IsNegotiated(jfp2PeerId))
            {
                throw new InvalidOperationException("JFP2 negotiation did not complete in benchmark setup");
            }
            if (jfp2HubCore.Route(jfp2PeerId, MessageKind.Position)?.Name != "JFP2")
            {
                throw new InvalidOperationException("Position did not route over JFP2 after negotiation");
            }

            jfp2HubCore.Objects.SetIdentity(jfp2HubCore.Identity.Id, identity);
            jfp2SendMeta = new MessageMeta { Sender = jfp2HubCore.Identity.Id };
            // one real Identity send first - steady-state Position sends shouldn't also carry it
            jfp2HubPlugin.Send(jfp2SendMeta, identity, new ReadOnlySpan<NodeId>(in jfp2PeerId));

            byte[]? captured = null;
            hubTransport.Capture = data => captured = data;
            jfp2HubPlugin.Send(jfp2SendMeta, position, new ReadOnlySpan<NodeId>(in jfp2PeerId));
            jfp2PositionDatagram = captured ?? throw new InvalidOperationException("jfp2 capture failed");
            hubTransport.Capture = null;

            hubTransport.Silent = true;
        }

        [Benchmark(Baseline = true)]
        public void EncodeLegacy() => legacyHubPlugin.Send(legacySendMeta, position, new ReadOnlySpan<NodeId>(in legacyPeerId));

        [Benchmark]
        public void EncodeJfp2() => jfp2HubPlugin.Send(jfp2SendMeta, position, new ReadOnlySpan<NodeId>(in jfp2PeerId));

        [Benchmark]
        public void DecodeLegacy() => legacyPeerCore.OnDatagram(legacyHubEndPoint, legacyPositionDatagram);

        [Benchmark]
        public void DecodeJfp2() => jfp2PeerCore.OnDatagram(jfp2HubEndPoint, jfp2PositionDatagram);

        [Benchmark]
        public IProtocolPlugin? RouteLookup() => jfp2HubCore.Route(jfp2PeerId, MessageKind.Position);
    }
}
