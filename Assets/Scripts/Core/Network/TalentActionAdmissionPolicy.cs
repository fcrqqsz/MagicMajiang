using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    public static class TalentActionAdmissionPolicy
    {
        public const TalentActivationWindow RequiredActivationWindow = TalentActivationWindow.MainTurn;

        /// <summary>Supplemental talent actions are admitted only during the base main-turn decision.</summary>
        public static bool TryValidateMainTurn(
            NetworkDecisionTracker tracker,
            long decisionId,
            int seatIndex,
            out string errorCode)
        {
            if (tracker == null)
            {
                errorCode = NetworkErrorCodes.NoActiveDecision;
                return false;
            }

            return tracker.TryValidateSupplementalAction(
                decisionId,
                seatIndex,
                NetworkDecisionPhase.MainTurn,
                out errorCode);
        }
    }
}
