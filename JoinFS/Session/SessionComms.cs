using JoinFS.Net;
using System;

namespace JoinFS
{
    /// <summary>
    /// Text comms over the session: posting notes, and exchanging the session channels' history
    /// with nodes as they connect (so the comms window shows what was said before we joined).
    /// </summary>
    public sealed class SessionComms
    {
        readonly INetworkOutbox outbox;
        readonly Func<Notes> notes;
        readonly ISessionUi ui;
        readonly IClock clock;

        /// <summary>History requests left to make (the first few nodes established after joining).</summary>
        int historyRequests = 0;

        /// <param name="notes">The notes store (created after the network, so looked up when needed).</param>
        public SessionComms(INetworkOutbox outbox, Func<Notes> notes, ISessionUi ui, IClock clock)
        {
            this.outbox = outbox;
            this.notes = notes;
            this.ui = ui;
            this.clock = clock;
        }

        /// <summary>Joining a session: ask the first few nodes for their history.</summary>
        public void OnJoining() => historyRequests = 3;

        public void OnPeerEstablished(NodeId nuid)
        {
            if (historyRequests > 0)
            {
                if (ui.CommsVisible)
                {
                    SendSessionCommsRequestMessage(nuid);
                }
                historyRequests--;
            }
        }

        /// <summary>A newly posted comms note, to everyone.</summary>
        public void SendCommsNoteMessage(Guid guid, string nickname, string callsign, uint noteId, float age, ushort channel, string text) =>
            outbox.Broadcast(new NotesBundle
            {
                Scope = CommsScope.Single,
                Users = [new NotesUser { Guid = guid, Nickname = nickname, Callsign = callsign, Notes = [new CommsNote { NoteId = noteId, Age = age, Channel = channel, Text = text }] }],
            }, guaranteed: true);

        /// <summary>Ask a node for its session comms history.</summary>
        public void SendSessionCommsRequestMessage(NodeId nuid) => outbox.SendTo(nuid, new CommsRequest { Scope = CommsScope.Session }, guaranteed: true);

        public void Handle(in MessageMeta meta, in CommsRequest request)
        {
            // only session comms are served (global/listen requests were never answered)
            if (request.Scope == CommsScope.Session)
            {
                SendSessionComms(meta.EndPoint);
            }
        }

        /// <summary>Our comms notes on session channels, in reply to a request.</summary>
        void SendSessionComms(System.Net.IPEndPoint endPoint)
        {
            double now = clock.Now;
            var bundle = new NotesBundle { Scope = CommsScope.Session, Users = [] };
            foreach (var user in notes().userNotesList)
            {
                var notesUser = new NotesUser { Guid = user.Key, Nickname = user.Value.nickname, Callsign = user.Value.callsign, Notes = [] };
                foreach (var commsNote in user.Value.commsList)
                {
                    if (commsNote.Value.channel != Notes.GLOBAL_CHANNEL)
                    {
                        notesUser.Notes.Add(new CommsNote
                        {
                            NoteId = commsNote.Key,
                            Age = (float)(now - commsNote.Value.time),
                            Channel = commsNote.Value.channel,
                            Text = commsNote.Value.text,
                        });
                    }
                }
                bundle.Users.Add(notesUser);
            }
            outbox.SendToEndPoint(endPoint, bundle, guaranteed: true);
        }

        public void Handle(in MessageMeta meta, in NotesBundle bundle)
        {
            Notes store = notes();
            foreach (NotesUser user in bundle.Users)
            {
                Guid guid = user.Guid;
                foreach (CommsNote note in user.Notes)
                {
                    store.ProcessCommsNote(ref guid, user.Nickname, user.Callsign, note.NoteId, note.Age, note.Channel, note.Text);
                }
            }
            ui.CommsChanged();
        }
    }
}
