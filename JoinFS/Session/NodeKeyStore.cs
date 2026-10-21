using JoinFS.Net;
using System;
using System.Collections.Generic;
using System.IO;

namespace JoinFS
{
    /// <summary>
    /// Keeps this instance's <see cref="NodeKey"/> (docs/network/jfp2/standalone-ipv6-plan.md D3, §5.1). The
    /// storage folder is shared by the instances of one variant, so each gets a slot, a file
    /// <c>node-0.key</c> to <c>node-3.key</c> (<see cref="Main.MAX_INSTANCES"/> of them), which it holds
    /// open with <see cref="FileShare.None"/> while it runs. The slot is the one whose file records this
    /// instance's UDP port, if it is free, otherwise the first free one, which then records the port;
    /// taking slots in start order alone would swap keys between a hub on 6112 and a client on 6113
    /// whenever they start in another order. App thread.
    ///
    /// The file is the key as plain bytes (no OS-specific protection, so it is the same everywhere):
    /// version u8 (1), private key 32 bytes, public key 64 bytes, port u16 LE. An unreadable file gets a
    /// new key. When no slot is free (more instances than slots, a read-only folder) the key lasts for
    /// this run only.
    /// </summary>
    public sealed class NodeKeyStore : IDisposable
    {
        const byte FileVersion = 1;
        const int FileSize = 1 + NodeKey.PrivateKeySize + NodeKey.PublicKeySize + 2;

        readonly string folder;
        readonly ISessionLog log;

        FileStream held;
        int slot = -1;
        NodeKey key;
        int port;

        public NodeKeyStore(string folder, ISessionLog log)
        {
            this.folder = folder;
            this.log = log;
        }

        /// <summary>The key, or null until <see cref="Claim"/>.</summary>
        public NodeKey Key => key;

        /// <summary>The slot held, or -1 (no key yet, or a key for this run only).</summary>
        public int Slot => slot;

        public static string SlotPath(string folder, int slot) => Path.Combine(folder, "node-" + slot + ".key");

        /// <summary>
        /// Take a slot and its key for the UDP port this instance is bound to (the first call), or record
        /// the new port in the slot already held.
        /// </summary>
        public NodeKey Claim(int port)
        {
            if (key == null)
            {
                this.port = port;
                Take();
            }
            else if (port != this.port)
            {
                this.port = port;
                Write();
            }
            return key;
        }

        void Take()
        {
            // existing files this instance can open, and the first slot that has no file yet
            List<(int slot, FileStream stream, NodeKey key, int port)> free = [];
            int firstMissing = -1;
            for (int i = 0; i < Main.MAX_INSTANCES; i++)
            {
                if (!File.Exists(SlotPath(folder, i)))
                {
                    if (firstMissing < 0)
                    {
                        firstMissing = i;
                    }
                    continue;
                }
                FileStream stream = Open(i);
                if (stream != null)
                {
                    bool readable = TryRead(stream, out NodeKey stored, out int storedPort);
                    free.Add((i, stream, readable ? stored : null, readable ? storedPort : 0));
                }
            }

            // the slot that records our port, else the lowest free one
            int chosen = free.FindIndex(entry => entry.key != null && entry.port == port);
            if (chosen < 0 && firstMissing >= 0 && (free.Count == 0 || firstMissing < free[0].slot))
            {
                FileStream created = Open(firstMissing);
                if (created != null)
                {
                    free.Insert(0, (firstMissing, created, null, 0));
                }
            }
            if (chosen < 0 && free.Count > 0)
            {
                chosen = 0;
            }
            for (int i = 0; i < free.Count; i++)
            {
                if (i != chosen)
                {
                    free[i].stream.Dispose();
                }
            }

            if (chosen < 0)
            {
                key = NodeKey.Generate();
                log.Event("No free node key slot in " + folder + ": the node key " + key.Id + " is for this run only");
                return;
            }

            (slot, held, key, _) = free[chosen];
            if (key == null)
            {
                key = NodeKey.Generate();
                log.Event("Node key " + key.Id + " created in slot " + slot);
            }
            else
            {
                log.Event("Node key " + key.Id + " from slot " + slot);
            }
            Write();
        }

        FileStream Open(int index)
        {
            try
            {
                return new FileStream(SlotPath(folder, index), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // held by another instance, or not ours to open
                return null;
            }
        }

        static bool TryRead(FileStream stream, out NodeKey key, out int port)
        {
            key = null;
            port = 0;
            byte[] data = new byte[FileSize];
            stream.Position = 0;
            if (stream.Length != FileSize || stream.Read(data, 0, FileSize) != FileSize || data[0] != FileVersion)
            {
                return false;
            }
            ReadOnlySpan<byte> bytes = data;
            if (!NodeKey.TryLoad(bytes.Slice(1, NodeKey.PrivateKeySize), bytes.Slice(1 + NodeKey.PrivateKeySize, NodeKey.PublicKeySize), out key))
            {
                return false;
            }
            port = bytes[FileSize - 2] | bytes[FileSize - 1] << 8;
            return true;
        }

        void Write()
        {
            if (held == null)
            {
                return;
            }
            byte[] data = new byte[FileSize];
            data[0] = FileVersion;
            key.PrivateKey.CopyTo(data.AsSpan(1));
            key.PublicKey.CopyTo(data.AsSpan(1 + NodeKey.PrivateKeySize));
            data[FileSize - 2] = (byte)port;
            data[FileSize - 1] = (byte)(port >> 8);
            try
            {
                held.Position = 0;
                held.Write(data, 0, FileSize);
                held.SetLength(FileSize);
                held.Flush(true);
            }
            catch (IOException ex)
            {
                log.Event("Cannot write node key slot " + slot + ": " + ex.Message);
            }
        }

        /// <summary>Release the slot for another instance.</summary>
        public void Dispose()
        {
            held?.Dispose();
            held = null;
            slot = -1;
        }
    }
}
