using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Talents;

namespace MahjongGame.Core
{
    public enum AlienationPreset
    {
        Low = 40,
        Standard = 80,
        High = 120
    }

    public static class AlienationBudgetPolicy
    {
        public static bool IsDefined(AlienationPreset preset) =>
            preset == AlienationPreset.Low ||
            preset == AlienationPreset.Standard ||
            preset == AlienationPreset.High;

        public static int GetLimit(AlienationPreset preset)
        {
            if (!IsDefined(preset))
                throw new ArgumentOutOfRangeException(nameof(preset));
            return (int)preset;
        }

        public static int Calculate(
            DeckConfig deck,
            IEnumerable<string> activeTalentIds,
            TalentRegistry registry)
        {
            if (deck == null) throw new ArgumentNullException(nameof(deck));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            deck.CalculateAlienationScore();
            int total = deck.AlienationScore;
            foreach (string id in (activeTalentIds ?? Enumerable.Empty<string>())
                         .Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                total += registry.GetCost(id);
            }
            return total;
        }
    }
}
