namespace JoinFS.Tests.Net
{
    /// <summary>
    /// Regression coverage for the reconnect-after-hub-restart bug found while testing
    /// Network.CheckForOrphanedSession live: a hub restart rolls a fresh Suid
    /// (MeshManager.Create's suid = (uint)core.Clock.Timestamp), but a client that still thinks
    /// it's Connected (its own Suid was never cleared - that's the whole premise of "orphaned")
    /// only adopts a JoinReply's Suid when !Connected (MeshManager.Handle(JoinReply)). Rejoining
    /// without first Leave()-ing therefore sends a JoinRequest and gets a JoinReply, but silently
    /// never re-establishes anything - exactly what CheckForOrphanedSession originally did, and
    /// why the Network button kept reading "reconnecting" (Waiting-orange) forever instead of
    /// recovering.
    /// </summary>
    public class MeshManagerReconnectTests
    {
        [Fact]
        public void RejoinWithoutLeave_AfterHubRestart_NeverAdoptsTheNewSuid()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1");
            TestNode client = mesh.Add("198.51.100.2");

            hub.Core.Mesh.Create(false, 0, false, "");
            client.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            uint originalSuid = client.Core.Mesh.Suid;
            Assert.True(client.Core.Mesh.Connected);
            Assert.True(client.Knows(hub));

            // hub "restarts": a fresh Create() rolls a new, clock-timestamp-based suid
            mesh.Clock.Advance(5);
            hub.Core.Mesh.Create(false, 0, false, "");
            Assert.NotEqual(originalSuid, hub.Core.Mesh.Suid);

            // client rejoins WITHOUT leaving first - the bug this regression guards against
            client.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            // the JoinRequest/JoinReply round-trip happened, but the client's stale suid meant
            // Handle(JoinReply) silently discarded it
            Assert.Equal(originalSuid, client.Core.Mesh.Suid);
        }

        [Fact]
        public void LeaveThenRejoin_AfterHubRestart_AdoptsTheNewSuidAndReconnects()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1");
            TestNode client = mesh.Add("198.51.100.2");

            hub.Core.Mesh.Create(false, 0, false, "");
            client.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            uint originalSuid = client.Core.Mesh.Suid;

            mesh.Clock.Advance(5);
            hub.Core.Mesh.Create(false, 0, false, "");
            uint newSuid = hub.Core.Mesh.Suid;
            Assert.NotEqual(originalSuid, newSuid);

            // the fix: Leave() resets Suid to 0 before rejoining, so the fresh JoinReply's Suid is
            // adopted (Handle(JoinReply)'s "!Connected" branch) instead of being compared against
            // a stale one
            client.Core.Mesh.Leave();
            client.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            Assert.Equal(newSuid, client.Core.Mesh.Suid);
            Assert.True(client.Knows(hub));
        }
    }
}
