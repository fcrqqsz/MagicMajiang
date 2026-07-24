using System;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    /// <summary>Prevents stale recovered UI from submitting an expired or AI-controlled decision.</summary>
    public static class ClientRecoveryInputPolicy
    {
        public static bool CanRestoreInput(
            SnapshotDecision decision,
            RoomSnapshotSeat localSeat,
            int localSeatIndex,
            long nowUnixMilliseconds)
        {
            bool isControlledByLocalHuman = decision != null
                && (((NetworkDecisionPhase)decision.phase == NetworkDecisionPhase.MainTurn
                        && decision.controllerSeatIndex == localSeatIndex)
                    || ((NetworkDecisionPhase)decision.phase == NetworkDecisionPhase.Response
                        && Array.IndexOf(decision.eligibleSeats ?? Array.Empty<int>(), localSeatIndex) >= 0
                        && Array.IndexOf(decision.submittedSeats ?? Array.Empty<int>(), localSeatIndex) < 0));

            return decision != null
                && decision.decisionId > 0
                && isControlledByLocalHuman
                && decision.deadlineUnixMilliseconds > nowUnixMilliseconds
                && localSeat != null
                && localSeat.seatIndex == localSeatIndex
                && localSeat.isOccupied
                && !localSeat.isAi
                && localSeat.isOnline
                && localSeat.controller == "OnlineHuman";
        }
    }
}
