using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MahjongGame.Core.Network;

namespace MahjongGame.Core.Agents
{
    public sealed class StandardAiDecisionStrategy : IAiDecisionStrategy
    {
        private readonly int _budgetMilliseconds;

        public StandardAiDecisionStrategy(int budgetMilliseconds = 20)
        {
            _budgetMilliseconds = Math.Max(1, budgetMilliseconds);
        }

        public AiDecisionResult Decide(AiDecisionContext context, CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            cancellationToken.ThrowIfCancellationRequested();
            var evaluationCache = new AiHandShapeEvaluationCache();
            if (context.AllowedActions.CanHu) return AiDecisionResult.Hu(context.TriggerTile);
            if (context.Phase == AiDecisionPhase.RobKongResponse) return AiDecisionResult.Skip();
            if (context.Phase == AiDecisionPhase.DiscardResponse)
                return ChooseResponse(context, cancellationToken, evaluationCache);
            AiDecisionResult kong = ChooseSelfKong(context, cancellationToken, evaluationCache);
            if (kong != null) return kong;
            TileData discard = ChooseDiscard(context, cancellationToken, evaluationCache);
            return discard == null ? AiDecisionResult.Skip() : AiDecisionResult.Discard(discard);
        }

        private static AiDecisionResult ChooseSelfKong(AiDecisionContext context, CancellationToken cancellationToken,
            AiHandShapeEvaluationCache evaluationCache)
        {
            if (context.RemainingWallCount <= 0 || context.SelfTurnKongOptions == null) return null;
            int baseline = BestDiscardShanten(context.Hand, context.Melds, cancellationToken, evaluationCache);
            if (context.AllowedActions.CanAnGan)
            {
                foreach (TileData target in context.SelfTurnKongOptions.AnGangTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<TileData> hand = context.Hand.ToList();
                    List<TileData> removed = hand
                        .Where(tile => tile.TileSuit == target.TileSuit && tile.Value == target.Value)
                        .Take(4).ToList();
                    if (removed.Count != 4) continue;
                    foreach (TileData tile in removed) hand.Remove(tile);
                    List<Meld> melds = context.Melds.ToList();
                    melds.Add(new Meld(MeldType.Kan_Concealed, removed, context.SeatIndex, true));
                    if (evaluationCache.CalculateShanten(hand, melds) <= baseline)
                        return AiDecisionResult.Kong(ClientActionType.AnGan, target);
                }
            }
            if (context.AllowedActions.CanJiaGang)
            {
                foreach (TileData target in context.SelfTurnKongOptions.JiaGangTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<TileData> hand = context.Hand.ToList();
                    TileData added = hand.FirstOrDefault(tile => tile.TileSuit == target.TileSuit && tile.Value == target.Value);
                    if (added == null) continue;
                    hand.Remove(added);
                    if (evaluationCache.CalculateShanten(hand, context.Melds) <= baseline)
                        return AiDecisionResult.Kong(ClientActionType.JiaGang, target);
                }
            }
            return null;
        }

