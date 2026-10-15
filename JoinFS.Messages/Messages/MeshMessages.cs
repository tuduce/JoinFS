using System.Collections.Generic;

// Canonical mesh messages: session membership (join/login/leave), liveness (pulse) and route
// discovery (pathfinder). Handled by the core's MeshManager, never by the application. Only the
// legacy plugin carries these today (docs/network-plugin-architecture.md §2.4).

namespace JoinFS.Net
{
    public enum JoinResult : byte
    {
        Accepted = 0,
        PasswordRequired = 1,
        LoginRequired = 2,
    }

    public enum LoginResult : byte
    {
        Accepted = 0,
        InvalidAddress = 1,
        VerifyPassword = 2,
        InvalidPassword = 3,
    }

    public struct KnownNode
    {
        public NodeId Node;
        /// <summary>The port the node was actually seen on (may differ from Node.port behind NAT).</summary>
        public ushort Port;
    }

    public struct JoinRequest : IMessage
    {
        public static MessageKind Kind => MessageKind.Join;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint PasswordHash;
    }

    public struct JoinReply : IMessage
    {
        public static MessageKind Kind => MessageKind.JoinReply;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint Suid;
        public List<KnownNode> Nodes;
    }

    public struct JoinFail : IMessage
    {
        public static MessageKind Kind => MessageKind.JoinFail;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public JoinResult Result;
    }

    public struct LoginRequest : IMessage
    {
        public static MessageKind Kind => MessageKind.Login;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        /// <summary>As sent; may not be a valid address.</summary>
        public string Email;
        public uint PasswordHash;
        /// <summary>The user has verified this password; the hub should store it.</summary>
        public bool Verify;
    }

    public struct LoginFail : IMessage
    {
        public static MessageKind Kind => MessageKind.LoginFail;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public LoginResult Result;
    }

    /// <summary>Introduces a newly connected node to the rest of the session.</summary>
    public struct AddNode : IMessage
    {
        public static MessageKind Kind => MessageKind.AddNode;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint Suid;
        public KnownNode Node;
    }

    public struct Leave : IMessage
    {
        public static MessageKind Kind => MessageKind.Leave;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint Suid;
    }

    public struct Pulse : IMessage
    {
        public static MessageKind Kind => MessageKind.Pulse;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint Suid;
        /// <summary>Sender's clock timestamp, echoed back in <see cref="PulseResponse"/> for RTT.</summary>
        public long Time;
        public bool LowBandwidth;
    }

    public struct PulseResponse : IMessage
    {
        public static MessageKind Kind => MessageKind.PulseResponse;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public long Time;
    }

    /// <summary>Asks a directly connected node which of <see cref="Nodes"/> it can reach directly.</summary>
    public struct Pathfinder : IMessage
    {
        public static MessageKind Kind => MessageKind.Pathfinder;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint Suid;
        public List<NodeId> Nodes;
    }

    /// <summary>The subset of a <see cref="Pathfinder"/> request's nodes the responder can relay to.</summary>
    public struct PathfinderResponse : IMessage
    {
        public static MessageKind Kind => MessageKind.PathfinderResponse;
        public void Dispatch(IMessageHandler h, in MessageMeta meta) => h.Handle(in meta, in this);

        public uint Suid;
        public List<NodeId> Nodes;
    }
}
