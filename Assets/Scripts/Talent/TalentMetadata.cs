namespace MahjongGame.Talents
{
    public enum TalentStateScope
    {
        Round,
        Match
    }

    [System.Flags]
    public enum TalentActivationWindow
    {
        None = 0,
        MainTurn = 1,
        Response = 2
    }

    public enum TalentRevealPolicy
    {
        HiddenUntilPublicEffect,
        PublicAtMatchStart,
        OwnerOnly
    }

    public enum TalentSideboardPolicy
    {
        Flexible,
        MainOnly,
        MainOnlyLocked
    }

    public sealed class TalentMetadata
    {
        public TalentStateScope StateScope { get; }
        public TalentActivationWindow ActivationWindow { get; }
        public TalentRevealPolicy RevealPolicy { get; }
        public TalentSideboardPolicy SideboardPolicy { get; }

        public TalentMetadata(
            TalentStateScope stateScope,
            TalentActivationWindow activationWindow,
            TalentRevealPolicy revealPolicy,
            TalentSideboardPolicy sideboardPolicy)
        {
            StateScope = stateScope;
            ActivationWindow = activationWindow;
            RevealPolicy = revealPolicy;
            SideboardPolicy = sideboardPolicy;
        }
    }
}
