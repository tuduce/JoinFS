using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;

namespace JoinFS.Net.Jfp2.Codecs
{
    // The bodies of the directory classes (docs/network/jfp2/protocol.md §5.16), application classes 10 to 17, all version 1,
    // little-endian. Status and StatusRequest (classes 7 and 8) are the directory's other two. Every one fits a datagram on its own.
    //
    //   HubList              count u8 | entries (hub entry: name 8 | port u16 | candidate count u8 | candidates, each a length u8 and a WireEndPoint)
    //   UserListRequest      reserved u8 (0)
    //   HubUser              guid 16 | flags u8 (1 atc, 2 ifr) | nickname | frequency u16 | latitude f32 | longitude f32 | altitude u16 | speed u16 |
    //                        squawk u16 | level u8 | range u8 | heading u16 | callsign | icao type | departure | destination | rules | route |
    //                        remarks | alternate | flight speed | flight altitude | registration | icao airline | flight number (strings: u16 length, UTF-8)
    //   UserPositionsRequest reserved u8 (0)
    //   UserPositions        count u8 | each: guid 16 | latitude f32 | longitude f32 | altitude u16 | speed u16 | squawk u16 | heading u16
    //   Online, UserLookup   uuid u32
    //   UserLookupAnswer     uuid u32 | name 8 | port u16 | candidate count u8 | candidates (as in HubList)

    /// <summary>Writes and reads the list of endpoints that HubList and UserLookupAnswer carry: a count, then each as a length byte and a <see cref="WireEndPoint"/> (§4.4).</summary>
    public static class CandidateList
    {
        /// <summary>The most candidates an entry carries (the same bound as the Hello's <see cref="HandshakeMessage.MaxCandidates"/>).</summary>
        public const int Max = HandshakeMessage.MaxCandidates;

        public static int Count(IReadOnlyList<IPEndPoint> candidates) => Math.Min(candidates?.Count ?? 0, Max);

        /// <summary>Bytes of the count, without the candidates.</summary>
        public const int CountSize = 1;

        public static int SizeOf(IReadOnlyList<IPEndPoint> candidates)
        {
            int size = 0;
            for (int n = 0; n < Count(candidates); n++) size += 1 + WireEndPoint.SizeOf(candidates[n]);
            return size;
        }

        /// <summary>Writes the count byte and the candidates; returns the bytes written.</summary>
        public static int Write(IReadOnlyList<IPEndPoint> candidates, Span<byte> dest)
        {
            int count = Count(candidates);
            dest[0] = (byte)count;
            int i = CountSize;
            for (int n = 0; n < count; n++)
            {
                int length = WireEndPoint.WriteTo(candidates[n], dest[(i + 1)..]);
                dest[i] = (byte)length;
                i += 1 + length;
            }
            return i;
        }

        /// <summary>Reads the count byte and the candidates; one of a family this build does not know is skipped by its length.</summary>
        public static List<IPEndPoint> Read(ReadOnlySpan<byte> src, ref int i)
        {
            int count = Math.Min((int)src[i++], Max);
            var list = new List<IPEndPoint>(count);
            for (int n = 0; n < count && i < src.Length; n++)
            {
                int length = src[i++];
                if (length > src.Length - i)
                {
                    break;
                }
                if (WireEndPoint.TryReadFrom(src.Slice(i, length), out IPEndPoint endPoint))
                {
                    list.Add(endPoint);
                }
                i += length;
            }
            return list;
        }
    }

    public sealed class HubListV1Codec : ICodec<HubList>
    {
        public byte MessageClass => MessageClasses.HubList;
        public byte SchemaVersion => 1;

        /// <summary>The most bytes of entries a message carries; the sender splits a longer list across messages.</summary>
        public const int Budget = 1000;

        const int EntryFixedSize = NodeId.WireSize + 2 + 1;

        public int Encode(in HubList v, Span<byte> dest)
        {
            int count = 0;
            int i = 1;
            foreach (HubAddress hub in v.Hubs ?? [])
            {
                int size = EntryFixedSize + CandidateList.SizeOf(hub.Candidates);
                if (count == byte.MaxValue || i + size > Budget)
                {
                    break;
                }
                hub.Node.WriteTo(dest.Slice(i, NodeId.WireSize)); i += NodeId.WireSize;
                BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(i, 2), hub.Port); i += 2;
                i += CandidateList.Write(hub.Candidates, dest[i..]);
                count++;
            }
            dest[0] = (byte)count;
            return i;
        }

