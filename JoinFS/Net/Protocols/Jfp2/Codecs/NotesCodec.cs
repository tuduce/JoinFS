using System;
using System.Buffers.Binary;
using System.Collections.Generic;

// docs/protocol-v2-implementation-plan.md Phase 5. The legacy Notes message (docs/network-
// protocol.md §8.9) is a nested, length-prefixed, multi-type container - in the current codebase it
// has 5 different producer methods with 2 different (and neither correct) Length-field formulas, and
// 3 of those 5 have zero call sites anywhere in the repo, alongside two Notes-family request messages
// (GlobalCommsRequest, CommsListenRequest) that are also fully unreferenced. Porting that whole
// container mechanically would mean faithfully reproducing dead code and an already-broken length
// formula - not what "mechanical port, no new design questions" was supposed to mean once actually
// examined.
//
// JFP2's Notes class is deliberately scoped to the one shape that is actually live, high-value real-
// time chat traffic: a single posted/relayed comms note (JoinFS/Notes.cs's PostCommsNote - a user
// typing a message - and ProcessCommsNote's hub-relay of a global-channel note), i.e. exactly
// Network.SendCommsNoteMessage's parameter list. The bulk "catch-up dump of every user's every note"
// exchange (SessionCommsRequest + the full userNotesList reply) stays entirely on the legacy path -
// it only ever fires once per newly-established connection, so it isn't hot-path traffic JFP2 needs to
// optimize, and its nested repeated-group shape doesn't fit this single-note codec. See the
// implementation plan for the full reasoning.

namespace JoinFS.Net.Jfp2.Codecs
{
    /// <summary>One posted or relayed comms note - mirrors Network.SendCommsNoteMessage's parameters
    /// exactly.</summary>
    public struct NoteUpdate
    {
        public Guid Guid;
        public string Nickname;
        public string Callsign;
        public uint NoteId;
        public float Age;
        public ushort Channel;
        public string Text;
    }

    public sealed class NotesV1Codec : ICodec<NoteUpdate>
    {
        public byte MessageClass => MessageClasses.Notes;
        public byte SchemaVersion => 1;

        public int Encode(in NoteUpdate v, Span<byte> dest)
        {
            var bytes = new List<byte>(96);
            bytes.AddRange(v.Guid.ToByteArray()); // 16 bytes
            WireText.WriteString(bytes, v.Nickname);
            WireText.WriteString(bytes, v.Callsign);
            Span<byte> fixedFields = stackalloc byte[4 + 4 + 2];
            BinaryPrimitives.WriteUInt32LittleEndian(fixedFields.Slice(0, 4), v.NoteId);
            BinaryPrimitives.WriteSingleLittleEndian(fixedFields.Slice(4, 4), v.Age);
            BinaryPrimitives.WriteUInt16LittleEndian(fixedFields.Slice(8, 2), v.Channel);
            bytes.AddRange(fixedFields.ToArray());
            WireText.WriteString(bytes, v.Text);
            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public NoteUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new NoteUpdate();
            v.Guid = new Guid(src.Slice(i, 16).ToArray()); i += 16;
            v.Nickname = WireText.ReadString(src, ref i);
            v.Callsign = WireText.ReadString(src, ref i);
            v.NoteId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            v.Age = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.Channel = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Text = WireText.ReadString(src, ref i);
            return v;
        }
    }
}
