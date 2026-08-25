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

    public static class PlayerDisplayNamePolicy
    {
        public static string Resolve(RoomGameSnapshot snapshot, int seatIndex) =>
            Resolve(snapshot, seatIndex, null);

        public static string Resolve(
            RoomGameSnapshot snapshot,
            int seatIndex,
            RoomSeatMessage[] roomSeats)
        {
            RoomSnapshotSeat seat = (snapshot?.seats ?? Array.Empty<RoomSnapshotSeat>())
                .FirstOrDefault(candidate => candidate != null && candidate.seatIndex == seatIndex);
            if (seat?.isAi == true) return $"AI {seatIndex + 1}";
            if (!string.IsNullOrWhiteSpace(seat?.displayName)) return seat.displayName.Trim();

            RoomSeatMessage roomSeat = (roomSeats ?? Array.Empty<RoomSeatMessage>())
                .FirstOrDefault(candidate => candidate != null && candidate.seatIndex == seatIndex);
            if (roomSeat?.isAi == true) return $"AI {seatIndex + 1}";
            return string.IsNullOrWhiteSpace(roomSeat?.displayName)
                ? "未知玩家"
                : roomSeat.displayName.Trim();
        }
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

        public static string GetNextAutomaticChoice(
            TalentActionPanelState state,
            IEnumerable<string> suppressedTalentIds)
        {
            if (state?.IsOpen != true) return null;

            var suppressed = new HashSet<string>(
                suppressedTalentIds ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            IReadOnlyList<TalentActionPanelOption> options =
                state.Options ?? Array.Empty<TalentActionPanelOption>();
            if (options.Any(option => option?.Option?.Choice != null && option.IsPending))
                return null;

            return options
                .FirstOrDefault(option => option?.Option?.Choice != null
                                          && !option.IsPending
                                          && string.IsNullOrWhiteSpace(option.Option.SelectedChoiceId)
                                          && !suppressed.Contains(option.TalentId))
                ?.TalentId;
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
            RoomGameSnapshot snapshot,
            RoomSeatMessage[] roomSeats = null)
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
                                 && option.Option.TargetSeatIndex >= 0)
                .Select(option =>
                {
                    TalentActionOption authorized = option.Option;
                    seats.TryGetValue(authorized.TargetSeatIndex, out RoomSnapshotSeat seat);
                    if (seat == null) return null;

                    if (!string.IsNullOrWhiteSpace(authorized.TargetTalentId))
                    {
                        known.TryGetValue(
                            authorized.TargetSeatIndex + ":" + authorized.TargetTalentId,
                            out SnapshotKnownTalent publicTalent);
                        if (publicTalent == null) return null;

                        return new TalentActionTargetPresentation
                        {
                            Option = CloneOption(authorized),
                            SeatIndex = authorized.TargetSeatIndex,
                            SeatDisplayName = PlayerDisplayNamePolicy.Resolve(
                                snapshot,
                                authorized.TargetSeatIndex,
                                roomSeats),
                            TalentDisplayName = TalentRegistry.Instance.GetDisplayName(authorized.TargetTalentId),
                            PublicCharge = Math.Max(0, publicTalent.lastPublicValue)
                        };
                    }

                    return new TalentActionTargetPresentation
                    {
                        Option = CloneOption(authorized),
                        SeatIndex = authorized.TargetSeatIndex,
                        SeatDisplayName = PlayerDisplayNamePolicy.Resolve(
                            snapshot,
                            authorized.TargetSeatIndex,
                            roomSeats),
                        TalentDisplayName = string.Empty,
                        PublicCharge = 0
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

        public static TalentActionOption SelectAuthorizedChoice(
            TalentActionPanelState state,
            string talentId,
            string choiceId)
        {
            if (state?.IsOpen != true
                || string.IsNullOrWhiteSpace(talentId)
                || string.IsNullOrWhiteSpace(choiceId))
            {
                return null;
            }

            TalentActionPanelOption current = (state.Options
                                                ?? Array.Empty<TalentActionPanelOption>())
                .FirstOrDefault(option => option?.Option?.Choice != null
                                          && !option.IsPending
                                          && string.Equals(
                                              option.TalentId,
                                              talentId,
                                              StringComparison.Ordinal)
                                          && option.Option.Choice.Contains(choiceId));
            return SelectChoice(current?.Option, choiceId);
        }
    }
}
