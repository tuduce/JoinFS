using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>The node key slots (plan D3, §5.1).</summary>
    public class NodeKeyStoreTests : IDisposable
    {
        readonly string folder = Directory.CreateTempSubdirectory("joinfs-keys-").FullName;
        readonly FakeLog log = new();

        public void Dispose() => Directory.Delete(folder, true);

        NodeKeyStore Store() => new(folder, log);

        [Fact]
        public void SameSlot_ReopensTheSameKey()
        {
            using NodeKeyStore first = Store();
            NodeId id = first.Claim(6112).Id;
            first.Dispose();

            using NodeKeyStore second = Store();

            Assert.Equal(id, second.Claim(6112).Id);
            Assert.Equal(0, second.Slot);
        }

        [Fact]
        public void EveryStart_LogsTheKeyNameAsAnEvent()
        {
            using NodeKeyStore first = Store();
            NodeId id = first.Claim(6112).Id;
            first.Dispose();
            log.Events.Clear();

            using NodeKeyStore second = Store();
            second.Claim(6112);

            // an event, not a NETWORK line: the network monitor is off at start
            Assert.Contains(log.Events, line => line.Contains(id.ToString()) && line.Contains("slot 0"));
        }

        [Fact]
        public void SlotRecordingThisPort_IsPreferred()
        {
            NodeId hub;
            NodeId client;
            using (NodeKeyStore a = Store())
            {
                hub = a.Claim(6112).Id;
                using NodeKeyStore b = Store();
                client = b.Claim(6113).Id;
                Assert.NotEqual(hub, client);
            }

            // restarted in the other order: the port, not the start order, decides
            using NodeKeyStore c = Store();
            Assert.Equal(client, c.Claim(6113).Id);
            using NodeKeyStore d = Store();
            Assert.Equal(hub, d.Claim(6112).Id);
        }

        [Fact]
        public void LiveInstances_GetDifferentKeysAndSlots()
        {
            using NodeKeyStore a = Store();
            using NodeKeyStore b = Store();

            Assert.NotEqual(a.Claim(6112).Id, b.Claim(6112).Id);
            Assert.NotEqual(a.Slot, b.Slot);
        }

        [Fact]
        public void AllSlotsHeld_GivesAKeyForThisRunOnly()
        {
            NodeKeyStore[] held = Enumerable.Range(0, Main.MAX_INSTANCES).Select(_ => Store()).ToArray();
            foreach (NodeKeyStore store in held) store.Claim(6112);
            try
            {
                using NodeKeyStore extra = Store();

                Assert.NotNull(extra.Claim(6112));
                Assert.Equal(-1, extra.Slot);
                Assert.Contains(log.Events, e => e.Contains("this run only"));
            }
            finally
            {
                foreach (NodeKeyStore store in held) store.Dispose();
            }
        }

        [Fact]
        public void CorruptFile_IsReplaced()
        {
            File.WriteAllBytes(NodeKeyStore.SlotPath(folder, 0), [1, 2, 3]);

            using NodeKeyStore store = Store();

            NodeId id = store.Claim(6112).Id;
            Assert.Equal(0, store.Slot);
            Assert.Contains(log.Events, e => e.Contains("created"));
            store.Dispose();
            using NodeKeyStore again = Store();
            Assert.Equal(id, again.Claim(6112).Id);
        }

        [Fact]
        public void MovingToAnotherPort_KeepsTheKeyAndRecordsThePort()
        {
            NodeId moved;
            NodeId stayed;
            using (NodeKeyStore a = Store())
            using (NodeKeyStore b = Store())
            {
                moved = a.Claim(6112).Id;
                stayed = b.Claim(6113).Id;
                Assert.Equal(moved, a.Claim(6200).Id);
            }

            // restarted in the other order, on the new port
            using NodeKeyStore c = Store();
            Assert.Equal(stayed, c.Claim(6113).Id);
            using NodeKeyStore d = Store();
            Assert.Equal(moved, d.Claim(6200).Id);
        }

        [Fact]
        public void OnlyTheSlotsInUseHaveFiles()
        {
            using NodeKeyStore store = Store();
            store.Claim(6112);

            Assert.Single(Directory.GetFiles(folder, "node-*.key"));
        }
    }
}
