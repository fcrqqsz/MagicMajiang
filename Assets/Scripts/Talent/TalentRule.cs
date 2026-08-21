using MahjongGame.Core;
using MahjongGame.Core.Fan;
using MahjongGame.Core.Network;
using System.Collections.Generic;

namespace MahjongGame.Talents
{
    public abstract class TalentRule
    {
        public string Id { get; set; }
        public TalentTier Tier { get; set; }
        public int AlienationCost { get; set; }
        public TalentPhase[] Phases { get; set; }
        public virtual TalentScope Scope => TalentScope.Self;
        public virtual int Priority => 0;
        public int OwnerSeatIndex { get; internal set; }

        public void Initialize(string id, TalentTier tier, int cost, TalentPhase[] phases)
        {
            Id = id;
            Tier = tier;
            AlienationCost = cost;
            Phases = phases;
        }

        // 各阶段钩子，子类按需覆写
        public virtual void OnWallBuilding(TalentWallContext ctx) { }
        public virtual TileData OnDraw(TalentContext ctx, TileData tile) => tile;
        public virtual TileData OnDiscard(TalentContext ctx, TileData tile) => tile;
        public virtual bool OnActionValidation(TalentContext ctx, ClientActionType actionType, TileData targetTile) => true;
        public virtual void OnScoring(TalentContext ctx, FanContext fanCtx) { }

        public virtual void InitializeMatchState(TalentMatchContext context) { }
        public virtual int GetMatchStartScoreDelta(TalentMatchContext context) => 0;
        public virtual void OnRoundStarted(TalentRoundContext context) { }
        public virtual void OnInitialHandCompleted(TalentInitialHandContext context) { }
        public virtual void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome) { }
        public virtual void OnActionCommitted(TalentActionCommittedContext context) { }
        public virtual int GetRoundStartPeekCount(TalentRoundContext context) => 0;
        public virtual void ConfigureScoring(TalentScoringContext context, ScoringOptions options) { }
        public virtual bool TryBlockNegativeEffect(
            TalentNegativeEffectContext context,
            TalentNegativeEffect effect) => false;
        public virtual void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            output.Add(new TalentActionOption { TalentId = Id });
        }
        public virtual TalentActionResult TryActivate(
            TalentActivationContext context,
            TalentActionRequest request) => TalentActionResult.NotSupported();
        /// <summary>Returns the one rule-approved integer safe for the owning seat's snapshot.</summary>
        public virtual int GetSnapshotPrivateValue(TalentRuntimeState state) => 0;
        public virtual int GetPostLegalFanBonus(TalentWinContext context) => 0;
        public virtual int GetPostLegalFanPenalty(TalentWinContext context) => 0;
        public virtual void OnAcceptedWin(TalentWinContext context) { }
    }
}
