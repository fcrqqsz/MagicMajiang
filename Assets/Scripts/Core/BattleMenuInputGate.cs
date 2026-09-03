namespace MahjongGame.Core
{
    /// <summary>Additional local input constraint, independent of server decision permissions.</summary>
    public sealed class BattleMenuInputGate
    {
        private static BattleMenuInputGate _instance;
        public static BattleMenuInputGate Instance => _instance ?? (_instance = new BattleMenuInputGate());
        private bool _blocked;
        private int _releasedFrame = -1;

        public void SetBlocked(bool blocked, int frameNumber)
        {
            if (_blocked && !blocked) _releasedFrame = frameNumber;
            _blocked = blocked;
        }

        public bool IsBlocked(int frameNumber) => _blocked || frameNumber == _releasedFrame;
        public bool CanInteract(bool decisionAllows, int frameNumber) => decisionAllows && !IsBlocked(frameNumber);
    }
}
