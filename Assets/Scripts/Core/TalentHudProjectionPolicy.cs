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
        public int Value { get; set; }
        public bool ShowValue { get; set; }
        public string StatusText { get; set; }
        public bool IsInspectable { get; set; }
        public bool ShouldLogWarning { get; set; }
    }

    public sealed class TalentSeatSummary
    {
        public int SeatIndex { get; set; }
        public string PlayerDisplayName { get; set; }
        public IReadOnlyList<TalentHudItem> Visible { get; set; } = Array.Empty<TalentHudItem>();
        public IReadOnlyList<TalentHudItem> Expanded { get; set; } = Array.Empty<TalentHudItem>();
        public int CollapsedCount { get; set; }
    }

    public sealed class TalentHudView
    {
        public IReadOnlyList<TalentHudItem> OwnVisible { get; set; } = Array.Empty<TalentHudItem>();
        public IReadOnlyList<TalentHudItem> OwnCollapsed { get; set; } = Array.Empty<TalentHudItem>();
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
            IEnumerable<TalentRuntimeEventMessage> publicEvents = null,
            RoomSeatMessage[] roomSeats = null)
        {
            var ownTalents = snapshot?.privateSeat?.ownTalents ?? Array.Empty<SnapshotOwnTalent>();
            int ownModifiedTileCount = CountOwnModifiedPhysicalTiles(snapshot?.privateSeat);
            var activeOwn = ownTalents
                .Where(talent => talent != null && talent.isActive)
                .OrderBy(talent => talent.talentId, StringComparer.Ordinal)
                .Select(talent => CreateOwnItem(talent, ownModifiedTileCount, snapshot, roomSeats))
                .ToArray();
            var inactiveOwn = ownTalents
                .Where(talent => talent != null && !talent.isActive && IsKnownTalent(talent.talentId))
                .OrderBy(talent => talent.talentId, StringComparer.Ordinal)
                .Select(talent => CreateOwnItem(talent, ownModifiedTileCount, snapshot, roomSeats))
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
                    group => BuildOpponentSummary(
                        group.Key,
                        PlayerDisplayNamePolicy.Resolve(snapshot, group.Key, roomSeats),
                        group,
                        recency));

            return new TalentHudView
            {
                OwnVisible = activeOwn,
                OwnCollapsed = inactiveOwn,
                OwnCollapsedCount = ownTalents.Count(talent => talent != null && !talent.isActive),
                Seats = seats
            };
        }

        private static TalentSeatSummary BuildOpponentSummary(
            int seatIndex,
            string playerDisplayName,
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
                PlayerDisplayName = playerDisplayName,
                Visible = ordered.Take(OpponentVisibleLimit)
                    .Select(talent => CreateItem(talent.talentId, false, false, talent.lastPublicValue))
                    .ToArray(),
                Expanded = ordered
                    .Select(talent => CreateItem(talent.talentId, false, false, talent.lastPublicValue))
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

        private static TalentHudItem CreateItem(
            string talentId,
            bool isActive,
            bool showActiveState,
            int value = 0) => new TalentHudItem
        {
            TalentId = talentId,
            DisplayName = TalentRegistry.Instance.GetDisplayName(talentId),
            IsActive = isActive,
            ShowActiveState = showActiveState,
            Value = value,
            ShowValue = value != 0
        };

        private static TalentHudItem CreateOwnItem(
            SnapshotOwnTalent talent,
            int ownModifiedTileCount,
            RoomGameSnapshot snapshot,
            RoomSeatMessage[] roomSeats)
        {
            if (!IsKnownTalent(talent.talentId))
            {
                return new TalentHudItem
                {
                    TalentId = string.Empty,
                    DisplayName = "未知天赋",
                    IsActive = true,
                    ShowActiveState = true,
                    ShouldLogWarning = true
                };
            }

            string targetPlayerDisplayName = string.Equals(
                    talent.talentId,
                    "call_the_mark",
                    StringComparison.Ordinal)
                && string.Equals(talent.privateStatusKey, "pending", StringComparison.Ordinal)
                ? PlayerDisplayNamePolicy.Resolve(snapshot, talent.privateValue - 1, roomSeats)
                : null;
            string statusText = TalentChipStatusPolicy.Build(
                talent.talentId,
                talent.privateValue,
                talent.privateStatusKey,
                ownModifiedTileCount,
                targetPlayerDisplayName);
            return new TalentHudItem
            {
                TalentId = talent.talentId,
                DisplayName = TalentRegistry.Instance.GetDisplayName(talent.talentId),
                IsActive = talent.isActive,
                ShowActiveState = true,
                Value = talent.privateValue,
                ShowValue = string.IsNullOrEmpty(statusText) && talent.privateValue != 0,
                StatusText = statusText,
                IsInspectable = TalentObservationPolicy.IsInspectable(talent.talentId)
            };
        }

        private static int CountOwnModifiedPhysicalTiles(SnapshotPrivateSeat seat)
        {
            int concealedCount = (seat?.concealedHand ?? Array.Empty<SimpleTileData>())
                .Count(tile => tile != null && tile.isValid && tile.isModified);
            int meldCount = (seat?.melds ?? Array.Empty<SnapshotMeld>())
                .Where(meld => meld != null)
                .SelectMany(meld => meld.tiles ?? Array.Empty<SimpleTileData>())
                .Count(tile => tile != null && tile.isValid && tile.isModified);
            return concealedCount + meldCount;
        }

        private static bool IsKnownTalent(string talentId) =>
            !string.IsNullOrWhiteSpace(talentId) && TalentRegistry.Instance.HasTalent(talentId);

        private static bool IsPinnedPublicTalent(string talentId) =>
            TalentRegistry.Instance.GetMetadata(talentId).RevealPolicy == TalentRevealPolicy.PublicAtMatchStart;
    }
}
