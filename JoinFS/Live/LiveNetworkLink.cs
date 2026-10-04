using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JoinFS.Net;
using JoinFS.Properties;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The network button of the new UI, on the real session. Joining, leaving and creating go through the same Main and Network calls the
    /// old window used, and the state comes from the session snapshot, read the way the old button read it.
    ///
    /// A protected session is a conversation. The join goes out, the session answers "password required", and only then is a password
    /// asked for: first the one remembered for that session, then the user. Poll() holds that conversation, as the old window's refresh did.
    /// </summary>
    class LiveNetworkLink : INetworkLink
    {
        readonly Main main;

        // a password the user already gave before joining, to answer a request with
        string suppliedPassword = null;
        // what the last join was to, for naming the session in the prompt
        string lastJoinLabel = null;
        // set while the user is being asked: the session that wants the password, and its end point
        string awaitingLabel = null;
        IPEndPoint awaitingEndPoint = null;

        public LiveNetworkLink(Main main)
        {
            this.main = main;
        }

        public bool ReportsState => true;

        /// <summary>
        /// The state, as the old Network button showed it: connected is green, on the way or retrying is orange, anything else is red.
        /// </summary>
        public static ConnectionState MapState(SessionState state, bool reconnecting, bool joiningUser)
        {
            return NetworkButtonStyle.For(state, reconnecting, joiningUser) switch
            {
                NetworkButtonLook.Active => ConnectionState.Connected,
                NetworkButtonLook.Waiting => ConnectionState.Connecting,
                _ => ConnectionState.Disconnected,
            };
        }

        public ConnectionState State
        {
            get
            {
                lock (main.conch)
                {
                    return MapState(main.network.Snapshot.State, main.network.Reconnecting, main.network.scheduleJoinUser);
                }
            }
        }

        public string MeshCode => UserDirectory.UuidToString(main.uuid);

        public string PasswordRequestedBy => awaitingLabel;

        public void Poll()
        {
            if (awaitingEndPoint != null)
            {
                // the user is being asked already
                return;
            }

            lock (main.conch)
            {
                Network network = main.network;
                if (network.Snapshot.State != SessionState.Connecting || network.Snapshot.JoinResult != JoinResult.PasswordRequired)
                {
                    return;
                }

                // the session wants a password: leave it, and join again with one
                network.ScheduleLeave();
                IPEndPoint endPoint = network.joinEndPoint;

                // the user gave one before joining
                if (suppliedPassword != null)
                {
                    JoinWithPassword(endPoint, suppliedPassword);
                    suppliedPassword = null;
                    return;
                }

                // the one that worked last time, once
                if (main.attempedUsedPassword == false)
                {
                    uint used = main.log.GetUsedPassword(endPoint);
                    if (used != 0)
                    {
                        network.ScheduleJoin(endPoint, used);
                        main.attempedUsedPassword = true;
                        return;
                    }
                }

                // nothing known: ask
                awaitingEndPoint = endPoint;
                awaitingLabel = lastJoinLabel ?? endPoint.ToString();
            }
        }

        public Task JoinAsync(AddressBookEntry hub, string password, CancellationToken cancellationToken)
        {
            suppliedPassword = password;
            lastJoinLabel = hub.Name;
            awaitingEndPoint = null;
            awaitingLabel = null;

            // the same call the old Join button made; it resolves a hub, an address book name or an address, and joins Global by its name
            main.Join(hub.BuiltIn ? Resources.Strings.Global : hub.Name);
            return Task.CompletedTask;
        }

        public Task<string> CreateMeshAsync(CancellationToken cancellationToken)
        {
            lock (main.conch)
            {
                main.network.ScheduleLeave();
                main.network.ScheduleCreate();
            }
            return Task.FromResult(MeshCode);
        }

        public Task DisconnectAsync()
        {
            suppliedPassword = null;
            awaitingEndPoint = null;
            awaitingLabel = null;
            lock (main.conch)
            {
                // a join that is still looking for its user, then the session itself
                main.network.scheduleJoinUser = false;
                main.network.ScheduleLeave();
            }
            return Task.CompletedTask;
        }

        public void SubmitPassword(string password)
        {
            IPEndPoint endPoint = awaitingEndPoint;
            awaitingEndPoint = null;
            awaitingLabel = null;
            if (endPoint != null)
            {
                lock (main.conch)
                {
                    JoinWithPassword(endPoint, password);
                }
            }
        }

        public void CancelPasswordRequest()
        {
            // the session was left when it asked
            awaitingEndPoint = null;
            awaitingLabel = null;
        }

        /// <summary>
        /// Join again with a password and remember it for that session. The caller holds the lock.
        /// </summary>
        void JoinWithPassword(IPEndPoint endPoint, string password)
        {
            uint hash = NetHash.HashPassword(password.TrimStart(' ').TrimEnd(' '));
            main.network.ScheduleJoin(endPoint, hash);
            if (hash != 0)
            {
                main.log.AddPassword(endPoint, hash);
            }
        }
    }
}
