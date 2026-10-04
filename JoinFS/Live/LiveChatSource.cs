using System;
using System.Collections.Generic;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The chat of the session, from <c>main.notes</c>: what the pilots post on the session channel, kept there for a few minutes, and what
    /// is posted by sending. The "." commands are answered here and never leave: their lines are kept for an hour, as the old session
    /// window kept them. What counts as new for the dot is what others said since the chat was last looked at.
    /// </summary>
    class LiveChatSource : IChatSource
    {
        /// <summary>How long the answer to a command stays, in seconds</summary>
        const double COMMAND_LINE_LIFE = 3600.0;

        /// <summary>The least time between two messages, in seconds. It keeps a held key from flooding the session.</summary>
        const double SEND_INTERVAL = 1.0;

        readonly Main main;

        /// <summary>The answers to commands, with when they were given. Only the UI thread touches them.</summary>
        readonly List<ChatMessage> commandLines = [];

        double lastSend = double.NegativeInfinity;
        double lastRead = 0.0;

        public LiveChatSource(Main main)
        {
            this.main = main;
        }

        public bool IsConnected => main.network.Connected;

        public bool CanSend => IsConnected && main.ElapsedTime - lastSend >= SEND_INTERVAL;

        public IReadOnlyList<ChatMessage> GetMessages()
        {
            List<ChatMessage> lines = [];

            // the answers to commands that have not yet expired
            commandLines.RemoveAll(line => main.ElapsedTime - line.Time > COMMAND_LINE_LIFE);
            lines.AddRange(commandLines);

            if (IsConnected)
            {
                lock (main.conch)
                {
                    foreach (var userNotes in main.notes.userNotesList)
                    {
                        // what an ignored pilot says is not shown
                        Guid guid = userNotes.Key;
                        if (main.log.IgnoreNode(ref guid))
                        {
                            continue;
                        }

                        foreach (var note in userNotes.Value.commsList)
                        {
                            // empty notes are only there to take the place of ones that ended
                            if (note.Value.text.Length > 0 && note.Value.channel == Notes.SESSION_CHANNEL)
                            {
                                lines.Add(new ChatMessage(userNotes.Value.uniqueNickname, note.Value.text, userNotes.Value.callsign, note.Value.time));
                            }
                        }
                    }
                }
            }

            // oldest first
            lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            return lines;
        }

        public void Send(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (text[0] == '.')
            {
                Command(text);
                return;
            }

            lock (main.conch)
            {
                // only to a session
                if (main.network.Connected)
                {
                    main.notes.PostCommsNote(Notes.SESSION_CHANNEL, text);
                }
            }
            lastSend = main.ElapsedTime;
        }

        /// <summary>
        /// A line that starts with "." asks JoinFS, not the session. The only command is help.
        /// </summary>
        void Command(string text)
        {
            double now = main.ElapsedTime;
            if (text.Equals(".help", StringComparison.OrdinalIgnoreCase))
            {
                commandLines.Add(new ChatMessage("", "Command list:", Time: now, IsLocal: true));
                commandLines.Add(new ChatMessage("", ".help - Show the command list", Time: now + 0.001, IsLocal: true));
            }
            else
            {
                commandLines.Add(new ChatMessage("", "Unknown command, '" + text + "'", Time: now, IsLocal: true));
            }
        }

        public bool HasUnread
        {
            get
            {
                if (!IsConnected)
                {
                    return false;
                }

                lock (main.conch)
                {
                    foreach (var userNotes in main.notes.userNotesList)
                    {
                        // what you said yourself, and what an ignored pilot said, is not news
                        Guid guid = userNotes.Key;
                        if (guid == main.guid || main.log.IgnoreNode(ref guid))
                        {
                            continue;
                        }

                        foreach (var note in userNotes.Value.commsList)
                        {
                            if (note.Value.text.Length > 0 && note.Value.channel == Notes.SESSION_CHANNEL && note.Value.time > lastRead)
                            {
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
        }

        public void MarkRead() => lastRead = main.ElapsedTime;
    }
}
