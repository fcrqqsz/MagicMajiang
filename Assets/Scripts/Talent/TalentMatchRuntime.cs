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
        private RuntimePhase _phase;
        private GameSession _session;
        private object _sessionIdentity;
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
                ValidateCarriedConfig(config, seatIndex);

                foreach (string talentId in config.SlotTalentIds)
                    TryAddEntry(seatIndex, talentId, isActive: true, registry, ref sequence);
                foreach (string talentId in config.ReserveTalentIds)
                    TryAddEntry(seatIndex, talentId, isActive: false, registry, ref sequence);
            }
        }

        private void TryAddEntry(
            int seatIndex,
            string talentId,
            bool isActive,
            TalentRegistry registry,
            ref int sequence)
        {
            if (string.IsNullOrWhiteSpace(talentId)) return;

            TalentRule rule = registry.CreateInstance(talentId, seatIndex);
            if (rule == null)
            {
                throw new ArgumentException(
                    $"Seat {seatIndex} carries unknown talent id: {talentId}",
                    nameof(talentId));
            }

            _entries.Add(new RuntimeEntry(
                seatIndex,
                rule,
                registry.GetMetadata(talentId),
                new TalentRuntimeState { IsActive = isActive },
                sequence++));
        }

        private static void ValidateCarriedConfig(TalentSlotConfig config, int seatIndex)
        {
            if (config == null)
                throw new ArgumentException($"Seat {seatIndex} talent config cannot be null.", nameof(config));
            if (config.SlotTalentIds == null
                || config.SlotTalentIds.Length != TalentSlotConfig.MainSlotCount)
            {
                throw new ArgumentException(
                    $"Seat {seatIndex} must have exactly {TalentSlotConfig.MainSlotCount} main talent slots.",
                    nameof(config));
            }
            if (config.ReserveTalentIds == null
                || config.ReserveTalentIds.Length != TalentSlotConfig.ReserveSlotCount)
            {
                throw new ArgumentException(
                    $"Seat {seatIndex} must have exactly {TalentSlotConfig.ReserveSlotCount} reserve talent slots.",
                    nameof(config));
            }

            HashSet<string> carriedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string talentId in config.SlotTalentIds.Concat(config.ReserveTalentIds))
            {
                if (string.IsNullOrWhiteSpace(talentId)) continue;
                if (!carriedIds.Add(talentId))
                    throw new ArgumentException($"Seat {seatIndex} carries duplicate talent id: {talentId}", nameof(config));
            }
        }

        public void BeginMatch(GameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (_phase != RuntimePhase.NotStarted)
                throw new InvalidOperationException("Talent match runtime has already begun this match.");
            _session = session;
            _sessionIdentity = TalentSessionSnapshot.Create(session).Identity;
            _phase = RuntimePhase.BetweenRounds;

            foreach (RuntimeEntry entry in _entries)
            {
                TalentMatchContext context = CreateMatchContext(
                    entry,
                    session,
                    allowEvents: entry.State.IsActive);
                entry.Rule.InitializeMatchState(context);
            }

            foreach (RuntimeEntry entry in GetAllActiveEntries())
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
                    }, isAuthoritativeScoreDelta: scoreDelta != 0);
                }
            }
        }

        public void BeginRound(TalentRoundContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureSession(context);
            EnsurePhase(RuntimePhase.BetweenRounds, nameof(BeginRound));

            _privatePeekTiles.Clear();
            foreach (RuntimeEntry entry in _entries)
                entry.State.ResetRoundState();

            _phase = RuntimePhase.RoundStarted;
            foreach (RuntimeEntry entry in GetAllActiveEntries())
                entry.Rule.OnRoundStarted(BindRoundContext(context, entry));
        }

        public void ApplyWallBuilding(TalentWallContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureSession(context);
            EnsurePhase(RuntimePhase.RoundStarted, nameof(ApplyWallBuilding));

            foreach (RuntimeEntry entry in GetGlobalPipeline(TalentPhase.WallBuilding))
                entry.Rule.OnWallBuilding(BindWallContext(context, entry));
            _phase = RuntimePhase.WallBuilt;
        }

        public void ResolvePostShuffle(TalentPostShuffleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureSession(context);
            EnsurePhase(RuntimePhase.WallBuilt, nameof(ResolvePostShuffle));

            foreach (RuntimeEntry entry in GetAllActiveEntries())
            {
                TalentRoundContext roundContext = new TalentRoundContext(
                    context.Session,
                    context.GameState,
                    context.DeckSnapshots);
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
            _phase = RuntimePhase.RoundReady;
        }

        public TileData ApplyDraw(TalentDrawContext context, TileData drawnTile)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureReadyRound(context, nameof(ApplyDraw));

            TileData currentTile = drawnTile;
            foreach (RuntimeEntry entry in GetSeatPipeline(TalentPhase.OnDraw, context.CurrentSeatIndex))
                currentTile = entry.Rule.OnDraw(BindContext(context, entry), currentTile);
            return currentTile;
        }

        public TileData ApplyDiscard(TalentDiscardContext context, TileData discardedTile)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureReadyRound(context, nameof(ApplyDiscard));

            TileData currentTile = discardedTile;
            foreach (RuntimeEntry entry in GetSeatPipeline(TalentPhase.OnDiscard, context.CurrentSeatIndex))
                currentTile = entry.Rule.OnDiscard(BindContext(context, entry), currentTile);
            return currentTile;
        }

        public void ValidateAction(TalentActionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureReadyRound(context, nameof(ValidateAction));

            context.IsAllowed = true;
            foreach (RuntimeEntry entry in GetSeatPipeline(TalentPhase.ActionValidation, context.CurrentSeatIndex))
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
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureReadyRound(context, nameof(BuildScoringOptions));

            return BuildScoringOptions(context, excludedEntry: null);
        }

        private ScoringOptions BuildScoringOptions(
            TalentScoringContext context,
            RuntimeEntry excludedEntry)
        {
            ScoringOptions options = new ScoringOptions();
            foreach (RuntimeEntry entry in GetActiveEntriesForSeat(context.CurrentSeatIndex))
            {
                if (ReferenceEquals(entry, excludedEntry)) continue;
                entry.Rule.ConfigureScoring(context.BindScoring(
                    entry.OwnerSeatIndex,
                    entry.State,
                    runtimeEvent => EmitEvent(entry, runtimeEvent)), options);
            }
            return options;
        }

        public void NotifyTileBecamePublic(TalentPublicTileContext context, TileData tile)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureReadyRound(context, nameof(NotifyTileBecamePublic));
            if (tile == null || !tile.IsModified || string.IsNullOrEmpty(tile.SpecialEffectID)) return;

            RuntimeEntry source = GetAllActiveEntries()
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
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureReadyRound(context, nameof(ResolveAcceptedWinVisibility));

            TalentScoringContext scoringContext = new TalentScoringContext(
                _session,
                context.CurrentSeatIndex);
            ScoringOptions acceptedOptions = BuildScoringOptions(scoringContext, excludedEntry: null);
            foreach (RuntimeEntry entry in GetActiveEntriesForSeat(context.CurrentSeatIndex))
            {
                if (entry.State.IsRevealed
                    || entry.Metadata.RevealPolicy == TalentRevealPolicy.OwnerOnly)
                {
                    continue;
                }

                ScoringOptions withoutEntry = BuildScoringOptions(scoringContext, entry);
                if (HaveEqualValues(acceptedOptions, withoutEntry)) continue;

                TalentWinEvaluation counterfactual = context.EvaluateWithOptions(withoutEntry);
                if (counterfactual != null
                    && counterfactual.IsLegal == context.AcceptedResult.IsLegal
                    && counterfactual.FinalFan == context.AcceptedResult.FinalFan)
                {
                    continue;
                }

                entry.State.IsRevealed = true;
                EmitEvent(entry, new TalentRuntimeEvent
                {
                    EventType = RevealEventType,
                    Visibility = TalentEventVisibility.Public
                });
            }
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
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            if (session == null) throw new ArgumentNullException(nameof(session));
            EnsureSession(session);
            EnsurePhase(RuntimePhase.RoundReady, nameof(EndRound));
            ValidateOptionalSeat(outcome.WinnerSeatIndex, nameof(outcome.WinnerSeatIndex));
            ValidateOptionalSeat(outcome.DiscarderSeatIndex, nameof(outcome.DiscarderSeatIndex));

            TalentRoundContext context = new TalentRoundContext(session);
            foreach (RuntimeEntry entry in GetAllActiveEntries())
            {
                entry.Rule.OnRoundEnded(BindRoundContext(
                    context,
                    entry,
                    (delta, reason) =>
                    {
                        session.Scores[entry.OwnerSeatIndex] += delta;
                        EmitEvent(entry, new TalentRuntimeEvent
                        {
                            EventType = reason,
                            Visibility = TalentEventVisibility.Public,
                            Value = delta
                        }, isAuthoritativeScoreDelta: true);
                    }), outcome);
            }
            _phase = RuntimePhase.BetweenRounds;
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

        private IEnumerable<RuntimeEntry> GetGlobalPipeline(TalentPhase phase)
        {
            return GetAllActiveEntries()
                .Where(entry => entry.Rule.Phases != null && entry.Rule.Phases.Contains(phase));
        }

        private IEnumerable<RuntimeEntry> GetSeatPipeline(TalentPhase phase, int currentSeatIndex)
        {
            return GetActiveEntriesForSeat(currentSeatIndex)
                .Where(entry => entry.Rule.Phases != null && entry.Rule.Phases.Contains(phase));
        }

        private IEnumerable<RuntimeEntry> GetAllActiveEntries()
        {
            return _entries
                .Where(entry => entry.State.IsActive)
                .OrderByDescending(entry => entry.Rule.Priority)
                .ThenBy(entry => entry.Sequence);
        }

        private IEnumerable<RuntimeEntry> GetActiveEntriesForSeat(int currentSeatIndex)
        {
            ValidateSeatIndex(currentSeatIndex);
            return _entries
                .Where(entry => entry.State.IsActive
                                && (entry.Rule.Scope == TalentScope.Global
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

        private TalentRoundContext BindRoundContext(
            TalentRoundContext context,
            RuntimeEntry entry,
            Action<int, string> scoreDeltaSink = null)
        {
            return context.BindRound(
                entry.OwnerSeatIndex,
                entry.State,
                runtimeEvent => EmitEvent(entry, runtimeEvent),
                scoreDeltaSink);
        }

        private static bool HaveEqualValues(ScoringOptions left, ScoringOptions right)
        {
            return left.BonusFan == right.BonusFan
                   && left.RelaxedPureStraight == right.RelaxedPureStraight;
        }

        private TalentContext BindContext(TalentContext context, RuntimeEntry entry)
        {
            return context.Bind(
                entry.OwnerSeatIndex,
                entry.State,
                runtimeEvent => EmitEvent(entry, runtimeEvent));
        }

        private TalentWallContext BindWallContext(TalentWallContext context, RuntimeEntry entry)
        {
            return context.BindWall(
                entry.OwnerSeatIndex,
                entry.State,
                runtimeEvent => EmitEvent(entry, runtimeEvent));
        }

        private void EmitEvent(
            RuntimeEntry entry,
            TalentRuntimeEvent source,
            bool isAuthoritativeScoreDelta = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            TalentRuntimeEvent runtimeEvent = new TalentRuntimeEvent
            {
                EventId = ++_nextEventId,
                OwnerSeatIndex = entry.OwnerSeatIndex,
                TalentId = entry.Rule.Id,
                EventType = source.EventType,
                Visibility = source.Visibility,
                Value = source.Value,
                IsScoreDelta = isAuthoritativeScoreDelta
            };
            _events.Add(runtimeEvent);

            if (runtimeEvent.Visibility == TalentEventVisibility.Public
                && entry.Metadata.RevealPolicy != TalentRevealPolicy.OwnerOnly)
            {
                entry.State.IsRevealed = true;
            }
        }

        private void EnsureReadyRound(TalentContext context, string operation)
        {
            EnsureSession(context);
            EnsurePhase(RuntimePhase.RoundReady, operation);
        }

        private void EnsureSession(TalentContext context)
        {
            if (!ReferenceEquals(_sessionIdentity, context.Session.Identity))
                throw new InvalidOperationException("Talent context belongs to a different match session.");
        }

        private void EnsureSession(GameSession session)
        {
            if (_phase == RuntimePhase.NotStarted)
                throw new InvalidOperationException("BeginMatch must be called before talent runtime use.");
            if (!ReferenceEquals(_session, session))
                throw new InvalidOperationException("Talent context belongs to a different match session.");
        }

        private void EnsurePhase(RuntimePhase expected, string operation)
        {
            if (_phase != expected)
                throw new InvalidOperationException($"{operation} is invalid during talent runtime phase {_phase}.");
        }

        private static void ValidateOptionalSeat(int? seatIndex, string parameterName)
        {
            if (seatIndex.HasValue) ValidateSeatIndex(seatIndex.Value, parameterName);
        }

        private static void ValidateSeatIndex(int seatIndex, string parameterName = "seatIndex")
        {
            if (seatIndex < 0 || seatIndex > 3)
                throw new ArgumentOutOfRangeException(parameterName, seatIndex, "Seat index must be 0..3.");
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

        private enum RuntimePhase
        {
            NotStarted,
            BetweenRounds,
            RoundStarted,
            WallBuilt,
            RoundReady
        }
    }
}
