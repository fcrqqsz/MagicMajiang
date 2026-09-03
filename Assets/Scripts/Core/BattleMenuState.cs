namespace MahjongGame.Core
{
    public enum BattleMenuPage { Closed, Home, Settings, ConfirmExit, Leaving }

    /// <summary>Local menu navigation; never pauses or mutates the authoritative match.</summary>
    public sealed class BattleMenuState
    {
        public BattleMenuPage Page { get; private set; }
        public bool IsOpen => Page != BattleMenuPage.Closed;
        public bool IsLeaving => Page == BattleMenuPage.Leaving;

        public void Open()
        {
            if (!IsLeaving) Page = BattleMenuPage.Home;
        }

        public void ShowSettings()
        {
            if (Page == BattleMenuPage.Home) Page = BattleMenuPage.Settings;
        }

        public void Close()
        {
            if (!IsLeaving) Page = BattleMenuPage.Closed;
        }

        public void Escape()
        {
            if (IsLeaving) return;
            if (Page == BattleMenuPage.Closed) Open();
            else if (Page == BattleMenuPage.Home) Close();
            else Page = BattleMenuPage.Home;
        }

        public void OnAuthoritativeBoundary() => Close();

        /// <returns>True only when navigation should start immediately.</returns>
        public bool RequestExit(bool sessionCompleted)
        {
            if (Page != BattleMenuPage.Home) return false;
            Page = sessionCompleted ? BattleMenuPage.Leaving : BattleMenuPage.ConfirmExit;
            return sessionCompleted;
        }

        public bool ConfirmExit()
        {
            if (Page != BattleMenuPage.ConfirmExit) return false;
            Page = BattleMenuPage.Leaving;
            return true;
        }
    }
}
