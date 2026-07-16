using System.Collections.Generic;
using System.Linq;
using MahjongGame.Talents;
using MahjongGame.Talents.Impl;

namespace MahjongGame.Core.Network
{
    /// <summary>Applies session-start effects without allowing later rounds to repeat them.</summary>
    public static class SessionTalentPolicy
    {
        public static bool ApplyStartingCapitalOnce(GameSession session, IReadOnlyDictionary<int, TalentSlotConfig> talentConfigs, ref bool alreadyApplied)
        {
            if (alreadyApplied || session == null || talentConfigs == null) return false;

            foreach (var pair in talentConfigs)
            {
                if (pair.Key < 0 || pair.Key >= session.Scores.Length || pair.Value == null) continue;
                if (pair.Value.GetAllEquippedIds().Contains("starting_capital"))
                    session.Scores[pair.Key] += StartingCapitalTalent.BonusScore;
            }

            alreadyApplied = true;
            return true;
        }
    }
}
