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
            public long Generation;
            public bool IsAuthenticated;
            public string PlayerId;
            public string DisplayName;
            public string RoomId;
            public int SeatIndex = -1;
            public DateTime LastActivityUtc;
        }

        private readonly Dictionary<string, ConnectionRecord> _connections = new Dictionary<string, ConnectionRecord>();
        private readonly Dictionary<string, long> _lastGenerationByConnectionId = new Dictionary<string, long>();
        private readonly Dictionary<string, string> _activeConnectionByPlayerId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _heartbeatTimeout;

        public ConnectionRegistry()
            : this(ConnectionLivenessPolicy.DefaultHeartbeatTimeoutSeconds)
        {
        }

        public ConnectionRegistry(int heartbeatTimeoutSeconds)
        {
            _heartbeatTimeout = TimeSpan.FromSeconds(Math.Max(1, heartbeatTimeoutSeconds));
        }

        public bool Register(string connectionId, GameEndpoint endpoint)
        {
            if (string.IsNullOrEmpty(connectionId) || endpoint == null) return false;
            long generation = _lastGenerationByConnectionId.TryGetValue(connectionId, out var previousGeneration)
                ? previousGeneration + 1
                : 1;
            return Register(connectionId, endpoint, generation);
        }

        /// <summary>Registers the generation allocated synchronously at physical connection ingress.</summary>
        public bool Register(string connectionId, GameEndpoint endpoint, long ingressGeneration)
        {
            if (string.IsNullOrEmpty(connectionId) || endpoint == null || ingressGeneration <= 0) return false;
            if (_lastGenerationByConnectionId.TryGetValue(connectionId, out var previousGeneration)
                && ingressGeneration <= previousGeneration) return false;

            if (_connections.TryGetValue(connectionId, out var replacedRecord))
                ReleaseIdentity(replacedRecord);

            _lastGenerationByConnectionId[connectionId] = ingressGeneration;
            _connections[connectionId] = new ConnectionRecord
            {
                ConnectionId = connectionId,
                Endpoint = endpoint,
                Generation = ingressGeneration,
                LastActivityUtc = DateTime.UtcNow
            };
            return true;
        }

        public bool TryGet(string connectionId, out ConnectionRecord record) => _connections.TryGetValue(connectionId, out record);

        /// <summary>
        /// Returns the still-active physical record that must be detached from its room before a
        /// newer ingress generation with the same connection ID replaces it.
        /// </summary>
        public bool TryGetSupersededRecord(string connectionId, long incomingGeneration, out ConnectionRecord record)
        {
            record = null;
            return incomingGeneration > 0
                && _connections.TryGetValue(connectionId, out record)
                && incomingGeneration > record.Generation;
        }

        public bool TryAuthenticate(string connectionId, GameEndpoint endpoint, AuthenticatedIdentity identity, DateTime utcNow, out string errorCode)
        {
            errorCode = null;
            if (!IsCurrentEndpoint(connectionId, endpoint, out var record)
                || identity == null
                || string.IsNullOrWhiteSpace(identity.PlayerId)
                || string.IsNullOrWhiteSpace(identity.DisplayName))
            {
                errorCode = NetworkErrorCodes.AuthenticationRequired;
                return false;
            }

            if (_activeConnectionByPlayerId.TryGetValue(identity.PlayerId, out var activeConnectionId)
                && !string.Equals(activeConnectionId, connectionId, StringComparison.Ordinal))
            {
                errorCode = NetworkErrorCodes.IdentityInUse;
                return false;
            }

            ReleaseIdentity(record);
            record.IsAuthenticated = true;
            record.PlayerId = identity.PlayerId;
            record.DisplayName = identity.DisplayName;
            record.LastActivityUtc = utcNow;
            _activeConnectionByPlayerId[identity.PlayerId] = connectionId;
            return true;
        }

        public bool Touch(string connectionId, DateTime utcNow)
        {
            if (!TryGet(connectionId, out var record)) return false;
            record.LastActivityUtc = utcNow;
            return true;
        }

        public bool Touch(string connectionId, GameEndpoint endpoint, DateTime utcNow)
        {
            if (!IsCurrentEndpoint(connectionId, endpoint, out var record)) return false;
            record.LastActivityUtc = utcNow;
            return true;
        }

        public bool CanSubmitRoomCommands(string connectionId, GameEndpoint endpoint) =>
            IsCurrentEndpoint(connectionId, endpoint, out var record) && record.IsAuthenticated;

        public bool TryGetGeneration(string connectionId, GameEndpoint endpoint, out long generation)
        {
            generation = 0;
            if (!IsCurrentEndpoint(connectionId, endpoint, out var record)) return false;
            generation = record.Generation;
            return true;
        }

        public bool IsActiveConnection(string connectionId, GameEndpoint endpoint, long generation) =>
            IsCurrentEndpoint(connectionId, endpoint, out var record) && record.Generation == generation;

        public List<string> GetExpiredAuthenticatedConnections(DateTime utcNow)
        {
            return _connections.Values
                .Where(record => record.IsAuthenticated
                    && ConnectionLivenessPolicy.IsExpired(record.LastActivityUtc, utcNow, _heartbeatTimeout))
                .Select(record => record.ConnectionId)
                .ToList();
        }

        public bool BindRoomSeat(string connectionId, string roomId, int seatIndex)
        {
            if (!TryGet(connectionId, out var record) || !record.IsAuthenticated || !string.IsNullOrEmpty(record.RoomId) || seatIndex < 0 || seatIndex > 3) return false;
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
            ReleaseIdentity(record);
            _connections.Remove(connectionId);
            return true;
        }

        public bool Remove(string connectionId, GameEndpoint endpoint, out ConnectionRecord record)
        {
            if (!IsCurrentEndpoint(connectionId, endpoint, out record)) return false;
            ReleaseIdentity(record);
            _connections.Remove(connectionId);
            return true;
        }

        private bool IsCurrentEndpoint(string connectionId, GameEndpoint endpoint, out ConnectionRecord record) =>
            _connections.TryGetValue(connectionId, out record) && ReferenceEquals(record.Endpoint, endpoint);

        private void ReleaseIdentity(ConnectionRecord record)
        {
            if (record == null || !record.IsAuthenticated || string.IsNullOrEmpty(record.PlayerId)) return;

            if (_activeConnectionByPlayerId.TryGetValue(record.PlayerId, out var activeConnectionId)
                && string.Equals(activeConnectionId, record.ConnectionId, StringComparison.Ordinal))
                _activeConnectionByPlayerId.Remove(record.PlayerId);

            record.IsAuthenticated = false;
            record.PlayerId = null;
            record.DisplayName = null;
        }
    }
}
