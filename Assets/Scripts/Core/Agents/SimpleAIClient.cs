using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using MahjongGame.Core.Network;
using MahjongGame.Core;

namespace MahjongGame.Core.Agents
{
    /// <summary>
    /// 规则化AI胖客户端。自己在本地计算胡牌、吃碰权限和打牌策略。
    /// </summary>
    public class SimpleAIClient : IPlayerClient
    {
        public int PlayerId { get; private set; }
        
        private IServer _server;
        
        // AI 在本地维护自己的状态
        private List<TileData> _hand = new List<TileData>();
        private List<Meld> _melds = new List<Meld>();

        public SimpleAIClient(int playerId, IServer server)
        {
            PlayerId = playerId;
            _server = server;
        }

        public void OnGameStart(List<TileData> startingHand)
        {
            _hand = new List<TileData>(startingHand);
            _melds.Clear();
            SortHand();
            Debug.Log($"[AI {PlayerId}] 收到初始手牌，共 {_hand.Count} 张");
        }

        public void OnPlayerDrawn(int playerId)
        {
            // AI 不需要视觉表现，因此空实现即可
        }

        public async void OnTileDrawn(TileData drawnTile)
        {
            _hand.Add(drawnTile);
            SortHand(); // 摸牌后先理牌

            // 模拟 AI 思考延迟
            await Task.Delay(500);

            // 1. 本地校验自摸或杠
            // 注意：此时 _hand 已经包含了 drawnTile (Count=14)
            var actions = ActionValidator.CheckSelfActions(_hand, _melds, drawnTile);
            if (actions.HasAction)
            {
                if (actions.CanHu)
                {
                    // 胖客户端：本地调用核心逻辑算番
                    int totalFan;
                    List<string> fanDetails;
                    bool canWin = MahjongLogic.CheckWinWithFan(_hand, _melds, drawnTile, true, out totalFan, out fanDetails);
                    
                    if (canWin)
                    {
                        var action = new ClientAction(PlayerId, ClientActionType.Hu, drawnTile);
                        action.SetHuDetails(totalFan, fanDetails);
                        _server.SubmitAction(action);
                        return;
                    }
                }
                
                // 暂时不处理暗杠/加杠，直接跳过到打牌阶段
            }

            // 2. 决定打出一张牌 (简单的孤张判定策略)
            TileData tileToDiscard = ChooseTileToDiscard();
            _hand.Remove(tileToDiscard);
            
            Debug.Log($"[AI {PlayerId}] 决定打出: {tileToDiscard}");
            _server.SubmitAction(ClientAction.Discard(PlayerId, tileToDiscard));
        }

