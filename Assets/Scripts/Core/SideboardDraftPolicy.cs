using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core
{
    public static class SideboardDraftErrorCodes
    {
        public const string ReadOnly = "SideboardDraftReadOnly";
        public const string LockedTalent = "SideboardDraftLockedTalent";
        public const string UnknownTalent = "SideboardDraftUnknownTalent";
        public const string NotCarried = "SideboardDraftTalentNotCarried";
        public const string DuplicateTalent = "SideboardDraftDuplicateTalent";
        public const string InvalidSelection = "SideboardDraftInvalidSelection";
        public const string AlienationLimitExceeded = "SideboardDraftAlienationLimitExceeded";
    }

    /// <summary>A local, immutable presentation draft. It never represents client authority.</summary>
    public sealed class SideboardDraft
    {
        public long DecisionId { get; }
        public long DeadlineUnixMilliseconds { get; }
        public IReadOnlyList<string> CarriedMainTalentIds { get; }
        public IReadOnlyList<string> CarriedReserveTalentIds { get; }
        public IReadOnlyList<string> ActiveTalentIds { get; }
        public IReadOnlyList<bool> SeatLocked { get; }
        public int AlienationLimit { get; }
        public int TotalAlienation { get; }
        public int DeckAlienation { get; }
        public int ActiveTalentAlienation => Math.Max(0, TotalAlienation - DeckAlienation);
        public bool IsOverLimit => TotalAlienation > AlienationLimit;
        public bool IsReadOnly { get; }
        public string ErrorCode { get; }
        public bool CanLock => !IsReadOnly && string.IsNullOrEmpty(ErrorCode) && !IsOverLimit;

        internal SideboardDraft(
            long decisionId,
            long deadlineUnixMilliseconds,
            IEnumerable<string> carriedMainTalentIds,
            IEnumerable<string> carriedReserveTalentIds,
            IEnumerable<string> activeTalentIds,
            IEnumerable<bool> seatLocked,
            int alienationLimit,
            int totalAlienation,
            int deckAlienation,
            bool isReadOnly,
            string errorCode)
        {
            DecisionId = decisionId;
            DeadlineUnixMilliseconds = deadlineUnixMilliseconds;
            CarriedMainTalentIds = ReadOnlyCopy(carriedMainTalentIds);
            CarriedReserveTalentIds = ReadOnlyCopy(carriedReserveTalentIds);
            ActiveTalentIds = ReadOnlyCopy(activeTalentIds);
            SeatLocked = new ReadOnlyCollection<bool>((seatLocked ?? Array.Empty<bool>()).ToArray());
            AlienationLimit = Math.Max(0, alienationLimit);
            TotalAlienation = Math.Max(0, totalAlienation);
            DeckAlienation = Math.Max(0, deckAlienation);
            IsReadOnly = isReadOnly;
            ErrorCode = errorCode;
        }

        private static ReadOnlyCollection<string> ReadOnlyCopy(IEnumerable<string> source) =>
            new ReadOnlyCollection<string>((source ?? Array.Empty<string>()).ToArray());
    }

    public static class SideboardDraftPolicy
    {
        public static SideboardDraft Create(SideboardStartedMessage started)
        {
            if (started == null) throw new ArgumentNullException(nameof(started));

            TalentRegistry registry = TalentRegistry.Instance;
            string[] main = CopySlots(started.carriedMainTalentIds, TalentSlotConfig.MainSlotCount);
            string[] reserve = CopySlots(started.carriedReserveTalentIds, TalentSlotConfig.ReserveSlotCount);
            string[] carried = EnumerateCarried(main, reserve).ToArray();
            string[] requested = NormalizeIds(started.currentActiveTalentIds).ToArray();
            var selected = new HashSet<string>(requested, StringComparer.Ordinal);
            string[] active = carried.Where(selected.Contains).ToArray();
            int talentCost = active.Sum(registry.GetCost);
            int deckCost = Math.Max(0, started.currentTotalAlienation - talentCost);
            int limit = Math.Max(0, started.alienationLimit);
            AlienationGaugeView gauge = BuildGauge(deckCost, talentCost, limit);
            string errorCode = GetSourceError(started, carried, requested, talentCost, gauge.Total, gauge.Limit, registry);
            bool isReadOnly = !string.IsNullOrEmpty(errorCode)
                              && errorCode != SideboardDraftErrorCodes.AlienationLimitExceeded;

            return new SideboardDraft(
                started.decisionId,
                started.deadlineUnixMilliseconds,
                main,
                reserve,
                active,
                Array.Empty<bool>(),
                gauge.Limit,
                gauge.Total,
                gauge.DeckCost,
                isReadOnly,
                errorCode);
        }

        private static string GetSourceError(
            SideboardStartedMessage started,
            string[] carried,
            string[] requested,
            int talentCost,
            int totalAlienation,
            int limit,
            TalentRegistry registry)
        {
            if (carried.Any(id => !registry.HasTalent(id)) || requested.Any(id => !registry.HasTalent(id)))
                return SideboardDraftErrorCodes.UnknownTalent;
            if (carried.Distinct(StringComparer.Ordinal).Count() != carried.Length
                || requested.Distinct(StringComparer.Ordinal).Count() != requested.Length)
            {
                return SideboardDraftErrorCodes.DuplicateTalent;
            }
            var carriedSet = new HashSet<string>(carried, StringComparer.Ordinal);
            if (requested.Any(id => !carriedSet.Contains(id)))
                return SideboardDraftErrorCodes.NotCarried;
            if (!TalentLoadoutSlotPolicy.TryBuild(
                    started.carriedMainTalentIds,
                    started.carriedReserveTalentIds,
                    registry,
                    out TalentSlotConfig rebuilt)
                || !AlienationBudgetPolicy.IsDefined((AlienationPreset)started.alienationLimit)
                || started.currentTotalAlienation < talentCost)
            {
                return SideboardDraftErrorCodes.InvalidSelection;
            }
            foreach (string carriedId in SideboardLoadoutPolicy.GetCarriedIdsInSlotOrder(rebuilt))
            {
                if (registry.GetMetadata(carriedId).SideboardPolicy == TalentSideboardPolicy.MainOnlyLocked
                    && !requested.Contains(carriedId, StringComparer.Ordinal))
                {
                    return SideboardDraftErrorCodes.LockedTalent;
                }
            }
            return totalAlienation > limit ? SideboardDraftErrorCodes.AlienationLimitExceeded : null;
        }

        public static SideboardDraft CreateReadOnly(SnapshotSideboardState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return new SideboardDraft(
                state.decisionId,
                state.deadlineUnixMilliseconds,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                state.seatLocked,
                0,
                0,
                0,
                true,
                null);
        }

        public static SideboardDraft SetActive(
            SideboardDraft source,
            string talentId,
            bool isActive,
            TalentRegistry registry)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.IsReadOnly) return WithError(source, SideboardDraftErrorCodes.ReadOnly);
            if (registry == null) return WithError(source, SideboardDraftErrorCodes.InvalidSelection);

            string normalizedId = talentId?.Trim();
            if (string.IsNullOrEmpty(normalizedId) || !registry.HasTalent(normalizedId))
                return WithError(source, SideboardDraftErrorCodes.UnknownTalent);
            if (!EnumerateCarried(source).Contains(normalizedId, StringComparer.Ordinal))
                return WithError(source, SideboardDraftErrorCodes.NotCarried);

            bool alreadyActive = source.ActiveTalentIds.Contains(normalizedId, StringComparer.Ordinal);
            if (isActive && alreadyActive)
                return WithError(source, SideboardDraftErrorCodes.DuplicateTalent);
            if (!isActive && !alreadyActive)
                return WithError(source, SideboardDraftErrorCodes.InvalidSelection);
            if (!isActive
                && registry.GetMetadata(normalizedId).SideboardPolicy == TalentSideboardPolicy.MainOnlyLocked)
            {
                return WithError(source, SideboardDraftErrorCodes.LockedTalent);
            }

            IEnumerable<string> replacement = isActive
                ? source.ActiveTalentIds.Concat(new[] { normalizedId })
                : source.ActiveTalentIds.Where(id => !string.Equals(id, normalizedId, StringComparison.Ordinal));
            return ReplaceActive(source, replacement, registry);
        }

        public static SideboardDraft ReplaceActive(
            SideboardDraft source,
            IEnumerable<string> activeTalentIds,
            TalentRegistry registry)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.IsReadOnly) return WithError(source, SideboardDraftErrorCodes.ReadOnly);
            if (activeTalentIds == null || registry == null)
                return WithError(source, SideboardDraftErrorCodes.InvalidSelection);

            string[] requested = NormalizeIds(activeTalentIds).ToArray();
            if (requested.Length > TalentSlotConfig.MainSlotCount + TalentSlotConfig.ReserveSlotCount)
                return WithError(source, SideboardDraftErrorCodes.InvalidSelection);
            if (requested.Distinct(StringComparer.Ordinal).Count() != requested.Length)
                return WithError(source, SideboardDraftErrorCodes.DuplicateTalent);
            if (requested.Any(id => !registry.HasTalent(id)))
                return WithError(source, SideboardDraftErrorCodes.UnknownTalent);

            string[] carried = EnumerateCarried(source).ToArray();
            var carriedSet = new HashSet<string>(carried, StringComparer.Ordinal);
            if (requested.Any(id => !carriedSet.Contains(id)))
                return WithError(source, SideboardDraftErrorCodes.NotCarried);

            var selected = new HashSet<string>(requested, StringComparer.Ordinal);
            foreach (string carriedId in carried)
            {
                if (registry.GetMetadata(carriedId).SideboardPolicy == TalentSideboardPolicy.MainOnlyLocked
                    && !selected.Contains(carriedId))
                {
                    return WithError(source, SideboardDraftErrorCodes.LockedTalent);
                }
            }

            string[] canonical = carried.Where(selected.Contains).ToArray();
            int talentCost = canonical.Sum(registry.GetCost);
            AlienationGaugeView gauge = BuildGauge(source.DeckAlienation, talentCost, source.AlienationLimit);
            return new SideboardDraft(
                source.DecisionId,
                source.DeadlineUnixMilliseconds,
                source.CarriedMainTalentIds,
                source.CarriedReserveTalentIds,
                canonical,
                source.SeatLocked,
                gauge.Limit,
                gauge.Total,
                gauge.DeckCost,
                false,
                gauge.IsOverLimit ? SideboardDraftErrorCodes.AlienationLimitExceeded : null);
        }

        private static SideboardDraft WithError(SideboardDraft source, string errorCode) =>
            new SideboardDraft(
                source.DecisionId,
                source.DeadlineUnixMilliseconds,
                source.CarriedMainTalentIds,
                source.CarriedReserveTalentIds,
                source.ActiveTalentIds,
                source.SeatLocked,
                source.AlienationLimit,
                source.TotalAlienation,
                source.DeckAlienation,
                source.IsReadOnly,
                errorCode);

        private static AlienationGaugeView BuildGauge(int deckCost, int talentCost, int limit)
        {
            var preset = (AlienationPreset)limit;
            if (!AlienationBudgetPolicy.IsDefined(preset)) preset = AlienationPreset.Standard;
            return AlienationGaugePolicy.Build(deckCost, talentCost, preset);
        }

        private static string[] CopySlots(string[] source, int count)
        {
            var result = new string[count];
            for (int index = 0; index < count; index++)
            {
                string value = source != null && index < source.Length ? source[index] : null;
                result[index] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            return result;
        }

        private static IEnumerable<string> EnumerateCarried(SideboardDraft source) =>
            EnumerateCarried(source.CarriedMainTalentIds, source.CarriedReserveTalentIds);

        private static IEnumerable<string> EnumerateCarried(
            IEnumerable<string> main,
            IEnumerable<string> reserve) =>
            (main ?? Array.Empty<string>())
                .Concat(reserve ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id));

        private static IEnumerable<string> NormalizeIds(IEnumerable<string> ids) =>
            (ids ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim());
    }
}
