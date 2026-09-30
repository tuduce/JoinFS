namespace JoinFS.Net
{
    /// <summary>
    /// Receives canonical messages through double dispatch (<see cref="IMessage.Dispatch"/>), so a
    /// consumer gets a statically typed overload per message with no casts, boxing or switch on
    /// kind. Every overload defaults to "ignore": the mesh manager, the directory and the
    /// application each implement only the kinds they own.
    /// </summary>
    public interface IMessageHandler
    {
        // objects
        void Handle(in MessageMeta meta, in PositionUpdate message) { }
        void Handle(in MessageMeta meta, in ObjectPositionUpdate message) { }
        void Handle(in MessageMeta meta, in IdentityUpdate message) { }
        void Handle(in MessageMeta meta, in VariableSyncUpdate message) { }
        void Handle(in MessageMeta meta, in EventUpdate message) { }
        void Handle(in MessageMeta meta, in RemoveObject message) { }
        void Handle(in MessageMeta meta, in FlightPlanUpdate message) { }
        void Handle(in MessageMeta meta, in ShowOnRadar message) { }

        // peers / session
        void Handle(in MessageMeta meta, in PeerInfo message) { }
        void Handle(in MessageMeta meta, in StatusRequestUpdate message) { }
        void Handle(in MessageMeta meta, in StatusUpdate message) { }
        void Handle(in MessageMeta meta, in WeatherRequest message) { }
        void Handle(in MessageMeta meta, in WeatherReply message) { }
        void Handle(in MessageMeta meta, in WeatherUpdate message) { }

        // hub directory
        void Handle(in MessageMeta meta, in HubList message) { }
        void Handle(in MessageMeta meta, in UserListRequest message) { }
        void Handle(in MessageMeta meta, in HubUserUpdate message) { }
        void Handle(in MessageMeta meta, in UserPositionsRequest message) { }
        void Handle(in MessageMeta meta, in UserPositions message) { }
        void Handle(in MessageMeta meta, in OnlineAnnouncement message) { }
        void Handle(in MessageMeta meta, in UserNuidRequest message) { }
        void Handle(in MessageMeta meta, in UserNuidReply message) { }

        // comms
        void Handle(in MessageMeta meta, in CommsRequest message) { }
        void Handle(in MessageMeta meta, in NotesBundle message) { }

        // mesh
        void Handle(in MessageMeta meta, in JoinRequest message) { }
        void Handle(in MessageMeta meta, in JoinReply message) { }
        void Handle(in MessageMeta meta, in JoinFail message) { }
        void Handle(in MessageMeta meta, in LoginRequest message) { }
        void Handle(in MessageMeta meta, in LoginFail message) { }
        void Handle(in MessageMeta meta, in AddNode message) { }
        void Handle(in MessageMeta meta, in Leave message) { }
        void Handle(in MessageMeta meta, in Pulse message) { }
        void Handle(in MessageMeta meta, in PulseResponse message) { }
        void Handle(in MessageMeta meta, in Pathfinder message) { }
        void Handle(in MessageMeta meta, in PathfinderResponse message) { }
    }
}
