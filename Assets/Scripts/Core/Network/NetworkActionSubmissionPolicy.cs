namespace MahjongGame.Core.Network
{
    /// <summary>Separates authenticated network actions from direct submissions made by a temporary AI controller.</summary>
    public static class NetworkActionSubmissionPolicy
    {
        public static bool CanProceedToActionHandling(bool isValidatedNetworkAction, bool requiresDirectAiAuthorization,
            bool isDirectAiAuthorized)
        {
            return isValidatedNetworkAction || !requiresDirectAiAuthorization || isDirectAiAuthorized;
        }

        /// <summary>A direct local controller must pass both its controller guard and the active decision tracker.</summary>
        public static bool CanProcessDirectAction(bool isDirectControllerAuthorized, bool isDecisionAdmissionAccepted)
        {
            return isDirectControllerAuthorized && isDecisionAdmissionAccepted;
        }
    }
}
