using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents
{
    public class TalentContext
    {
        private readonly IReadOnlyDictionary<int, TalentDeckSnapshot> _deckSnapshots;
        private Action<TalentRuntimeEvent> _eventSink;
        private int? _ownerSeatIndex;
        private TalentRuntimeState _state;

        public int? CurrentSeatIndex { get; }
        public int OwnerSeatIndex => _ownerSeatIndex
            ?? throw new InvalidOperationException("Talent context has not been bound to a runtime entry.");
        public TalentGameStateSnapshot GameState { get; }
        public TalentSessionSnapshot Session { get; }
        public TalentDeckSnapshot OwnerDeckConfig { get; private set; }
        public TalentRuntimeState State => _state
            ?? throw new InvalidOperationException("Talent context has not been bound to runtime state.");

        public bool IsOwnersTurn => CurrentSeatIndex.HasValue
                                    && CurrentSeatIndex.Value == OwnerSeatIndex;
        internal IReadOnlyDictionary<int, TalentDeckSnapshot> DeckSnapshots => _deckSnapshots;

        internal TalentContext(
            GameSession session,
            int? currentSeatIndex = null,
            ServerGameState gameState = null,
            IReadOnlyDictionary<int, DeckConfig> deckConfigs = null)
            : this(
                TalentSessionSnapshot.Create(session),
                currentSeatIndex,
                TalentGameStateSnapshot.Create(gameState),
                CreateDeckSnapshots(deckConfigs))
        {
        }

        internal TalentContext(
            TalentSessionSnapshot session,
            int? currentSeatIndex,
            TalentGameStateSnapshot gameState,
            IReadOnlyDictionary<int, TalentDeckSnapshot> deckSnapshots)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            if (currentSeatIndex.HasValue) ValidateSeatIndex(currentSeatIndex.Value);
            CurrentSeatIndex = currentSeatIndex;
            GameState = gameState;
            _deckSnapshots = deckSnapshots ?? EmptyDeckSnapshots;
        }

        public void Emit(TalentRuntimeEvent runtimeEvent)
        {
            if (runtimeEvent == null) throw new ArgumentNullException(nameof(runtimeEvent));
            _eventSink?.Invoke(runtimeEvent);
        }

        public void EmitPublic(string eventType, int value)
        {
            Emit(new TalentRuntimeEvent
            {
                EventType = eventType,
                Visibility = TalentEventVisibility.Public,
                Value = value
            });
        }

        public void SetPublicCounter(
            string key,
            int value,
            TalentStateScope scope)
        {
            State.SetCounter(key, value, scope);
            EmitPublic("public_counter_changed", value);
        }

        public void RevealWithPublicCounter(
            string key,
            int value,
            TalentStateScope scope)
        {
            if (!State.IsRevealed)
            {
                State.IsRevealed = true;
                EmitPublic("talent_revealed", 0);
            }
            SetPublicCounter(key, value, scope);
        }

        internal static int ValidateSeatIndex(int seatIndex)
        {
            if (seatIndex < 0 || seatIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(seatIndex), seatIndex, "Seat index must be 0..3.");
            return seatIndex;
        }

        internal static IReadOnlyDictionary<int, TalentDeckSnapshot> CreateDeckSnapshots(
            IReadOnlyDictionary<int, DeckConfig> deckConfigs)
        {
            if (deckConfigs == null) return EmptyDeckSnapshots;

            Dictionary<int, TalentDeckSnapshot> snapshots = new Dictionary<int, TalentDeckSnapshot>();
            foreach (KeyValuePair<int, DeckConfig> pair in deckConfigs)
            {
                ValidateSeatIndex(pair.Key);
                if (pair.Value != null)
                    snapshots[pair.Key] = TalentDeckSnapshot.Create(pair.Value);
            }
            return new ReadOnlyDictionary<int, TalentDeckSnapshot>(snapshots);
        }

        internal void ConfigureEntry(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            _ownerSeatIndex = ValidateSeatIndex(ownerSeatIndex);
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _eventSink = eventSink;
            OwnerDeckConfig = _deckSnapshots.TryGetValue(ownerSeatIndex, out TalentDeckSnapshot deck)
                ? deck
                : null;
        }

        internal TalentContext Bind(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            TalentContext context = new TalentContext(
                Session,
                CurrentSeatIndex,
                GameState,
                _deckSnapshots);
            context.ConfigureEntry(ownerSeatIndex, state, eventSink);
            return context;
        }

        internal static TalentContext CreateLegacy(
            GameSession session,
            int currentSeatIndex,
            int ownerSeatIndex,
            ServerGameState gameState,
            IReadOnlyDictionary<int, DeckConfig> deckConfigs)
        {
            TalentContext context = new TalentContext(
                session,
                ValidateSeatIndex(currentSeatIndex),
                gameState,
                deckConfigs);
            context.ConfigureEntry(ownerSeatIndex, new TalentRuntimeState { IsActive = true }, null);
            return context;
        }

        private static readonly IReadOnlyDictionary<int, TalentDeckSnapshot> EmptyDeckSnapshots =
            new ReadOnlyDictionary<int, TalentDeckSnapshot>(
                new Dictionary<int, TalentDeckSnapshot>());
    }

    public sealed class TalentMatchContext : TalentContext
    {
        internal TalentMatchContext(
            GameSession session,
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
            : base(session)
        {
            ConfigureEntry(ownerSeatIndex, state, eventSink);
        }
    }

    public sealed class TalentRoundContext : TalentContext
    {
        private Action<int, string> _scoreDeltaSink;
        public TalentRoundActionLedgerSnapshot RoundActions { get; }

        public TalentRoundContext(GameSession session) : base(session)
        {
            RoundActions = TalentRoundActionLedgerSnapshot.Empty;
        }

        internal TalentRoundContext(
            GameSession session,
            TalentRoundActionLedgerSnapshot roundActions) : base(session)
        {
            RoundActions = roundActions ?? TalentRoundActionLedgerSnapshot.Empty;
        }

        public void ApplyScoreDelta(int delta, string reason)
        {
            if (_scoreDeltaSink == null)
                throw new InvalidOperationException("Score deltas are only available during round-end resolution.");
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A score-delta reason is required.", nameof(reason));
            _scoreDeltaSink(delta, reason);
        }

        internal TalentRoundContext(
            TalentSessionSnapshot session,
            TalentGameStateSnapshot gameState,
            IReadOnlyDictionary<int, TalentDeckSnapshot> deckSnapshots,
            TalentRoundActionLedgerSnapshot roundActions = null)
            : base(session, null, gameState, deckSnapshots)
        {
            RoundActions = roundActions ?? TalentRoundActionLedgerSnapshot.Empty;
        }

        internal TalentRoundContext BindRound(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink,
            Action<int, string> scoreDeltaSink = null)
        {
            TalentRoundContext context = new TalentRoundContext(
                Session,
                GameState,
                DeckSnapshots,
                RoundActions);
            context.ConfigureEntry(ownerSeatIndex, state, eventSink);
            context._scoreDeltaSink = scoreDeltaSink;
            return context;
        }
    }

    public sealed class TalentActionCommittedContext : TalentContext
    {
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;
        public TalentActionCommittedFacts Facts { get; }
        public TalentRoundActionLedgerSnapshot RoundActions { get; }

        internal TalentActionCommittedContext(
            GameSession session,
            TalentActionCommittedFacts facts,
            TalentRoundActionLedgerSnapshot roundActions)
            : base(session, ValidateSeatIndex(facts?.ActorSeatIndex
                                              ?? throw new ArgumentNullException(nameof(facts))))
        {
            Facts = facts;
            RoundActions = roundActions ?? throw new ArgumentNullException(nameof(roundActions));
        }

        private TalentActionCommittedContext(
            TalentSessionSnapshot session,
            TalentActionCommittedFacts facts,
            TalentRoundActionLedgerSnapshot roundActions)
            : base(session, facts.ActorSeatIndex, null, null)
        {
            Facts = facts;
            RoundActions = roundActions;
        }

        internal TalentActionCommittedContext BindCommittedAction(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            var context = new TalentActionCommittedContext(Session, Facts, RoundActions);
            context.ConfigureEntry(ownerSeatIndex, state, eventSink);
            return context;
        }
    }

    public sealed class TalentWallContext : TalentContext
    {
        public List<TileData> WallTiles { get; }

        public TalentWallContext(
            GameSession session,
            List<TileData> wallTiles,
            ServerGameState gameState = null,
            IReadOnlyDictionary<int, DeckConfig> deckConfigs = null)
            : base(session, null, gameState, deckConfigs)
        {
            WallTiles = wallTiles ?? throw new ArgumentNullException(nameof(wallTiles));
        }

        private TalentWallContext(
            TalentSessionSnapshot session,
            List<TileData> wallTiles,
            TalentGameStateSnapshot gameState,
            IReadOnlyDictionary<int, TalentDeckSnapshot> deckSnapshots)
            : base(session, null, gameState, deckSnapshots)
        {
            WallTiles = wallTiles;
        }

        internal TalentWallContext BindWall(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            TalentWallContext context = new TalentWallContext(
                Session,
                WallTiles,
                GameState,
                DeckSnapshots);
            context.ConfigureEntry(ownerSeatIndex, state, eventSink);
            return context;
        }
    }

    public sealed class TalentPostShuffleContext : TalentContext
    {
        public IReadOnlyList<TileData> ShuffledWallTiles { get; }

        public TalentPostShuffleContext(GameSession session, IReadOnlyList<TileData> shuffledWallTiles)
            : base(session)
        {
            ShuffledWallTiles = shuffledWallTiles ?? throw new ArgumentNullException(nameof(shuffledWallTiles));
        }
    }

    /// <summary>
    /// Authority-only source used to fan out owner-private initial-hand facts.
    /// Concrete rules never receive this aggregate context.
    /// </summary>
    public sealed class TalentInitialHandsContext : TalentContext
    {
        private readonly GameSession _sourceSession;
        private readonly ServerGameState _authority;
        private readonly Dictionary<int, List<TileData>> _stagedHandsBySeat;
        private readonly List<Action> _stagedEvents = new List<Action>();
        private string _invalidReason;

        internal TalentInitialHandsContext(
            GameSession session,
            ServerGameState gameState,
            IReadOnlyDictionary<int, DeckConfig> deckConfigs = null)
            : base(session, null, gameState, deckConfigs)
        {
            if (gameState == null) throw new ArgumentNullException(nameof(gameState));
            _sourceSession = session;
            _authority = gameState;
            _stagedHandsBySeat = new Dictionary<int, List<TileData>>();
            for (int seatIndex = 0; seatIndex < gameState.PlayerCount; seatIndex++)
                _stagedHandsBySeat[seatIndex] = gameState.GetHand(seatIndex);
        }

        internal TalentInitialHandContext BindInitialHand(
            int ownerSeatIndex,
            string talentId,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            if (!_stagedHandsBySeat.TryGetValue(ownerSeatIndex, out List<TileData> stagedHand))
                throw new InvalidOperationException($"Initial hand for seat {ownerSeatIndex} is unavailable.");
            TalentInitialHandFacts facts = new TalentInitialHandFacts(
                _sourceSession,
                ownerSeatIndex,
                stagedHand);
            var context = new TalentInitialHandContext(
                Session,
                GameState,
                DeckSnapshots,
                facts,
                (physicalTileId, suit, value) => TryTransformTile(
                    ownerSeatIndex,
                    talentId,
                    physicalTileId,
                    suit,
                    value));
            context.ConfigureEntry(ownerSeatIndex, state, runtimeEvent =>
            {
                if (runtimeEvent == null || eventSink == null) return;
                TalentRuntimeEvent copied = runtimeEvent.Copy();
                _stagedEvents.Add(() => eventSink(copied));
            });
            return context;
        }

        internal void Commit()
        {
            if (!string.IsNullOrWhiteSpace(_invalidReason))
                throw new InvalidOperationException(_invalidReason);
            if (!_authority.TryReplaceInitialHands(_stagedHandsBySeat, out string error))
                throw new InvalidOperationException(error ?? "Initial-hand transaction was rejected.");
            foreach (Action stagedEvent in _stagedEvents) stagedEvent();
            _stagedEvents.Clear();
        }

        private bool TryTransformTile(
            int ownerSeatIndex,
            string talentId,
            string physicalTileId,
            Suit suit,
            int value)
        {
            if (!IsValidTileShape(suit, value))
                return RejectMutation($"Invalid initial-hand target shape: {suit} {value}.");
            if (string.IsNullOrWhiteSpace(physicalTileId)
                || string.IsNullOrWhiteSpace(talentId)
                || !_stagedHandsBySeat.TryGetValue(ownerSeatIndex, out List<TileData> hand))
            {
                return RejectMutation("Initial-hand mutation requires an owner tile id and source talent.");
            }

            TileData[] matches = hand.Where(tile => tile != null
                                                    && string.Equals(
                                                        tile.ID,
                                                        physicalTileId,
                                                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                return RejectMutation($"Initial-hand physical tile '{physicalTileId}' is unavailable or ambiguous.");

            TileData tile = matches[0];
            tile.TileSuit = suit;
            tile.Value = value;
            tile.IsModified = true;
            tile.SpecialEffectID = talentId;
            return true;
        }

        private bool RejectMutation(string reason)
        {
            _invalidReason ??= reason;
            return false;
        }

        private static bool IsValidTileShape(Suit suit, int value) => suit switch
        {
            Suit.Man => value >= 1 && value <= 9,
            Suit.Pin => value >= 1 && value <= 9,
            Suit.Sou => value >= 1 && value <= 9,
            Suit.Wind => value >= 1 && value <= 4,
            Suit.Dragon => value >= 1 && value <= 3,
            _ => false
        };
    }

    public sealed class TalentInitialHandContext : TalentContext
    {
        public TalentInitialHandFacts Facts { get; }
        private readonly Func<string, Suit, int, bool> _transformTile;

        internal TalentInitialHandContext(
            TalentSessionSnapshot session,
            TalentGameStateSnapshot gameState,
            IReadOnlyDictionary<int, TalentDeckSnapshot> deckSnapshots,
            TalentInitialHandFacts facts,
            Func<string, Suit, int, bool> transformTile)
            : base(session, null, gameState, deckSnapshots)
        {
            Facts = facts ?? throw new ArgumentNullException(nameof(facts));
            _transformTile = transformTile ?? throw new ArgumentNullException(nameof(transformTile));
        }

        public bool TryTransformTile(string physicalTileId, Suit suit, int value) =>
            _transformTile(physicalTileId, suit, value);
    }

    public sealed class TalentDrawContext : TalentContext
    {
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;

        public TalentDrawContext(
            GameSession session,
            int currentSeatIndex,
            ServerGameState gameState = null,
            IReadOnlyDictionary<int, DeckConfig> deckConfigs = null)
            : base(session, ValidateSeatIndex(currentSeatIndex), gameState, deckConfigs)
        {
        }
    }

    public sealed class TalentDiscardContext : TalentContext
    {
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;

        public TalentDiscardContext(GameSession session, int currentSeatIndex)
            : base(session, ValidateSeatIndex(currentSeatIndex))
        {
        }
    }

    public sealed class TalentActionContext : TalentContext
    {
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;
        public ClientActionType ActionType { get; }
        public TileData TargetTile { get; }
        public bool IsAllowed { get; internal set; } = true;

        public TalentActionContext(
            GameSession session,
            int currentSeatIndex,
            ClientActionType actionType,
            TileData targetTile)
            : base(session, ValidateSeatIndex(currentSeatIndex))
        {
            ActionType = actionType;
            TargetTile = targetTile;
        }
    }

    public sealed class TalentActivationContext : TalentContext
    {
        private TalentMatchRuntime _runtime;
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;
        public TalentActivationWindow RequiredWindow { get; }
        public long DecisionId { get; }
        public bool IsFirstMainDecisionOfRound { get; private set; }

        public TalentActivationContext(
            GameSession session,
            int currentSeatIndex,
            TalentActivationWindow requiredWindow,
            long decisionId = 0)
            : base(session, ValidateSeatIndex(currentSeatIndex))
        {
            RequiredWindow = requiredWindow;
            DecisionId = decisionId;
        }

        private TalentActivationContext(
            TalentSessionSnapshot session,
            int currentSeatIndex,
            TalentActivationWindow requiredWindow,
            long decisionId)
            : base(session, currentSeatIndex, null, null)
        {
            RequiredWindow = requiredWindow;
            DecisionId = decisionId;
        }

        internal TalentActivationContext WithState(
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink,
            bool isFirstMainDecisionOfRound,
            TalentMatchRuntime runtime)
        {
            TalentActivationContext context = new TalentActivationContext(
                Session,
                CurrentSeatIndex,
                RequiredWindow,
                DecisionId)
            {
                IsFirstMainDecisionOfRound = isFirstMainDecisionOfRound
            };
            context.ConfigureEntry(CurrentSeatIndex, state, eventSink);
            context._runtime = runtime;
            return context;
        }

        public PublicChargeTarget ResolvePublicChargeTarget(TalentActionRequest request)
        {
            if (request == null) return null;
            return _runtime?.ResolvePublicChargeTarget(
                CurrentSeatIndex,
                request.TargetSeatIndex,
                request.TargetTalentId);
        }

        public TalentNegativeEffectResult ApplyNegativeEffect(TalentNegativeEffect effect)
        {
            if (_runtime == null)
                throw new InvalidOperationException("Talent activation context is not bound to a runtime.");
            return _runtime.ApplyNegativeEffect(effect);
        }
    }

    public sealed class TalentActionQueryContext : TalentContext
    {
        private TalentMatchRuntime _runtime;
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;
        public TalentActivationWindow RequiredWindow { get; }
        public long DecisionId { get; }
        public bool IsFirstMainDecisionOfRound { get; private set; }

        public TalentActionQueryContext(
            GameSession session,
            int currentSeatIndex,
            TalentActivationWindow requiredWindow,
            long decisionId)
            : base(session, ValidateSeatIndex(currentSeatIndex))
        {
            RequiredWindow = requiredWindow;
            DecisionId = decisionId;
        }

        private TalentActionQueryContext(
            TalentSessionSnapshot session,
            int currentSeatIndex,
            TalentActivationWindow requiredWindow,
            long decisionId)
            : base(session, currentSeatIndex, null, null)
        {
            RequiredWindow = requiredWindow;
            DecisionId = decisionId;
        }

        internal TalentActionQueryContext WithState(
            TalentRuntimeState state,
            bool isFirstMainDecisionOfRound,
            TalentMatchRuntime runtime)
        {
            var context = new TalentActionQueryContext(
                Session,
                CurrentSeatIndex,
                RequiredWindow,
                DecisionId)
            {
                IsFirstMainDecisionOfRound = isFirstMainDecisionOfRound
            };
            context.ConfigureEntry(CurrentSeatIndex, state, eventSink: null);
            context._runtime = runtime;
            return context;
        }

        public IReadOnlyList<PublicChargeTarget> GetPublicChargeTargets()
        {
            return _runtime?.GetPublicChargeTargets(CurrentSeatIndex)
                   ?? Array.Empty<PublicChargeTarget>();
        }
    }

    public sealed class TalentWinContext : TalentContext
    {
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;
        public TalentWinFacts Facts { get; }

        public TalentWinContext(
            GameSession session,
            int currentSeatIndex,
            TalentWinFacts facts)
            : base(session, ValidateSeatIndex(currentSeatIndex))
        {
            Facts = facts ?? throw new ArgumentNullException(nameof(facts));
            if (facts.WinnerSeatIndex != currentSeatIndex)
                throw new ArgumentException("Win facts must belong to the current winner seat.", nameof(facts));
        }

        private TalentWinContext(
            TalentSessionSnapshot session,
            int currentSeatIndex,
            TalentWinFacts facts)
            : base(session, currentSeatIndex, null, null)
        {
            Facts = facts;
        }

        internal TalentWinContext BindWin(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            var context = new TalentWinContext(Session, CurrentSeatIndex, Facts);
            context.ConfigureEntry(ownerSeatIndex, state, eventSink);
            return context;
        }
    }

    public sealed class TalentScoringContext : TalentContext
    {
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;

        public TalentScoringContext(GameSession session, int currentSeatIndex)
            : base(session, ValidateSeatIndex(currentSeatIndex))
        {
        }

        private TalentScoringContext(
            TalentSessionSnapshot session,
            int currentSeatIndex,
            TalentGameStateSnapshot gameState,
            IReadOnlyDictionary<int, TalentDeckSnapshot> deckSnapshots)
            : base(session, currentSeatIndex, gameState, deckSnapshots)
        {
        }

        internal TalentScoringContext BindScoring(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            TalentScoringContext context = new TalentScoringContext(
                Session,
                CurrentSeatIndex,
                GameState,
                DeckSnapshots);
            context.ConfigureEntry(ownerSeatIndex, state, eventSink);
            return context;
        }
    }

    public sealed class TalentPublicTileContext : TalentContext
    {
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;

        public TalentPublicTileContext(GameSession session, int currentSeatIndex)
            : base(session, ValidateSeatIndex(currentSeatIndex))
        {
        }
    }

    public sealed class TalentAcceptedWinContext : TalentContext
    {
        public new int CurrentSeatIndex => base.CurrentSeatIndex.Value;
        public TalentWinFacts Facts { get; }
        public TalentWinEvaluation AcceptedResult { get; }
        public Func<ScoringOptions, TalentWinEvaluation> EvaluateWithOptions { get; }

        public TalentAcceptedWinContext(
            GameSession session,
            int currentSeatIndex,
            TalentWinFacts facts,
            TalentWinEvaluation acceptedResult,
            Func<ScoringOptions, TalentWinEvaluation> evaluateWithOptions)
            : base(session, ValidateSeatIndex(currentSeatIndex))
        {
            Facts = facts ?? throw new ArgumentNullException(nameof(facts));
            if (facts.WinnerSeatIndex != currentSeatIndex)
                throw new ArgumentException("Win facts must belong to the current winner seat.", nameof(facts));
            AcceptedResult = acceptedResult ?? throw new ArgumentNullException(nameof(acceptedResult));
            EvaluateWithOptions = evaluateWithOptions ?? throw new ArgumentNullException(nameof(evaluateWithOptions));
        }
    }

    public sealed class TalentAcceptedWinAttributionContext : TalentContext
    {
        public int WinnerSeatIndex => CurrentSeatIndex.Value;
        public TalentWinFacts Facts { get; }
        public int AlreadyAcceptedFinalFan { get; }
        public Func<ScoringOptions, FanEvaluation> EvaluateOptions { get; }

        public TalentAcceptedWinAttributionContext(
            GameSession session,
            int winnerSeatIndex,
            int alreadyAcceptedFinalFan,
            TalentWinFacts facts,
            Func<ScoringOptions, FanEvaluation> evaluateOptions)
            : base(session, ValidateSeatIndex(winnerSeatIndex))
        {
            if (alreadyAcceptedFinalFan < 0)
                throw new ArgumentOutOfRangeException(nameof(alreadyAcceptedFinalFan));
            Facts = facts ?? throw new ArgumentNullException(nameof(facts));
            if (facts.WinnerSeatIndex != winnerSeatIndex)
                throw new ArgumentException("Win facts must belong to the attributed winner seat.", nameof(facts));
            AlreadyAcceptedFinalFan = alreadyAcceptedFinalFan;
            EvaluateOptions = evaluateOptions ?? throw new ArgumentNullException(nameof(evaluateOptions));
        }
    }

    public sealed class TalentWinEvaluation
    {
        public bool IsLegal { get; }
        public int FinalFan { get; }

        public TalentWinEvaluation(bool isLegal, int finalFan)
        {
            IsLegal = isLegal;
            FinalFan = finalFan;
        }
    }

    public sealed class TalentRoundOutcome
    {
        public int? WinnerSeatIndex { get; set; }
        public int? DiscarderSeatIndex { get; set; }
        public bool IsAborted { get; set; }
        public bool IsDraw => !IsAborted && !WinnerSeatIndex.HasValue;
        public int FinalFan { get; set; }
    }

    public sealed class TalentSessionSnapshot
    {
        private static readonly ConditionalWeakTable<GameSession, object> Identities =
            new ConditionalWeakTable<GameSession, object>();
        private readonly ReadOnlyCollection<int> _scores;

        public GameMode Mode { get; }
        public WindDirection PrevalentWind { get; }
        public int DealerSeatIndex { get; }
        public int RoundInWind { get; }
        public int TotalRoundsPlayed { get; }
        public IReadOnlyList<int> Scores => _scores;
        internal object Identity { get; }

        private TalentSessionSnapshot(GameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            Mode = session.Mode;
            PrevalentWind = session.PrevalentWind;
            DealerSeatIndex = session.DealerIndex;
            RoundInWind = session.RoundInWind;
            TotalRoundsPlayed = session.TotalRoundsPlayed;
            _scores = Array.AsReadOnly((int[])session.Scores.Clone());
            Identity = Identities.GetValue(session, _ => new object());
        }

        internal static TalentSessionSnapshot Create(GameSession session) =>
            new TalentSessionSnapshot(session);
    }

    public sealed class TalentGameStateSnapshot
    {
        private readonly ReadOnlyCollection<int> _handCounts;
        private readonly ReadOnlyCollection<int> _meldCounts;
        private readonly ReadOnlyCollection<int> _riverCounts;

        public IReadOnlyList<int> HandTileCounts => _handCounts;
        public IReadOnlyList<int> MeldCounts => _meldCounts;
        public IReadOnlyList<int> RiverTileCounts => _riverCounts;

        private TalentGameStateSnapshot(ServerGameState gameState)
        {
            int[] handCounts = new int[4];
            int[] meldCounts = new int[4];
            int[] riverCounts = new int[4];
            for (int seatIndex = 0; seatIndex < 4; seatIndex++)
            {
                handCounts[seatIndex] = gameState.GetHand(seatIndex).Count;
                meldCounts[seatIndex] = gameState.GetMelds(seatIndex).Count;
                riverCounts[seatIndex] = gameState.GetRiver(seatIndex).Count;
            }
            _handCounts = Array.AsReadOnly(handCounts);
            _meldCounts = Array.AsReadOnly(meldCounts);
            _riverCounts = Array.AsReadOnly(riverCounts);
        }

        internal static TalentGameStateSnapshot Create(ServerGameState gameState) =>
            gameState == null ? null : new TalentGameStateSnapshot(gameState);
    }

    public sealed class TalentDeckSnapshot
    {
        public int AlienationScore { get; }
        public int TotalTileCount { get; }

        private TalentDeckSnapshot(DeckConfig deckConfig)
        {
            AlienationScore = deckConfig.AlienationScore;
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            {
                int maximum = suit == Suit.Wind ? 4 : suit == Suit.Dragon ? 3 : 9;
                for (int value = 1; value <= maximum; value++)
                    TotalTileCount += deckConfig.GetCardCount(suit, value);
            }
        }

        internal static TalentDeckSnapshot Create(DeckConfig deckConfig) =>
            new TalentDeckSnapshot(deckConfig ?? throw new ArgumentNullException(nameof(deckConfig)));
    }
}
