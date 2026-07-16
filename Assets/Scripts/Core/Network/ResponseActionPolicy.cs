using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core.Network
{
    /// <summary>Defines which seat may claim a discarded tile during the response phase.</summary>
    public static class ResponseActionPolicy
    {
        public static bool CanRespondToDiscard(int responderId, int discarderId)
        {
            return responderId >= 0 && discarderId >= 0 && responderId != discarderId;
        }

        /// <summary>Selects the highest-priority response, resolving ties from the discarder&apos;s next seat onward.</summary>
        public static ClientAction SelectHighestPriorityResponse(IEnumerable<ClientAction> responses, int discarderId, int playerCount)
        {
            if (responses == null || playerCount <= 1) return null;

            return responses
                .Where(response => response != null && response.ActionType != ClientActionType.Skip)
                .OrderBy(response => GetActionPriority(response.ActionType))
                .ThenBy(response => GetSeatPriority(response.PlayerId, discarderId, playerCount))
                .FirstOrDefault();
        }

        /// <summary>Checks whether every player who can Hu on the discard has made a response.</summary>
        public static bool AllPotentialHuRespondersAnswered(IEnumerable<int> potentialHuPlayerIds, IEnumerable<int> respondedPlayerIds)
        {
            if (potentialHuPlayerIds == null) return true;
            var responded = new HashSet<int>(respondedPlayerIds ?? Enumerable.Empty<int>());
            return potentialHuPlayerIds.All(responded.Contains);
        }

        private static int GetActionPriority(ClientActionType actionType)
        {
            if (actionType == ClientActionType.Hu) return 0;
            if (actionType == ClientActionType.Pon || actionType == ClientActionType.MingGan) return 1;
            if (actionType == ClientActionType.Chi) return 2;
            return 3;
        }

        private static int GetSeatPriority(int responderId, int discarderId, int playerCount)
        {
            int distance = (responderId - discarderId + playerCount) % playerCount;
            return distance == 0 ? int.MaxValue : distance;
        }
    }
}
