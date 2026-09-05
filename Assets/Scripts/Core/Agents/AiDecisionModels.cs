using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MahjongGame.Core.Network;

namespace MahjongGame.Core.Agents
{
    public enum AiDecisionPhase
    {
        SelfTurn,
        DiscardResponse,
        RobKongResponse
    }

    public sealed class AiDecisionContext
    {
        public long DecisionId { get; }
        public int SeatIndex { get; }
        public AiDecisionPhase Phase { get; }
        public IReadOnlyList<TileData> Hand { get; }
        public IReadOnlyList<Meld> Melds { get; }
        public AllowedActions AllowedActions { get; }
        public TileData TriggerTile { get; }
        public IReadOnlyList<int[]> ChiCombinations { get; }
        public ScoringOptions ScoringOptions { get; }
        public WindDirection RoundWind { get; }
        public WindDirection SeatWind { get; }
        public int RemainingWallCount { get; }
        public int RandomSeed { get; }
        public SelfTurnKongOptions SelfTurnKongOptions { get; }

        private AiDecisionContext(long decisionId, int seatIndex, AiDecisionPhase phase,
            IEnumerable<TileData> hand, IEnumerable<Meld> melds, AllowedActions allowedActions,
            TileData triggerTile, IEnumerable<int[]> chiCombinations, ScoringOptions scoringOptions,
            WindDirection roundWind, WindDirection seatWind, int remainingWallCount, int randomSeed,
            SelfTurnKongOptions selfTurnKongOptions = null)
        {
            DecisionId = decisionId;
            SeatIndex = seatIndex;
            Phase = phase;
            Hand = (hand ?? Array.Empty<TileData>()).Where(tile => tile != null)
                .Select(CloneTile).ToList().AsReadOnly();
            Melds = (melds ?? Array.Empty<Meld>()).Where(meld => meld != null)
                .Select(CloneMeld).ToList().AsReadOnly();
            AllowedActions = allowedActions;
            TriggerTile = CloneTile(triggerTile);
            ChiCombinations = (chiCombinations ?? Array.Empty<int[]>())
                .Where(option => option != null)
                .Select(option => option.ToArray())
                .ToList().AsReadOnly();
            ScoringOptions = CloneScoringOptions(scoringOptions);
            RoundWind = roundWind;
            SeatWind = seatWind;
            RemainingWallCount = remainingWallCount;
            RandomSeed = randomSeed;
            SelfTurnKongOptions = selfTurnKongOptions == null
                ? new SelfTurnKongOptions(null, null)
                : new SelfTurnKongOptions(
                    selfTurnKongOptions.AnGangTargets.Select(CloneTile),
                    selfTurnKongOptions.JiaGangTargets.Select(CloneTile));
        }

        private static TileData CloneTile(TileData tile)
        {
            if (tile == null) return null;
            return new TileData(tile.TileSuit, tile.Value, tile.OriginalOwnerID)
            {
                ID = tile.ID,
                IsModified = tile.IsModified,
                SpecialEffectID = tile.SpecialEffectID
            };
        }

        private static Meld CloneMeld(Meld meld)
        {
            return new Meld(meld.Type,
                (meld.Tiles ?? new List<TileData>()).Where(tile => tile != null).Select(CloneTile).ToList(),
                meld.SourcePlayerID,
                meld.IsConcealed);
        }

        private static ScoringOptions CloneScoringOptions(ScoringOptions options)
        {
            if (options == null) return new ScoringOptions();
            return new ScoringOptions
            {
                BonusFan = options.BonusFan,
                MinimumFan = options.MinimumFan,
                RelaxedPureStraight = options.RelaxedPureStraight
            };
        }

        public static AiDecisionContext ForSelfTurn(long decisionId, int seatIndex,
            IEnumerable<TileData> hand, IEnumerable<Meld> melds, AllowedActions allowedActions,
            TileData drawnTile, ScoringOptions scoringOptions, int remainingWallCount, int randomSeed,
            WindDirection roundWind = WindDirection.East, WindDirection seatWind = WindDirection.East,
            SelfTurnKongOptions selfTurnKongOptions = null)
        {
            return new AiDecisionContext(decisionId, seatIndex, AiDecisionPhase.SelfTurn,
                hand, melds, allowedActions, drawnTile, null, scoringOptions,
                roundWind, seatWind, remainingWallCount, randomSeed, selfTurnKongOptions);
        }

        public static AiDecisionContext ForDiscardResponse(long decisionId, int seatIndex,
            IEnumerable<TileData> hand, IEnumerable<Meld> melds, AllowedActions allowedActions,
            TileData discardedTile, IEnumerable<int[]> chiCombinations, ScoringOptions scoringOptions,
            int remainingWallCount, int randomSeed,
            WindDirection roundWind = WindDirection.East, WindDirection seatWind = WindDirection.East)
        {
            return new AiDecisionContext(decisionId, seatIndex, AiDecisionPhase.DiscardResponse,
                hand, melds, allowedActions, discardedTile, chiCombinations, scoringOptions,
                roundWind, seatWind, remainingWallCount, randomSeed);
        }

        public static AiDecisionContext ForRobKong(long decisionId, int seatIndex,
            IEnumerable<TileData> hand, IEnumerable<Meld> melds, bool canHu, TileData targetTile,
            ScoringOptions scoringOptions, int remainingWallCount, int randomSeed,
            WindDirection roundWind = WindDirection.East, WindDirection seatWind = WindDirection.East)
        {
            return new AiDecisionContext(decisionId, seatIndex, AiDecisionPhase.RobKongResponse,
                hand, melds, new AllowedActions { CanHu = canHu }, targetTile, null, scoringOptions,
                roundWind, seatWind, remainingWallCount, randomSeed);
        }
    }

    public sealed class AiDecisionResult
    {
        public ClientActionType ActionType { get; }
        public TileData TargetTile { get; }
        public int[] ChiCombination { get; }

        private AiDecisionResult(ClientActionType actionType, TileData targetTile, int[] chiCombination = null)
        {
            ActionType = actionType;
            TargetTile = targetTile;
            ChiCombination = chiCombination?.ToArray();
        }

        public static AiDecisionResult Hu(TileData tile) => new AiDecisionResult(ClientActionType.Hu, tile);
        public static AiDecisionResult Discard(TileData tile) => new AiDecisionResult(ClientActionType.Discard, tile);
        public static AiDecisionResult Skip() => new AiDecisionResult(ClientActionType.Skip, null);
        public static AiDecisionResult Pon(TileData tile) => new AiDecisionResult(ClientActionType.Pon, tile);
        public static AiDecisionResult Chi(TileData tile, int[] combination) => new AiDecisionResult(ClientActionType.Chi, tile, combination);
        public static AiDecisionResult Kong(ClientActionType type, TileData tile) => new AiDecisionResult(type, tile);
    }

    public interface IAiDecisionStrategy
    {
        AiDecisionResult Decide(AiDecisionContext context, CancellationToken cancellationToken);
    }

    public interface IAiDecisionStrategyFactory
    {
        IAiDecisionStrategy Create(AiDifficulty difficulty);
    }

    public sealed class AiDecisionStrategyFactory : IAiDecisionStrategyFactory
    {
        public IAiDecisionStrategy Create(AiDifficulty difficulty) => difficulty == AiDifficulty.Standard
            ? new StandardAiDecisionStrategy()
            : new BeginnerAiDecisionStrategy();
    }
}
