using System;
using System.Collections.Generic;
using System.Globalization;

namespace JoinFS
{
    public class Notes
    {
        /// <summary>
        /// Main form
        /// </summary>
        Main main;

        /// <summary>
        /// Type of note
        /// </summary>
        public enum Type
        {
            Comms,
        }

        /// <summary>
        /// Next ID to assign to a note
        /// </summary>
        uint nextNoteId;

        /// <summary>
        /// Expire times
        /// </summary>
        public const ushort COMMS_EXPIRE = 10;
        public const ushort COMMS_EMPTY_EXPIRE = 2;

        /// <summary>
        /// Main channels
        /// </summary>
        public const ushort GLOBAL_CHANNEL = 0;
        public const ushort SESSION_CHANNEL = 1;
        public const ushort WHISPER_CHANNEL = 2;

        /// <summary>
        /// A comms note
        /// </summary>
        public class CommsNote
        {
            /// <summary>
            /// Time of note
            /// </summary>
            public double time;
            /// <summary>
            /// Which channel the text was posted on
            /// </summary>
            public ushort channel;
            /// <summary>
            /// Text message
            /// </summary>
            public string text;
            /// <summary>
            /// Time the note should expire
            /// </summary>
            public double expireTime;

            /// <summary>
            /// Constructor
            /// </summary>
            public CommsNote(double time, ushort channel, string text, double expireTime)
            {
                this.time = time;
                this.channel = channel;
                this.text = text;
                this.expireTime = expireTime;
            }
        }

        /// <summary>
        /// A user's notes
        /// </summary>
        public class UserNotes
        {
            /// <summary>
            /// Nickname of user
            /// </summary>
            public string nickname;
            /// <summary>
            /// Unique nickname for the user
            /// </summary>
            public string uniqueNickname;
            /// <summary>
            /// Callsign of the user
            /// </summary>
            public string callsign;
            /// <summary>
            /// List of comms notes
            /// </summary>
            public Dictionary<uint, CommsNote> commsList = new Dictionary<uint,CommsNote>();

            /// <summary>
            /// constructor
            /// </summary>
            /// <param name="nickname"></param>
            /// <param name="uniqueNickname"></param>
            public UserNotes(string nickname, string uniqueNickname, string callsign)
            {
                this.nickname = nickname;
                this.uniqueNickname = uniqueNickname;
                this.callsign = callsign;
            }
        }

        /// <summary>
        /// List of user notes
        /// </summary>
        public Dictionary<Guid, UserNotes> userNotesList = new Dictionary<Guid,UserNotes>();

        /// <summary>
        /// Constructor
        /// </summary>
        public Notes(Main main)
        {
            // set main form
            this.main = main;
            // assign a new ID
            nextNoteId = (uint)(new Random()).Next();
            // first empty send
            sendEmptyTime = main.ElapsedTime + 10.0f;
        }

        // notes to remove
        List<uint> removeNoteList = new List<uint>();
        // users to remove
        List<Guid> removeUserList = new List<Guid>();

        /// <summary>
        /// Process timer
        /// </summary>
        const double PROCESS_NOTES_TIME = 10.0f;
        double processNotesTime = 0.0;
        const double SEND_EMPTY_TIME = 240.0f;
        readonly double sendEmptyTime = 0.0;

        /// <summary>
        /// Process notes
        /// </summary>
        public void DoWork()
        {
            // process
            if (main.ElapsedTime > processNotesTime)
            {
                // next update
                processNotesTime = main.ElapsedTime + PROCESS_NOTES_TIME;

                // for each user
                foreach (var userNotes in userNotesList)
                {
                    // for each comms note
                    foreach (var note in userNotes.Value.commsList)
                    {
                        // check if note has expired
                        if (main.ElapsedTime > note.Value.expireTime)
                        {
                            // add to remove list
                            removeNoteList.Add(note.Key);
                        }
                    }

                    // for each note to remove
                    foreach (var noteId in removeNoteList)
                    {
                        // remove note
                        userNotes.Value.commsList.Remove(noteId);
                    }

                    // clear list
                    removeNoteList.Clear();

                    // check if notes are empty
                    if (userNotes.Value.commsList.Count == 0)
                    {
                        // add to remove list
                        removeUserList.Add(userNotes.Key);
                    }
                }

                // for each user to remove
                foreach (var user in removeUserList)
                {
                    // remove user
                    userNotesList.Remove(user);
                }

                // clear list
                removeUserList.Clear();
            }
        }