        public HubList Decode(ReadOnlySpan<byte> src)
        {
            int count = src[0];
            int i = 1;
            var hubs = new List<HubAddress>(count);
            for (int n = 0; n < count && i + EntryFixedSize <= src.Length; n++)
            {
                var hub = new HubAddress { Node = NodeId.ReadFrom(src[i..]) };
                i += NodeId.WireSize;
                hub.Port = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
                hub.Candidates = CandidateList.Read(src, ref i);
                hubs.Add(hub);
            }
            return new HubList { Hubs = hubs };
        }
    }

    /// <summary>An empty request: a reserved byte, so the datagram has a payload and a later version can add fields.</summary>
    public abstract class RequestCodec<T>(byte messageClass) : ICodec<T> where T : struct
    {
        public byte MessageClass => messageClass;
        public byte SchemaVersion => 1;
        public int Encode(in T v, Span<byte> dest) { dest[0] = 0; return 1; }
        public T Decode(ReadOnlySpan<byte> src) => default;
    }

    public sealed class UserListRequestV1Codec() : RequestCodec<UserListRequest>(MessageClasses.UserListRequest);

    public sealed class UserPositionsRequestV1Codec() : RequestCodec<UserPositionsRequest>(MessageClasses.UserPositionsRequest);

    public sealed class HubUserV1Codec : ICodec<HubUserUpdate>
    {
        public byte MessageClass => MessageClasses.HubUser;
        public byte SchemaVersion => 1;

        public const int NicknameLimit = 40;
        public const int CallsignLimit = 32;
        public const int IcaoTypeLimit = 8;
        public const int DepartureLimit = 8;
        public const int DestinationLimit = 8;
        public const int RulesLimit = 8;
        public const int RouteLimit = 256;
        public const int RemarksLimit = 128;
        public const int AlternateLimit = 8;
        public const int SpeedLimit = 16;
        public const int AltitudeLimit = 16;
        public const int RegistrationLimit = 32;
        public const int IcaoAirlineLimit = 8;
        public const int FlightNumberLimit = 16;

        /// <summary>The largest payload: 37 fixed bytes, then each string at its limit with its prefix.</summary>
        public const int MaxSize = 37 + 14 * WireText.PrefixSize + NicknameLimit + CallsignLimit + IcaoTypeLimit + DepartureLimit + DestinationLimit
            + RulesLimit + RouteLimit + RemarksLimit + AlternateLimit + SpeedLimit + AltitudeLimit + RegistrationLimit + IcaoAirlineLimit + FlightNumberLimit;

        public int Encode(in HubUserUpdate v, Span<byte> dest)
        {
            var bytes = new List<byte>(160);
            bytes.AddRange(v.Guid.ToByteArray());
            bytes.Add((byte)((v.Atc ? 1 : 0) | (v.Ifr ? 2 : 0)));
            WireText.WriteString(bytes, v.Nickname, NicknameLimit);
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(number, v.Frequency); bytes.AddRange(number[..2].ToArray());
            BinaryPrimitives.WriteSingleLittleEndian(number, v.Latitude); bytes.AddRange(number.ToArray());
            BinaryPrimitives.WriteSingleLittleEndian(number, v.Longitude); bytes.AddRange(number.ToArray());
            BinaryPrimitives.WriteUInt16LittleEndian(number, v.Altitude); bytes.AddRange(number[..2].ToArray());
            BinaryPrimitives.WriteUInt16LittleEndian(number, v.Speed); bytes.AddRange(number[..2].ToArray());
            BinaryPrimitives.WriteUInt16LittleEndian(number, v.Squawk); bytes.AddRange(number[..2].ToArray());
            bytes.Add(v.Level);
            bytes.Add(v.Range);
            BinaryPrimitives.WriteUInt16LittleEndian(number, v.Heading); bytes.AddRange(number[..2].ToArray());
            WireText.WriteString(bytes, v.Callsign, CallsignLimit);
            WireText.WriteString(bytes, v.IcaoType, IcaoTypeLimit);
            WireText.WriteString(bytes, v.Departure, DepartureLimit);
            WireText.WriteString(bytes, v.Destination, DestinationLimit);
            WireText.WriteString(bytes, v.Rules, RulesLimit);
            WireText.WriteString(bytes, v.Route, RouteLimit);
            WireText.WriteString(bytes, v.Remarks, RemarksLimit);
            WireText.WriteString(bytes, v.Alternate, AlternateLimit);
            WireText.WriteString(bytes, v.FlightSpeed, SpeedLimit);
            WireText.WriteString(bytes, v.FlightAltitude, AltitudeLimit);
            WireText.WriteString(bytes, v.Registration, RegistrationLimit);
            WireText.WriteString(bytes, v.IcaoAirline, IcaoAirlineLimit);
            WireText.WriteString(bytes, v.FlightNumber, FlightNumberLimit);
            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public HubUserUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new HubUserUpdate { Guid = new Guid(src.Slice(i, 16)) };
            i += 16;
            byte flags = src[i++];
            v.Atc = (flags & 1) != 0;
            v.Ifr = (flags & 2) != 0;
            v.Nickname = WireText.ReadString(src, ref i);
            v.Frequency = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Latitude = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.Longitude = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.Altitude = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Speed = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Squawk = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Level = src[i++];
            v.Range = src[i++];
            v.Heading = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Callsign = WireText.ReadString(src, ref i);
            v.IcaoType = WireText.ReadString(src, ref i);
            v.Departure = WireText.ReadString(src, ref i);
            v.Destination = WireText.ReadString(src, ref i);
            v.Rules = WireText.ReadString(src, ref i);
            v.Route = WireText.ReadString(src, ref i);
            v.Remarks = WireText.ReadString(src, ref i);
            v.Alternate = WireText.ReadString(src, ref i);
            v.FlightSpeed = WireText.ReadString(src, ref i);
            v.FlightAltitude = WireText.ReadString(src, ref i);
            v.Registration = WireText.ReadString(src, ref i);
            v.IcaoAirline = WireText.ReadString(src, ref i);
            v.FlightNumber = WireText.ReadString(src, ref i);
            return v;
        }
    }

    public sealed class UserPositionsV1Codec : ICodec<UserPositions>
    {
        public byte MessageClass => MessageClasses.UserPositions;
        public byte SchemaVersion => 1;

        public const int EntrySize = 16 + 4 + 4 + 2 + 2 + 2 + 2;

        /// <summary>The most users a message carries; the sender splits a longer list across messages.</summary>
        public const int MaxUsers = (Jfp2Limits.MaxPayloadSize - 1) / EntrySize;

        public int Encode(in UserPositions v, Span<byte> dest)
        {
            int count = Math.Min(v.Users?.Count ?? 0, MaxUsers);
            dest[0] = (byte)count;
            int i = 1;
            for (int n = 0; n < count; n++)
            {
                UserPosition user = v.Users[n];
                user.Guid.TryWriteBytes(dest.Slice(i, 16)); i += 16;
                BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), user.Latitude); i += 4;
                BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), user.Longitude); i += 4;
                BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(i, 2), user.Altitude); i += 2;
                BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(i, 2), user.Speed); i += 2;
                BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(i, 2), user.Squawk); i += 2;
                BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(i, 2), user.Heading); i += 2;
            }
            return i;
        }

        public UserPositions Decode(ReadOnlySpan<byte> src)
        {
            int count = src[0];
            int i = 1;
            var users = new List<UserPosition>(count);
            for (int n = 0; n < count && i + EntrySize <= src.Length; n++)
            {
                var user = new UserPosition { Guid = new Guid(src.Slice(i, 16)) };
                i += 16;
                user.Latitude = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
                user.Longitude = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
                user.Altitude = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
                user.Speed = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
                user.Squawk = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
                user.Heading = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
                users.Add(user);
            }
            return new UserPositions { Users = users };
        }
    }

    public sealed class OnlineV1Codec : ICodec<OnlineAnnouncement>
    {
        public byte MessageClass => MessageClasses.Online;
        public byte SchemaVersion => 1;

        public int Encode(in OnlineAnnouncement v, Span<byte> dest)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dest, v.Uuid);
            return 4;
        }

        public OnlineAnnouncement Decode(ReadOnlySpan<byte> src) => new() { Uuid = BinaryPrimitives.ReadUInt32LittleEndian(src) };
    }

    public sealed class UserLookupV1Codec : ICodec<UserNuidRequest>
    {
        public byte MessageClass => MessageClasses.UserLookup;
        public byte SchemaVersion => 1;

        public int Encode(in UserNuidRequest v, Span<byte> dest)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dest, v.Uuid);
            return 4;
        }

        public UserNuidRequest Decode(ReadOnlySpan<byte> src) => new() { Uuid = BinaryPrimitives.ReadUInt32LittleEndian(src) };
    }

    public sealed class UserLookupAnswerV1Codec : ICodec<UserNuidReply>
    {
        public byte MessageClass => MessageClasses.UserLookupAnswer;
        public byte SchemaVersion => 1;

        const int FixedSize = 4 + NodeId.WireSize + 2 + 1;

        public int Encode(in UserNuidReply v, Span<byte> dest)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dest, v.Uuid);
            v.Node.WriteTo(dest.Slice(4, NodeId.WireSize));
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(4 + NodeId.WireSize, 2), v.Port);
            return 4 + NodeId.WireSize + 2 + CandidateList.Write(v.Candidates, dest[(4 + NodeId.WireSize + 2)..]);
        }

        public UserNuidReply Decode(ReadOnlySpan<byte> src)
        {
            if (src.Length < FixedSize)
            {
                throw new ArgumentException("a UserLookupAnswer is shorter than its fixed part");
            }
            var v = new UserNuidReply
            {
                Uuid = BinaryPrimitives.ReadUInt32LittleEndian(src),
                Node = NodeId.ReadFrom(src[4..]),
                Port = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(4 + NodeId.WireSize, 2)),
            };
            int i = 4 + NodeId.WireSize + 2;
            v.Candidates = CandidateList.Read(src, ref i);
            return v;
        }
    }
}
