using JoinFS.Net;

namespace JoinFS
{
    /// <summary>How the Network button looks. Append new members only.</summary>
    public enum NetworkButtonLook
    {
        /// <summary>Orange: on the way, or retrying a lost connection.</summary>
        Waiting = 0,
        /// <summary>Green: connected.</summary>
        Active = 1,
        /// <summary>Red: not connected.</summary>
        Inactive = 2,
    }

    /// <summary>Which look the Network button has for the state of the session.</summary>
    public static class NetworkButtonStyle
    {
        public static NetworkButtonLook For(SessionState state, bool reconnecting, bool joiningUser)
        {
            // the retry passes through Connecting and a brief Unconnected: stay orange until it succeeds
            // or gives up (which resets the session and clears reconnecting)
            if (reconnecting)
            {
                return NetworkButtonLook.Waiting;
            }
            return state switch
            {
                SessionState.Connected => NetworkButtonLook.Active,
                SessionState.Unconnected => joiningUser ? NetworkButtonLook.Waiting : NetworkButtonLook.Inactive,
                _ => NetworkButtonLook.Waiting,
            };
        }
    }
}
