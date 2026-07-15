namespace MahjongGame.Core.Network
{
    /// <summary>Defines which intent types are valid in each authoritative server phase.</summary>
    public static class TurnActionPolicy
    {
        public static bool IsMainTurnAction(ClientActionType actionType)
        {
            return actionType == ClientActionType.Discard
                || actionType == ClientActionType.Hu
                || actionType == ClientActionType.AnGan
                || actionType == ClientActionType.JiaGang;
        }

        public static bool IsResponseAction(ClientActionType actionType)
        {
            return actionType == ClientActionType.Skip
                || actionType == ClientActionType.Hu
                || actionType == ClientActionType.Pon
                || actionType == ClientActionType.MingGan
                || actionType == ClientActionType.Chi;
        }
    }
}
