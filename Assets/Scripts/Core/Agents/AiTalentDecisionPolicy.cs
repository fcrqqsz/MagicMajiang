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
        private const int MaximumSupplementalActionsPerDecision = 6;

        public static TalentActionOption ChooseActiveAction(
            IReadOnlyList<TalentActionOption> authoritativeOptions)
        {
            TalentActionOption chosen = (authoritativeOptions ?? Array.Empty<TalentActionOption>())
                .Where(IsWellFormed)
                .OrderByDescending(option => option.AiPriority)
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

            bool anyAccepted = false;
            var submitted = new HashSet<string>(StringComparer.Ordinal);
            for (int attempt = 0; attempt < MaximumSupplementalActionsPerDecision; attempt++)
            {
                NetworkDecisionContext currentDecision = server.ActiveDecision;
                if (currentDecision == null
                    || currentDecision.DecisionId != decision.DecisionId
                    || currentDecision.Phase != NetworkDecisionPhase.MainTurn
                    || currentDecision.ActingSeatIndex != seatIndex)
                {
                    break;
                }

                TalentActionOption selected = ChooseActiveAction(
                    server.GetAvailableTalentActionsSnapshot(seatIndex));
                if (selected == null) break;
                string fingerprint = GetFingerprint(selected);
                if (!submitted.Add(fingerprint)) break;

                bool accepted = server.SubmitNetworkTalentAction(
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
                if (!accepted) break;
                anyAccepted = true;
            }
            return anyAccepted;
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
            if (original.Any(id => string.IsNullOrWhiteSpace(id)
                                   || !carried.Contains(id, StringComparer.Ordinal)))
                return original;
            if (!SideboardLoadoutPolicy.TryValidate(
                    carriedLoadout, original, preset, registry,
                    out string[] normalizedOriginal, out _, out _))
                return original;

            SnapshotKnownTalent[] activeThreats = (publicKnownOpponentTalents
                                                    ?? Array.Empty<SnapshotKnownTalent>())
                .Where(talent => talent != null
                                 && talent.ownerSeatIndex != seatIndex
                                 && talent.isKnown
                                 && talent.isActive
                                 && registry.HasTalent(talent.talentId))
                .ToArray();
            Type desiredCapability = activeThreats.Any(talent =>
                    registry.HasCapability<IPublicChargeControlTalent>(talent.talentId))
                ? typeof(IPublicChargeDefenseTalent)
                : activeThreats.Any(talent => talent.lastPublicValue > 0
                                              && registry.HasCapability<IPublicChargeTalent>(talent.talentId))
                    ? typeof(IPublicChargeControlTalent)
                    : null;
            if (desiredCapability == null)
            {
                accepted = true;
                return normalizedOriginal;
            }

            var selected = new HashSet<string>(normalizedOriginal, StringComparer.Ordinal);
            string promoted = carried.FirstOrDefault(id => !selected.Contains(id)
                                                           && HasCapability(registry, id, desiredCapability));
            if (promoted == null)
            {
                accepted = true;
                return normalizedOriginal;
            }

            selected.Add(promoted);
            if (TryValidateSelection(carriedLoadout, selected, preset, registry, out string[] promotedSelection))
            {
                accepted = true;
                return promotedSelection;
            }

            foreach (string removable in carried.Reverse().Where(id => selected.Contains(id)
                    && !string.Equals(id, promoted, StringComparison.Ordinal)
                    && registry.GetMetadata(id).SideboardPolicy == TalentSideboardPolicy.Flexible))
            {
                selected.Remove(removable);
                if (TryValidateSelection(carriedLoadout, selected, preset, registry, out promotedSelection))
                {
                    accepted = true;
                    return promotedSelection;
                }
                selected.Add(removable);
            }

            accepted = true;
            return normalizedOriginal;
        }

        private static bool IsWellFormed(TalentActionOption option) =>
            option != null && !string.IsNullOrWhiteSpace(option.TalentId);

        private static bool HasCapability(
            TalentRegistry registry,
            string talentId,
            Type capability) =>
            capability == typeof(IPublicChargeControlTalent)
                ? registry.HasCapability<IPublicChargeControlTalent>(talentId)
                : capability == typeof(IPublicChargeDefenseTalent)
                    && registry.HasCapability<IPublicChargeDefenseTalent>(talentId);

        private static bool TryValidateSelection(
            TrustedPlayerLoadout loadout,
            HashSet<string> selected,
            AlienationPreset preset,
            TalentRegistry registry,
            out string[] normalized) =>
            SideboardLoadoutPolicy.TryValidate(
                loadout,
                selected.ToArray(),
                preset,
                registry,
                out normalized,
                out _,
                out _);

        private static string GetFingerprint(TalentActionOption option) => string.Join(
            "|",
            option.TalentId ?? string.Empty,
            option.TargetSeatIndex,
            option.TargetTalentId ?? string.Empty,
            option.SelectedChoiceId ?? option.Choice?.DefaultChoiceId ?? string.Empty);

        private static TalentActionOption Clone(TalentActionOption option) => option == null
            ? null
            : new TalentActionOption
            {
                TalentId = option.TalentId,
                TargetSeatIndex = option.TargetSeatIndex,
                TargetTalentId = option.TargetTalentId,
                TargetPublicCharge = option.TargetPublicCharge,
                AiPriority = option.AiPriority,
                Choice = option.Choice,
                SelectedChoiceId = option.SelectedChoiceId
                    ?? option.Choice?.DefaultChoiceId
            };
    }
}