        private static int BestDiscardShanten(IEnumerable<TileData> hand, IEnumerable<Meld> melds,
            CancellationToken cancellationToken, AiHandShapeEvaluationCache evaluationCache)
        {
            List<TileData> tiles = hand.ToList();
            if (tiles.Count == 0) return int.MaxValue;
            int best = int.MaxValue;
            foreach (IGrouping<int, TileData> group in tiles.GroupBy(MahjongLogic.GetTileIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<TileData> remaining = tiles.ToList();
                remaining.Remove(group.First());
                best = Math.Min(best, evaluationCache.CalculateShanten(remaining, melds));
            }
            return best;
        }

        private TileData ChooseDiscard(AiDecisionContext context, CancellationToken cancellationToken,
            AiHandShapeEvaluationCache evaluationCache)
        {
            var stopwatch = Stopwatch.StartNew();
            Dictionary<int, List<MahjongLogic.WaitDetail>> legalWaits = MahjongLogic.GetWaitHints(
                context.Hand.ToList(), context.Melds.ToList(), context.RoundWind, context.SeatWind, context.ScoringOptions);
            DiscardScore bestScore = null;
            TileData bestTile = null;
            foreach (IGrouping<int, TileData> group in context.Hand.GroupBy(MahjongLogic.GetTileIndex).OrderBy(group => group.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TileData candidate = group.OrderBy(tile => tile.ID, StringComparer.Ordinal).First();
                List<TileData> remaining = context.Hand.Where(tile => !ReferenceEquals(tile, candidate)).ToList();
                if (remaining.Count == context.Hand.Count)
                {
                    remaining = context.Hand.ToList();
                    remaining.Remove(candidate);
                }
                DiscardScore score = Evaluate(
                    remaining,
                    context,
                    group.Key,
                    legalWaits,
                    includeImprovementScan: stopwatch.ElapsedMilliseconds < _budgetMilliseconds,
                    evaluationCache);
                if (bestScore == null || score.CompareTo(bestScore) > 0)
                {
                    bestScore = score;
                    bestTile = candidate;
                }
            }
            return bestTile;
        }

        private AiDecisionResult ChooseResponse(AiDecisionContext context, CancellationToken cancellationToken,
            AiHandShapeEvaluationCache evaluationCache)
        {
            var stopwatch = Stopwatch.StartNew();
            int baselineShanten = evaluationCache.CalculateShanten(context.Hand, context.Melds);
            int baselineLegalWaits = baselineShanten == 0
                ? CountLegalWinTypes(context.Hand, context.Melds, context)
                : 0;
            int baselineImprovements = CountImprovements(context.Hand, context.Melds, evaluationCache);
            ResponseCandidate best = null;
            if (context.AllowedActions.CanPon)
                best = EvaluateResponse(context, ClientActionType.Pon, null, 2, best, cancellationToken, evaluationCache);
            if (context.AllowedActions.CanMingGan && context.RemainingWallCount > 0
                && stopwatch.ElapsedMilliseconds < _budgetMilliseconds)
                best = EvaluateResponse(context, ClientActionType.MingGan, null, 3, best, cancellationToken, evaluationCache);
            bool evaluatedResponse = best != null;
            foreach (int[] combination in context.ChiCombinations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evaluatedResponse && stopwatch.ElapsedMilliseconds >= _budgetMilliseconds) break;
                best = EvaluateResponse(context, ClientActionType.Chi, combination, 0, best, cancellationToken, evaluationCache);
                evaluatedResponse = true;
            }
            if (best == null || best.Shanten > baselineShanten) return AiDecisionResult.Skip();
            if (best.Shanten == baselineShanten
                && best.LegalWaitCount <= baselineLegalWaits
                && best.ImprovementCount <= baselineImprovements)
                return AiDecisionResult.Skip();
            return best.ActionType == ClientActionType.Chi
                ? AiDecisionResult.Chi(context.TriggerTile, best.ChiCombination)
                : best.ActionType == ClientActionType.Pon
                    ? AiDecisionResult.Pon(context.TriggerTile)
                    : AiDecisionResult.Kong(best.ActionType, context.TriggerTile);
        }

        private static ResponseCandidate EvaluateResponse(AiDecisionContext context, ClientActionType type,
            int[] chiCombination, int matchingToRemove, ResponseCandidate current,
            CancellationToken cancellationToken, AiHandShapeEvaluationCache evaluationCache)
        {
            var remaining = context.Hand.ToList();
            if (type == ClientActionType.Chi)
            {
                foreach (int value in chiCombination ?? Array.Empty<int>())
                {
                    TileData tile = remaining.FirstOrDefault(item => item.TileSuit == context.TriggerTile.TileSuit && item.Value == value);
                    if (tile == null) return current;
                    remaining.Remove(tile);
                }
            }
            else
            {
                for (int i = 0; i < matchingToRemove; i++)
                {
                    TileData tile = remaining.FirstOrDefault(item => item.TileSuit == context.TriggerTile.TileSuit
                                                                     && item.Value == context.TriggerTile.Value);
                    if (tile == null) return current;
                    remaining.Remove(tile);
                }
            }

            var melds = context.Melds.ToList();
            MeldType meldType = type == ClientActionType.Chi ? MeldType.Chi
                : type == ClientActionType.Pon ? MeldType.Pon : MeldType.Kan_Exposed;
            melds.Add(new Meld(meldType, new List<TileData> { context.TriggerTile }, -1));
            ProjectedShape shape = type == ClientActionType.MingGan
                ? EvaluateProjectedShape(remaining, melds, context, evaluationCache)
                : FindBestPostResponseDiscard(remaining, melds, context, cancellationToken, evaluationCache);
            if (shape == null) return current;
            var candidate = new ResponseCandidate(type, chiCombination,
                shape.Shanten, shape.LegalWaitCount, shape.ImprovementCount);
            return current == null || candidate.CompareTo(current) > 0 ? candidate : current;
        }

        private static ProjectedShape FindBestPostResponseDiscard(List<TileData> hand, List<Meld> melds,
            AiDecisionContext context, CancellationToken cancellationToken,
            AiHandShapeEvaluationCache evaluationCache)
        {
            ProjectedShape best = null;
            foreach (IGrouping<int, TileData> group in hand.GroupBy(MahjongLogic.GetTileIndex).OrderBy(group => group.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<TileData> afterDiscard = hand.ToList();
                afterDiscard.Remove(group.First());
                ProjectedShape candidate = EvaluateProjectedShape(afterDiscard, melds, context, evaluationCache);
                if (best == null || candidate.CompareTo(best) > 0) best = candidate;
            }
            return best;
        }

        private static ProjectedShape EvaluateProjectedShape(List<TileData> hand, List<Meld> melds,
            AiDecisionContext context, AiHandShapeEvaluationCache evaluationCache)
        {
            int shanten = evaluationCache.CalculateShanten(hand, melds);
            int legalWaits = shanten == 0 ? CountLegalWinTypes(hand, melds, context) : 0;
            if (shanten == 0 && legalWaits == 0) shanten = 1;
            return new ProjectedShape(shanten, legalWaits, CountImprovements(hand, melds, evaluationCache));
        }

        private static int CountLegalWinTypes(IEnumerable<TileData> hand, IEnumerable<Meld> melds,
            AiDecisionContext context)
        {
            List<TileData> handList = hand.ToList();
            List<Meld> meldList = melds.ToList();
            int result = 0;
            for (int index = 0; index < MahjongLogic.MAX_TILE_INDEX; index++)
            {
                TileData candidate = CreateTile(index);
                if (MahjongLogic.CheckWinWithFan(handList, meldList, candidate, false,
                        out _, out _, context.RoundWind, context.SeatWind, context.ScoringOptions)
                    || MahjongLogic.CheckWinWithFan(handList, meldList, candidate, true,
                        out _, out _, context.RoundWind, context.SeatWind, context.ScoringOptions))
                    result++;
            }
            return result;
        }

        private static DiscardScore Evaluate(List<TileData> hand, AiDecisionContext context, int discardIndex,
            IReadOnlyDictionary<int, List<MahjongLogic.WaitDetail>> legalWaitsByDiscard,
            bool includeImprovementScan, AiHandShapeEvaluationCache evaluationCache)
        {
            int shanten = evaluationCache.CalculateShanten(hand, context.Melds);
            int legalWaits = 0;
            if (shanten == 0)
            {
                List<MahjongLogic.WaitDetail> waits = legalWaitsByDiscard.TryGetValue(discardIndex, out var found)
                    ? found
                    : null;
                legalWaits = waits?.Count ?? 0;
                if (legalWaits == 0) shanten = 1;
            }
            return new DiscardScore(shanten, legalWaits,
                includeImprovementScan ? CountImprovements(hand, context.Melds, evaluationCache) : 0,
                CalculateStructureQuality(hand));
        }

        private static int CountImprovements(IEnumerable<TileData> hand, IEnumerable<Meld> melds,
            AiHandShapeEvaluationCache evaluationCache)
        {
            List<TileData> current = hand.ToList();
            List<Meld> meldList = melds.ToList();
            int baseline = evaluationCache.CalculateShanten(current, meldList);
            int result = 0;
            for (int index = 0; index < MahjongLogic.MAX_TILE_INDEX; index++)
            {
                current.Add(CreateTile(index));
                if (evaluationCache.CalculateShanten(current, meldList) < baseline) result++;
                current.RemoveAt(current.Count - 1);
            }
            return result;
        }

        private static int CalculateStructureQuality(IEnumerable<TileData> hand)
        {
            int[] counts = MahjongLogic.ConvertToFrequencyArray(hand.ToList());
            int pairs = counts.Count(count => count >= 2);
            int connected = 0;
            int isolated = 0;
            for (int index = 0; index < counts.Length; index++)
            {
                if (counts[index] == 0) continue;
                if (index >= 27) { if (counts[index] == 1) isolated++; continue; }
                int relative = index % 9;
                bool neighbor = relative > 0 && counts[index - 1] > 0
                                || relative < 8 && counts[index + 1] > 0
                                || relative > 1 && counts[index - 2] > 0
                                || relative < 7 && counts[index + 2] > 0;
                if (neighbor) connected += counts[index]; else isolated += counts[index];
            }
            return pairs * 8 + connected * 2 - isolated * 3;
        }

        private static TileData CreateTile(int index)
        {
            if (index < 9) return new TileData(Suit.Man, index + 1, -1);
            if (index < 18) return new TileData(Suit.Pin, index - 8, -1);
            if (index < 27) return new TileData(Suit.Sou, index - 17, -1);
            if (index < 31) return new TileData(Suit.Wind, index - 26, -1);
            return new TileData(Suit.Dragon, index - 30, -1);
        }

        private sealed class DiscardScore : IComparable<DiscardScore>
        {
            private readonly int _shanten;
            private readonly int _legalWaits;
            private readonly int _improvements;
            private readonly int _structure;

            public DiscardScore(int shanten, int legalWaits, int improvements, int structure)
            {
                _shanten = shanten;
                _legalWaits = legalWaits;
                _improvements = improvements;
                _structure = structure;
            }

            public int CompareTo(DiscardScore other)
            {
                int comparison = other._shanten.CompareTo(_shanten);
                if (comparison != 0) return comparison;
                comparison = _legalWaits.CompareTo(other._legalWaits);
                if (comparison != 0) return comparison;
                comparison = _improvements.CompareTo(other._improvements);
                return comparison != 0 ? comparison : _structure.CompareTo(other._structure);
            }
        }

        private sealed class ResponseCandidate : IComparable<ResponseCandidate>
        {
            public ClientActionType ActionType { get; }
            public int[] ChiCombination { get; }
            public int Shanten { get; }
            public int LegalWaitCount { get; }
            public int ImprovementCount { get; }

            public ResponseCandidate(ClientActionType actionType, int[] chiCombination, int shanten,
                int legalWaitCount, int improvementCount)
            {
                ActionType = actionType;
                ChiCombination = chiCombination?.ToArray();
                Shanten = shanten;
                LegalWaitCount = legalWaitCount;
                ImprovementCount = improvementCount;
            }

            public int CompareTo(ResponseCandidate other)
            {
                int comparison = other.Shanten.CompareTo(Shanten);
                if (comparison != 0) return comparison;
                comparison = LegalWaitCount.CompareTo(other.LegalWaitCount);
                return comparison != 0 ? comparison : ImprovementCount.CompareTo(other.ImprovementCount);
            }
        }

        private sealed class ProjectedShape : IComparable<ProjectedShape>
        {
            public int Shanten { get; }
            public int LegalWaitCount { get; }
            public int ImprovementCount { get; }

            public ProjectedShape(int shanten, int legalWaitCount, int improvementCount)
            {
                Shanten = shanten;
                LegalWaitCount = legalWaitCount;
                ImprovementCount = improvementCount;
            }

            public int CompareTo(ProjectedShape other)
            {
                int comparison = other.Shanten.CompareTo(Shanten);
                if (comparison != 0) return comparison;
                comparison = LegalWaitCount.CompareTo(other.LegalWaitCount);
                return comparison != 0 ? comparison : ImprovementCount.CompareTo(other.ImprovementCount);
            }
        }
    }
}
