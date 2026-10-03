namespace JoinFS.Tests.Net
{
    /// <summary>
    /// What the automatic reconnect (Network.RetrySession) relies on in the mesh: a hub restart rolls a
    /// fresh Suid (MeshManager.Create's suid = (uint)core.Clock.Timestamp), and a client only adopts a
    /// JoinReply's Suid while it is not Connected (MeshManager.Handle(JoinReply)). So the client has to
    /// Leave() - which resets its Suid to 0 - before it rejoins.
    /// </summary>
    public class MeshManagerReconnectTests
    {
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

            client.Core.Mesh.Leave();
            Assert.False(client.Core.Mesh.Connected);
            client.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            Assert.Equal(newSuid, client.Core.Mesh.Suid);
            Assert.True(client.Knows(hub));
        }
    }
}
