using System.IO;
using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests
{
    /// <summary>
    /// The legacy-id views of a <see cref="NodeId"/> that the tests' kind-0 ids need: the endpoint a
    /// legacy id names, and its legacy wire form. A test id that has no legacy id is a test bug.
    /// </summary>
    static class NodeIdTestExtensions
    {
        static LegacyNodeId Legacy(NodeId id)
        {
            Assert.True(id.TryGetLegacy(out LegacyNodeId legacy));
            return legacy;
        }

        /// <summary>The endpoint of the legacy id's own address and port.</summary>
        public static IPEndPoint ToEndPoint(this NodeId id) => Legacy(id).ToEndPoint(Legacy(id).port);

        public static IPEndPoint ToEndPoint(this NodeId id, ushort port) => Legacy(id).ToEndPoint(port);

        /// <summary>Writes the legacy id's 7 wire bytes.</summary>
        public static void Write(this NodeId id, BinaryWriter writer) => Legacy(id).Write(writer);
    }
}
