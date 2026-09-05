using System;
using System.Linq;
using System.Threading;
using MahjongGame.Core.Network;

namespace MahjongGame.Core.Agents
{
    public sealed class BeginnerAiDecisionStrategy : IAiDecisionStrategy
    {
        public AiDecisionResult Decide(AiDecisionContext context, CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            cancellationToken.ThrowIfCancellationRequested();
            if (context.AllowedActions.CanHu) return AiDecisionResult.Hu(context.TriggerTile);

            var random = new DeterministicAiRandom(context.RandomSeed, context.DecisionId, context.SeatIndex);
            if (context.Phase != AiDecisionPhase.SelfTurn)
            {
                if (context.AllowedActions.CanPon && random.NextBoolean())
                    return AiDecisionResult.Pon(context.TriggerTile);
                if ((context.AllowedActions.CanChiLeft || context.AllowedActions.CanChiMiddle || context.AllowedActions.CanChiRight)
                    && context.ChiCombinations.Count > 0 && random.NextBoolean())
                    return AiDecisionResult.Chi(context.TriggerTile, context.ChiCombinations[0]);
                return AiDecisionResult.Skip();
            }

            var honors = context.Hand.Where(tile => tile.TileSuit == Suit.Wind || tile.TileSuit == Suit.Dragon).ToArray();
            TileData selected = honors.Length > 0
                ? honors[random.Next(honors.Length)]
                : context.Hand.Count > 0 ? context.Hand[random.Next(context.Hand.Count)] : null;
            return selected == null ? AiDecisionResult.Skip() : AiDecisionResult.Discard(selected);
        }

        private struct DeterministicAiRandom
        {
            private uint _state;

            public DeterministicAiRandom(int seed, long decisionId, int seatIndex)
            {
                _state = unchecked((uint)(seed * 397) ^ (uint)decisionId ^ (uint)(decisionId >> 32) ^ (uint)(seatIndex * 7919));
                if (_state == 0) _state = 0x9E3779B9u;
            }

            public int Next(int exclusiveMax)
            {
                if (exclusiveMax <= 1) return 0;
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return (int)(_state % (uint)exclusiveMax);
            }

            public bool NextBoolean() => Next(2) == 1;
        }
    }
}
