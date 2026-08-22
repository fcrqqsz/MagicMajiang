using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core
{
    public sealed class BaseActionAvailability
    {
        public bool CanDiscard { get; set; }
        public bool CanHu { get; set; }
        public bool CanPon { get; set; }
        public bool CanChi { get; set; }
        public bool CanKong { get; set; }
        public bool CanSkip { get; set; }
    }

    public sealed class TalentActionPanelOption
    {
        public TalentActionOption Option { get; set; }
        public string TalentId => Option?.TalentId;
        public bool IsPending { get; set; }
    }

    public sealed class TalentActionPanelState
    {
        public long DecisionId { get; set; }
        public bool IsOpen { get; set; }
        public BaseActionAvailability BaseActions { get; set; } = new BaseActionAvailability();
        public IReadOnlyList<TalentActionPanelOption> Options { get; set; }
            = Array.Empty<TalentActionPanelOption>();
        public string TargetSelection { get; set; }
        public string ChoiceSelection { get; set; }
    }

    public sealed class TalentActionTargetPresentation
    {
        public TalentActionOption Option { get; set; }
        public int SeatIndex { get; set; }
        public string SeatDisplayName { get; set; }
        public string TalentDisplayName { get; set; }
        public int PublicCharge { get; set; }
    }

    public static class TalentActionPanelPolicy
    {
        public static TalentActionPanelState Open(
            long decisionId,
            BaseActionAvailability baseActions,
            IEnumerable<TalentActionOption> options)
        {
            TalentActionPanelOption[] copiedOptions = (options ?? Array.Empty<TalentActionOption>())
                .Where(option => option != null && !string.IsNullOrWhiteSpace(option.TalentId))
                .Select(option => new TalentActionPanelOption
                {
                    Option = CloneOption(option)
                })
                .ToArray();

            return new TalentActionPanelState
            {
                DecisionId = decisionId,
                IsOpen = decisionId > 0 && copiedOptions.Length > 0,
                BaseActions = CloneBaseActions(baseActions),
                Options = copiedOptions
            };
        }

        public static TalentActionPanelState BeginSubmit(
            TalentActionPanelState state,
            string talentId)
        {
            TalentActionPanelState next = CloneState(state);
            if (!next.IsOpen || string.IsNullOrWhiteSpace(talentId)) return next;

            foreach (TalentActionPanelOption option in next.Options)
            {
                if (string.Equals(option.Option.TalentId, talentId, StringComparison.Ordinal))
                    option.IsPending = true;
            }
            next.TargetSelection = null;
            next.ChoiceSelection = null;
            return next;
        }

        public static TalentActionPanelState BeginChoiceSelection(
            TalentActionPanelState state,
            string talentId)
        {
            TalentActionPanelState next = CloneState(state);
            if (next.IsOpen && next.Options.Any(option =>
                    string.Equals(option.Option.TalentId, talentId, StringComparison.Ordinal)
                    && option.Option.Choice != null
                    && !option.IsPending))
            {
                next.TargetSelection = null;
                next.ChoiceSelection = talentId;
            }
            return next;
        }

        public static TalentActionPanelState CancelChoiceSelection(TalentActionPanelState state)
        {
            TalentActionPanelState next = CloneState(state);
            next.ChoiceSelection = null;
            return next;
        }

        public static TalentActionPanelState BeginTargetSelection(
            TalentActionPanelState state,
            string talentId)
        {
            TalentActionPanelState next = CloneState(state);
            if (next.IsOpen && next.Options.Any(option =>
                    string.Equals(option.Option.TalentId, talentId, StringComparison.Ordinal)
                    && !option.IsPending))
            {
                next.TargetSelection = talentId;
                next.ChoiceSelection = null;
            }
            return next;
        }

        public static TalentActionPanelState CancelTargetSelection(TalentActionPanelState state)
        {
            TalentActionPanelState next = CloneState(state);
            next.TargetSelection = null;
            return next;
        }

        public static TalentActionPanelState Resolve(
            TalentActionPanelState state,
            long decisionId,
            string talentId,
            bool accepted,
            string errorCode)
        {
            TalentActionPanelState next = CloneState(state);
            if (!next.IsOpen || next.DecisionId != decisionId) return next;
            if (IsDecisionTerminal(errorCode)) return Clear();
            if (accepted) return next;

            foreach (TalentActionPanelOption option in next.Options)
            {
                if (string.Equals(option.Option.TalentId, talentId, StringComparison.Ordinal))
                    option.IsPending = false;
            }
            return next;
        }

        public static TalentActionPanelState ResetForRecovery(TalentActionPanelState state) => Clear();

        public static TalentActionPanelState Clear() => new TalentActionPanelState();

        public static string GetRejectionCopy(string errorCode)
        {
            if (string.Equals(errorCode, TalentActionErrorCodes.InvalidTarget, StringComparison.Ordinal))
                return "目标已不可用";
            if (string.Equals(errorCode, TalentActionErrorCodes.InsufficientResource, StringComparison.Ordinal))
                return "充能不足";
            if (string.Equals(errorCode, TalentActionErrorCodes.AlreadyUsedThisTurn, StringComparison.Ordinal))
                return "本回合已使用";
            if (string.Equals(errorCode, TalentActionErrorCodes.InvalidChoice, StringComparison.Ordinal))
                return "选择已不可用";
            if (string.Equals(errorCode, TalentActionErrorCodes.NotCarriedOrInactive, StringComparison.Ordinal)
                || string.Equals(errorCode, TalentActionErrorCodes.NotAvailable, StringComparison.Ordinal))
                return "天赋当前不可用";
            if (string.Equals(errorCode, NetworkErrorCodes.WrongController, StringComparison.Ordinal))
                return "当前由托管操作";
            return "天赋动作未生效";
        }

        public static IReadOnlyList<TalentActionTargetPresentation> BuildAuthorizedTargets(
            IEnumerable<TalentActionPanelOption> options,
            string talentId,
            RoomGameSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(talentId) || snapshot == null)
                return Array.Empty<TalentActionTargetPresentation>();

            var seats = (snapshot.seats ?? Array.Empty<RoomSnapshotSeat>())
                .Where(seat => seat != null && seat.seatIndex >= 0 && seat.seatIndex < 4)
                .GroupBy(seat => seat.seatIndex)
                .ToDictionary(group => group.Key, group => group.First());
            var known = (snapshot.knownTalents ?? Array.Empty<SnapshotKnownTalent>())
                .Where(target => target != null && target.isKnown && target.isActive)
                .GroupBy(target => target.ownerSeatIndex + ":" + target.talentId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            return (options ?? Array.Empty<TalentActionPanelOption>())
                .Where(option => option?.Option != null
                                 && string.Equals(option.Option.TalentId, talentId, StringComparison.Ordinal)
                                 && option.Option.TargetSeatIndex >= 0
                                 && !string.IsNullOrWhiteSpace(option.Option.TargetTalentId))
                .Select(option =>
                {
                    TalentActionOption authorized = option.Option;
                    known.TryGetValue(
                        authorized.TargetSeatIndex + ":" + authorized.TargetTalentId,
                        out SnapshotKnownTalent publicTalent);
                    seats.TryGetValue(authorized.TargetSeatIndex, out RoomSnapshotSeat seat);
                    if (publicTalent == null || seat == null) return null;

                    return new TalentActionTargetPresentation
                    {
                        Option = CloneOption(authorized),
                        SeatIndex = authorized.TargetSeatIndex,
                        SeatDisplayName = string.IsNullOrWhiteSpace(seat.displayName)
                            ? $"玩家 {authorized.TargetSeatIndex + 1}"
                            : seat.displayName,
                        TalentDisplayName = TalentRegistry.Instance.GetDisplayName(authorized.TargetTalentId),
                        PublicCharge = Math.Max(0, publicTalent.lastPublicValue)
                    };
                })
                .Where(target => target != null)
                .ToArray();
        }

        private static bool IsDecisionTerminal(string errorCode) =>
            string.Equals(errorCode, NetworkErrorCodes.StaleDecision, StringComparison.Ordinal)
            || string.Equals(errorCode, NetworkErrorCodes.DecisionExpired, StringComparison.Ordinal);

        private static TalentActionPanelState CloneState(TalentActionPanelState state)
        {
            if (state == null) return Clear();
            return new TalentActionPanelState
            {
                DecisionId = state.DecisionId,
                IsOpen = state.IsOpen,
                BaseActions = CloneBaseActions(state.BaseActions),
                Options = (state.Options ?? Array.Empty<TalentActionPanelOption>())
                    .Where(option => option?.Option != null)
                    .Select(option => new TalentActionPanelOption
                    {
                        Option = CloneOption(option.Option),
                        IsPending = option.IsPending
                    })
                    .ToArray(),
                TargetSelection = state.TargetSelection,
                ChoiceSelection = state.ChoiceSelection
            };
        }

        private static BaseActionAvailability CloneBaseActions(BaseActionAvailability source) =>
            new BaseActionAvailability
            {
                CanDiscard = source?.CanDiscard ?? false,
                CanHu = source?.CanHu ?? false,
                CanPon = source?.CanPon ?? false,
                CanChi = source?.CanChi ?? false,
                CanKong = source?.CanKong ?? false,
                CanSkip = source?.CanSkip ?? false
            };

        public static TalentActionOption CloneOption(TalentActionOption source) =>
            source == null
                ? null
                : new TalentActionOption
                {
                    TalentId = source.TalentId,
                    TargetSeatIndex = source.TargetSeatIndex,
                    TargetTalentId = source.TargetTalentId,
                    TargetPublicCharge = source.TargetPublicCharge,
                    AiPriority = source.AiPriority,
                    Choice = source.Choice,
                    SelectedChoiceId = source.SelectedChoiceId
                };

        public static TalentActionOption SelectChoice(
            TalentActionOption source,
            string choiceId)
        {
            if (source?.Choice == null || !source.Choice.Contains(choiceId)) return null;
            TalentActionOption selected = CloneOption(source);
            selected.SelectedChoiceId = choiceId;
            return selected;
        }
    }
}
