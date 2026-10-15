using System;
using System.Collections.Generic;

// Canonical comms (text chat) messages.

namespace JoinFS.Net
{
    public enum CommsScope : byte
    {
        /// <summary>Notes on session channels (everything except the global channel).</summary>
        Session,
        /// <summary>Notes on the global channel only.</summary>
        Global,
        /// <summary>Every note the sender holds.</summary>
        All,
        /// <summary>One newly posted note.</summary>
        Single,
    }

    /// <summary>
    /// Ask the recipient for its notes. <see cref="CommsScope.All"/> is "listen" (legacy
    /// CommsListenRequest); Single is not meaningful here.
    /// </summary>
    public struct CommsRequest : IMessage
    {
        public static MessageKind Kind => MessageKind.CommsRequest;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public CommsScope Scope;
    }

    public struct CommsNote
    {
        public uint NoteId;
        /// <summary>Seconds since the note was posted, at the time of sending.</summary>
        public float Age;
        public ushort Channel;
        public string Text;
    }

    public struct NotesUser
    {
        public Guid Guid;
        public string Nickname;
        public string Callsign;
        /// <summary>May be empty: bulk replies announce every known user.</summary>
        public List<CommsNote> Notes;
    }

    /// <summary>A newly posted note (Scope = Single, one user with one note) or a reply to a <see cref="CommsRequest"/>.</summary>
    public struct NotesBundle : IMessage
    {
        public static MessageKind Kind => MessageKind.Notes;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public CommsScope Scope;
        public List<NotesUser> Users;
    }
}
