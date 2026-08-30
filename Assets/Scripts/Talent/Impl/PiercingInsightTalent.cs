using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("piercing_insight", "洞若观火", "每小局一次，选择一名其他玩家，私下查看其当前暗手中的所有数牌；已知牌会持续显示至公开离手或失效。明牌排序仅用于整理信息，不代表真实手牌位置。",
        TalentTier.Large, 26,
        StateScope = TalentStateScope.Round,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class PiercingInsightTalent : TalentRule
    {
        private const string UsedKey = "used";
        private const string PublicTargetEventType = "piercing_insight_target";

        public override void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            if (context == null || !context.IsOwnersTurn || output == null)
                return;

            if (context.State.GetCounter(UsedKey, TalentStateScope.Round) > 0)
                return;

            for (int seatIndex = 0; seatIndex < 4; seatIndex++)
            {
                if (seatIndex == context.CurrentSeatIndex) continue;

                output.Add(new TalentActionOption
                {
                    TalentId = Id,
                    TargetSeatIndex = seatIndex,
                    AiPriority = 100
                });
            }
        }

        public override TalentActionResult TryActivate(
            TalentActivationContext context,
            TalentActionRequest request)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (request == null)
                return TalentActionResult.Reject(TalentActionErrorCodes.NotAvailable);

            if (request.TargetSeatIndex < 0
                || request.TargetSeatIndex > 3
                || request.TargetSeatIndex == context.CurrentSeatIndex)
            {
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidTarget);
            }

            if (context.State.GetCounter(UsedKey, TalentStateScope.Round) > 0)
                return TalentActionResult.Reject(TalentActionErrorCodes.AlreadyUsedThisTurn);

            if (!context.TryGetConcealedHandSnapshot(request.TargetSeatIndex, out IReadOnlyList<TileData> hand))
                return TalentActionResult.Reject(TalentActionErrorCodes.NotAvailable);

            TileData[] numericTiles = hand
                .Where(tile => tile != null && (tile.TileSuit == Suit.Man || tile.TileSuit == Suit.Pin || tile.TileSuit == Suit.Sou))
                .ToArray();

            context.RecordPrivateTileReveal(request.TargetSeatIndex, numericTiles);
            context.State.SetCounter(UsedKey, 1, TalentStateScope.Round);

            return TalentActionResult.Success(
                effectApplied: true,
                publicStateEventType: PublicTargetEventType,
                publicStateValue: request.TargetSeatIndex + 1);
        }

        public override int GetSnapshotPrivateValue(TalentRuntimeState state)
        {
            if (state == null) return 0;
            return Math.Max(0, 1 - state.GetCounter(UsedKey, TalentStateScope.Round));
        }
    }
}
