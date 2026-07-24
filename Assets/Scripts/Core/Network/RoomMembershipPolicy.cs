using System;
using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core.Network
{
    /// <summary>Protects the one-logical-seat invariant while a human membership is reserved.</summary>
    public static class RoomMembershipPolicy
    {
        public static bool RequiresReconnect(IEnumerable<string> existingHumanPlayerIds, string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return false;
            string normalizedPlayerId = playerId.Trim();
            return (existingHumanPlayerIds ?? Enumerable.Empty<string>()).Any(existing =>
                !string.IsNullOrWhiteSpace(existing)
                && string.Equals(existing.Trim(), normalizedPlayerId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A temporary AI controls a decision, not a logical seat. An offline human
        /// membership remains reserved until expiration regardless of that controller.
        /// </summary>
        public static bool RequiresReconnectForDisconnectedHumanSeat(bool isAi, bool isOnline, string seatPlayerId, string playerId)
        {
            return !isAi && !isOnline && RequiresReconnect(new[] { seatPlayerId }, playerId);
        }
    }
}
