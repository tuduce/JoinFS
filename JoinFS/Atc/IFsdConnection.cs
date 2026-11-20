namespace JoinFS
{
    /// <summary>
    /// One TCP connection of an ATC client, as the <see cref="AtcGateway"/> sees it. The gateway never touches a socket:
    /// tests give it a fake, <see cref="FsdListener"/> gives it a real one. Both calls return at once.
    /// </summary>
    public interface IFsdConnection
    {
        /// <summary>The client's address, for the log.</summary>
        string RemoteAddress { get; }

        /// <summary>Queue one line (without its line ending) for the client. The connection adds the line ending.</summary>
        void Send(string line);

        /// <summary>Close once every queued line has been written.</summary>
        void Close();
    }

    /// <summary>What <see cref="FsdListener"/> reports about its connections. Always called on the app thread.</summary>
    public interface IFsdHandler
    {
        void OnConnected(IFsdConnection connection);

        /// <summary>A line of the client, without its line ending, never empty.</summary>
        void OnLine(IFsdConnection connection, string line);

        /// <summary>The connection is gone, from either side. May come after the handler closed it itself.</summary>
        void OnClosed(IFsdConnection connection);
    }
}
