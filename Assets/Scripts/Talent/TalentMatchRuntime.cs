using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using UnityEngine;

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
        private readonly Dictionary<int, TalentPrivateTileReveal> _privateTileReveals =
            new Dictionary<int, TalentPrivateTileReveal>();
        private readonly Dictionary<int, long> _privateTileRevealVersions =
            new Dictionary<int, long>();
        private readonly Dictionary<int, long> _firstMainDecisionIds =
            new Dictionary<int, long>();
        private readonly List<TalentActionCommittedFacts> _roundActions =
            new List<TalentActionCommittedFacts>();
        private readonly HashSet<long> _committedActionDecisionIds = new HashSet<long>();
        private readonly ITalentTelemetrySink _telemetrySink;
        private readonly string _anonymousSessionId;
        private readonly AlienationPreset _telemetryPreset;
        private RuntimePhase _phase;
        private GameSession _session;
        private object _sessionIdentity;
        private long _nextEventId;

        public TalentMatchRuntime(
            IReadOnlyDictionary<int, TalentSlotConfig> loadouts,
            TalentRegistry registry) : this(
                loadouts,
                registry,
                NullTalentTelemetrySink.Instance,
                Guid.NewGuid().ToString("N"),
                AlienationPreset.Standard)
        {
        }

        public TalentMatchRuntime(
            IReadOnlyDictionary<int, TalentSlotConfig> loadouts,
            TalentRegistry registry,
            ITalentTelemetrySink telemetrySink,
            string anonymousSessionId,
            AlienationPreset telemetryPreset)
        {
            if (loadouts == null) throw new ArgumentNullException(nameof(loadouts));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (string.IsNullOrWhiteSpace(anonymousSessionId))
                throw new ArgumentException("An anonymous telemetry session id is required.", nameof(anonymousSessionId));

            _telemetrySink = telemetrySink ?? NullTalentTelemetrySink.Instance;
            _anonymousSessionId = anonymousSessionId;
            _telemetryPreset = telemetryPreset;

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
            _privateTileReveals.Clear();
            _privateTileRevealVersions.Clear();

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

            RecordTelemetry(CreateTelemetryRecord("match_start"));
        }

        public void BeginRound(TalentRoundContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureSession(context);
            EnsurePhase(RuntimePhase.BetweenRounds, nameof(BeginRound));

            _privatePeekTiles.Clear();
            _privateTileReveals.Clear();
            _privateTileRevealVersions.Clear();
            _firstMainDecisionIds.Clear();
            _roundActions.Clear();
            _committedActionDecisionIds.Clear();
            foreach (RuntimeEntry entry in _entries)
                entry.State.ResetRoundState();

            _phase = RuntimePhase.RoundStarted;
            foreach (RuntimeEntry entry in GetAllActiveEntries())
                entry.Rule.OnRoundStarted(BindRoundContext(context, entry));
            RecordTelemetry(CreateTelemetryRecord("round_start"));
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
            EnsurePhase(RuntimePhase.InitialHandsCompleted, nameof(ResolvePostShuffle));

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

        public void CompleteInitialHands(TalentInitialHandsContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureSession(context);
            EnsurePhase(RuntimePhase.WallBuilt, nameof(CompleteInitialHands));

            foreach (RuntimeEntry entry in GetGlobalPipeline(TalentPhase.InitialHandCompleted))
            {
                entry.Rule.OnInitialHandCompleted(context.BindInitialHand(
                    entry.OwnerSeatIndex,
                    entry.Rule.Id,
                    entry.State,
                    runtimeEvent => EmitEvent(entry, runtimeEvent)));
            }
            context.Commit();
            _phase = RuntimePhase.InitialHandsCompleted;
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

        public bool CommitAction(TalentActionCommittedFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            EnsurePhase(RuntimePhase.RoundReady, nameof(CommitAction));
            if (!_committedActionDecisionIds.Add(facts.DecisionId)) return false;

            _roundActions.Add(facts);
            var ledger = new TalentRoundActionLedgerSnapshot(_roundActions);
            var context = new TalentActionCommittedContext(_session, facts, ledger);
            foreach (RuntimeEntry entry in GetActiveEntriesForSeat(facts.ActorSeatIndex))
            {
                entry.Rule.OnActionCommitted(context.BindCommittedAction(
                    entry.OwnerSeatIndex,
                    entry.State,
                    runtimeEvent => EmitEvent(entry, runtimeEvent)));
            }
            return true;
        }

        public TalentNegativeEffectResult ApplyNegativeEffect(TalentNegativeEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            EnsurePhase(RuntimePhase.RoundReady, nameof(ApplyNegativeEffect));

            if (!string.Equals(
                    effect.EffectType,
                    TalentNegativeEffectTypes.ReducePublicChargeLayer,
                    StringComparison.Ordinal))
            {
                Debug.LogWarning($"[TalentMatchRuntime] Rejected unknown negative effect type: {effect.EffectType}");
                return new TalentNegativeEffectResult();
            }
            ValidateSeatIndex(effect.SourceSeatIndex, nameof(effect.SourceSeatIndex));
            ValidateSeatIndex(effect.TargetSeatIndex, nameof(effect.TargetSeatIndex));
            RuntimeEntry targetEntry = FindActiveEntry(effect.TargetSeatIndex, effect.TargetTalentId);
            if (targetEntry == null || !(targetEntry.Rule is IPublicChargeTalent publicChargeTarget))
            {
                Debug.LogWarning(
                    "[TalentMatchRuntime] Rejected negative effect without an active public-charge target.");
                return new TalentNegativeEffectResult();
            }
            if (effect.SourceSeatIndex == effect.TargetSeatIndex
                || !targetEntry.State.IsRevealed
                || publicChargeTarget.GetCurrentCharge(targetEntry.State) <= 0)
            {
                Debug.LogWarning(
                    "[TalentMatchRuntime] Rejected negative effect against an ineligible public-charge target.");
                return new TalentNegativeEffectResult();
            }

            foreach (RuntimeEntry entry in GetActiveTargetDefenses(effect.TargetSeatIndex))
            {
                TalentNegativeEffectContext context = new TalentNegativeEffectContext(
                    entry.State,
                    runtimeEvent => EmitEvent(entry, runtimeEvent));
                if (!entry.Rule.TryBlockNegativeEffect(context, effect)) continue;

                if (!context.HasPublicEffect)
                {
                    EmitEvent(entry, new TalentRuntimeEvent
                    {
                        EventType = "blocked_negative_effect",
                        Visibility = TalentEventVisibility.Public,
                        Value = 1
                    });
                }
                return new TalentNegativeEffectResult
                {
                    WasBlocked = true,
                    BlockingTalentId = entry.Rule.Id
                };
            }

            bool wasApplied = publicChargeTarget.TryReduceCharge(targetEntry.State, amount: 1);
            if (wasApplied)
            {
                EmitEvent(targetEntry, new TalentRuntimeEvent
                {
                    EventType = "public_charge_reduced",
                    Visibility = TalentEventVisibility.Public,
                    Value = publicChargeTarget.GetCurrentCharge(targetEntry.State)
                });
            }
            return new TalentNegativeEffectResult { WasApplied = wasApplied };
        }

        public void OpenMainDecision(int ownerSeatIndex, long decisionId)
        {
            ValidateSeatIndex(ownerSeatIndex, nameof(ownerSeatIndex));
            EnsurePhase(RuntimePhase.RoundReady, nameof(OpenMainDecision));
            if (decisionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(decisionId), decisionId, "Decision id must be positive.");
            if (!_firstMainDecisionIds.ContainsKey(ownerSeatIndex))
                _firstMainDecisionIds[ownerSeatIndex] = decisionId;
        }

        public IReadOnlyList<TalentActionOption> GetAvailableActions(
            int ownerSeatIndex,
            TalentActionQueryContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            ValidateSeatIndex(ownerSeatIndex);
            EnsureReadyRound(context, nameof(GetAvailableActions));
            if (context.CurrentSeatIndex != ownerSeatIndex
                || context.RequiredWindow == TalentActivationWindow.None)
            {
                return Array.Empty<TalentActionOption>();
            }

            bool isFirstMainDecision = IsFirstMainDecision(
                ownerSeatIndex,
                context.RequiredWindow,
                context.DecisionId);
            var options = new List<TalentActionOption>();
            foreach (RuntimeEntry entry in _entries
                         .Where(entry => entry.OwnerSeatIndex == ownerSeatIndex
                                         && entry.State.IsActive
                                         && entry.Metadata.ActivationWindow.HasFlag(context.RequiredWindow))
                         .OrderBy(entry => entry.Sequence))
            {
                entry.Rule.GetAvailableActions(
                    context.WithState(
                        entry.State.CreateDetachedCopy(),
                        isFirstMainDecision,
                        this),
                    options);
            }
            return options;
        }

        public TalentActionResult TryActivate(
            int ownerSeatIndex,
            TalentActionRequest request,
            TalentActivationContext context)
        {
            if (request == null || context == null)
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidTarget);
            ValidateSeatIndex(ownerSeatIndex);
            EnsureReadyRound(context, nameof(TryActivate));

            if (context.CurrentSeatIndex != ownerSeatIndex)
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidTarget);
            if (request.DecisionId != context.DecisionId)
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidTarget);
            if (context.RequiredWindow == TalentActivationWindow.None)
                return TalentActionResult.Reject(TalentActionErrorCodes.NotAvailable);

            RuntimeEntry entry = FindActiveEntry(ownerSeatIndex, request.TalentId);
            if (entry == null)
                return TalentActionResult.Reject(TalentActionErrorCodes.NotCarriedOrInactive);

            if (!entry.Metadata.ActivationWindow.HasFlag(context.RequiredWindow))
                return TalentActionResult.Reject(TalentActionErrorCodes.NotAvailable);

            TalentActionResult choiceValidation = ValidateAuthorizedChoice(
                entry,
                request,
                context);
            if (choiceValidation != null) return choiceValidation;

            TalentActionResult result = entry.Rule.TryActivate(
                context.WithState(
                    entry.State,
                    runtimeEvent => EmitEvent(entry, runtimeEvent),
                    IsFirstMainDecision(
                        ownerSeatIndex,
                        context.RequiredWindow,
                        context.DecisionId),
                    this,
                    entry.Rule.Id),
                request)
                ?? TalentActionResult.NotSupported();
            if (result.Accepted && result.EffectApplied)
            {
                EmitEvent(entry, new TalentRuntimeEvent
                {
                    EventType = "active_talent_applied",
                    Visibility = TalentEventVisibility.Public
                });
                if (!string.IsNullOrWhiteSpace(result.PublicStateEventType))
                {
                    EmitEvent(entry, new TalentRuntimeEvent
                    {
                        EventType = result.PublicStateEventType,
                        Visibility = TalentEventVisibility.Public,
                        Value = result.PublicStateValue
                    });
                }
            }
            return result;
        }

        private TalentActionResult ValidateAuthorizedChoice(
            RuntimeEntry entry,
            TalentActionRequest request,
            TalentActivationContext context)
        {
            var advertised = new List<TalentActionOption>();
            entry.Rule.GetAvailableActions(
                new TalentActionQueryContext(
                        _session,
                        context.CurrentSeatIndex,
                        context.RequiredWindow,
                        context.DecisionId)
                    .WithState(
                        entry.State.CreateDetachedCopy(),
                        IsFirstMainDecision(
                            context.CurrentSeatIndex,
                            context.RequiredWindow,
                            context.DecisionId),
                        this),
                advertised);
            TalentActionOption[] matching = advertised
                .Where(option => option != null
                                 && string.Equals(option.TalentId, request.TalentId, StringComparison.Ordinal)
                                 && option.TargetSeatIndex == request.TargetSeatIndex
                                 && string.Equals(
                                     option.TargetTalentId ?? string.Empty,
                                     request.TargetTalentId ?? string.Empty,
                                     StringComparison.Ordinal))
                .ToArray();
            bool requestHasChoice = !string.IsNullOrWhiteSpace(request.ChoiceId);
            TalentActionOption[] choices = matching
                .Where(option => option.Choice != null)
                .ToArray();

            if (choices.Length == 0)
            {
                return requestHasChoice
                    ? TalentActionResult.Reject(TalentActionErrorCodes.InvalidChoice)
                    : null;
            }

            return requestHasChoice && choices.Any(option => option.Choice.Contains(request.ChoiceId))
                ? null
                : TalentActionResult.Reject(TalentActionErrorCodes.InvalidChoice);
        }

        public int GetPublicCounter(int ownerSeatIndex, string talentId, string key)
        {
            ValidateSeatIndex(ownerSeatIndex, nameof(ownerSeatIndex));
            RuntimeEntry entry = FindCarriedEntry(ownerSeatIndex, talentId);
            if (entry == null || !entry.State.IsRevealed) return 0;
            return entry.State.GetCounter(key, TalentStateScope.Match);
        }

        public int GetPrivateCounter(int ownerSeatIndex, string talentId, string key)
        {
            ValidateSeatIndex(ownerSeatIndex, nameof(ownerSeatIndex));
            RuntimeEntry entry = FindCarriedEntry(ownerSeatIndex, talentId);
            return entry?.State.GetCounter(key, TalentStateScope.Match) ?? 0;
        }

        public void ReplaceActiveSet(int ownerSeatIndex, IEnumerable<string> activeTalentIds)
        {
            ValidateSeatIndex(ownerSeatIndex, nameof(ownerSeatIndex));
            if (activeTalentIds == null)
                throw new ArgumentNullException(nameof(activeTalentIds));

            HashSet<string> activeIds = new HashSet<string>(
                activeTalentIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
            foreach (string talentId in activeIds)
            {
                if (FindCarriedEntry(ownerSeatIndex, talentId) == null)
                {
                    throw new ArgumentException(
                        $"Seat {ownerSeatIndex} does not carry talent id: {talentId}",
                        nameof(activeTalentIds));
                }
            }

            foreach (RuntimeEntry entry in _entries.Where(entry => entry.OwnerSeatIndex == ownerSeatIndex))
                entry.State.IsActive = activeIds.Contains(entry.Rule.Id);
        }

        public IReadOnlyList<string> GetActiveTalentIds(int ownerSeatIndex)
        {
            ValidateSeatIndex(ownerSeatIndex, nameof(ownerSeatIndex));
            return _entries
                .Where(entry => entry.OwnerSeatIndex == ownerSeatIndex && entry.State.IsActive)
                .Select(entry => entry.Rule.Id)
                .ToArray();
        }

        public IReadOnlyList<TalentSnapshotEntry> GetSnapshotEntries()
        {
            return _entries
                .OrderBy(entry => entry.Sequence)
                .Select(entry =>
                {
                    TalentRuntimeEvent lastPublicEvent = _events
                        .LastOrDefault(runtimeEvent => runtimeEvent.OwnerSeatIndex == entry.OwnerSeatIndex
                                                       && string.Equals(runtimeEvent.TalentId, entry.Rule.Id,
                                                           StringComparison.Ordinal)
                                                       && runtimeEvent.Visibility == TalentEventVisibility.Public);
                    return new TalentSnapshotEntry
                    {
                        OwnerSeatIndex = entry.OwnerSeatIndex,
                        TalentId = entry.Rule.Id,
                        IsActive = entry.State.IsActive,
                        IsRevealed = entry.State.IsRevealed,
                        PrivateValue = entry.Rule.GetSnapshotPrivateValue(entry.State.CreateDetachedCopy()),
                        PrivateStatusKey = entry.Rule.GetSnapshotPrivateStatusKey(entry.State.CreateDetachedCopy()),
                        LastPublicEventType = lastPublicEvent?.EventType,
                        LastPublicValue = lastPublicEvent?.Value ?? 0
                    };
                })
                .ToArray();
        }

        internal PublicChargeTarget ResolvePublicChargeTarget(
            int sourceSeatIndex,
            int targetSeatIndex,
            string targetTalentId)
        {
            if (sourceSeatIndex < 0 || sourceSeatIndex > 3
                || targetSeatIndex < 0 || targetSeatIndex > 3
                || sourceSeatIndex == targetSeatIndex)
            {
                return null;
            }

            RuntimeEntry targetEntry = FindActiveEntry(targetSeatIndex, targetTalentId);
            if (targetEntry == null
                || !targetEntry.State.IsRevealed
                || !(targetEntry.Rule is IPublicChargeTalent publicChargeTarget))
            {
                return null;
            }

            int currentCharge = publicChargeTarget.GetCurrentCharge(targetEntry.State);
            return currentCharge > 0
                ? new PublicChargeTarget(targetSeatIndex, targetEntry.Rule.Id, currentCharge)
                : null;
        }

        public IReadOnlyList<PublicChargeTarget> GetPublicChargeTargets(int requestingSeatIndex)
        {
            ValidateSeatIndex(requestingSeatIndex, nameof(requestingSeatIndex));
            return _entries
                .Where(entry => entry.OwnerSeatIndex != requestingSeatIndex
                                && entry.State.IsActive
                                && entry.State.IsRevealed
                                && entry.Rule is IPublicChargeTalent)
                .OrderBy(entry => entry.Sequence)
                .Select(entry => new
                {
                    Entry = entry,
                    Charge = ((IPublicChargeTalent)entry.Rule).GetCurrentCharge(entry.State)
                })
                .Where(target => target.Charge > 0)
                .Select(target => new PublicChargeTarget(
                    target.Entry.OwnerSeatIndex,
                    target.Entry.Rule.Id,
                    target.Charge))
                .ToArray();
        }

        public TalentFanResolution ResolvePostLegalFan(
            TalentWinContext context,
            int eligibilityFan)
        {
            return ResolvePostLegalFan(context, eligibilityFan, counterfactualOptions: null);
        }

        internal TalentFanResolution ResolvePostLegalFan(
            TalentWinContext context,
            int eligibilityFan,
            ScoringOptions counterfactualOptions)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureReadyRound(context, nameof(ResolvePostLegalFan));

            int bonusFan = 0;
            var requestedPenalties = new List<int>();
            foreach (RuntimeEntry entry in GetActiveEntriesForSeat(context.CurrentSeatIndex))
            {
                if (ReferenceEquals(
                        entry,
                        counterfactualOptions?.ExcludedTalentEntryIdentity))
                {
                    continue;
                }
                TalentWinContext bound = context.BindWin(
                    entry.OwnerSeatIndex,
                    entry.State.CreateDetachedCopy(),
                    eventSink: null);
                bonusFan += Math.Max(0, entry.Rule.GetPostLegalFanBonus(bound));
                requestedPenalties.Add(entry.Rule.GetPostLegalFanPenalty(bound));
            }

            int negativeFan = TalentFanModifierPolicy.SumPenalties(requestedPenalties);
            return new TalentFanResolution
            {
                EligibilityFan = eligibilityFan,
                PostLegalBonusFan = bonusFan,
                NegativeFan = negativeFan,
                FinalFan = Math.Max(0, eligibilityFan + bonusFan + negativeFan)
            };
        }

        public TalentFanResolution ResolveAcceptedWinFan(
            TalentAcceptedWinAttributionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureReadyRound(context, nameof(ResolveAcceptedWinFan));

            try
            {
                return ResolveAcceptedWinFanCore(context);
            }
            catch (Exception error)
            {
                Debug.LogError(
                    $"[TalentMatchRuntime] Accepted-win fan attribution failed: " +
                    $"seat={context.WinnerSeatIndex}, accepted={context.AlreadyAcceptedFinalFan}, error={error}");
                return CreateFailedAttribution(context.AlreadyAcceptedFinalFan);
            }
        }

        private TalentFanResolution ResolveAcceptedWinFanCore(
            TalentAcceptedWinAttributionContext context)
        {

            RuntimeEntry[] entries = GetAttributionEntries(context.WinnerSeatIndex).ToArray();
            var scoringContext = new TalentScoringContext(_session, context.WinnerSeatIndex);
            FanEvaluation baseEvaluation = EvaluateAttributionCandidate(
                context, BuildScoringOptions(scoringContext, Array.Empty<RuntimeEntry>()));
            int baseFan = baseEvaluation.HasWinningShape ? baseEvaluation.Fan : 0;
            int previousPositiveFan = baseFan;
            FanEvaluation previousEvaluation = baseEvaluation;
            var contributions = new List<TalentFanContribution>();

            for (int index = 0; index < entries.Length; index++)
            {
                RuntimeEntry[] included = entries.Take(index + 1).ToArray();
                ScoringOptions options = BuildScoringOptions(scoringContext, included);
                FanEvaluation evaluation = EvaluateAttributionCandidate(context, options);
                int eligibilityFan = evaluation.HasWinningShape ? evaluation.Fan : 0;
                int postLegalBonus = SumPostLegalBonuses(
                    context.WinnerSeatIndex,
                    included,
                    context.Facts);
                int nextPositiveFan = Math.Max(0, eligibilityFan + postLegalBonus);
                int delta = nextPositiveFan - previousPositiveFan;
                if (delta != 0)
                {
                    contributions.Add(new TalentFanContribution
                    {
                        TalentId = entries[index].Rule.Id,
                        FanDelta = delta,
                        Category = eligibilityFan != (previousEvaluation.HasWinningShape
                            ? previousEvaluation.Fan
                            : 0)
                            ? TalentFanContributionCategory.Eligibility
                            : TalentFanContributionCategory.PostLegal,
                        Sequence = entries[index].Sequence
                    });
                }

                previousPositiveFan = nextPositiveFan;
                previousEvaluation = evaluation;
            }

            int previousFinalFan = previousPositiveFan;
            var requestedPenalties = new List<int>();
            foreach (RuntimeEntry entry in entries)
            {
                int requestedPenalty = GetPostLegalPenalty(
                    context.WinnerSeatIndex,
                    entry,
                    context.Facts);
                if (requestedPenalty >= 0) continue;

                requestedPenalties.Add(requestedPenalty);
                int effectiveNegative = TalentFanModifierPolicy.SumPenalties(requestedPenalties);
                int nextFinalFan = Math.Max(0, previousPositiveFan + effectiveNegative);
                int delta = nextFinalFan - previousFinalFan;
                if (delta != 0)
                {
                    contributions.Add(new TalentFanContribution
                    {
                        TalentId = entry.Rule.Id,
                        FanDelta = delta,
                        Category = TalentFanContributionCategory.Negative,
                        Sequence = entry.Sequence
                    });
                }
                previousFinalFan = nextFinalFan;
            }

            ScoringOptions authoritativeOptions = BuildScoringOptions(scoringContext, entries);
            FanEvaluation authoritativeEvaluation = EvaluateAttributionCandidate(
                context, authoritativeOptions);
            int eligibility = authoritativeEvaluation.HasWinningShape
                ? authoritativeEvaluation.Fan
                : 0;
            int bonus = SumPostLegalBonuses(context.WinnerSeatIndex, entries, context.Facts);
            int negative = SumPostLegalPenalties(context.WinnerSeatIndex, entries, context.Facts);
            int authoritativeFinal = Math.Max(0, eligibility + bonus + negative);
            int attributedFinal = baseFan + contributions.Sum(row => row.FanDelta);
            if (attributedFinal != authoritativeFinal
                || authoritativeFinal != context.AlreadyAcceptedFinalFan)
            {
                Debug.LogError(
                    $"[TalentMatchRuntime] Accepted-win fan attribution mismatch: " +
                    $"seat={context.WinnerSeatIndex}, base={baseFan}, " +
                    $"attributed={attributedFinal}, recomputed={authoritativeFinal}, " +
                    $"accepted={context.AlreadyAcceptedFinalFan}.");
                return CreateFailedAttribution(context.AlreadyAcceptedFinalFan);
            }

            return new TalentFanResolution
            {
                IsAttributionComplete = true,
                BaseFan = baseFan,
                EligibilityFan = eligibility,
                PostLegalBonusFan = bonus,
                NegativeFan = negative,
                FinalFan = context.AlreadyAcceptedFinalFan,
                Contributions = contributions.ToArray()
            };
        }

        private static TalentFanResolution CreateFailedAttribution(int acceptedFinalFan)
        {
            return new TalentFanResolution
            {
                BaseFan = 0,
                EligibilityFan = 0,
                PostLegalBonusFan = 0,
                NegativeFan = 0,
                FinalFan = acceptedFinalFan,
                Contributions = Array.Empty<TalentFanContribution>()
            };
        }

        private static FanEvaluation EvaluateAttributionCandidate(
            TalentAcceptedWinAttributionContext context,
            ScoringOptions options)
        {
            FanEvaluation evaluation = context.EvaluateOptions(options);
            return evaluation ?? new FanEvaluation
            {
                HasWinningShape = false,
                Fan = 0,
                FanDetails = null
            };
        }

        private int SumPostLegalBonuses(
            int winnerSeatIndex,
            IEnumerable<RuntimeEntry> entries,
            TalentWinFacts facts)
        {
            int bonus = 0;
            var winContext = new TalentWinContext(_session, winnerSeatIndex, facts);
            foreach (RuntimeEntry entry in entries)
            {
                TalentWinContext bound = winContext.BindWin(
                    entry.OwnerSeatIndex,
                    entry.State.CreateDetachedCopy(),
                    eventSink: null);
                bonus += Math.Max(0, entry.Rule.GetPostLegalFanBonus(bound));
            }
            return bonus;
        }

        private int SumPostLegalPenalties(
            int winnerSeatIndex,
            IEnumerable<RuntimeEntry> entries,
            TalentWinFacts facts)
        {
            var winContext = new TalentWinContext(_session, winnerSeatIndex, facts);
            var requested = new List<int>();
            foreach (RuntimeEntry entry in entries)
            {
                TalentWinContext bound = winContext.BindWin(
                    entry.OwnerSeatIndex,
                    entry.State.CreateDetachedCopy(),
                    eventSink: null);
                requested.Add(entry.Rule.GetPostLegalFanPenalty(bound));
            }
            return TalentFanModifierPolicy.SumPenalties(requested);
        }

        private int GetPostLegalPenalty(
            int winnerSeatIndex,
            RuntimeEntry entry,
            TalentWinFacts facts)
        {
            var winContext = new TalentWinContext(_session, winnerSeatIndex, facts);
            TalentWinContext bound = winContext.BindWin(
                entry.OwnerSeatIndex,
                entry.State.CreateDetachedCopy(),
                eventSink: null);
            return entry.Rule.GetPostLegalFanPenalty(bound);
        }

        public void ConfirmAcceptedWin(TalentWinContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EnsureReadyRound(context, nameof(ConfirmAcceptedWin));
            foreach (RuntimeEntry entry in GetActiveEntriesForSeat(context.CurrentSeatIndex))
            {
                entry.Rule.OnAcceptedWin(context.BindWin(
                    entry.OwnerSeatIndex,
                    entry.State,
                    runtimeEvent => EmitEvent(entry, runtimeEvent)));
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
            options.ExcludedTalentEntryIdentity = excludedEntry;
            foreach (RuntimeEntry entry in GetActiveEntriesForSeat(context.CurrentSeatIndex))
            {
                if (ReferenceEquals(entry, excludedEntry)) continue;
                entry.Rule.ConfigureScoring(context.BindScoring(
                    entry.OwnerSeatIndex,
                    entry.State.CreateDetachedCopy(),
                    eventSink: null), options);
            }
            return options;
        }

        private ScoringOptions BuildScoringOptions(
            TalentScoringContext context,
            IReadOnlyCollection<RuntimeEntry> includedEntries)
        {
            var included = new HashSet<RuntimeEntry>(includedEntries ?? Array.Empty<RuntimeEntry>());
            var options = new ScoringOptions();
            foreach (RuntimeEntry entry in GetAttributionEntries(context.CurrentSeatIndex))
            {
                if (!included.Contains(entry)) continue;
                entry.Rule.ConfigureScoring(context.BindScoring(
                    entry.OwnerSeatIndex,
                    entry.State.CreateDetachedCopy(),
                    eventSink: null), options);
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
            foreach (RuntimeEntry entry in GetActiveEntriesForSeat(context.CurrentSeatIndex))
            {
                if (entry.State.IsRevealed
                    || entry.Metadata.RevealPolicy == TalentRevealPolicy.OwnerOnly)
                {
                    continue;
                }

                ScoringOptions withoutEntry = BuildScoringOptions(scoringContext, entry);

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

        public void RecordPrivateTileReveal(
            int viewerSeatIndex,
            int targetSeatIndex,
            string talentId,
            IEnumerable<TileData> revealedTiles,
            int roundNumber)
        {
            ValidateSeatIndex(viewerSeatIndex, nameof(viewerSeatIndex));
            ValidateSeatIndex(targetSeatIndex, nameof(targetSeatIndex));
            if (string.IsNullOrWhiteSpace(talentId))
                throw new ArgumentException("A talent id is required.", nameof(talentId));

            _privateTileReveals[viewerSeatIndex] = new TalentPrivateTileReveal(
                talentId,
                viewerSeatIndex,
                targetSeatIndex,
                roundNumber,
                revealedTiles);
            _privateTileRevealVersions[viewerSeatIndex] = checked(
                GetPrivateTileRevealVersion(viewerSeatIndex) + 1);
        }

        public TalentPrivateTileReveal GetPrivateTileReveal(int viewerSeatIndex)
        {
            ValidateSeatIndex(viewerSeatIndex, nameof(viewerSeatIndex));
            return _privateTileReveals.TryGetValue(viewerSeatIndex, out TalentPrivateTileReveal reveal)
                ? reveal.CreateDetachedCopy()
                : null;
        }

        public long GetPrivateTileRevealVersion(int viewerSeatIndex)
        {
            ValidateSeatIndex(viewerSeatIndex, nameof(viewerSeatIndex));
            return _privateTileRevealVersions.TryGetValue(viewerSeatIndex, out long version)
                ? version
                : 0;
        }

        public void EndRound(
            TalentRoundOutcome outcome,
            GameSession session,
            int[] drawsPerSeat = null)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            if (session == null) throw new ArgumentNullException(nameof(session));
            EnsureSession(session);
            if (outcome.IsAborted)
            {
                if (_phase != RuntimePhase.RoundStarted
                    && _phase != RuntimePhase.WallBuilt
                    && _phase != RuntimePhase.InitialHandsCompleted
                    && _phase != RuntimePhase.RoundReady)
                {
                    throw new InvalidOperationException(
                        $"{nameof(EndRound)} is invalid during talent runtime phase {_phase}.");
                }
            }
            else
            {
                EnsurePhase(RuntimePhase.RoundReady, nameof(EndRound));
            }
            ValidateOptionalSeat(outcome.WinnerSeatIndex, nameof(outcome.WinnerSeatIndex));
            ValidateOptionalSeat(outcome.DiscarderSeatIndex, nameof(outcome.DiscarderSeatIndex));

            TalentRoundContext context = new TalentRoundContext(
                session,
                new TalentRoundActionLedgerSnapshot(_roundActions));
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
            _privateTileReveals.Clear();
            _privateTileRevealVersions.Clear();
            _phase = RuntimePhase.BetweenRounds;
            TalentTelemetryRecord telemetryRecord = CreateTelemetryRecord("round_end");
            telemetryRecord.completedRound = session.TotalRoundsPlayed + 1;
            telemetryRecord.drawsPerSeat = CopyDraws(drawsPerSeat);
            telemetryRecord.finalFan = outcome.FinalFan;
            telemetryRecord.winnerSeatIndex = outcome.WinnerSeatIndex ?? -1;
            RecordTelemetry(telemetryRecord);
        }

        public void RecordAcceptedWinTelemetry(
            int winnerSeatIndex,
            TalentFanResolution resolution,
            int[] drawsPerSeat)
        {
            ValidateSeatIndex(winnerSeatIndex, nameof(winnerSeatIndex));
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));

            TalentTelemetryRecord record = CreateTelemetryRecord("accepted_win");
            record.completedRound = _session.TotalRoundsPlayed + 1;
            record.drawsPerSeat = CopyDraws(drawsPerSeat);
            record.baseFan = resolution.BaseFan;
            record.eligibilityFan = resolution.EligibilityFan;
            record.postLegalBonusFan = resolution.PostLegalBonusFan;
            record.negativeFan = resolution.NegativeFan;
            record.finalFan = resolution.FinalFan;
            record.winnerSeatIndex = winnerSeatIndex;
            RecordTelemetry(record);
        }

        public void RecordSideboardLockTelemetry(
            int seatIndex,
            bool accepted,
            bool original,
            bool timeout)
        {
            ValidateSeatIndex(seatIndex, nameof(seatIndex));
            TalentTelemetryRecord record = CreateTelemetryRecord("sideboard_lock");
            record.seatIndex = seatIndex;
            record.sideboardAccepted = accepted;
            record.sideboardOriginal = original;
            record.sideboardTimeout = timeout;
            RecordTelemetry(record);
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

        private bool IsFirstMainDecision(
            int ownerSeatIndex,
            TalentActivationWindow requiredWindow,
            long decisionId)
        {
            return requiredWindow == TalentActivationWindow.MainTurn
                   && _firstMainDecisionIds.TryGetValue(ownerSeatIndex, out long firstDecisionId)
                   && firstDecisionId == decisionId;
        }

        private RuntimeEntry FindActiveEntry(int ownerSeatIndex, string talentId)
        {
            if (string.IsNullOrWhiteSpace(talentId)) return null;
            return _entries.FirstOrDefault(entry => entry.OwnerSeatIndex == ownerSeatIndex
                                                    && entry.State.IsActive
                                                    && string.Equals(entry.Rule.Id, talentId,
                                                        StringComparison.Ordinal));
        }

        private RuntimeEntry FindCarriedEntry(int ownerSeatIndex, string talentId)
        {
            if (string.IsNullOrWhiteSpace(talentId)) return null;
            return _entries.FirstOrDefault(entry => entry.OwnerSeatIndex == ownerSeatIndex
                                                    && string.Equals(entry.Rule.Id, talentId,
                                                        StringComparison.Ordinal));
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

        private IEnumerable<RuntimeEntry> GetAttributionEntries(int winnerSeatIndex)
        {
            ValidateSeatIndex(winnerSeatIndex);
            return _entries
                .Where(entry => entry.State.IsActive
                                && (entry.Rule.Scope == TalentScope.Global
                                    || entry.OwnerSeatIndex == winnerSeatIndex))
                .OrderBy(entry => entry.Sequence);
        }

        private IEnumerable<RuntimeEntry> GetActiveTargetDefenses(int targetSeatIndex)
        {
            return _entries
                .Where(entry => entry.State.IsActive && entry.OwnerSeatIndex == targetSeatIndex)
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
                && (string.Equals(runtimeEvent.EventType, "active_talent_applied", StringComparison.Ordinal)
                    || string.Equals(runtimeEvent.EventType, "blocked_negative_effect", StringComparison.Ordinal)))
            {
                TalentTelemetryRecord telemetryRecord = CreateTelemetryRecord(runtimeEvent.EventType);
                telemetryRecord.seatIndex = runtimeEvent.OwnerSeatIndex;
                telemetryRecord.talentId = runtimeEvent.TalentId;
                telemetryRecord.publicValue = runtimeEvent.Value;
                telemetryRecord.controlApplied = string.Equals(
                    runtimeEvent.EventType,
                    "active_talent_applied",
                    StringComparison.Ordinal);
                telemetryRecord.controlBlocked = string.Equals(
                    runtimeEvent.EventType,
                    "blocked_negative_effect",
                    StringComparison.Ordinal);
                RecordTelemetry(telemetryRecord);
            }

            if (runtimeEvent.Visibility == TalentEventVisibility.Public
                && entry.Metadata.RevealPolicy != TalentRevealPolicy.OwnerOnly)
            {
                entry.State.IsRevealed = true;
            }
        }

        private TalentTelemetryRecord CreateTelemetryRecord(string eventType)
        {
            return new TalentTelemetryRecord
            {
                anonymousSessionId = _anonymousSessionId,
                preset = TalentTelemetry.FormatPreset(_telemetryPreset),
                mode = TalentTelemetry.FormatMode(_session?.Mode ?? GameMode.Single),
                completedRound = _session?.TotalRoundsPlayed ?? 0,
                eventType = eventType
            };
        }

        private void RecordTelemetry(TalentTelemetryRecord record) =>
            TalentTelemetry.RecordSafely(_telemetrySink, record);

        private static int[] CopyDraws(int[] drawsPerSeat)
        {
            var result = new int[4];
            if (drawsPerSeat != null)
                Array.Copy(drawsPerSeat, result, Math.Min(drawsPerSeat.Length, result.Length));
            return result;
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
            InitialHandsCompleted,
            RoundReady
        }
    }
}