        /// <summary>
        /// Check if nickname is in use
        /// </summary>
        /// <param name="nickname"></param>
        /// <returns></returns>
        bool IsNicknameInUse(string nickname)
        {
            // for each user
            foreach (var userNotes in userNotesList)
            {
                // check unique nickname
                if (userNotes.Value.uniqueNickname.Equals(nickname, StringComparison.OrdinalIgnoreCase))
                {
                    // in use
                    return true;
                }
            }

            // not used
            return false;
        }

        /// <summary>
        /// Return a unique nickname
        /// </summary>
        /// <param name="nickname"></param>
        /// <returns></returns>
        string GetUniqueNickname(string nickname)
        {
            // initialize nickname
            string uniqueNickname = nickname;
            // check if in use
            if (IsNicknameInUse(uniqueNickname))
            {
                int n = 0;
                do
                {
                    // try next 
                    n++;
                    // make nickname
                    uniqueNickname = nickname + n.ToString(CultureInfo.InvariantCulture);
                } while (IsNicknameInUse(uniqueNickname));
            }

            // return unique nickname
            return uniqueNickname;
        }

        /// <summary>
        /// Ensure that a user is in the list
        /// </summary>
        /// <param name="guid"></param>
        void RegisterUser(Guid guid, string nickname, string callsign)
        {
            // check if user needs to be added
            if (userNotesList.ContainsKey(guid) == false)
            {
                // add user notes
                userNotesList.Add(guid, new UserNotes(nickname, GetUniqueNickname(nickname), callsign));
            }
            else
            {
                // check if nickname has changed
                if (userNotesList[guid].nickname.Equals(nickname) == false)
                {
                    // update unique nickname
                    userNotesList[guid].uniqueNickname = GetUniqueNickname(nickname);
                    // update nickname
                    userNotesList[guid].nickname = nickname;
                }
            }
        }

        /// <summary>
        /// Post a note to the network
        /// </summary>
        /// <param name="note"></param>
        public void PostCommsNote(ushort channel, string text)
        {
            // get note ID
            uint noteId = nextNoteId++;
            // get nickname
            string nickname = main.settingsNickname;
            // get callsign
            string callsign = "";
            // get callsign
            callsign = main.sim != null ? main.sim.userFlightPlan.callsign : "";
            // register user
            RegisterUser(main.guid, nickname, callsign);
            // check that note is note already stored
            if (userNotesList[main.guid].commsList.ContainsKey(noteId) == false)
            {
                // calculate expire time
                double expireTime = main.ElapsedTime + 60.0 * (text.Length == 0 ? COMMS_EMPTY_EXPIRE : COMMS_EXPIRE);
                // add the note to the list
                userNotesList[main.guid].commsList.Add(noteId, new CommsNote(main.ElapsedTime, channel, text, expireTime));
                // send note message
                main.network.SendCommsNoteMessage(main.guid, nickname, callsign, noteId, 0.0f, channel, text);
#if !CONSOLE
                // check for comms window
                if (main.sessionForm.Visible)
                {
                    // refresh
                    if (main.mainForm != null)
                    {
                        main.mainForm.refreshComms = true;
                    }
                }
#endif
            }
        }

        /// <summary>
        /// Store a comms note
        /// </summary>
        /// <param name="guid"></param>
        /// <param name="id"></param>
        /// <param name="?"></param>
        public void ProcessCommsNote(ref Guid guid, string nickname, string callsign, uint noteId, float age, ushort channel, string text)
        {
            // register user
            RegisterUser(guid, nickname, callsign);
            // check that note is note already stored
            if (userNotesList[guid].commsList.ContainsKey(noteId) == false)
            {
                // calculate expire time
                double expireTime = main.ElapsedTime + 60.0 * (text.Length == 0 ? COMMS_EMPTY_EXPIRE : COMMS_EXPIRE);
                // add the new note to the list
                userNotesList[guid].commsList.Add(noteId, new CommsNote(main.ElapsedTime - age, channel, text, expireTime));
                // check if global channel and this is a hub
                if (channel == GLOBAL_CHANNEL && main.settingsHub)
                {
                    // pass on the new note
                    main.network.SendCommsNoteMessage(guid, nickname, callsign, noteId, age, channel, text);
                }
            }
#if !CONSOLE
            // check for comms window
            if (main.sessionForm.Visible)
            {
                // refresh
                if (main.mainForm != null)
                {
                    main.mainForm.refreshComms = true;
                }
            }
#endif
        }
    }
}
