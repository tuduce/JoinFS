using System;
using System.Collections.Generic;

// docs/protocol-v2-implementation-plan.md Phase 5: WeatherReply and WeatherUpdate (docs/network-
// protocol.md §8.4) share the identical wire shape ({ Metar: string }) but need independent schema
// versions/negotiation (different reliability semantics, different receive-side handling - a Reply
// only ever updates this node's own weather, an Update applies to a specific peer's aircraft) so they
// get two codec classes, one per canonical type (JoinFS.Net.WeatherReply and WeatherUpdate).
//
// WeatherRequest is deliberately NOT ported - see the implementation plan. WriteWeatherRequestMessage
// has zero call sites anywhere in the current codebase (confirmed by repo-wide search) and its NetId
// field is written but never read by the receive handler, which replies unconditionally from
// main.sim.scheduleMetar regardless of what NetId was sent - there is nothing live to mechanically
// port. A WeatherRequest therefore always travels over legacy; the reply (Network's WeatherRequest
// handler) goes out over JFP2 when the requester negotiated WeatherReply.

using JoinFS.Net;

namespace JoinFS.Net.Jfp2.Codecs
{
    public sealed class WeatherReplyV1Codec : ICodec<WeatherReply>
    {
        public byte MessageClass => MessageClasses.WeatherReply;
        public byte SchemaVersion => 1;

        public int Encode(in WeatherReply v, Span<byte> dest)
        {
            var bytes = new List<byte>(64);
            WireText.WriteString(bytes, v.Metar);
            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public WeatherReply Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            return new WeatherReply { Metar = WireText.ReadString(src, ref i) };
        }
    }

    public sealed class WeatherUpdateV1Codec : ICodec<WeatherUpdate>
    {
        public byte MessageClass => MessageClasses.Weather;
        public byte SchemaVersion => 1;

        public int Encode(in WeatherUpdate v, Span<byte> dest)
        {
            var bytes = new List<byte>(64);
            WireText.WriteString(bytes, v.Metar);
            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public WeatherUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            return new WeatherUpdate { Metar = WireText.ReadString(src, ref i) };
        }
    }
}
