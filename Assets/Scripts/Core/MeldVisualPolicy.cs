namespace MahjongGame.Core
{
    /// <summary>Defines which declared-meld tiles are shown face-down on the table.</summary>
    public static class MeldVisualPolicy
    {
        /// <summary>
        /// MCR concealed kongs remain fully concealed during play: all four tiles are face-down.
        /// </summary>
        public static bool IsTileFaceDown(MeldType type, int tileIndex)
        {
            return type == MeldType.Kan_Concealed;
        }
    }
}
