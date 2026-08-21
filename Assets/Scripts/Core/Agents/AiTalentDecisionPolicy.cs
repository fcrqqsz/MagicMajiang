using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core.Agents
{
    /// <summary>Chooses from a server-authored option set without reading private game or runtime state.</summary>
    public static class AiTalentDecisionPolicy
    {
        public static TalentActionOption ChooseActiveAction(
            IReadOnlyList<TalentActionOption> authoritativeOptions)
        {
            TalentActionOption chosen = (authoritativeOptions ?? Array.Empty<TalentActionOption>())
                .Where(IsWellFormed)
                .OrderBy(GetCategory)
                .ThenByDescending(option => option.TargetPublicCharge)
                .ThenBy(option => option.TargetSeatIndex)
                .ThenBy(option => option.TargetTalentId ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(option => option.TalentId, StringComparer.Ordinal)
                .FirstOrDefault();
            return Clone(chosen);
        }

        public static bool TrySubmitActiveAction(GameServer server, int seatIndex)
        {
            if (server == null) return false;
            NetworkDecisionContext decision = server.ActiveDecision;
            if (decision == null
                || decision.Phase != NetworkDecisionPhase.MainTurn
                || decision.ActingSeatIndex != seatIndex
                || decision.DecisionId <= 0)
            {
                return false;
            }

            TalentActionOption selected = ChooseActiveAction(
                server.GetAvailableTalentActionsSnapshot(seatIndex));
            if (selected == null) return false;

            return server.SubmitNetworkTalentAction(
                seatIndex,
                new TalentActionMessage
                {
                    decisionId = decision.DecisionId,
                    talentId = selected.TalentId,
                    targetSeatIndex = selected.TargetSeatIndex,
                    targetTalentId = selected.TargetTalentId,
                    selectedChoiceId = selected.SelectedChoiceId
                },
                out _);
        }

        public static string[] ChooseSideboard(
            TrustedPlayerLoadout carriedLoadout,
            IReadOnlyCollection<string> originalActiveTalentIds,
            IReadOnlyList<SnapshotKnownTalent> publicKnownOpponentTalents,
            AlienationPreset preset,
            int seatIndex,
            int seed,
            out bool accepted)
        {
            string[] original = (originalActiveTalentIds ?? Array.Empty<string>()).ToArray();
            accepted = false;
            if (carriedLoadout?.TalentConfig == null
                || seatIndex < 0 || seatIndex > 3
                || !AlienationBudgetPolicy.IsDefined(preset))
            {
                return original;
            }

            TalentRegistry registry = TalentRegistry.Instance;
            string[] carried = SideboardLoadoutPolicy.GetCarriedIdsInSlotOrder(
                carriedLoadout.TalentConfig);
            var selected = new HashSet<string>(original, StringComparer.Ordinal);
            if (selected.Any(id => string.IsNullOrWhiteSpace(id) || !carried.Contains(id, StringComparer.Ordinal)))
                return original;

            foreach (string talentId in carried)
            {
                if (registry.GetMetadata(talentId).SideboardPolicy == TalentSideboardPolicy.MainOnlyLocked)
                    selected.Add(talentId);
            }

            bool hasPublicChargedLargeThreat = (publicKnownOpponentTalents
                                                 ?? Array.Empty<SnapshotKnownTalent>())
                .Any(talent => talent != null
                               && talent.ownerSeatIndex != seatIndex
                               && talent.isKnown
                               && talent.lastPublicValue > 0
                               && registry.HasTalent(talent.talentId)
                               && registry.GetTier(talent.talentId) == TalentTier.Large);
            if (hasPublicChargedLargeThreat)
            {
                AddIfCarried(selected, carried, "interception");
                AddIfCarried(selected, carried, "composure");
            }

            foreach (string talentId in GetArchetypePriority(seed, seatIndex))
                AddIfCarried(selected, carried, talentId);

            while (!SideboardLoadoutPolicy.TryValidate(
                       carriedLoadout,
                       selected.ToArray(),
                       preset,
                       registry,
                       out string[] normalized,
                       out _,
                       out _))
            {
                string removable = selected
                    .Where(id => registry.HasTalent(id)
                                 && registry.GetMetadata(id).SideboardPolicy == TalentSideboardPolicy.Flexible)
                    .OrderBy(id => GetSideboardPriority(id, hasPublicChargedLargeThreat, seed, seatIndex))
                    .ThenByDescending(registry.GetCost)
                    .ThenBy(id => id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (removable == null) return original;
                selected.Remove(removable);
            }

            SideboardLoadoutPolicy.TryValidate(
                carriedLoadout,
                selected.ToArray(),
                preset,
                registry,
                out string[] acceptedSelection,
                out _,
                out _);
            accepted = true;
            return acceptedSelection;
        }

        private static bool IsWellFormed(TalentActionOption option) =>
            option != null && !string.IsNullOrWhiteSpace(option.TalentId);

        private static int GetCategory(TalentActionOption option)
        {
            if (string.Equals(option.TalentId, "sheathed_edge", StringComparison.Ordinal)) return 0;
            if (string.Equals(option.TalentId, "interception", StringComparison.Ordinal)) return 1;
            return 2;
        }

        private static void AddIfCarried(HashSet<string> selected, string[] carried, string talentId)
        {
            if (carried.Contains(talentId, StringComparer.Ordinal)) selected.Add(talentId);
        }

        private static IReadOnlyList<string> GetArchetypePriority(int seed, int seatIndex)
        {
            string[][] priorities =
            {
                new[] { "sheathed_edge", "head_start", "midas_touch", "dragon_ascent", "interception", "composure", "peek", "starting_capital", "draw_reward" },
                new[] { "interception", "composure", "sheathed_edge", "head_start", "dragon_ascent", "midas_touch", "starting_capital", "draw_reward", "peek" },
                new[] { "peek", "starting_capital", "draw_reward", "midas_touch", "head_start", "dragon_ascent", "interception", "composure", "sheathed_edge" }
            };
            int index = (seed + seatIndex) % priorities.Length;
            if (index < 0) index += priorities.Length;
            return priorities[index];
        }

        private static int GetSideboardPriority(
            string talentId,
            bool hasPublicChargedLargeThreat,
            int seed,
            int seatIndex)
        {
            if (hasPublicChargedLargeThreat
                && (string.Equals(talentId, "interception", StringComparison.Ordinal)
                    || string.Equals(talentId, "composure", StringComparison.Ordinal)))
            {
                return int.MaxValue;
            }

            IReadOnlyList<string> priorities = GetArchetypePriority(seed, seatIndex);
            int index = priorities.ToList().FindIndex(id => string.Equals(id, talentId, StringComparison.Ordinal));
            return index < 0 ? int.MinValue : priorities.Count - index;
        }

        private static TalentActionOption Clone(TalentActionOption option) => option == null
            ? null
            : new TalentActionOption
            {
                TalentId = option.TalentId,
                TargetSeatIndex = option.TargetSeatIndex,
                TargetTalentId = option.TargetTalentId,
                TargetPublicCharge = option.TargetPublicCharge,
                Choice = option.Choice,
                SelectedChoiceId = option.SelectedChoiceId
                    ?? option.Choice?.DefaultChoiceId
            };
    }
}