        public async void OnOtherPlayerDiscarded(int discarderId, TileData discardedTile)
        {
            // 模拟 AI 思考延迟
            await Task.Delay(UnityEngine.Random.Range(200, 600));

            bool isNextPlayer = (discarderId + 1) % 4 == PlayerId;

            // 本地计算权限
            var actions = ActionValidator.CheckActions(_hand, _melds, discardedTile, isNextPlayer);

            if (actions.HasAction)
            {
                if (actions.CanHu)
                {
                    int totalFan;
                    List<string> fanDetails;
                    // 他人打出的牌，加入手牌计算番数
                    bool canWin = MahjongLogic.CheckWinWithFan(_hand, _melds, discardedTile, false, out totalFan, out fanDetails);
                    if (canWin)
                    {
                        var action = new ClientAction(PlayerId, ClientActionType.Hu, discardedTile);
                        action.SetHuDetails(totalFan, fanDetails);
                        _server.SubmitAction(action);
                        return;
                    }
                }

                // 简单的吃碰响应（50% 概率）
                if (actions.CanPon && UnityEngine.Random.value > 0.5f)
                {
                    _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Pon, discardedTile));
                    return;
                }
                // AI 吃牌决策：如果是上家，且能吃，且50%概率
                else if (isNextPlayer && (actions.CanChiLeft || actions.CanChiMiddle || actions.CanChiRight) && UnityEngine.Random.value > 0.5f)
                {
                    // 找出所有能吃的组合
                    var chiOptions = ActionValidator.GetChiCombinations(_hand, discardedTile);
                    if (chiOptions.Count > 0) {
                        // 简化AI：选择第一个能吃的组合
                        _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Chi, discardedTile, chiOptions[0]));
                        return;
                    }
                }
            }

            // 没有动作或者放弃
            _server.SubmitAction(ClientAction.Skip(PlayerId));
        }

        public async void OnActionResolved(int actionPlayerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations)
        {
            // 如果是自己执行了吃碰杠，更新手牌和副露，然后需要打出一张牌
            if (actionPlayerId == PlayerId)
            {
                if (actionType == ClientActionType.Pon)
                {
                    ExecutePonLocally(targetTile);
                    await Task.Delay(500); // 思考出牌
                    SortHand(); // 吃碰后理牌，再决定打哪张
                    TileData tileToDiscard = ChooseTileToDiscard();
                    _hand.Remove(tileToDiscard);
                    _server.SubmitAction(ClientAction.Discard(PlayerId, tileToDiscard));
                }
                else if (actionType == ClientActionType.Chi)
                {
                    ExecuteChiLocally(targetTile, chiCombinations);
                    await Task.Delay(500);
                    SortHand(); // 吃碰后理牌，再决定打哪张
                    TileData tileToDiscard = ChooseTileToDiscard();
                    _hand.Remove(tileToDiscard);
                    _server.SubmitAction(ClientAction.Discard(PlayerId, tileToDiscard));
                }
                else if (actionType == ClientActionType.MingGan)
                {
                    ExecuteMingGanLocally(targetTile);
                    SortHand(); // 杠完理牌
                    // 杠牌后服务器会重新分发岭上牌，所以这里不需要出牌
                }
            }
        }

        public void OnDrawGame()
        {
            Debug.Log($"[AI {PlayerId}] 收到流局广播");
        }

        public void OnPlayerWin(int winnerId, int totalFan, List<string> fanDetails, bool isSelfDraw)
        {
            Debug.Log($"[AI {PlayerId}] 确认玩家 {winnerId} 胡牌，番数：{totalFan}");
        }

        // --- 本地内部逻辑 ---

        private void SortHand()
        {
            _hand.Sort((a, b) =>
            {
                if (a.TileSuit != b.TileSuit) return a.TileSuit.CompareTo(b.TileSuit);
                return a.Value.CompareTo(b.Value);
            });
        }

        private TileData ChooseTileToDiscard()
        {
            if (GameManager.Instance != null && GameManager.Instance.forceAIDiscard && GameManager.Instance.aiCheatDiscards != null && GameManager.Instance.aiCheatDiscards.Count > 0)
            {
                var t = GameManager.Instance.aiCheatDiscards[0];
                TileData cheat = new TileData(t.TileSuit, t.Value, PlayerId);
                Debug.Log($"<color=red>[AI Cheat]</color> AI {PlayerId} 强制打出: {cheat.TileSuit} {cheat.Value}");
                return cheat;
            }

            // 基础策略：优先打出单张的字牌(风/箭)，然后打出单张的老头牌(1,9)
            // 简单实现：随机找一个字牌打，如果没有，随机打
            var winds = _hand.Where(t => t.TileSuit == Suit.Wind || t.TileSuit == Suit.Dragon).ToList();
            if (winds.Count > 0)
            {
                return winds[UnityEngine.Random.Range(0, winds.Count)];
            }
            return _hand[UnityEngine.Random.Range(0, _hand.Count)];
        }

        private void ExecutePonLocally(TileData target)
        {
            var matchingTiles = new List<TileData>();
            for (int i = _hand.Count - 1; i >= 0; i--)
            {
                if (_hand[i].TileSuit == target.TileSuit && _hand[i].Value == target.Value)
                {
                    matchingTiles.Add(_hand[i]);
                    _hand.RemoveAt(i);
                    if (matchingTiles.Count == 2) break;
                }
            }

            if (matchingTiles.Count != 2) return; // 防御

            List<TileData> meldTiles = new List<TileData> { target, matchingTiles[0], matchingTiles[1] };
            // TODO: sourceId 应该由服务器在 OnActionResolved 中提供，暂时用-1
            _melds.Add(new Meld(MeldType.Pon, meldTiles, -1));
        }

        private void ExecuteChiLocally(TileData target, int[] combos)
        {
            if (combos == null || combos.Length != 2) return; // 防御

            var tilesToRemove = new List<TileData>();
            foreach (var val in combos)
            {
                var match = _hand.FirstOrDefault(t => t.TileSuit == target.TileSuit && t.Value == val);
                if (match != null)
                {
                    tilesToRemove.Add(match);
                }
            }

            if (tilesToRemove.Count != 2) return; // 防御

            foreach(var tile in tilesToRemove) _hand.Remove(tile);

            List<TileData> meldTiles = new List<TileData> { target, tilesToRemove[0], tilesToRemove[1] };
            // TODO: sourceId 应该由服务器在 OnActionResolved 中提供，暂时用-1
            _melds.Add(new Meld(MeldType.Chi, meldTiles, -1));
        }

        private void ExecuteMingGanLocally(TileData target)
        {
            var matchingTiles = new List<TileData>();
            for (int i = _hand.Count - 1; i >= 0; i--)
            {
                if (_hand[i].TileSuit == target.TileSuit && _hand[i].Value == target.Value)
                {
                    matchingTiles.Add(_hand[i]);
                    _hand.RemoveAt(i);
                    if (matchingTiles.Count == 3) break;
                }
            }

            if (matchingTiles.Count != 3) return; // 防御

            List<TileData> meldTiles = new List<TileData> { target, matchingTiles[0], matchingTiles[1], matchingTiles[2] };
            // TODO: sourceId 应该由服务器在 OnActionResolved 中提供，暂时用-1
            _melds.Add(new Meld(MeldType.Kan_Exposed, meldTiles, -1));
        }
    }
}
