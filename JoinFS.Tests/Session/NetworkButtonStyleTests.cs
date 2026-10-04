using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>Colour of the Network button: green connected, orange on the way (or retrying), red not connected.</summary>
    public class NetworkButtonStyleTests
    {
        [Theory]
        [InlineData(SessionState.Connected, false, false, NetworkButtonLook.Active)]
        [InlineData(SessionState.Connecting, false, false, NetworkButtonLook.Waiting)]
        [InlineData(SessionState.Unconnected, false, false, NetworkButtonLook.Inactive)]
        // lost connection, retrying in the background: orange in every state the retry passes through
        [InlineData(SessionState.Connected, true, false, NetworkButtonLook.Waiting)]
        [InlineData(SessionState.Connecting, true, false, NetworkButtonLook.Waiting)]
        [InlineData(SessionState.Unconnected, true, false, NetworkButtonLook.Waiting)]
        // joining a user: still on the way
        [InlineData(SessionState.Unconnected, false, true, NetworkButtonLook.Waiting)]
        public void LooksLike(SessionState state, bool reconnecting, bool joiningUser, NetworkButtonLook expected)
        {
            Assert.Equal(expected, NetworkButtonStyle.For(state, reconnecting, joiningUser));
        }

        [Fact]
        public void Looks_AreAppendOnly()
        {
            Assert.Equal(0, (int)NetworkButtonLook.Waiting);
            Assert.Equal(1, (int)NetworkButtonLook.Active);
            Assert.Equal(2, (int)NetworkButtonLook.Inactive);
        }

        [Fact]
        public void SessionOrigin_ValuesAreStable()
        {
            Assert.Equal(0, (int)Network.SessionOrigin.None);
            Assert.Equal(1, (int)Network.SessionOrigin.Join);
            Assert.Equal(2, (int)Network.SessionOrigin.Login);
            Assert.Equal(3, (int)Network.SessionOrigin.Global);
        }

        [Fact]
        public void ReconnectAction_ValuesAreStable()
        {
            Assert.Equal(0, (int)ReconnectAction.None);
            Assert.Equal(1, (int)ReconnectAction.Retry);
            Assert.Equal(2, (int)ReconnectAction.GiveUp);
        }
    }
}
