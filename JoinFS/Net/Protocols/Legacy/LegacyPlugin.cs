using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace JoinFS.Net.Legacy
{
    /// <summary>
    /// The legacy JoinFS wire protocol as a plugin: the 21-byte header framing, guaranteed delivery,
    /// FLAG_FORWARD relaying, and a codec for every legacy message (mesh and application). The wire
    /// is frozen - byte-for-byte what released versions send, pinned by the golden fixtures in
    /// JoinFS.Tests/Legacy - but the internals are free: this replaces the transport half of
    /// Node.cs and the serialization half of Network.cs.
    ///
    /// Legacy is the baseline protocol: every node speaks it, so it can carry every kind to every
    /// node (including unknown endpoints); newer plugins win for the kinds they negotiate.
    /// </summary>
    public sealed class LegacyPlugin : IProtocolPlugin
    {
        IProtocolHost host;
        LegacyReliability reliability;
        readonly ushort firstGuaranteedId;

        // send side: one message is prepared at a time and committed to every target
        readonly MemoryStream sendStream = new(1024);
        readonly BinaryWriter writer;
        readonly Encoder encoder;
        readonly List<NodeId> targets = [];
        MessageMeta sendMeta;
        bool sendGuaranteed;
        ushort sendGuaranteedId;
        NodeId sendSender;

        // receive side
        readonly MemoryStream receiveStream = new(1024);
        readonly BinaryReader reader;
        byte[] receiveBuffer = new byte[1024];

        public LegacyPlugin(ushort firstGuaranteedId = 1)
        {
            this.firstGuaranteedId = firstGuaranteedId;
            writer = new BinaryWriter(sendStream);
            reader = new BinaryReader(receiveStream);
            encoder = new Encoder(this);
        }

        public string Name => "Legacy";
        public int Preference => 0;

        public int GuaranteedOutCount => reliability?.OutgoingCount ?? 0;
        public int GuaranteedInCount => reliability?.IncomingCount ?? 0;

        public void Attach(IProtocolHost host)
        {
            this.host = host;
            reliability = new LegacyReliability(host, firstGuaranteedId);
        }

        public bool Accepts(ReadOnlySpan<byte> datagram) => datagram[0] == LegacyWire.VersionLowByte;

        /// <summary>Everything, to everyone - except Identity, which legacy carries inline in positions.</summary>
        public bool CanCarry(NodeId peer, MessageKind kind) => kind != MessageKind.Identity;

        public void Tick() => reliability.Tick();

        public void OnPeerRemoved(Peer peer) => reliability.RemovePeer(peer);

        public void OnSessionReset() => reliability.Clear();

        // ================================================================== send

        public void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage
        {
            sendMeta = meta;
            targets.Clear();
            foreach (NodeId recipient in recipients) targets.Add(recipient);
            message.Dispatch(encoder, meta);
        }

        BinaryWriter BeginHeader(byte flags, bool guaranteed)
        {
            sendGuaranteed = guaranteed;
            sendGuaranteedId = guaranteed ? reliability.NextId() : (ushort)0;
            if (guaranteed) flags |= LegacyWire.FlagGuaranteed;
            NodeId sender = sendMeta.Sender.Valid() ? sendMeta.Sender : host.Identity.Id;
            sendSender = sender;
            // a message sent on another node's behalf (protocol translation at a hub) is shaped like
            // a legacy relay: original sender in the header, Forward flag set
            if (sender != host.Identity.Id) flags |= LegacyWire.FlagForward;
            sendStream.SetLength(0);
            writer.Write(LegacyWire.Version);
            writer.Write(flags);
            writer.Write(sendGuaranteedId);
            writer.Write((byte)0);
            writer.Write((byte)1);
            sender.Write(writer);
            // recipient: patched per target in Commit
            sendMeta.Recipient.Write(writer);
            return writer;
        }

        BinaryWriter BeginInternal(LegacyWire.InternalId id, bool guaranteed)
        {
            BeginHeader(LegacyWire.FlagInternal, guaranteed);
            writer.Write((short)id);
            return writer;
        }

        BinaryWriter BeginApp(LegacyWire.AppId id, bool guaranteed)
        {
            BeginHeader(0, guaranteed);
            writer.Write(LegacyWire.DataVersion);
            writer.Write((short)id);
            return writer;
        }

        /// <summary>Send the prepared message to the explicit endpoint, or to each target via its route.</summary>
        void Commit()
        {
            writer.Flush();
            byte[] data = sendStream.GetBuffer();
            int length = (int)sendStream.Length;
            if (sendMeta.EndPoint != null)
            {
                // header recipient as prepared
                IPEndPoint endPoint = sendMeta.EndPoint;
                if (sendGuaranteed)
                {
                    IPEndPoint queueEndPoint = sendMeta.Recipient.Valid() && host.Peers.TryGet(sendMeta.Recipient, out Peer known) ? known.RouteEndPoint : endPoint;
                    reliability.Send(sendGuaranteedId, sendSender, sendMeta.Recipient, queueEndPoint, data.AsSpan(0, length));
                }
                else
                {
                    host.Transport.Send(endPoint, data.AsSpan(0, length));
                }
                return;
            }
            foreach (NodeId target in targets)
            {
                target.Write(data.AsSpan(LegacyWire.RecipientOffset));
                IPEndPoint endPoint = host.Peers.TryGet(target, out Peer peer) ? peer.RouteEndPoint : host.Identity.MakeEndPoint(target, target.port);
                if (sendGuaranteed)
                {
                    reliability.Send(sendGuaranteedId, sendSender, target, endPoint, data.AsSpan(0, length));
                }
                else
                {
                    host.Transport.Send(endPoint, data.AsSpan(0, length));
                }
            }
        }

        static void WritePosition(BinaryWriter w, in PositionUpdate p)
        {
            w.Write(p.Latitude);
            w.Write(p.Longitude);
            w.Write(p.Altitude);
            w.Write(p.Pitch);
            w.Write(p.Bank);
            w.Write(p.Heading);
            w.Write(p.VelocityX);
            w.Write(p.VelocityY);
            w.Write(p.VelocityZ);
            w.Write(p.AngularVelocityX);
            w.Write(p.AngularVelocityY);
            w.Write(p.AngularVelocityZ);
            w.Write(p.AccelerationX);
            w.Write(p.AccelerationY);
            w.Write(p.AccelerationZ);
            w.Write(LegacyWire.ConvertToAxis(p.Rudder));
            w.Write(LegacyWire.ConvertToAxis(p.Elevator));
            w.Write(LegacyWire.ConvertToAxis(p.Aileron));
            w.Write(LegacyWire.ConvertToAxis(p.BrakeLeft));
            w.Write(LegacyWire.ConvertToAxis(p.BrakeRight));
            w.Write(p.Elevation);
            w.Write(GroundFlags(p.StateFlags));
        }

        static byte GroundFlags(PositionStateFlags flags)
        {
            byte ground = 0;
            if ((flags & PositionStateFlags.OnGround) != 0) ground |= 0x01;
            if ((flags & PositionStateFlags.ElevationCorrection) != 0) ground |= 0x02;
            return ground;
        }

        static PositionStateFlags FromGroundFlags(byte ground)
        {
            PositionStateFlags flags = PositionStateFlags.None;
            if ((ground & 0x01) != 0) flags |= PositionStateFlags.OnGround;
            if ((ground & 0x02) != 0) flags |= PositionStateFlags.ElevationCorrection;
            return flags;
        }

        static void WriteNote(BinaryWriter w, in CommsNote note, bool allNotesLength)
        {
            w.Write((byte)0);
            w.Write(note.NoteId);
            w.Write(LegacyWire.NoteTypeComms);
            w.Write(LegacyWire.CommsExpire);
            // two historical formulas for a length field nobody reads for comms notes; kept for byte fidelity
            w.Write((ushort)(allNotesLength ? 2 + note.Text.Length + 2 : 4 + 2 + 1 + note.Text.Length));
            w.Write(note.Age);
            w.Write(note.Channel);
            w.Write(note.Text);
        }

        /// <summary>Canonical to legacy wire, one overload per message type (double dispatch from <see cref="Send"/>).</summary>
        sealed class Encoder(LegacyPlugin p) : IMessageHandler
        {
            // ---------------------------------------------------------- mesh

            public void Handle(in MessageMeta meta, in JoinRequest m)
            {
                p.BeginInternal(LegacyWire.InternalId.Join, true).Write(m.PasswordHash);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in JoinReply m)
            {
                BinaryWriter w = p.BeginInternal(LegacyWire.InternalId.JoinReply, true);
                w.Write(m.Suid);
                w.Write((ushort)m.Nodes.Count);
                foreach (KnownNode node in m.Nodes)
                {
                    node.Node.Write(w);
                    w.Write(node.Port);
                }
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in JoinFail m)
            {
                p.BeginInternal(LegacyWire.InternalId.JoinFail, true).Write((byte)m.Result);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in LoginRequest m)
            {
                BinaryWriter w = p.BeginInternal(LegacyWire.InternalId.Login, true);
                w.Write(m.Email);
                w.Write(m.PasswordHash);
                w.Write(m.Verify);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in LoginFail m)
            {
                p.BeginInternal(LegacyWire.InternalId.LoginFail, true).Write((byte)m.Result);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in AddNode m)
            {
                BinaryWriter w = p.BeginInternal(LegacyWire.InternalId.AddNode, true);
                w.Write(m.Suid);
                m.Node.Node.Write(w);
                w.Write(m.Node.Port);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in Leave m)
            {
                p.BeginInternal(LegacyWire.InternalId.Leave, false).Write(m.Suid);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in Pulse m)
            {
                BinaryWriter w = p.BeginInternal(LegacyWire.InternalId.Pulse, false);
                w.Write(m.Suid);
                w.Write(m.Time);
                w.Write(m.LowBandwidth ? LegacyWire.PulseFlagLowBandwidth : (byte)0);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in PulseResponse m)
            {
                p.BeginInternal(LegacyWire.InternalId.PulseResponse, false).Write(m.Time);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in Pathfinder m) => WriteNodeList(LegacyWire.InternalId.Pathfinder, m.Suid, m.Nodes);

            public void Handle(in MessageMeta meta, in PathfinderResponse m) => WriteNodeList(LegacyWire.InternalId.PathfinderResponse, m.Suid, m.Nodes);

            void WriteNodeList(LegacyWire.InternalId id, uint suid, List<NodeId> nodes)
            {
                BinaryWriter w = p.BeginInternal(id, false);
                w.Write(suid);
                w.Write((ushort)nodes.Count);
                foreach (NodeId node in nodes) node.Write(w);
                p.Commit();
            }

            // ---------------------------------------------------------- objects

            public void Handle(in MessageMeta meta, in PositionUpdate m)
            {
                if (!p.host.Objects.TryGetIdentity(meta.Sender, m.ObjectId, out IdentityUpdate id))
                {
                    // the legacy position carries identity inline; without it there is nothing valid to send yet
                    p.host.Log(NetLogLevel.Network, "LEGACY: no identity for " + meta.Sender + "/" + m.ObjectId + ", position not sent");
                    return;
                }
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.AircraftPosition, false);
                w.Write(m.ObjectId);
                w.Write((m.StateFlags & PositionStateFlags.UserControlled) != 0);
                w.Write(id.IsPlane);
                w.Write(id.Callsign);
                w.Write(id.Model);
                w.Write(id.TypeRole);
                w.Write((byte)((m.StateFlags & PositionStateFlags.Paused) != 0 ? 0x1 : 0x0));
                w.Write(m.NetTime);
                WritePosition(w, m);
                w.Write(id.Livery);
                w.Write(id.IcaoType);
                w.Write(id.IcaoAirline);
                w.Write(id.Registration);
                w.Write(id.FlightNumber);
                w.Write(id.ClassCode);
                w.Write(id.Wtc);
                w.Write(id.ClassCodeConfirmed);
                w.Write(m.StaticCgToGround);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in ObjectPositionUpdate m)
            {
                if (!p.host.Objects.TryGetIdentity(meta.Sender, m.ObjectId, out IdentityUpdate id))
                {
                    p.host.Log(NetLogLevel.Network, "LEGACY: no identity for " + meta.Sender + "/" + m.ObjectId + ", object position not sent");
                    return;
                }
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.ObjectPosition, false);
                w.Write(m.ObjectId);
                w.Write(id.Model);
                w.Write(id.TypeRole);
                w.Write((byte)((m.StateFlags & PositionStateFlags.Paused) != 0 ? 0x1 : 0x0));
                w.Write(m.NetTime);
                w.Write(m.Latitude);
                w.Write(m.Longitude);
                w.Write(m.Altitude);
                w.Write(m.Pitch);
                w.Write(m.Bank);
                w.Write(m.Heading);
                w.Write(m.VelocityX);
                w.Write(m.VelocityY);
                w.Write(m.VelocityZ);
                w.Write(m.AngularVelocityX);
                w.Write(m.AngularVelocityY);
                w.Write(m.AngularVelocityZ);
                w.Write(m.AccelerationX);
                w.Write(m.AccelerationY);
                w.Write(m.AccelerationZ);
                w.Write(m.Height);
                w.Write(GroundFlags(m.StateFlags));
                w.Write(id.Livery);
                w.Write(id.IcaoType);
                w.Write(id.IcaoAirline);
                w.Write(id.ClassCode);
                w.Write(id.Wtc);
                w.Write(id.ClassCodeConfirmed);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in VariableSyncUpdate m)
            {
                // one legacy message type per value kind, each chunked; the empty trailing
                // ushort is the (unused) "change times" count. The wire's Owner slot (legacy-only;
                // the canonical VariableSyncUpdate no longer carries a separate one, see its own
                // comment) gets the message's true origin, same as every other message's header.
                WriteVariables(meta.Sender, m, VariableKind.Int32, LegacyWire.AppId.IntegerVariables, LegacyWire.MaxIntegerVariables);
                WriteVariables(meta.Sender, m, VariableKind.Float32, LegacyWire.AppId.FloatVariables, LegacyWire.MaxFloatVariables);
                WriteVariables(meta.Sender, m, VariableKind.String8, LegacyWire.AppId.String8Variables, LegacyWire.MaxString8Variables);
            }

            void WriteVariables(NodeId owner, in VariableSyncUpdate m, VariableKind kind, LegacyWire.AppId id, int max)
            {
                BinaryWriter w = null;
                long countPosition = 0;
                ushort count = 0;
                foreach (VariableEntry entry in m.Entries)
                {
                    if (entry.Kind != kind)
                    {
                        continue;
                    }
                    if (w == null)
                    {
                        w = p.BeginApp(id, false);
                        owner.Write(w);
                        w.Write(m.ObjectId);
                        countPosition = w.BaseStream.Position;
                        count = 0;
                        w.Write(count);
                    }
                    w.Write(entry.Vuid);
                    switch (kind)
                    {
                        case VariableKind.Int32: w.Write(entry.IntValue); break;
                        case VariableKind.Float32: w.Write(entry.FloatValue); break;
                        default: w.Write(entry.StringValue); break;
                    }
                    if (++count >= max)
                    {
                        Finish(w, countPosition, count);
                        w = null;
                    }
                }
                if (w != null)
                {
                    Finish(w, countPosition, count);
                }
            }

            void Finish(BinaryWriter w, long countPosition, ushort count)
            {
                w.Write((ushort)0);
                w.BaseStream.Position = countPosition;
                w.Write(count);
                w.BaseStream.Position = w.BaseStream.Length;
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in EventUpdate m)
            {
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.SimEvent, true);
                w.Write(m.ObjectId);
                w.Write(m.EventId);
                w.Write(m.Data);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in RemoveObject m)
            {
                p.BeginApp(LegacyWire.AppId.RemoveObject, true).Write(m.ObjectId);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in FlightPlanUpdate m)
            {
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.FlightPlan, false);
                m.Owner.Write(w);
                w.Write(m.ObjectId);
                w.Write(LegacyWire.FlightPlanFormatVersion);
                w.Write(m.IcaoType);
                w.Write(m.Departure);
                w.Write(m.Destination);
                w.Write(m.Rules);
                w.Write(m.Route);
                w.Write(m.Remarks);
                w.Write(m.Alternate);
                w.Write(m.Speed);
                w.Write(m.Altitude);
                w.Write(m.Callsign);
                w.Write(m.Registration);
                w.Write(m.IcaoAirline);
                w.Write(m.FlightNumber);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in ShowOnRadar m)
            {
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.ShowOnRadar, true);
                m.Owner.Write(w);
                w.Write(m.ObjectId);
                w.Write(m.Show);
                p.Commit();
            }

            // ---------------------------------------------------------- peers / session

            public void Handle(in MessageMeta meta, in PeerInfo m)
            {
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.SharedData, false);
                w.Write((byte)m.Share);
                w.Write(m.Nickname);
                w.Write(m.Guid.ToByteArray());
                byte flags = 0;
                if (m.Hub) flags |= 0x01;
                if (m.Atc) flags |= 0x02;
                if (m.SimulatorConnected) flags |= 0x04;
                w.Write(flags);
                w.Write(m.AtcAirport);
                w.Write(m.AtcLevel);
                w.Write((ushort)m.AtcFrequency);
                w.Write(m.ActivityCircle);
                w.Write(m.Version);
                w.Write(m.Simulator);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in StatusRequestUpdate m)
            {
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.StatusRequest, false);
                w.Write((byte)(m.HubEnabled ? 1 : 0));
                w.Write((byte)(m.HubListRequested ? 1 : 0));
                w.Write(m.Uuid);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in StatusUpdate m)
            {
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.Status, false);
                w.Write(m.Guid.ToByteArray());
                w.Write(m.AppVersion);
                w.Write(m.Users);
                w.Write(m.AtcCount);
                if (m.AtcCount > 0)
                {
                    w.Write(m.AtcAirport);
                    w.Write((byte)m.AtcLevel);
                }
                w.Write(m.Planes);
                w.Write(m.Helicopters);
                w.Write(m.Boats);
                w.Write(m.Vehicles);
                w.Write(m.HubEnabled);
                if (m.HubEnabled)
                {
                    w.Write(m.Address);
                    w.Write(m.Name);
                    w.Write(m.About);
                    w.Write(m.Voip);
                    w.Write(m.NextEvent);
                    w.Write(m.Airport);
                    w.Write(m.ActivityCircle);
                    byte flags = 0;
                    if (m.GlobalSession) flags |= 0x02;
                    if (m.PasswordRequired) flags |= 0x04;
                    w.Write(flags);
                }
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in WeatherRequest m)
            {
                p.BeginApp(LegacyWire.AppId.WeatherRequest, true).Write(m.ObjectId);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in WeatherReply m)
            {
                p.BeginApp(LegacyWire.AppId.WeatherReply, true).Write(m.Metar);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in WeatherUpdate m)
            {
                p.BeginApp(LegacyWire.AppId.WeatherUpdate, false).Write(m.Metar);
                p.Commit();
            }

            // ---------------------------------------------------------- hub directory

            public void Handle(in MessageMeta meta, in HubList m)
            {
                for (int start = 0; start < m.Hubs.Count; start += LegacyWire.MaxHubListEntries)
                {
                    int count = Math.Min(m.Hubs.Count - start, LegacyWire.MaxHubListEntries);
                    BinaryWriter w = p.BeginApp(LegacyWire.AppId.HubList, false);
                    w.Write((ushort)count);
                    for (int i = start; i < start + count; i++)
                    {
                        m.Hubs[i].Node.Write(w);
                        w.Write(m.Hubs[i].Port);
                    }
                    p.Commit();
                }
            }

            public void Handle(in MessageMeta meta, in UserListRequest m)
            {
                p.BeginApp(LegacyWire.AppId.UserListRequest, false);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in HubUserUpdate m)
            {
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.UserList2, false);
                w.Write(m.Guid.ToByteArray());
                byte flags = 0;
                if (m.Atc) flags |= 0x01;
                if (m.Ifr) flags |= 0x02;
                w.Write(flags);
                w.Write(m.Callsign);
                w.Write(m.Nickname);
                w.Write(m.Frequency);
                w.Write(m.Latitude);
                w.Write(m.Longitude);
                w.Write(m.Altitude);
                w.Write(m.Speed);
                w.Write(m.Squawk);
                w.Write(m.Level);
                w.Write(m.Range);
                w.Write(m.Heading);
                w.Write(m.IcaoType);
                w.Write(m.Departure);
                w.Write(m.Destination);
                w.Write(m.Rules);
                w.Write(m.Route);
                w.Write(m.Remarks);
                w.Write(m.Alternate);
                w.Write(m.FlightSpeed);
                w.Write(m.FlightAltitude);
                w.Write(m.Registration);
                w.Write(m.IcaoAirline);
                w.Write(m.FlightNumber);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in UserPositionsRequest m)
            {
                p.BeginApp(LegacyWire.AppId.UserPositionsRequest, false);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in UserPositions m)
            {
                for (int start = 0; start < m.Users.Count; start += LegacyWire.MaxUserPositions)
                {
                    int count = Math.Min(m.Users.Count - start, LegacyWire.MaxUserPositions);
                    BinaryWriter w = p.BeginApp(LegacyWire.AppId.UserPositions, false);
                    w.Write((ushort)count);
                    for (int i = start; i < start + count; i++)
                    {
                        UserPosition u = m.Users[i];
                        w.Write(u.Guid.ToByteArray());
                        w.Write(u.Latitude);
                        w.Write(u.Longitude);
                        w.Write(u.Altitude);
                        w.Write(u.Speed);
                        w.Write(u.Squawk);
                        w.Write(u.Heading);
                    }
                    p.Commit();
                }
            }

            public void Handle(in MessageMeta meta, in OnlineAnnouncement m)
            {
                p.BeginApp(LegacyWire.AppId.Online, false).Write(m.Uuid);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in UserNuidRequest m)
            {
                p.BeginApp(LegacyWire.AppId.UserNuidRequest, true).Write(m.Uuid);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in UserNuidReply m)
            {
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.UserNuid, true);
                w.Write(m.Uuid);
                m.Node.Write(w);
                w.Write(m.Port);
                p.Commit();
            }

            // ---------------------------------------------------------- comms

            public void Handle(in MessageMeta meta, in CommsRequest m)
            {
                LegacyWire.AppId id = m.Scope switch
                {
                    CommsScope.Global => LegacyWire.AppId.GlobalCommsRequest,
                    CommsScope.All => LegacyWire.AppId.CommsListenRequest,
                    _ => LegacyWire.AppId.SessionCommsRequest,
                };
                p.BeginApp(id, true);
                p.Commit();
            }

            public void Handle(in MessageMeta meta, in NotesBundle m)
            {
                BinaryWriter w = p.BeginApp(LegacyWire.AppId.Notes, true);
                bool allNotesLength = m.Scope == CommsScope.All;
                foreach (NotesUser user in m.Users)
                {
                    w.Write((byte)0);
                    w.Write(user.Guid.ToByteArray());
                    w.Write(user.Nickname);
                    w.Write(user.Callsign);
                    foreach (CommsNote note in user.Notes)
                    {
                        WriteNote(w, note, allNotesLength);
                    }
                    w.Write((byte)1);
                }
                w.Write((byte)1);
                p.Commit();
            }
        }

        // ================================================================== receive

        public void OnDatagram(IPEndPoint from, ReadOnlySpan<byte> datagram)
        {
            Stats.Total.Record(datagram.Length);
            if (datagram.Length < LegacyWire.DataOffset)
            {
                return;
            }
            if (receiveBuffer.Length < datagram.Length)
            {
                receiveBuffer = new byte[Math.Max(datagram.Length, receiveBuffer.Length * 2)];
            }
            datagram.CopyTo(receiveBuffer);
            try
            {
                Receive(from, receiveBuffer, datagram.Length);
            }
            catch (Exception ex)
            {
                host.Log(NetLogLevel.Event, "ERROR: Failed to read Node message: " + ex.Message);
            }
        }

        void Receive(IPEndPoint from, byte[] data, int length)
        {
            if ((short)(data[0] | data[1] << 8) != LegacyWire.Version)
            {
                Stats.WrongVersion.Record(length);
                return;
            }
            byte flags = data[LegacyWire.FlagsOffset];
            bool direct = (flags & LegacyWire.FlagForward) == 0;
            ushort guaranteedId = (ushort)(data[LegacyWire.GuaranteedIdOffset] | data[LegacyWire.GuaranteedIdOffset + 1] << 8);
            byte guaranteedIndex = data[LegacyWire.GuaranteedIndexOffset];
            byte guaranteedCount = data[LegacyWire.GuaranteedCountOffset];
            NodeId sender = NodeId.Read(data.AsSpan(LegacyWire.SenderOffset));
            NodeId recipient = NodeId.Read(data.AsSpan(LegacyWire.RecipientOffset));
            NodeId local = host.Identity.Id;

            host.Peers.TryGet(sender, out Peer senderPeer);
            // once the sender answers us, treat its traffic as coming from its route
            IPEndPoint endPoint = senderPeer != null && senderPeer.SendEstablished ? senderPeer.RouteEndPoint : from;

            if (local.Valid() && sender == local)
            {
                return;
            }
            if (recipient != local && recipient.Valid() && local.Valid())
            {
                // an ack for a message this hub delivered on someone else's behalf (translation) ends here
                if ((flags & LegacyWire.FlagInternal) != 0 && length >= LegacyWire.DataOffset + 5
                    && (short)(data[LegacyWire.DataOffset] | data[LegacyWire.DataOffset + 1] << 8) == (short)LegacyWire.InternalId.GuaranteedDone
                    && reliability.AcknowledgeProxied(recipient, sender, (ushort)(data[LegacyWire.DataOffset + 2] | data[LegacyWire.DataOffset + 3] << 8), data[LegacyWire.DataOffset + 4]))
                {
                    return;
                }
                // for someone else: relay it unchanged (every node speaks legacy), once
                if (direct && !host.LowBandwidth && host.Peers.TryGet(recipient, out Peer target) && target.Direct && host.TryAcquireRelay(sender))
                {
                    data[LegacyWire.FlagsOffset] |= LegacyWire.FlagForward;
                    host.Transport.Send(target.EndPoint, data.AsSpan(0, length));
                    host.Log(NetLogLevel.Network, "NETWORK: Forwarded " + sender + " " + recipient);
                }
                return;
            }

            bool guaranteed = (flags & LegacyWire.FlagGuaranteed) != 0;
            if (guaranteed)
            {
                SendGuaranteedDone(sender, endPoint, guaranteedId, guaranteedIndex);
                IPEndPoint nodeEndPoint = senderPeer != null ? senderPeer.EndPoint : endPoint;
                if (!reliability.Receive(nodeEndPoint, guaranteedId, guaranteedIndex, guaranteedCount, data, length, out data, out length))
                {
                    return;
                }
            }

            var meta = new MessageMeta
            {
                Sender = sender,
                Recipient = recipient,
                EndPoint = endPoint,
                Guaranteed = guaranteed,
                Forwarded = !direct,
            };
            receiveStream.SetLength(0);
            receiveStream.Write(data, 0, length);
            receiveStream.Position = LegacyWire.DataOffset;
            if ((flags & LegacyWire.FlagInternal) != 0)
            {
                ReceiveInternal(meta, length);
            }
            else
            {
                ReceiveApp(meta, length);
            }
        }

        void SendGuaranteedDone(NodeId sender, IPEndPoint endPoint, ushort id, byte index)
        {
            Span<byte> done = stackalloc byte[LegacyWire.DataOffset + 5];
            BitConverter.TryWriteBytes(done, LegacyWire.Version);
            done[LegacyWire.FlagsOffset] = LegacyWire.FlagInternal;
            done[3] = 0; done[4] = 0; // not itself guaranteed
            done[LegacyWire.GuaranteedIndexOffset] = 0;
            done[LegacyWire.GuaranteedCountOffset] = 1;
            host.Identity.Id.Write(done[LegacyWire.SenderOffset..]);
            sender.Write(done[LegacyWire.RecipientOffset..]);
            BitConverter.TryWriteBytes(done[LegacyWire.DataOffset..], (short)LegacyWire.InternalId.GuaranteedDone);
            BitConverter.TryWriteBytes(done[(LegacyWire.DataOffset + 2)..], id);
            done[LegacyWire.DataOffset + 4] = index;
            host.Transport.Send(endPoint, done);
        }

        bool More => receiveStream.Position < receiveStream.Length;

        void ReceiveInternal(in MessageMeta meta, int length)
        {
            var id = (LegacyWire.InternalId)reader.ReadInt16();
            try
            {
                switch (id)
                {
                    case LegacyWire.InternalId.Join:
                        {
                            Stats.Join.Record(length);
                            uint hash = 0;
                            try { hash = reader.ReadUInt32(); } catch { }
                            host.Deliver(meta, new JoinRequest { PasswordHash = hash });
                        }
                        break;
                    case LegacyWire.InternalId.JoinReply:
                        {
                            Stats.JoinReply.Record(length);
                            var m = new JoinReply { Suid = reader.ReadUInt32(), Nodes = [] };
                            int count = reader.ReadInt16();
                            for (int i = 0; i < count; i++)
                            {
                                m.Nodes.Add(new KnownNode { Node = NodeId.Read(reader), Port = reader.ReadUInt16() });
                            }
                            host.Deliver(meta, m);
                        }
                        break;
                    case LegacyWire.InternalId.Leave:
                        Stats.Leave.Record(length);
                        host.Deliver(meta, new Leave { Suid = reader.ReadUInt32() });
                        break;
                    case LegacyWire.InternalId.AddNode:
                        Stats.AddNode.Record(length);
                        host.Deliver(meta, new AddNode { Suid = reader.ReadUInt32(), Node = new KnownNode { Node = NodeId.Read(reader), Port = reader.ReadUInt16() } });
                        break;
                    case LegacyWire.InternalId.Pulse:
                        Stats.Pulse.Record(length);
                        host.Deliver(meta, new Pulse
                        {
                            Suid = reader.ReadUInt32(),
                            Time = reader.ReadInt64(),
                            LowBandwidth = (reader.ReadByte() & LegacyWire.PulseFlagLowBandwidth) != 0,
                        });
                        break;
                    case LegacyWire.InternalId.PulseResponse:
                        Stats.PulseResponse.Record(length);
                        host.Deliver(meta, new PulseResponse { Time = reader.ReadInt64() });
                        break;
                    case LegacyWire.InternalId.GuaranteedDone:
                        Stats.GuaranteedDone.Record(length);
                        reliability.Acknowledge(meta.Sender, reader.ReadUInt16(), reader.ReadByte());
                        break;
                    case LegacyWire.InternalId.Pathfinder:
                    case LegacyWire.InternalId.PathfinderResponse:
                        {
                            (id == LegacyWire.InternalId.Pathfinder ? Stats.Pathfinder : Stats.PathfinderResponse).Record(length);
                            uint suid = reader.ReadUInt32();
                            int count = reader.ReadInt16();
                            List<NodeId> nodes = new(Math.Max(count, 0));
                            for (int i = 0; i < count; i++) nodes.Add(NodeId.Read(reader));
                            if (id == LegacyWire.InternalId.Pathfinder)
                                host.Deliver(meta, new Pathfinder { Suid = suid, Nodes = nodes });
                            else
                                host.Deliver(meta, new PathfinderResponse { Suid = suid, Nodes = nodes });
                        }
                        break;
                    case LegacyWire.InternalId.JoinFail:
                        Stats.JoinFail.Record(length);
                        host.Deliver(meta, new JoinFail { Result = (JoinResult)reader.ReadByte() });
                        break;
                    case LegacyWire.InternalId.Login:
                        {
                            Stats.Login.Record(length);
                            // an unreadable address is reported by the mesh as InvalidAddress
                            string email;
                            try { email = reader.ReadString(); } catch { email = ""; }
                            uint hash = More ? reader.ReadUInt32() : 0;
                            bool verify = More && reader.ReadBoolean();
                            host.Deliver(meta, new LoginRequest { Email = email, PasswordHash = hash, Verify = verify });
                        }
                        break;
                    case LegacyWire.InternalId.LoginFail:
                        Stats.LoginFail.Record(length);
                        host.Deliver(meta, new LoginFail { Result = (LoginResult)reader.ReadByte() });
                        break;
                }
            }
            catch (Exception ex)
            {
                host.Log(NetLogLevel.Event, "ERROR: Failed to read " + id + " message. " + ex.Message);
            }
        }

        string OptionalString() => More ? reader.ReadString() : "";

        void ReceiveApp(in MessageMeta meta, int length)
        {
            short dataVersion = reader.ReadInt16();
            if (dataVersion < LegacyWire.MinDataVersion)
            {
                return;
            }
            var id = (LegacyWire.AppId)reader.ReadInt16();
            try
            {
                switch (id)
                {
                    case LegacyWire.AppId.ObjectPosition: ReadObjectPosition(meta, dataVersion, length); break;
                    case LegacyWire.AppId.AircraftPosition: ReadAircraftPosition(meta, dataVersion, length); break;
                    case LegacyWire.AppId.SimEvent:
                        Stats.SimEvent.Record(length);
                        host.Deliver(meta, new EventUpdate { ObjectId = reader.ReadUInt32(), EventId = reader.ReadUInt32(), Data = reader.ReadUInt32() });
                        break;
                    case LegacyWire.AppId.WeatherRequest:
                        Stats.WeatherRequest.Record(length);
                        host.Deliver(meta, new WeatherRequest { ObjectId = More ? reader.ReadUInt32() : 0 });
                        break;
                    case LegacyWire.AppId.WeatherReply:
                        Stats.WeatherReply.Record(length);
                        host.Deliver(meta, new WeatherReply { Metar = reader.ReadString() });
                        break;
                    case LegacyWire.AppId.WeatherUpdate:
                        Stats.WeatherUpdate.Record(length);
                        host.Deliver(meta, new WeatherUpdate { Metar = reader.ReadString() });
                        break;
                    case LegacyWire.AppId.SharedData: ReadSharedData(meta, dataVersion, length); break;
                    case LegacyWire.AppId.StatusRequest:
                        {
                            Stats.StatusRequest.Record(length);
                            var m = new StatusRequestUpdate { HubEnabled = reader.ReadByte() != 0, HubListRequested = reader.ReadByte() != 0 };
                            // uuid is the last field; older peers may not send it
                            try { m.Uuid = reader.ReadUInt32(); } catch { }
                            host.Deliver(meta, m);
                        }
                        break;
                    case LegacyWire.AppId.Status: ReadStatus(meta, dataVersion, length); break;
                    case LegacyWire.AppId.HubList:
                        {
                            Stats.HubList.Record(length);
                            int count = reader.ReadUInt16();
                            var m = new HubList { Hubs = new(count) };
                            for (int i = 0; i < count; i++) m.Hubs.Add(new HubAddress { Node = NodeId.Read(reader), Port = reader.ReadUInt16() });
                            host.Deliver(meta, m);
                        }
                        break;
                    case LegacyWire.AppId.RemoveObject:
                        Stats.RemoveObject.Record(length);
                        host.Deliver(meta, new RemoveObject { ObjectId = reader.ReadUInt32() });
                        break;
                    case LegacyWire.AppId.UserListRequest:
                        Stats.UserListRequest.Record(length);
                        host.Deliver(meta, new UserListRequest());
                        break;
                    case LegacyWire.AppId.UserList: ReadUserList(meta, length); break;
                    case LegacyWire.AppId.UserList2: ReadUserList2(meta, dataVersion, length); break;
                    case LegacyWire.AppId.UserPositionsRequest:
                        Stats.UserPositionRequest.Record(length);
                        host.Deliver(meta, new UserPositionsRequest());
                        break;
                    case LegacyWire.AppId.UserPositions:
                        {
                            Stats.UserPositions.Record(length);
                            int count = reader.ReadUInt16();
                            var m = new UserPositions { Users = new(count) };
                            for (int i = 0; i < count; i++)
                            {
                                m.Users.Add(new UserPosition
                                {
                                    Guid = new Guid(reader.ReadBytes(16)),
                                    Latitude = reader.ReadSingle(),
                                    Longitude = reader.ReadSingle(),
                                    Altitude = reader.ReadUInt16(),
                                    Speed = reader.ReadUInt16(),
                                    Squawk = reader.ReadUInt16(),
                                    Heading = reader.ReadUInt16(),
                                });
                            }
                            host.Deliver(meta, m);
                        }
                        break;
                    case LegacyWire.AppId.SessionCommsRequest:
                        Stats.SessionCommsRequest.Record(length);
                        host.Deliver(meta, new CommsRequest { Scope = CommsScope.Session });
                        break;
                    case LegacyWire.AppId.GlobalCommsRequest:
                        host.Deliver(meta, new CommsRequest { Scope = CommsScope.Global });
                        break;
                    case LegacyWire.AppId.CommsListenRequest:
                        host.Deliver(meta, new CommsRequest { Scope = CommsScope.All });
                        break;
                    case LegacyWire.AppId.Notes: ReadNotes(meta, length); break;
                    case LegacyWire.AppId.UserNuidRequest:
                        Stats.UserNuidRequest.Record(length);
                        host.Deliver(meta, new UserNuidRequest { Uuid = reader.ReadUInt32() });
                        break;
                    case LegacyWire.AppId.UserNuid:
                        Stats.UserNuid.Record(length);
                        host.Deliver(meta, new UserNuidReply { Uuid = reader.ReadUInt32(), Node = NodeId.Read(reader), Port = reader.ReadUInt16() });
                        break;
                    case LegacyWire.AppId.Online:
                        Stats.Online.Record(length);
                        host.Deliver(meta, new OnlineAnnouncement { Uuid = reader.ReadUInt32() });
                        break;
                    case LegacyWire.AppId.FlightPlan: ReadFlightPlan(meta, dataVersion, length); break;
                    case LegacyWire.AppId.IntegerVariables:
                    case LegacyWire.AppId.FloatVariables:
                    case LegacyWire.AppId.String8Variables:
                        ReadVariables(meta, id, length);
                        break;
                    case LegacyWire.AppId.ShowOnRadar:
                        host.Deliver(meta, new ShowOnRadar { Owner = NodeId.Read(reader), ObjectId = reader.ReadUInt32(), Show = reader.ReadBoolean() });
                        break;
                }
            }
            catch (Exception ex)
            {
                host.Log(NetLogLevel.Event, "ERROR: Failed to read " + id + " message. " + ex.Message);
            }
        }

        void ReadAircraftPosition(in MessageMeta meta, short dataVersion, int length)
        {
            Stats.AircraftPosition.Record(length);
            uint netId = reader.ReadUInt32();
            bool user = reader.ReadBoolean();
            var id = new IdentityUpdate { ObjectId = netId, IsAircraft = true };
            id.IsPlane = reader.ReadBoolean();
            id.Callsign = reader.ReadString();
            id.Model = reader.ReadString();
            id.TypeRole = reader.ReadByte();
            byte flags = reader.ReadByte();
            var p = new PositionUpdate { ObjectId = netId, NetTime = reader.ReadDouble() };
            p.Latitude = reader.ReadDouble();
            p.Longitude = reader.ReadDouble();
            p.Altitude = reader.ReadDouble();
            p.Pitch = reader.ReadSingle();
            p.Bank = reader.ReadSingle();
            p.Heading = reader.ReadSingle();
            p.VelocityX = reader.ReadSingle();
            p.VelocityY = reader.ReadSingle();
            p.VelocityZ = reader.ReadSingle();
            p.AngularVelocityX = reader.ReadSingle();
            p.AngularVelocityY = reader.ReadSingle();
            p.AngularVelocityZ = reader.ReadSingle();
            p.AccelerationX = reader.ReadSingle();
            p.AccelerationY = reader.ReadSingle();
            p.AccelerationZ = reader.ReadSingle();
            p.Rudder = LegacyWire.ConvertFromAxis(reader.ReadInt16());
            p.Elevator = LegacyWire.ConvertFromAxis(reader.ReadInt16());
            p.Aileron = LegacyWire.ConvertFromAxis(reader.ReadInt16());
            p.BrakeLeft = LegacyWire.ConvertFromAxis(reader.ReadInt16());
            p.BrakeRight = LegacyWire.ConvertFromAxis(reader.ReadInt16());
            p.Elevation = dataVersion >= 10023 ? reader.ReadSingle() : 0.0f;
            p.StateFlags = FromGroundFlags(dataVersion >= 10023 ? reader.ReadByte() : (byte)0);
            if (user) p.StateFlags |= PositionStateFlags.UserControlled;
            if ((flags & 0x01) != 0) p.StateFlags |= PositionStateFlags.Paused;
            p.StaticCgToGround = float.NaN;
            if (netId != uint.MaxValue)
            {
                // identity fields trail the position, each added in a later version
                id.Livery = OptionalString();
                id.IcaoType = OptionalString();
                id.IcaoAirline = OptionalString();
                id.Registration = OptionalString();
                id.FlightNumber = OptionalString();
                id.ClassCode = OptionalString();
                id.Wtc = OptionalString();
                id.ClassCodeConfirmed = More && reader.ReadBoolean();
                if (More) p.StaticCgToGround = reader.ReadSingle();
                // identity before position, so a new object can be created with it
                host.Deliver(meta, id);
            }
            host.Deliver(meta, p);
        }

        void ReadObjectPosition(in MessageMeta meta, short dataVersion, int length)
        {
            Stats.ObjectPosition.Record(length);
            uint netId = reader.ReadUInt32();
            var id = new IdentityUpdate { ObjectId = netId, IsAircraft = false, Callsign = "", Registration = "", FlightNumber = "" };
            id.Model = reader.ReadString();
            id.TypeRole = reader.ReadByte();
            byte flags = reader.ReadByte();
            var p = new ObjectPositionUpdate { ObjectId = netId, NetTime = reader.ReadDouble() };
            p.Latitude = reader.ReadDouble();
            p.Longitude = reader.ReadDouble();
            p.Altitude = reader.ReadDouble();
            p.Pitch = reader.ReadSingle();
            p.Bank = reader.ReadSingle();
            p.Heading = reader.ReadSingle();
            p.VelocityX = reader.ReadSingle();
            p.VelocityY = reader.ReadSingle();
            p.VelocityZ = reader.ReadSingle();
            p.AngularVelocityX = reader.ReadSingle();
            p.AngularVelocityY = reader.ReadSingle();
            p.AngularVelocityZ = reader.ReadSingle();
            p.AccelerationX = reader.ReadSingle();
            p.AccelerationY = reader.ReadSingle();
            p.AccelerationZ = reader.ReadSingle();
            p.Height = dataVersion >= 10023 ? reader.ReadSingle() : 0.0f;
            p.StateFlags = FromGroundFlags(dataVersion >= 10023 ? reader.ReadByte() : (byte)0);
            if ((flags & 0x01) != 0) p.StateFlags |= PositionStateFlags.Paused;
            id.Livery = OptionalString();
            id.IcaoType = OptionalString();
            id.IcaoAirline = OptionalString();
            id.ClassCode = OptionalString();
            id.Wtc = OptionalString();
            id.ClassCodeConfirmed = More && reader.ReadBoolean();
            host.Deliver(meta, id);
            host.Deliver(meta, p);
        }

        void ReadVariables(in MessageMeta meta, LegacyWire.AppId id, int length)
        {
            VariableKind kind = id switch
            {
                LegacyWire.AppId.IntegerVariables => VariableKind.Int32,
                LegacyWire.AppId.FloatVariables => VariableKind.Float32,
                _ => VariableKind.String8,
            };
            (kind switch { VariableKind.Int32 => Stats.IntegerVariables, VariableKind.Float32 => Stats.FloatVariables, _ => Stats.String8Variables }).Record(length);
            NodeId wireOwner = NodeId.Read(reader);
            // Insurance for docs/network-plugin-architecture.md §2.11 item 3's redundancy claim: no
            // known sender (this codebase's, or any released build's, as far as could be confirmed)
            // sets this to anything but its own id, which meta.Sender already carries - but if one
            // ever does, this is the only place that would notice, since the canonical
            // VariableSyncUpdate no longer carries the wire's Owner field at all.
            if (wireOwner != meta.Sender)
            {
                host.Log(NetLogLevel.Event, "LEGACY: VariableSync wire Owner " + wireOwner + " disagrees with sender " + meta.Sender + " - investigate before trusting Sender here");
            }
            var m = new VariableSyncUpdate { ObjectId = reader.ReadUInt32() };
            int count = reader.ReadUInt16();
            m.Entries = new List<VariableEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var e = new VariableEntry { Vuid = reader.ReadUInt32(), Kind = kind };
                switch (kind)
                {
                    case VariableKind.Int32: e.IntValue = reader.ReadInt32(); break;
                    case VariableKind.Float32: e.FloatValue = reader.ReadSingle(); break;
                    default: e.StringValue = reader.ReadString(); break;
                }
                m.Entries.Add(e);
            }
            host.Deliver(meta, m);
        }

        void ReadSharedData(in MessageMeta meta, short dataVersion, int length)
        {
            Stats.SharedData.Record(length);
            var m = new PeerInfo { Share = (ShareCockpitFlags)reader.ReadByte() };
            m.Nickname = reader.ReadString();
            m.Guid = new Guid(reader.ReadBytes(16));
            byte flags = reader.ReadByte();
            m.Hub = (flags & 0x01) != 0;
            m.Atc = (flags & 0x02) != 0;
            m.AtcAirport = reader.ReadString();
            m.AtcLevel = reader.ReadByte();
            m.AtcFrequency = reader.ReadInt16();
            m.ActivityCircle = reader.ReadByte();
            m.Version = dataVersion >= 10019 ? reader.ReadString() : "";
            m.Simulator = dataVersion >= 10019 ? reader.ReadString() : "";
            m.SimulatorConnected = dataVersion < 10024 || (flags & 0x04) != 0;
            host.Deliver(meta, m);
        }

        void ReadStatus(in MessageMeta meta, short dataVersion, int length)
        {
            Stats.Status.Record(length);
            var m = new StatusUpdate
            {
                Guid = new Guid(reader.ReadBytes(16)),
                AppVersion = reader.ReadString(),
                Users = reader.ReadUInt16(),
            };
            m.AtcCount = reader.ReadUInt16();
            m.AtcAirport = m.AtcCount > 0 ? reader.ReadString() : "";
            m.AtcLevel = m.AtcCount > 0 ? reader.ReadByte() : 2;
            m.Planes = reader.ReadUInt16();
            m.Helicopters = reader.ReadUInt16();
            m.Boats = reader.ReadUInt16();
            m.Vehicles = reader.ReadUInt16();
            m.HubEnabled = reader.ReadBoolean();
            if (m.HubEnabled)
            {
                m.Address = reader.ReadString().Trim(' ');
                m.Name = reader.ReadString().Trim(' ');
                m.About = reader.ReadString().Trim(' ');
                m.Voip = reader.ReadString().Trim(' ');
                m.NextEvent = reader.ReadString().Trim(' ');
                m.Airport = reader.ReadString().Trim(' ');
                m.ActivityCircle = reader.ReadInt32();
                byte flags = dataVersion >= 10025 ? reader.ReadByte() : (byte)0;
                m.GlobalSession = (flags & 0x02) != 0;
                m.PasswordRequired = (flags & 0x04) != 0;
                if (m.Address.Length == 0)
                {
                    m.Address = meta.EndPoint.ToString();
                }
            }
            host.Deliver(meta, m);
        }

        void ReadUserList(in MessageMeta meta, int length)
        {
            // pre-UserList2 format: several users per message, fewer fields
            Stats.UserList.Record(length);
            int count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                var u = new HubUserUpdate { Guid = new Guid(reader.ReadBytes(16)) };
                byte flags = reader.ReadByte();
                u.Atc = (flags & 0x01) != 0;
                u.Ifr = (flags & 0x02) != 0;
                u.Callsign = reader.ReadString();
                u.Nickname = reader.ReadString();
                u.Frequency = reader.ReadUInt16();
                u.Latitude = reader.ReadSingle();
                u.Longitude = reader.ReadSingle();
                u.Altitude = reader.ReadUInt16();
                u.Speed = reader.ReadUInt16();
                u.IcaoType = reader.ReadString();
                u.Departure = reader.ReadString();
                u.Destination = reader.ReadString();
                u.Squawk = reader.ReadUInt16();
                u.Level = reader.ReadByte();
                u.Range = reader.ReadByte();
                u.Heading = reader.ReadUInt16();
                u.Rules = u.Route = u.Remarks = u.Alternate = u.FlightSpeed = u.FlightAltitude = "";
                u.Registration = u.IcaoAirline = u.FlightNumber = "";
                host.Deliver(meta, u);
            }
        }

        void ReadUserList2(in MessageMeta meta, short dataVersion, int length)
        {
            Stats.UserList2.Record(length);
            var u = new HubUserUpdate { Guid = new Guid(reader.ReadBytes(16)) };
            byte flags = reader.ReadByte();
            u.Atc = (flags & 0x01) != 0;
            u.Ifr = (flags & 0x02) != 0;
            u.Callsign = reader.ReadString();
            u.Nickname = reader.ReadString();
            u.Frequency = reader.ReadUInt16();
            u.Latitude = reader.ReadSingle();
            u.Longitude = reader.ReadSingle();
            u.Altitude = reader.ReadUInt16();
            u.Speed = reader.ReadUInt16();
            u.Squawk = reader.ReadUInt16();
            u.Level = reader.ReadByte();
            u.Range = reader.ReadByte();
            u.Heading = reader.ReadUInt16();
            u.IcaoType = reader.ReadString();
            u.Departure = reader.ReadString();
            u.Destination = reader.ReadString();
            u.Rules = reader.ReadString();
            u.Route = reader.ReadString();
            u.Remarks = reader.ReadString();
            u.Alternate = dataVersion >= 21003 ? reader.ReadString() : "";
            u.FlightSpeed = dataVersion >= 21003 ? reader.ReadString() : "";
            u.FlightAltitude = dataVersion >= 21003 ? reader.ReadString() : "";
            u.Registration = dataVersion >= 21006 ? reader.ReadString() : "";
            u.IcaoAirline = dataVersion >= 21006 ? reader.ReadString() : "";
            u.FlightNumber = dataVersion >= 21006 ? reader.ReadString() : "";
            host.Deliver(meta, u);
        }

        void ReadFlightPlan(in MessageMeta meta, short dataVersion, int length)
        {
            Stats.FlightPlan.Record(length);
            var m = new FlightPlanUpdate
            {
                Owner = NodeId.Read(reader),
                ObjectId = reader.ReadUInt32(),
            };
            reader.ReadByte(); // the message's own format-version byte - never gates anything, discarded (LegacyWire.FlightPlanFormatVersion)
            m.IcaoType = reader.ReadString();
            m.Departure = reader.ReadString();
            m.Destination = reader.ReadString();
            m.Rules = reader.ReadString();
            m.Route = reader.ReadString();
            m.Remarks = reader.ReadString();
            m.Alternate = dataVersion >= 21003 ? reader.ReadString() : "";
            m.Speed = dataVersion >= 21003 ? reader.ReadString() : "";
            m.Altitude = dataVersion >= 21003 ? reader.ReadString() : "";
            m.Callsign = dataVersion >= 21003 ? reader.ReadString() : "";
            m.Registration = dataVersion >= 21006 ? reader.ReadString() : "";
            m.IcaoAirline = dataVersion >= 21006 ? reader.ReadString() : "";
            m.FlightNumber = dataVersion >= 21006 ? reader.ReadString() : "";
            host.Deliver(meta, m);
        }

        void ReadNotes(in MessageMeta meta, int length)
        {
            Stats.Notes.Record(length);
            var m = new NotesBundle { Scope = CommsScope.All, Users = [] };
            while (reader.ReadByte() == 0)
            {
                var user = new NotesUser
                {
                    Guid = new Guid(reader.ReadBytes(16)),
                    Nickname = reader.ReadString(),
                    Callsign = reader.ReadString(),
                    Notes = [],
                };
                while (reader.ReadByte() == 0)
                {
                    uint noteId = reader.ReadUInt32();
                    ushort type = reader.ReadUInt16();
                    reader.ReadUInt16(); // expire
                    ushort noteLength = reader.ReadUInt16();
                    if (type == LegacyWire.NoteTypeComms)
                    {
                        user.Notes.Add(new CommsNote { NoteId = noteId, Age = reader.ReadSingle(), Channel = reader.ReadUInt16(), Text = reader.ReadString() });
                    }
                    else
                    {
                        reader.ReadBytes(noteLength);
                    }
                }
                m.Users.Add(user);
            }
            host.Deliver(meta, m);
        }
    }
}
