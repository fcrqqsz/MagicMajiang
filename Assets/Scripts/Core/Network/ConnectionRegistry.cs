using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Network.Transport;

namespace MahjongGame.Core.Network
{
    /// <summary>Authoritative ownership of a websocket connection and its room seat.</summary>
    public sealed class ConnectionRegistry
    {
        public sealed class ConnectionRecord
        {
            public string ConnectionId;
            public GameEndpoint Endpoint;
            public string RoomId;
            public int SeatIndex = -1;
            public string Nickname = "Player";
            public DateTime LastActivityUtc;
        }

        private readonly Dictionary<string, ConnectionRecord> _connections = new Dictionary<string, ConnectionRecord>();

        public bool Register(string connectionId, GameEndpoint endpoint)
        {
            if (string.IsNullOrEmpty(connectionId) || endpoint == null || _connections.ContainsKey(connectionId)) return false;
            _connections[connectionId] = new ConnectionRecord { ConnectionId = connectionId, Endpoint = endpoint, LastActivityUtc = DateTime.UtcNow };
            return true;
        }

        public bool TryGet(string connectionId, out ConnectionRecord record) => _connections.TryGetValue(connectionId, out record);

        public bool SetNickname(string connectionId, string nickname)
        {
            if (!TryGet(connectionId, out var record)) return false;
            record.Nickname = string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname.Trim();
            return true;
        }

        public bool Touch(string connectionId, DateTime utcNow)
        {
            if (!TryGet(connectionId, out var record)) return false;
            record.LastActivityUtc = utcNow;
            return true;
        }

        public List<string> GetExpiredRoomConnections(DateTime utcNow)
        {
            return _connections.Values
                .Where(record => !string.IsNullOrEmpty(record.RoomId)
                    && ConnectionLivenessPolicy.IsExpired(record.LastActivityUtc, utcNow))
                .Select(record => record.ConnectionId)
                .ToList();
        }

        public bool BindRoomSeat(string connectionId, string roomId, int seatIndex)
        {
            if (!TryGet(connectionId, out var record) || !string.IsNullOrEmpty(record.RoomId) || seatIndex < 0 || seatIndex > 3) return false;
            record.RoomId = roomId;
            record.SeatIndex = seatIndex;
            return true;
        }

        public bool UnbindRoomSeat(string connectionId)
        {
            if (!TryGet(connectionId, out var record)) return false;
            record.RoomId = null;
            record.SeatIndex = -1;
            return true;
        }

        public bool Remove(string connectionId, out ConnectionRecord record)
        {
            if (!TryGet(connectionId, out record)) return false;
            _connections.Remove(connectionId);
            return true;
        }
    }
}
