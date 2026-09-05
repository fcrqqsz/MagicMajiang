using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    public enum RoomSeatKind
    {
        Human = 0,
        PermanentAi = 1
    }

    public enum AiDifficulty
    {
        Beginner = 0,
        Standard = 1
    }

    public enum AiLoadoutTemplate
    {
        Aggressive = 0,
        Stable = 1,
        TalentSynergy = 2,
        Custom = 3
    }

    public sealed class AiSeatConfig
    {
        public AiDifficulty Difficulty { get; }
        public AiLoadoutTemplate Template { get; }
        public TrustedPlayerLoadout Loadout { get; }

        public AiSeatConfig(AiDifficulty difficulty, AiLoadoutTemplate template, TrustedPlayerLoadout loadout)
        {
            Difficulty = difficulty;
            Template = template;
            Loadout = PlayerLoadoutCodec.CloneTrustedLoadout(loadout);
        }

        public AiSeatConfigMessage ToMessage()
        {
            return new AiSeatConfigMessage
            {
                difficulty = (int)Difficulty,
                template = (int)Template,
                loadout = Loadout == null
                    ? null
                    : PlayerLoadoutCodec.CreateMessage(
                        Loadout.DeckConfig,
                        Loadout.TalentConfig,
                        Loadout.AlienationPreset)
            };
        }
    }
}
