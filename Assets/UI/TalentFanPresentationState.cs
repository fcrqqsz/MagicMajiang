using MahjongGame.Core.Network.Messages;
using System.Collections.Generic;

namespace MahjongGame.UI
{
    public interface ILocalResultPresentation
    {
        void ShowWin(int totalFan, List<string> fanDetails, bool isSelfDraw,
            WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown);

        void ShowLose(int winnerId, int totalFan, List<string> fanDetails,
            WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown);

        void ReceiveRecoveryTalentFanBreakdown(TalentFanBreakdownMessage talentFanBreakdown);
    }

    /// <summary>
    /// Testable delivery boundary shared by the local live result and recovery entry points.
    /// It owns the transport-to-presentation copy; rendering remains in ResultPanelController.
    /// </summary>
    public sealed class LocalResultPresentationBridge
    {
        private readonly ILocalResultPresentation _presentation;

        public LocalResultPresentationBridge(ILocalResultPresentation presentation)
        {
            _presentation = presentation ??
                throw new System.ArgumentNullException(nameof(presentation));
        }

        public void ShowLiveWin(int localPlayerId, int winnerId, int totalFan,
            List<string> fanDetails, bool isSelfDraw, WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown)
        {
            TalentFanBreakdownMessage copy =
                TalentFanBreakdownMessage.Clone(talentFanBreakdown);
            if (winnerId == localPlayerId)
            {
                _presentation.ShowWin(
                    totalFan, fanDetails, isSelfDraw, winningHand, copy);
            }
            else
            {
                _presentation.ShowLose(
                    winnerId, totalFan, fanDetails, winningHand, copy);
            }
        }

        public void ShowRecovery(RoomGameSnapshot snapshot)
        {
            _presentation.ReceiveRecoveryTalentFanBreakdown(
                TalentFanBreakdownMessage.Clone(
                    snapshot?.result?.talentFanBreakdown));
        }
    }

    /// <summary>
    /// Stable data boundary between authoritative result transport and result presentation.
    /// Visual contribution rows are intentionally implemented by a later UI task.
    /// </summary>
    public sealed class TalentFanPresentationState
    {
        private TalentFanBreakdownMessage _current;

        public TalentFanBreakdownMessage Current =>
            TalentFanBreakdownMessage.Clone(_current);

        public void ApplyLive(TalentFanBreakdownMessage breakdown)
        {
            _current = TalentFanBreakdownMessage.Clone(breakdown);
        }

        public void ApplyRecovery(RoomGameSnapshot snapshot)
        {
            _current = TalentFanBreakdownMessage.Clone(
                snapshot?.result?.talentFanBreakdown);
        }

        public void ApplyRecovery(TalentFanBreakdownMessage breakdown)
        {
            _current = TalentFanBreakdownMessage.Clone(breakdown);
        }
    }
}
