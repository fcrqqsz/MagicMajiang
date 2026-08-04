using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents
{
    public sealed class TalentMatchRuntime
    {
        private const string RevealEventType = "talent_revealed";

        private readonly List<RuntimeEntry> _entries = new List<RuntimeEntry>();
        private readonly List<TalentRuntimeEvent> _events = new List<TalentRuntimeEvent>();
        private readonly Dictionary<int, long> _eventCursors = new Dictionary<int, long>();
        private readonly Dictionary<int, List<TileData>> _privatePeekTiles =
            new Dictionary<int, List<TileData>>();
        private bool _matchStarted;
        private long _nextEventId;

        public TalentMatchRuntime(
            IReadOnlyDictionary<int, TalentSlotConfig> loadouts,
            TalentRegistry registry)
        {
            if (loadouts == null) throw new ArgumentNullException(nameof(loadouts));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            int sequence = 0;
            foreach (KeyValuePair<int, TalentSlotConfig> loadout in loadouts)
            {
                int seatIndex = loadout.Key;
                if (seatIndex < 0 || seatIndex > 3)
                    throw new ArgumentOutOfRangeException(nameof(loadouts), seatIndex, "Seat index must be 0..3.");

                TalentSlotConfig config = loadout.Value;
                if (config == null) continue;

                HashSet<string> activeIds = new HashSet<string>(
                    config.GetMainIds(),
                    StringComparer.Ordinal);
                foreach (string talentId in config.GetCarriedIds())
                {
                    TalentRule rule = registry.CreateInstance(talentId, seatIndex);
                    if (rule == null)
                        throw new InvalidOperationException($"Unknown carried talent id: {talentId}");

                    _entries.Add(new RuntimeEntry(
                        seatIndex,
                        rule,
                        registry.GetMetadata(talentId),
                        new TalentRuntimeState { IsActive = activeIds.Contains(talentId) },
                        sequence++));
                }
            }
        }

        public void BeginMatch(GameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (_matchStarted)
                throw new InvalidOperationException("Talent match runtime has already begun this match.");
            _matchStarted = true;

            foreach (RuntimeEntry entry in _entries)
            {
                TalentMatchContext context = CreateMatchContext(
                    entry,
                    session,
                    allowEvents: entry.State.IsActive);
                entry.Rule.InitializeMatchState(context);
            }

            foreach (RuntimeEntry entry in GetActiveEntries())
            {
                TalentMatchContext context = CreateMatchContext(entry, session, allowEvents: true);
                int scoreDelta = entry.Rule.GetMatchStartScoreDelta(context);
                session.Scores[entry.OwnerSeatIndex] += scoreDelta;

                if (entry.Metadata.RevealPolicy == TalentRevealPolicy.PublicAtMatchStart)
                {
                    entry.State.IsRevealed = true;
                    EmitEvent(entry, new TalentRuntimeEvent
                    {
                        EventType = RevealEventType,
                        Visibility = TalentEventVisibility.Public,
                        Value = scoreDelta
                    });
                }
            }
        }

        public void BeginRound(TalentRoundContext context)
        {
            EnsureMatchStarted();
            if (context == null) throw new ArgumentNullException(nameof(context));

            _privatePeekTiles.Clear();
            foreach (RuntimeEntry entry in _entries)
                entry.State.ResetRoundState();

            foreach (RuntimeEntry entry in GetActiveEntries())
                entry.Rule.OnRoundStarted(BindRoundContext(context, entry));
        }

        public void ApplyWallBuilding(TalentWallContext context)
        {
            EnsureMatchStarted();
            if (context == null) throw new ArgumentNullException(nameof(context));

            foreach (RuntimeEntry entry in GetPipeline(TalentPhase.WallBuilding, currentSeatIndex: -1))
                entry.Rule.OnWallBuilding(BindContext(context, entry));
        }

        public void ResolvePostShuffle(TalentPostShuffleContext context)
        {
            EnsureMatchStarted();
            if (context == null) throw new ArgumentNullException(nameof(context));

            foreach (RuntimeEntry entry in GetActiveEntries())
            {
                TalentRoundContext roundContext = new TalentRoundContext(context.Session);
                int peekCount = entry.Rule.GetRoundStartPeekCount(BindRoundContext(roundContext, entry));
                if (peekCount <= 0) continue;

                int actualCount = Math.Min(peekCount, context.ShuffledWallTiles.Count);
                if (_privatePeekTiles.TryGetValue(entry.OwnerSeatIndex, out List<TileData> existing)
                    && existing.Count >= actualCount)
                {
                    continue;
                }

                List<TileData> snapshot = new List<TileData>(actualCount);
                for (int index = 0; index < actualCount; index++)
                    snapshot.Add(CopyTile(context.ShuffledWallTiles[index]));
                _privatePeekTiles[entry.OwnerSeatIndex] = snapshot;
            }
        }

        public TileData ApplyDraw(TalentDrawContext context, TileData drawnTile)
        {
            EnsureMatchStarted();
            if (context == null) throw new ArgumentNullException(nameof(context));

            TileData currentTile = drawnTile;
            foreach (RuntimeEntry entry in GetPipeline(TalentPhase.OnDraw, context.CurrentSeatIndex))
                currentTile = entry.Rule.OnDraw(BindContext(context, entry), currentTile);
            return currentTile;
        }

        public TileData ApplyDiscard(TalentDiscardContext context, TileData discardedTile)
        {
            EnsureMatchStarted();
            if (context == null) throw new ArgumentNullException(nameof(context));

            TileData currentTile = discardedTile;
            foreach (RuntimeEntry entry in GetPipeline(TalentPhase.OnDiscard, context.CurrentSeatIndex))
                currentTile = entry.Rule.OnDiscard(BindContext(context, entry), currentTile);
            return currentTile;
        }

        public void ValidateAction(TalentActionContext context)
        {
            EnsureMatchStarted();
            if (context == null) throw new ArgumentNullException(nameof(context));

            context.IsAllowed = true;
            foreach (RuntimeEntry entry in GetPipeline(TalentPhase.ActionValidation, context.CurrentSeatIndex))
            {
                if (entry.Rule.OnActionValidation(
                        BindContext(context, entry),
                        context.ActionType,
                        context.TargetTile))
                {
                    continue;
                }

                context.IsAllowed = false;
                return;
            }
        }

        public ScoringOptions BuildScoringOptions(TalentScoringContext context)
        {
            EnsureMatchStarted();
            if (context == null) throw new ArgumentNullException(nameof(context));

            ScoringOptions options = new ScoringOptions();
            foreach (RuntimeEntry entry in GetActiveEntries(context.CurrentSeatIndex))
                entry.Rule.ConfigureScoring(context.BindScoring(
                    entry.OwnerSeatIndex,
                    entry.State,
                    runtimeEvent => EmitEvent(entry, runtimeEvent)), options);
            return options;
        }

        public void NotifyTileBecamePublic(TalentPublicTileContext context, TileData tile)
        {
            EnsureMatchStarted();
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tile == null || !tile.IsModified || string.IsNullOrEmpty(tile.SpecialEffectID)) return;

            RuntimeEntry source = GetActiveEntries()
                .FirstOrDefault(entry => entry.OwnerSeatIndex == context.CurrentSeatIndex
                                         && string.Equals(
                                             entry.Rule.Id,
                                             tile.SpecialEffectID,
                                             StringComparison.Ordinal));
            if (source == null
                || source.State.IsRevealed
                || source.Metadata.RevealPolicy == TalentRevealPolicy.OwnerOnly)
            {
                return;
            }

            source.State.IsRevealed = true;
            EmitEvent(source, new TalentRuntimeEvent
            {
                EventType = RevealEventType,
                Visibility = TalentEventVisibility.Public
            });
        }

        public void ResolveAcceptedWinVisibility(TalentAcceptedWinContext context)
        {
            EnsureMatchStarted();
            if (context == null) throw new ArgumentNullException(nameof(context));
        }

        public IReadOnlyList<TileData> GetPrivatePeekTiles(int seatIndex)
        {
            ValidateSeatIndex(seatIndex);
            if (!_privatePeekTiles.TryGetValue(seatIndex, out List<TileData> tiles))
                return Array.Empty<TileData>();
            return tiles.Select(CopyTile).ToArray();
        }

        public void EndRound(TalentRoundOutcome outcome, GameSession session)
        {
            EnsureMatchStarted();
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            if (session == null) throw new ArgumentNullException(nameof(session));

            TalentRoundContext context = new TalentRoundContext(session);
            foreach (RuntimeEntry entry in GetActiveEntries())
                entry.Rule.OnRoundEnded(BindRoundContext(context, entry), outcome);
        }

        public IReadOnlyList<TalentRuntimeEvent> DrainEventsForSeat(int seatIndex)
        {
            ValidateSeatIndex(seatIndex);
            long cursor = _eventCursors.TryGetValue(seatIndex, out long existingCursor)
                ? existingCursor
                : 0;

            TalentRuntimeEvent[] visibleEvents = _events
                .Where(runtimeEvent => runtimeEvent.EventId > cursor
                                       && (runtimeEvent.Visibility == TalentEventVisibility.Public
                                           || runtimeEvent.OwnerSeatIndex == seatIndex))
                .Select(runtimeEvent => runtimeEvent.Copy())
                .ToArray();
            _eventCursors[seatIndex] = _nextEventId;
            return visibleEvents;
        }

        private IEnumerable<RuntimeEntry> GetPipeline(TalentPhase phase, int currentSeatIndex)
        {
            return GetActiveEntries(currentSeatIndex)
                .Where(entry => entry.Rule.Phases != null && entry.Rule.Phases.Contains(phase));
        }

        private IEnumerable<RuntimeEntry> GetActiveEntries(int currentSeatIndex = -1)
        {
            return _entries
                .Where(entry => entry.State.IsActive
                                && (currentSeatIndex < 0
                                    || entry.Rule.Scope == TalentScope.Global
                                    || entry.OwnerSeatIndex == currentSeatIndex))
                .OrderByDescending(entry => entry.Rule.Priority)
                .ThenBy(entry => entry.Sequence);
        }

        private TalentMatchContext CreateMatchContext(
            RuntimeEntry entry,
            GameSession session,
            bool allowEvents)
        {
            Action<TalentRuntimeEvent> eventSink = allowEvents
                ? runtimeEvent => EmitEvent(entry, runtimeEvent)
                : null;
            return new TalentMatchContext(session, entry.OwnerSeatIndex, entry.State, eventSink);
        }

        private TalentRoundContext BindRoundContext(TalentRoundContext context, RuntimeEntry entry)
        {
            return context.BindRound(
                entry.OwnerSeatIndex,
                entry.State,
                runtimeEvent => EmitEvent(entry, runtimeEvent));
        }

        private TalentContext BindContext(TalentContext context, RuntimeEntry entry)
        {
            return context.Bind(
                entry.OwnerSeatIndex,
                entry.State,
                runtimeEvent => EmitEvent(entry, runtimeEvent));
        }

        private void EmitEvent(RuntimeEntry entry, TalentRuntimeEvent source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            TalentRuntimeEvent runtimeEvent = new TalentRuntimeEvent
            {
                EventId = ++_nextEventId,
                OwnerSeatIndex = entry.OwnerSeatIndex,
                TalentId = entry.Rule.Id,
                EventType = source.EventType,
                Visibility = source.Visibility,
                Value = source.Value
            };
            _events.Add(runtimeEvent);

            if (runtimeEvent.Visibility == TalentEventVisibility.Public
                && entry.Metadata.RevealPolicy != TalentRevealPolicy.OwnerOnly)
            {
                entry.State.IsRevealed = true;
            }
        }

        private void EnsureMatchStarted()
        {
            if (!_matchStarted)
                throw new InvalidOperationException("BeginMatch must be called before talent runtime use.");
        }

        private static void ValidateSeatIndex(int seatIndex)
        {
            if (seatIndex < 0 || seatIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(seatIndex), seatIndex, "Seat index must be 0..3.");
        }

        private static TileData CopyTile(TileData tile)
        {
            if (tile == null) return null;
            return new TileData(tile.TileSuit, tile.Value, tile.OriginalOwnerID)
            {
                ID = tile.ID,
                IsModified = tile.IsModified,
                SpecialEffectID = tile.SpecialEffectID
            };
        }

        private sealed class RuntimeEntry
        {
            public int OwnerSeatIndex { get; }
            public TalentRule Rule { get; }
            public TalentMetadata Metadata { get; }
            public TalentRuntimeState State { get; }
            public int Sequence { get; }

            public RuntimeEntry(
                int ownerSeatIndex,
                TalentRule rule,
                TalentMetadata metadata,
                TalentRuntimeState state,
                int sequence)
            {
                OwnerSeatIndex = ownerSeatIndex;
                Rule = rule;
                Metadata = metadata;
                State = state;
                Sequence = sequence;
            }
        }
    }
}
