using System;

namespace MahjongGame.Core
{
    public sealed class AlienationGaugeView
    {
        public int Total { get; }
        public int Limit { get; }
        public float Fill01 { get; }
        public int Overflow { get; }
        public bool IsOverLimit { get; }
        public bool CanSave { get; }
        public int DeckCost { get; }
        public int TalentCost { get; }

        public AlienationGaugeView(int deckCost, int talentCost, int limit)
        {
            DeckCost = deckCost;
            TalentCost = talentCost;
            Total = deckCost + talentCost;
            Limit = limit;
            Fill01 = Math.Min(1f, Total / (float)limit);
            Overflow = Math.Max(0, Total - limit);
            IsOverLimit = Overflow > 0;
            CanSave = true;
        }
    }

    public static class AlienationGaugePolicy
    {
        public static AlienationGaugeView Build(
            int deckCost,
            int talentCost,
            AlienationPreset preset)
        {
            int safeDeckCost = Math.Max(0, deckCost);
            int safeTalentCost = Math.Max(0, talentCost);
            AlienationPreset displayPreset = AlienationBudgetPolicy.IsDefined(preset)
                ? preset
                : AlienationPreset.Standard;
            return new AlienationGaugeView(
                safeDeckCost,
                safeTalentCost,
                AlienationBudgetPolicy.GetLimit(displayPreset));
        }
    }
}
