using System;
using System.Collections.Generic;
using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents
{
    public class TalentContext
    {
        private Action<TalentRuntimeEvent> _eventSink;

        public int CurrentSeatIndex { get; internal set; } = -1;
        public int OwnerSeatIndex { get; internal set; } = -1;
        public List<TileData> WallTiles { get; internal set; }
        public ServerGameState GameState { get; internal set; }
        public GameSession Session { get; internal set; }
        public DeckConfig OwnerDeckConfig { get; internal set; }
        public TalentRuntimeState State { get; internal set; }

        public bool IsOwnersTurn => CurrentSeatIndex == OwnerSeatIndex;

        public void Emit(TalentRuntimeEvent runtimeEvent)
        {
            if (runtimeEvent == null) throw new ArgumentNullException(nameof(runtimeEvent));
            _eventSink?.Invoke(runtimeEvent);
        }

        internal void ConfigureEntry(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            OwnerSeatIndex = ownerSeatIndex;
            State = state;
            _eventSink = eventSink;
        }

        internal TalentContext Bind(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            TalentContext context = new TalentContext
            {
                CurrentSeatIndex = CurrentSeatIndex,
                WallTiles = WallTiles,
                GameState = GameState,
                Session = Session,
                OwnerDeckConfig = OwnerDeckConfig
            };
            context.ConfigureEntry(ownerSeatIndex, state, eventSink);
            return context;
        }
    }

    public sealed class TalentMatchContext : TalentContext
    {
        internal TalentMatchContext(
            GameSession session,
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            Session = session;
            ConfigureEntry(ownerSeatIndex, state, eventSink);
        }
    }

    public sealed class TalentRoundContext : TalentContext
    {
        public TalentRoundContext(GameSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal TalentRoundContext BindRound(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            TalentRoundContext context = new TalentRoundContext(Session)
            {
                CurrentSeatIndex = CurrentSeatIndex,
                GameState = GameState,
                OwnerDeckConfig = OwnerDeckConfig
            };
            context.ConfigureEntry(ownerSeatIndex, state, eventSink);
            return context;
        }
    }

    public sealed class TalentWallContext : TalentContext
    {
        public TalentWallContext(GameSession session, List<TileData> wallTiles)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            WallTiles = wallTiles ?? throw new ArgumentNullException(nameof(wallTiles));
        }
    }

    public sealed class TalentPostShuffleContext : TalentContext
    {
        public IReadOnlyList<TileData> ShuffledWallTiles { get; }

        public TalentPostShuffleContext(GameSession session, IReadOnlyList<TileData> shuffledWallTiles)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            ShuffledWallTiles = shuffledWallTiles ?? throw new ArgumentNullException(nameof(shuffledWallTiles));
        }
    }

    public sealed class TalentDrawContext : TalentContext
    {
        public TalentDrawContext(GameSession session, int currentSeatIndex)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            CurrentSeatIndex = currentSeatIndex;
        }
    }

    public sealed class TalentDiscardContext : TalentContext
    {
        public TalentDiscardContext(GameSession session, int currentSeatIndex)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            CurrentSeatIndex = currentSeatIndex;
        }
    }

    public sealed class TalentActionContext : TalentContext
    {
        public ClientActionType ActionType { get; }
        public TileData TargetTile { get; }
        public bool IsAllowed { get; internal set; } = true;

        public TalentActionContext(
            GameSession session,
            int currentSeatIndex,
            ClientActionType actionType,
            TileData targetTile)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            CurrentSeatIndex = currentSeatIndex;
            ActionType = actionType;
            TargetTile = targetTile;
        }
    }

    public sealed class TalentScoringContext : TalentContext
    {
        public TalentScoringContext(GameSession session, int currentSeatIndex)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            CurrentSeatIndex = currentSeatIndex;
        }

        internal TalentScoringContext BindScoring(
            int ownerSeatIndex,
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            TalentScoringContext context = new TalentScoringContext(Session, CurrentSeatIndex)
            {
                GameState = GameState,
                OwnerDeckConfig = OwnerDeckConfig
            };
            context.ConfigureEntry(ownerSeatIndex, state, eventSink);
            return context;
        }
    }

    public sealed class TalentPublicTileContext : TalentContext
    {
        public TalentPublicTileContext(GameSession session, int currentSeatIndex)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            CurrentSeatIndex = currentSeatIndex;
        }
    }

    public sealed class TalentAcceptedWinContext : TalentContext
    {
        public TalentAcceptedWinContext(GameSession session, int currentSeatIndex)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            CurrentSeatIndex = currentSeatIndex;
        }
    }

    public sealed class TalentRoundOutcome
    {
        public int? WinnerSeatIndex { get; set; }
        public int? DiscarderSeatIndex { get; set; }
        public bool IsDraw => !WinnerSeatIndex.HasValue;
        public int FinalFan { get; set; }
    }
}
