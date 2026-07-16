namespace MahjongGame.Core.Network
{
    /// <summary>Defines whether a human departure may preserve the room without hot-swapping an in-round client.</summary>
    public static class RoomDeparturePolicy
    {
        public static bool ShouldKeepRoomAfterDeparture(RoomState roomState, bool hasRemainingHumanPlayers, bool aiFill)
        {
            if (!hasRemainingHumanPlayers) return false;

            return roomState == RoomState.WaitingForPlayers
                || roomState == RoomState.WaitingForMatchReady
                || (aiFill && (roomState == RoomState.LoadingGameScene || roomState == RoomState.WaitingForNextRound));
        }

        /// <summary>AI only takes over a seat after the pre-match waiting stages have ended.</summary>
        public static bool ShouldReplaceWithAi(RoomState roomState, bool aiFill)
        {
            return aiFill && (roomState == RoomState.LoadingGameScene || roomState == RoomState.WaitingForNextRound);
        }
    }
}
