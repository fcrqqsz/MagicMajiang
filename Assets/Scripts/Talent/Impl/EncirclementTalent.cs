using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("encirclement", "合围", "本家提交吃、碰或明杠时记录来源席位；来自至少2个不同对手后公开发动。发动后当局合法胡牌最终结算额外+4番。",
        TalentTier.Small, 8, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class EncirclementTalent : TalentRule
    {
        private const string TriggeredKey = "triggered";

        public override void OnActionCommitted(TalentActionCommittedContext context)
        {
            if (context.Facts.ActorSeatIndex != context.OwnerSeatIndex) return;

            switch (context.Facts.ActionType)
            {
                case ClientActionType.Chi:
                case ClientActionType.Pon:
                case ClientActionType.MingGan:
                    break;
                default:
                    return;
            }

            if (!context.Facts.SourceSeatIndex.HasValue) return;

            int sourceSeat = context.Facts.SourceSeatIndex.Value;
            if (sourceSeat == context.OwnerSeatIndex || sourceSeat < 0 || sourceSeat > 3) return;

            context.State.SetFlag($"source_{sourceSeat}", true, TalentStateScope.Round);

            if (context.State.GetFlag(TriggeredKey, TalentStateScope.Round)) return;

            int distinctCount = 0;
            for (int s = 0; s < 4; s++)
            {
                if (s != context.OwnerSeatIndex && context.State.GetFlag($"source_{s}", TalentStateScope.Round))
                {
                    distinctCount++;
                }
            }

            if (distinctCount >= 2)
            {
                context.State.SetFlag(TriggeredKey, true, TalentStateScope.Round);
                context.EmitPublic("encirclement_triggered", 1);
            }
        }

        public override int GetPostLegalFanBonus(TalentWinContext context) =>
            context.State.GetFlag(TriggeredKey, TalentStateScope.Round) ? 4 : 0;
    }
}
