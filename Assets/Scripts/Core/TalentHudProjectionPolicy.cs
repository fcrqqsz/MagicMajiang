using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core
{
    public sealed class TalentHudItem
    {
        public string TalentId { get; set; }
        public string DisplayName { get; set; }
        public bool IsActive { get; set; }
        public bool ShowActiveState { get; set; }
        public bool ShouldLogWarning { get; set; }
    }

    public sealed class TalentSeatSummary
    {
        public int SeatIndex { get; set; }
        public IReadOnlyList<TalentHudItem> Visible { get; set; } = Array.Empty<TalentHudItem>();
        public int CollapsedCount { get; set; }
    }

    public sealed class TalentHudView
    {
        public IReadOnlyList<TalentHudItem> OwnVisible { get; set; } = Array.Empty<TalentHudItem>();
        public int OwnCollapsedCount { get; set; }
        public IReadOnlyDictionary<int, TalentSeatSummary> Seats { get; set; }
            = new Dictionary<int, TalentSeatSummary>();
    }

    /// <summary>
    /// Builds a presentation-only, privacy-preserving talent HUD projection from an already-filtered snapshot.
    /// It intentionally cannot infer opponent loadout size or active sideboard state.
    /// </summary>
    public static class TalentHudProjectionPolicy
    {
        private const int OpponentVisibleLimit = 2;

        public static TalentHudView Build(
            RoomGameSnapshot snapshot,
            int localSeatIndex,
            IEnumerable<TalentRuntimeEventMessage> publicEvents = null)
        {
            var ownTalents = snapshot?.privateSeat?.ownTalents ?? Array.Empty<SnapshotOwnTalent>();
            var activeOwn = ownTalents
                .Where(talent => talent != null && talent.isActive)
                .OrderBy(talent => talent.talentId, StringComparer.Ordinal)
                .Select(CreateOwnItem)
                .ToArray();

            var recency = BuildPublicRecency(publicEvents);
            var seats = (snapshot?.knownTalents ?? Array.Empty<SnapshotKnownTalent>())
                .Where(talent => talent != null
                                 && talent.ownerSeatIndex != localSeatIndex
                                 && talent.isKnown
                                 && IsKnownTalent(talent.talentId))
                .GroupBy(talent => talent.ownerSeatIndex)
                .ToDictionary(
                    group => group.Key,
                    group => BuildOpponentSummary(group.Key, group, recency));

            return new TalentHudView
            {
                OwnVisible = activeOwn,
                OwnCollapsedCount = ownTalents.Count(talent => talent != null && !talent.isActive),
                Seats = seats
            };
        }

        private static TalentSeatSummary BuildOpponentSummary(
            int seatIndex,
            IEnumerable<SnapshotKnownTalent> talents,
            IReadOnlyDictionary<string, long> recency)
        {
            SnapshotKnownTalent[] ordered = talents
                .GroupBy(talent => talent.talentId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(talent => IsPinnedPublicTalent(talent.talentId))
                .ThenByDescending(talent => GetRecency(recency, seatIndex, talent.talentId))
                .ThenBy(talent => talent.talentId, StringComparer.Ordinal)
                .ToArray();

            return new TalentSeatSummary
            {
                SeatIndex = seatIndex,
                Visible = ordered.Take(OpponentVisibleLimit)
                    .Select(talent => CreateItem(talent.talentId, false, false))
                    .ToArray(),
                CollapsedCount = Math.Max(0, ordered.Length - OpponentVisibleLimit)
            };
        }

        private static IReadOnlyDictionary<string, long> BuildPublicRecency(
            IEnumerable<TalentRuntimeEventMessage> publicEvents)
        {
            return (publicEvents ?? Array.Empty<TalentRuntimeEventMessage>())
                .Where(runtimeEvent => runtimeEvent != null
                                       && runtimeEvent.visibility == (int)TalentEventVisibility.Public
                                       && runtimeEvent.eventId > 0
                                       && IsKnownTalent(runtimeEvent.talentId))
                .GroupBy(runtimeEvent => EventKey(runtimeEvent.ownerSeatIndex, runtimeEvent.talentId), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Max(runtimeEvent => runtimeEvent.eventId), StringComparer.Ordinal);
        }

        private static long GetRecency(IReadOnlyDictionary<string, long> recency, int seatIndex, string talentId) =>
            recency.TryGetValue(EventKey(seatIndex, talentId), out long eventId) ? eventId : 0;

        private static string EventKey(int seatIndex, string talentId) => seatIndex + ":" + talentId;

        private static TalentHudItem CreateItem(string talentId, bool isActive, bool showActiveState) => new TalentHudItem
        {
            TalentId = talentId,
            DisplayName = TalentRegistry.Instance.GetDisplayName(talentId),
            IsActive = isActive,
            ShowActiveState = showActiveState
        };

        private static TalentHudItem CreateOwnItem(SnapshotOwnTalent talent) =>
            IsKnownTalent(talent.talentId)
                ? CreateItem(talent.talentId, true, true)
                : new TalentHudItem
                {
                    TalentId = string.Empty,
                    DisplayName = "未知天赋",
                    IsActive = true,
                    ShowActiveState = true,
                    ShouldLogWarning = true
                };

        private static bool IsKnownTalent(string talentId) =>
            !string.IsNullOrWhiteSpace(talentId) && TalentRegistry.Instance.HasTalent(talentId);

        private static bool IsPinnedPublicTalent(string talentId) =>
            TalentRegistry.Instance.GetMetadata(talentId).RevealPolicy == TalentRevealPolicy.PublicAtMatchStart;
    }
}
