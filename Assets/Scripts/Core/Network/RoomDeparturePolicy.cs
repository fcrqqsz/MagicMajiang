namespace MahjongGame.Core.Network
{
    /// <summary>Compatibility facade for E3 room lifecycle decisions.</summary>
    public static class RoomDeparturePolicy
    {
        public static bool ShouldKeepRoomAfterDeparture(RoomState roomState, bool hasRemainingHumanPlayers, bool aiFill)
        {
            return !RoomLifecyclePolicy.ShouldCloseWhenNoHumanOnline(hasRemainingHumanPlayers ? 1 : 0)
                && roomState != RoomState.Closed;
        }

        /// <summary>AI only takes over a seat after the pre-match waiting stages have ended.</summary>
        public static bool ShouldReplaceWithAi(RoomState roomState, bool aiFill)
        {
            return roomState == RoomState.LoadingGameScene
                || roomState == RoomState.InRound
                || roomState == RoomState.WaitingForNextRound;
        }
    }
}
