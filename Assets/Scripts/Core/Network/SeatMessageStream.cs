using System;
using System.Collections.Generic;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;

namespace MahjongGame.Core.Network
{
    /// <summary>
    /// Owns one human seat's ordered outbound envelope history for the lifetime of a room seat.
    /// </summary>
    public sealed class SeatMessageStream
    {
        public const int DefaultCacheCapacity = 256;

        private readonly string[] _serializedCache;
        private GameEndpoint _endpoint;
        private readonly List<string> _deferredDeliveries = new List<string>();
        private bool _deliveryPaused;
        private int _nextSequence = 1;
        private int _cacheStart;
        private int _cacheCount;

        public SeatMessageStream(GameEndpoint endpoint, int cacheCapacity = DefaultCacheCapacity)
        {
            if (cacheCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(cacheCapacity));

            _endpoint = endpoint;
            _serializedCache = new string[cacheCapacity];
            StreamId = Guid.NewGuid().ToString("N");
        }

        /// <summary>Non-secret, room-lifetime lineage used only to select the correct cached stream.</summary>
        public string StreamId { get; }
        public int LatestSequence => _nextSequence - 1;

        /// <summary>Serializes, caches, and delivers the next envelope for this seat.</summary>
        public void Send(string type, object payload)
        {
            if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("A message type is required.", nameof(type));

            int sequence = _nextSequence;
            string serializedEnvelope = MessageSerializer.Serialize(type, sequence, payload);
            Cache(serializedEnvelope);
            _nextSequence = checked(sequence + 1);
            if (_deliveryPaused)
            {
                _deferredDeliveries.Add(serializedEnvelope);
                return;
            }

            _endpoint?.SendMessage(serializedEnvelope);
        }

        /// <summary>
        /// Gets all cached envelopes after <paramref name="lastSeq"/> when the cache can replay
        /// an unbroken sequence from that point.
        /// </summary>
        public bool TryGetMessagesAfter(int lastSeq, out NetworkMessageEnvelope[] messages)
        {
            messages = Array.Empty<NetworkMessageEnvelope>();
            int latestSequence = _nextSequence - 1;
            if (lastSeq < 0 || lastSeq > latestSequence) return false;
            if (_cacheCount == 0) return lastSeq == latestSequence;

            var cached = new List<NetworkMessageEnvelope>(_cacheCount);
            int expectedSequence = latestSequence - _cacheCount + 1;
            if (lastSeq < expectedSequence - 1) return false;

            for (int i = 0; i < _cacheCount; i++)
            {
                string serializedEnvelope = _serializedCache[(_cacheStart + i) % _serializedCache.Length];
                var envelope = MessageSerializer.DeserializeEnvelope(serializedEnvelope);
                if (envelope == null || envelope.seq != expectedSequence++) return false;
                if (envelope.seq > lastSeq) cached.Add(envelope);
            }

            messages = cached.ToArray();
            return true;
        }

        /// <summary>Changes only the physical delivery target; cached envelopes and sequence remain intact.</summary>
        public void RebindEndpoint(GameEndpoint endpoint)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        /// <summary>Stops physical delivery while retaining the logical stream and its cache.</summary>
        public void DetachEndpoint(GameEndpoint endpoint)
        {
            if (endpoint == null || ReferenceEquals(_endpoint, endpoint)) _endpoint = null;
        }

        /// <summary>
        /// Atomically composes recovery for a new physical endpoint. Cached replay is used only
        /// when the client still owns a matching projection; otherwise a caller-provided
        /// authoritative snapshot establishes the baseline. Envelopes emitted while the snapshot
        /// is being built are delivered after the control message in their original order.
        /// </summary>
        public ReconnectStateMessage DeliverReconnectState(
            GameEndpoint endpoint,
            int lastSeq,
            bool hasProjection,
            Func<RoomGameSnapshot> snapshotFactory)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            RebindEndpoint(endpoint);
            int recoveryFence = LatestSequence;
            _deliveryPaused = true;
            ReconnectStateMessage recovery;
            try
            {
                if (hasProjection && TryGetMessagesAfter(lastSeq, out var replay))
                {
                    recovery = new ReconnectStateMessage
                    {
                        baselineSeq = lastSeq,
                        snapshot = null,
                        missedMessages = replay
                    };
                }
                else
                {
                    recovery = new ReconnectStateMessage
                    {
                        baselineSeq = recoveryFence,
                        snapshot = snapshotFactory?.Invoke(),
                        missedMessages = Array.Empty<NetworkMessageEnvelope>()
                    };
                }
            }
            catch
            {
                _deliveryPaused = false;
                _deferredDeliveries.Clear();
                throw;
            }

            endpoint.SendMessage(MessageSerializer.Serialize("ReconnectState", 0, recovery));
            _deliveryPaused = false;
            FlushDeferredDeliveries();
            return recovery;
        }

        private void FlushDeferredDeliveries()
        {
            if (_endpoint == null || _deferredDeliveries.Count == 0) return;

            foreach (var serializedEnvelope in _deferredDeliveries)
                _endpoint.SendMessage(serializedEnvelope);
            _deferredDeliveries.Clear();
        }

        private void Cache(string serializedEnvelope)
        {
            if (_cacheCount < _serializedCache.Length)
            {
                _serializedCache[(_cacheStart + _cacheCount) % _serializedCache.Length] = serializedEnvelope;
                _cacheCount++;
                return;
            }

            _serializedCache[_cacheStart] = serializedEnvelope;
            _cacheStart = (_cacheStart + 1) % _serializedCache.Length;
        }
    }
}
