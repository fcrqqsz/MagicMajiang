using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MahjongGame.Core;
using MahjongGame.Core.Fan;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents
{
    /// <summary>
    /// 纯 C# 管道执行器，每局由 GameServer 创建，避免跨局状态残留
    /// </summary>
    public class TalentManager
    {
        private Dictionary<int, List<TalentRule>> _playerTalents = new Dictionary<int, List<TalentRule>>();
        private Dictionary<TalentPhase, List<TalentRule>> _phasePipelines = new Dictionary<TalentPhase, List<TalentRule>>();

        public void Initialize(Dictionary<int, TalentSlotConfig> playerConfigs)
        {
            _playerTalents.Clear();
            _phasePipelines.Clear();

            foreach (TalentPhase phase in System.Enum.GetValues(typeof(TalentPhase)))
            {
                _phasePipelines[phase] = new List<TalentRule>();
            }

            if (playerConfigs == null) return;

            foreach (var kvp in playerConfigs)
            {
                int playerId = kvp.Key;
                var slotConfig = kvp.Value;
                var talents = new List<TalentRule>();

                foreach (var talentId in slotConfig.GetAllEquippedIds())
                {
                    var rule = TalentRegistry.Instance.CreateInstance(talentId, playerId);
                    if (rule != null)
                    {
                        talents.Add(rule);
                        foreach (var phase in rule.Phases)
                        {
                            _phasePipelines[phase].Add(rule);
                        }
                    }
                }

                _playerTalents[playerId] = talents;
            }

            // 按 Priority 降序排列各管道
            foreach (var pipeline in _phasePipelines.Values)
            {
                pipeline.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }

            int total = _phasePipelines.Values.Sum(p => p.Count);
            if (total > 0)
                Debug.Log($"[TalentManager] 天赋管道初始化完毕，共 {_playerTalents.Count} 个玩家, {total} 条管道条目。");
        }

        public bool HasAnyTalents => _playerTalents.Values.Any(list => list.Count > 0);

        public bool PlayerHasTalent(int playerId, string talentId)
        {
            if (!_playerTalents.TryGetValue(playerId, out var talents)) return false;
            return talents.Any(t => t.Id == talentId);
        }

        public void ExecuteWallBuilding(List<TileData> wallTiles, ServerGameState gameState, GameSession session, Dictionary<int, DeckConfig> deckConfigs)
        {
            var pipeline = _phasePipelines[TalentPhase.WallBuilding];
            if (pipeline.Count == 0) return;

            foreach (var rule in pipeline)
            {
                var ctx = CreateContext(rule, -1, gameState, session, deckConfigs);
                ctx.WallTiles = wallTiles;
                rule.OnWallBuilding(ctx);
            }
        }

        public TileData ExecuteOnDraw(int playerId, TileData tile, ServerGameState gameState, GameSession session, Dictionary<int, DeckConfig> deckConfigs)
        {
            var pipeline = _phasePipelines[TalentPhase.OnDraw];
            if (pipeline.Count == 0) return tile;

            foreach (var rule in pipeline)
            {
                if (rule.Scope == TalentScope.Self && rule.OwnerPlayerId != playerId)
                    continue;

                var ctx = CreateContext(rule, playerId, gameState, session, deckConfigs);
                tile = rule.OnDraw(ctx, tile);
            }
            return tile;
        }

        public TileData ExecuteOnDiscard(int playerId, TileData tile, ServerGameState gameState, GameSession session, Dictionary<int, DeckConfig> deckConfigs)
        {
            var pipeline = _phasePipelines[TalentPhase.OnDiscard];
            if (pipeline.Count == 0) return tile;

            foreach (var rule in pipeline)
            {
                if (rule.Scope == TalentScope.Self && rule.OwnerPlayerId != playerId)
                    continue;

                var ctx = CreateContext(rule, playerId, gameState, session, deckConfigs);
                tile = rule.OnDiscard(ctx, tile);
            }
            return tile;
        }

        public bool ExecuteActionValidation(int playerId, ClientActionType actionType, TileData targetTile, ServerGameState gameState, GameSession session, Dictionary<int, DeckConfig> deckConfigs)
        {
            var pipeline = _phasePipelines[TalentPhase.ActionValidation];
            if (pipeline.Count == 0) return true;

            foreach (var rule in pipeline)
            {
                if (rule.Scope == TalentScope.Self && rule.OwnerPlayerId != playerId)
                    continue;

                var ctx = CreateContext(rule, playerId, gameState, session, deckConfigs);
                if (!rule.OnActionValidation(ctx, actionType, targetTile))
                    return false;
            }
            return true;
        }

        public void ExecuteScoring(FanContext fanCtx, int playerId, ServerGameState gameState, GameSession session, Dictionary<int, DeckConfig> deckConfigs)
        {
            var pipeline = _phasePipelines[TalentPhase.Scoring];
            if (pipeline.Count == 0) return;

            foreach (var rule in pipeline)
            {
                if (rule.Scope == TalentScope.Self && rule.OwnerPlayerId != playerId)
                    continue;

                var ctx = CreateContext(rule, playerId, gameState, session, deckConfigs);
                rule.OnScoring(ctx, fanCtx);
            }
        }

        private TalentContext CreateContext(TalentRule rule, int currentPlayerId, ServerGameState gameState, GameSession session, Dictionary<int, DeckConfig> deckConfigs)
        {
            return new TalentContext
            {
                CurrentPlayerId = currentPlayerId,
                TalentOwnerId = rule.OwnerPlayerId,
                GameState = gameState,
                Session = session,
                OwnerDeckConfig = deckConfigs != null && deckConfigs.TryGetValue(rule.OwnerPlayerId, out var cfg) ? cfg : null
            };
        }
    }
}
