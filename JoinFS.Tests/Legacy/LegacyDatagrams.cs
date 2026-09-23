using JoinFS.Net;

namespace JoinFS.Tests.Legacy
{
    /// <summary>Builds raw legacy datagrams the way a remote (released) JoinFS node would.</summary>
    static class LegacyDatagrams
    {
        public static byte[] Build(NodeId sender, NodeId recipient, bool internalMessage, bool guaranteed, ushort guaranteedId,
            Action<BinaryWriter> payload, bool forwarded = false)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((short)0x520b);
            byte flags = 0;
            if (internalMessage) flags |= 0x01;
            if (guaranteed) flags |= 0x02;
            if (forwarded) flags |= 0x04;
            w.Write(flags);
            w.Write(guaranteedId);
            w.Write((byte)0);
            w.Write((byte)1);
            sender.Write(w);
            recipient.Write(w);
            payload(w);
            w.Flush();
            return ms.ToArray();
        }
    }
}
