using System;
using System.Collections.Generic;

// docs/protocol-v2-implementation-plan.md Phase 5: WeatherReply and WeatherUpdate (docs/network-
// protocol.md §8.4) share the identical wire shape ({ Metar: string }) but need independent schema
// versions/negotiation (different reliability semantics, different receive-side handling - a Reply
// only ever updates this node's own weather, an Update applies to a specific peer's aircraft) so they
// get two codec classes sharing one WeatherReport struct rather than one codec serving two classes.
//
// WeatherRequest is deliberately NOT ported - see the implementation plan. WriteWeatherRequestMessage
// has zero call sites anywhere in the current codebase (confirmed by repo-wide search) and its NetId
// field is written but never read by the receive handler, which replies unconditionally from
// main.sim.scheduleMetar regardless of what NetId was sent - there is nothing live to mechanically
// port. The receive side (case MESSAGE_ID.WeatherRequest) stays untouched and keeps replying to any
// legacy WeatherRequest it gets (from a real legacy peer, or in principle a JFP2 peer that fell back
// to legacy for this one message) - only the *reply* half is upgraded to use JFP2 when eligible, via
// Network.SendWeatherReply.

namespace JoinFS.Jfp2.Codecs
{
    public struct WeatherReport
    {
        public string Metar;
    }

    public sealed class WeatherReplyV1Codec : ICodec<WeatherReport>
    {
        public byte MessageClass => MessageClasses.WeatherReply;
        public byte SchemaVersion => 1;

        public int Encode(in WeatherReport v, Span<byte> dest)
        {
            var bytes = new List<byte>(64);
            WireText.WriteString(bytes, v.Metar);
            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public WeatherReport Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            return new WeatherReport { Metar = WireText.ReadString(src, ref i) };
        }
    }

    public sealed class WeatherUpdateV1Codec : ICodec<WeatherReport>
    {
        public byte MessageClass => MessageClasses.Weather;
        public byte SchemaVersion => 1;

        public int Encode(in WeatherReport v, Span<byte> dest)
        {
            var bytes = new List<byte>(64);
            WireText.WriteString(bytes, v.Metar);
            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public WeatherReport Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            return new WeatherReport { Metar = WireText.ReadString(src, ref i) };
        }
    }
}
