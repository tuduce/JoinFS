using System;
using System.Collections.Concurrent;

namespace JoinFS.Net
{
    /// <summary>Receives what the network thread queued for the app, in order.</summary>
    public interface INetworkEventHandler
    {
        void OnNetworkEvent(in NetworkEvent e);
    }

    /// <summary>An item on the network→app queue: a canonical message or a session event.</summary>
    interface IInbound
    {
        /// <summary>Hand the item to the app's handlers, then return it to its pool.</summary>
        void Dispatch(IMessageHandler messages, INetworkEventHandler events);
    }

    /// <summary>A canonical message on its way to the app, in a pooled box (no allocation per message in steady state).</summary>
    sealed class InboundMessage<T> : IInbound where T : struct, IMessage
    {
        static readonly ConcurrentQueue<InboundMessage<T>> pool = new();

        MessageMeta meta;
        T message;

        public static InboundMessage<T> Rent(in MessageMeta meta, in T message)
        {
            if (!pool.TryDequeue(out InboundMessage<T> item)) item = new InboundMessage<T>();
            item.meta = meta;
            item.message = message;
            return item;
        }

        public void Dispatch(IMessageHandler messages, INetworkEventHandler events)
        {
            MessageMeta m = meta;
            T body = message;
            meta = default;
            message = default;
            pool.Enqueue(this);
            body.Dispatch(messages, m);
        }
    }

    sealed class InboundEvent(NetworkEvent e) : IInbound
    {
        public void Dispatch(IMessageHandler messages, INetworkEventHandler events) => events?.OnNetworkEvent(e);
    }

    /// <summary>Work for the network thread: an app command, a message to send, or a received datagram.</summary>
    interface IWork
    {
        void Execute(NetworkCore core);
    }

    sealed class ActionWork(Action<NetworkCore> action) : IWork
    {
        public void Execute(NetworkCore core) => action(core);
    }

    /// <summary>An outgoing canonical message with its recipients, pooled.</summary>
    sealed class SendWork<T> : IWork where T : struct, IMessage
    {
        static readonly ConcurrentQueue<SendWork<T>> pool = new();

        MessageMeta meta;
        T message;
        NodeId[] recipients = new NodeId[4];
        int count;

        public static SendWork<T> Rent(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients)
        {
            if (!pool.TryDequeue(out SendWork<T> item)) item = new SendWork<T>();
            item.meta = meta;
            item.message = message;
            if (item.recipients.Length < recipients.Length) item.recipients = new NodeId[Math.Max(recipients.Length, item.recipients.Length * 2)];
            recipients.CopyTo(item.recipients);
            item.count = recipients.Length;
            return item;
        }

        public void Execute(NetworkCore core)
        {
            try
            {
                core.Send(meta, message, new ReadOnlySpan<NodeId>(recipients, 0, count));
            }
            finally
            {
                meta = default;
                message = default;
                count = 0;
                pool.Enqueue(this);
            }
        }
    }

    /// <summary>
    /// One of our objects' identity plus a position to send: the identity lands in the object cache
    /// before the position is routed, so every protocol can use it. Pooled (sent every tick).
    /// </summary>
    sealed class ObjectStateWork<T> : IWork where T : struct, IMessage
    {
        static readonly ConcurrentQueue<ObjectStateWork<T>> pool = new();

        IdentityUpdate identity;
        T position;
        NodeId[] recipients = new NodeId[4];
        int count;

        public static ObjectStateWork<T> Rent(in IdentityUpdate identity, in T position, ReadOnlySpan<NodeId> recipients)
        {
            if (!pool.TryDequeue(out ObjectStateWork<T> item)) item = new ObjectStateWork<T>();
            item.identity = identity;
            item.position = position;
            if (item.recipients.Length < recipients.Length) item.recipients = new NodeId[Math.Max(recipients.Length, item.recipients.Length * 2)];
            recipients.CopyTo(item.recipients);
            item.count = recipients.Length;
            return item;
        }

        public void Execute(NetworkCore core)
        {
            try
            {
                core.Objects.SetIdentity(core.Identity.Id, identity);
                core.Send(new MessageMeta(), position, new ReadOnlySpan<NodeId>(recipients, 0, count));
            }
            finally
            {
                identity = default;
                position = default;
                count = 0;
                pool.Enqueue(this);
            }
        }
    }

    /// <summary>Sends to every peer in the session (resolved on the network thread).</summary>
    sealed class BroadcastWork<T>(T message, bool guaranteed) : IWork where T : struct, IMessage
    {
        public void Execute(NetworkCore core) => core.Broadcast(message, guaranteed);
    }

    /// <summary>A datagram from the receive thread, in a buffer rented from ArrayPool.</summary>
    sealed class DatagramWork : IWork
    {
        static readonly ConcurrentQueue<DatagramWork> pool = new();

        System.Net.IPEndPoint from;
        byte[] buffer;
        int length;

        public static DatagramWork Rent(System.Net.IPEndPoint from, byte[] buffer, int length)
        {
            if (!pool.TryDequeue(out DatagramWork item)) item = new DatagramWork();
            item.from = from;
            item.buffer = buffer;
            item.length = length;
            return item;
        }

        public void Execute(NetworkCore core)
        {
            try
            {
                core.OnDatagram(from, buffer.AsSpan(0, length));
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                from = null;
                buffer = null;
                pool.Enqueue(this);
            }
        }
    }
}
