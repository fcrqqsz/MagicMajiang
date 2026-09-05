using System;

namespace MahjongGame.Core.Network
{
    public enum RoomSeatControlState
    {
        Vacant,
        OnlineHuman,
        OfflineReserved,
        AiControlled,
        PermanentAi
    }

    public enum RoomSeatDepartureDisposition
    {
        OfflineReserved,
        CloseRoom
    }

    public enum RoomSeatExpiryDisposition
    {
        Vacant,
        PermanentAi
    }

    public enum DecisionControllerKind
    {
        Human,
        AI
    }

    /// <summary>Pure policy for offline human seats and permanent AI continuity.</summary>
    public static class RoomLifecyclePolicy
    {
        public static DecisionControllerKind SelectDecisionController(bool isOnline, bool humanDecisionAlreadyOpen)
        {
            return isOnline || humanDecisionAlreadyOpen ? DecisionControllerKind.Human : DecisionControllerKind.AI;
        }

        public static RoomSeatDepartureDisposition GetDisconnectDisposition(RoomState roomState, bool hasOtherOnlineHuman)
        {
            return RoomSeatDepartureDisposition.OfflineReserved;
        }

        public static RoomSeatExpiryDisposition GetExpiryDisposition(RoomState roomState)
        {
            return roomState == RoomState.WaitingForPlayers || roomState == RoomState.WaitingForMatchReady
                ? RoomSeatExpiryDisposition.Vacant
                : RoomSeatExpiryDisposition.PermanentAi;
        }

        public static bool ShouldAutoReadyOfflineSeat(RoomState roomState) => roomState == RoomState.WaitingForNextRound;

        /// <summary>Once seats are locked, a disconnected human can be temporarily controlled at safe decision boundaries.</summary>
        public static bool ShouldAdvanceAfterWaitingMemberChange(bool hasHumanPlayers) => hasHumanPlayers;

        /// <summary>Between rounds an offline reserved human is ready for the temporary controller; online humans still choose Ready.</summary>
        public static bool ShouldAutoReadyNextRoundSeat(bool isOnline) => !isOnline;

        public static bool ShouldCloseWhenNoHumanOnline(int onlineHumanCount) => onlineHumanCount <= 0;

        /// <summary>Physical presence is independent from which controller owns one active decision.</summary>
        public static bool ShouldCountAsOnlineHuman(bool isAi, bool isOnline) => !isAi && isOnline;

        /// <summary>An online reconnected human must never expire merely because a prior offline deadline elapsed.</summary>
        public static bool ShouldExpireOfflineSeat(bool isOnline, DateTime offlineExpiresAtUtc, DateTime utcNow) =>
            !isOnline && offlineExpiresAtUtc != default && utcNow >= offlineExpiresAtUtc;
    }
}
