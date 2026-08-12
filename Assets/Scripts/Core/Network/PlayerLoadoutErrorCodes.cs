namespace MahjongGame.Core.Network
{
    public static class PlayerLoadoutErrorCodes
    {
        public const string MissingLoadout = "MissingLoadout";
        public const string InvalidDeck = "InvalidDeck";
        public const string InvalidTalent = "InvalidTalent";
        public const string InvalidAlienationPreset = "InvalidAlienationPreset";
        public const string AlienationPresetMismatch = "AlienationPresetMismatch";
        public const string AlienationLimitExceeded = "AlienationLimitExceeded";
        public const string UnsupportedLoadoutVersion = "UnsupportedLoadoutVersion";
    }
}
