namespace MahjongGame.Core.Network
{
    /// <summary>Non-secret E3 recovery hint. It intentionally excludes credentials, seat index, loadout, snapshot, and last sequence.</summary>
    public sealed class ClientReconnectTicket
    {
        public string serverAddress;
        public string username;
        public string roomId;
        public string streamId;
    }

    public interface IClientReconnectTicketStore
    {
        void Save(ClientReconnectTicket ticket);
        bool TryLoad(out ClientReconnectTicket ticket);
        void Clear();
    }

    public sealed class InMemoryClientReconnectTicketStore : IClientReconnectTicketStore
    {
        private ClientReconnectTicket _ticket;

        public void Save(ClientReconnectTicket ticket)
        {
            _ticket = ticket == null ? null : new ClientReconnectTicket
            {
                serverAddress = ticket.serverAddress,
                username = ticket.username,
                roomId = ticket.roomId,
                streamId = ticket.streamId
            };
        }

        public bool TryLoad(out ClientReconnectTicket ticket)
        {
            ticket = _ticket == null ? null : new ClientReconnectTicket
            {
                serverAddress = _ticket.serverAddress,
                username = _ticket.username,
                roomId = _ticket.roomId,
                streamId = _ticket.streamId
            };
            return ticket != null;
        }

        public void Clear() => _ticket = null;
    }

    public static class ClientReconnectTicketPolicy
    {
        public static bool ShouldClearForRoomError(string errorCode) =>
            errorCode == NetworkErrorCodes.RoomNotFound
            || errorCode == NetworkErrorCodes.SeatExpired
            || errorCode == NetworkErrorCodes.StreamMismatch
            || errorCode == NetworkErrorCodes.ProtocolMismatch;

        public static bool ShouldClearForFinalResultExit() => true;

        /// <summary>Tickets are only a recovery hint for the same development identity that created them.</summary>
        public static bool MatchesUsername(ClientReconnectTicket ticket, string username)
        {
            if (ticket == null) return false;
            return UsernameIdentityPolicy.TryNormalize(ticket.username, out _, out var ticketPlayerId, out _)
                && UsernameIdentityPolicy.TryNormalize(username, out _, out var loginPlayerId, out _)
                && string.Equals(ticketPlayerId, loginPlayerId, System.StringComparison.Ordinal);
        }

        public static bool ShouldAutoReconnectAfterLogin(ClientReconnectTicket ticket, string username)
        {
            return MatchesUsername(ticket, username);
        }
    }
}
