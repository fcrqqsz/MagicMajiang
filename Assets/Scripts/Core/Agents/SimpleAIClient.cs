using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core;

namespace MahjongGame.Core.Agents
{
    /// <summary>
    /// 规则化AI胖客户端。自己在本地计算胡牌、吃碰权限和打牌策略。
    /// </summary>
    public class SimpleAIClient : IPlayerClient
    {
        public int PlayerId { get; private set; }
        public CancellationToken TurnCancellationToken { get; set; }

        private IServer _server;
        
        // AI 在本地维护自己的状态
        private List<TileData> _hand = new List<TileData>();
        private List<Meld> _melds = new List<Meld>();

        // 风位信息
        private WindDirection _roundWind = WindDirection.East;
        private WindDirection _seatWind = WindDirection.East;
        private ScoringOptions _scoringOptions = new ScoringOptions();

        public SimpleAIClient(int playerId, IServer server)
        {
            PlayerId = playerId;
            _server = server;
        }

        public void SetServer(IServer server)
        {
            _server = server;
        }

        /// <summary>Seeds a temporary controller from the server's authoritative state at a decision boundary.</summary>
        public void RestoreAuthoritativeState(List<TileData> hand, List<Meld> melds, ScoringOptions scoringOptions)
        {
            _hand = (hand ?? new List<TileData>()).Select(CloneTile).ToList();
            _melds = (melds ?? new List<Meld>()).Where(meld => meld != null).Select(CloneMeld).ToList();
            OnTalentInfo(scoringOptions);
            SortHand();
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

        public void OnTurnWithoutDraw()
        {
            // 吃、碰后的 AI 出牌仍由 OnActionResolved 中的策略处理。
        }

        public void OnWallCountChanged(int remainingCount)
        {
            // AI does not render wall state.
        }

        public async void OnTileDrawn(TileData drawnTile, bool isKongReplacementDraw)
        {
            var ct = TurnCancellationToken;
            try
            {
                _hand.Add(drawnTile);
                SortHand();

                await Task.Delay(500, ct);
                TryUseActiveTalent();

                var actions = ActionValidator.CheckSelfActions(
                    _hand,
                    _melds,
                    drawnTile,
                    _scoringOptions,
                    _roundWind,
                    _seatWind,
                    isKongWin: isKongReplacementDraw);
                if (actions.HasAction)
                {
                    if (actions.CanHu)
                    {
                        int totalFan;
                        List<string> fanDetails;
                        bool canWin = MahjongLogic.CheckWinWithFan(
                            _hand,
                            _melds,
                            drawnTile,
                            true,
                            out totalFan,
                            out fanDetails,
                            _roundWind,
                            _seatWind,
                            _scoringOptions,
                            isKongWin: isKongReplacementDraw);

                        if (canWin)
                        {
                            var action = new ClientAction(PlayerId, ClientActionType.Hu, drawnTile);
                            action.SetHuDetails(totalFan, fanDetails);
                            _server.SubmitAction(action);
                            return;
                        }
                    }
                }

                TileData tileToDiscard = ChooseTileToDiscard();
                _hand.Remove(tileToDiscard);

                Debug.Log($"[AI {PlayerId}] 决定打出: {tileToDiscard}");
                _server.SubmitAction(ClientAction.Discard(PlayerId, tileToDiscard));
            }
            catch (OperationCanceledException)
            {
                // 超时 — OnTimeout 会处理手牌同步
            }
        }

        public async void OnOtherPlayerDiscarded(int discarderId, TileData discardedTile)
        {
            if (!Network.ResponseActionPolicy.CanRespondToDiscard(PlayerId, discarderId)) return;

            var ct = TurnCancellationToken;
            try
            {
                await Task.Delay(UnityEngine.Random.Range(200, 600), ct);

                bool isNextPlayer = (discarderId + 1) % 4 == PlayerId;

                var actions = ActionValidator.CheckActions(_hand, _melds, discardedTile, isNextPlayer, _scoringOptions, _roundWind, _seatWind);

                if (actions.HasAction)
                {
                    if (actions.CanHu)
                    {
                        int totalFan;
                        List<string> fanDetails;
                        bool canWin = MahjongLogic.CheckWinWithFan(_hand, _melds, discardedTile, false, out totalFan, out fanDetails, _roundWind, _seatWind, _scoringOptions);
                        if (canWin)
                        {
                            var action = new ClientAction(PlayerId, ClientActionType.Hu, discardedTile);
                            action.SetHuDetails(totalFan, fanDetails);
                            _server.SubmitAction(action);
                            return;
                        }
                    }

                    if (actions.CanPon && UnityEngine.Random.value > 0.5f)
                    {
                        _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Pon, discardedTile));
                        return;
                    }
                    else if (isNextPlayer && (actions.CanChiLeft || actions.CanChiMiddle || actions.CanChiRight) && UnityEngine.Random.value > 0.5f)
                    {
                        var chiOptions = ActionValidator.GetChiCombinations(_hand, discardedTile);
                        if (chiOptions.Count > 0)
                        {
                            _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Chi, discardedTile, chiOptions[0]));
                            return;
                        }
                    }
                }

                _server.SubmitAction(ClientAction.Skip(PlayerId));
            }
            catch (OperationCanceledException)
            {
                // 响应超时 — 服务端自动填充 Skip
            }
        }

        public async void OnAddedKongDeclared(int declaringPlayerId, TileData targetTile)
        {
            if (!Network.ResponseActionPolicy.CanRobAddedKong(PlayerId, declaringPlayerId)) return;

            var ct = TurnCancellationToken;
            try
            {
                await Task.Delay(UnityEngine.Random.Range(200, 600), ct);
                bool canWin = MahjongLogic.CheckWinWithFan(_hand, _melds, targetTile, false,
                    out int totalFan, out List<string> fanDetails, _roundWind, _seatWind, _scoringOptions, true);
                if (canWin)
                {
                    var action = new ClientAction(PlayerId, ClientActionType.Hu, targetTile);
                    action.SetHuDetails(totalFan, fanDetails);
                    _server.SubmitAction(action);
                    return;
                }

                _server.SubmitAction(ClientAction.Skip(PlayerId));
            }
            catch (OperationCanceledException)
            {
                // 响应超时 — 服务端自动填充 Skip
            }
        }

        public async void OnActionResolved(int actionPlayerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations)
        {
            if (actionPlayerId == PlayerId)
            {
                var ct = TurnCancellationToken;
                try
                {
                    if (actionType == ClientActionType.Pon)
                    {
                        ExecutePonLocally(targetTile);
                        await Task.Delay(500, ct);
                        SortHand();
                        TryUseActiveTalent();
                        TileData tileToDiscard = ChooseTileToDiscard();
                        _hand.Remove(tileToDiscard);
                        _server.SubmitAction(ClientAction.Discard(PlayerId, tileToDiscard));
                    }
                    else if (actionType == ClientActionType.Chi)
                    {
                        ExecuteChiLocally(targetTile, chiCombinations);
                        await Task.Delay(500, ct);
                        SortHand();
                        TryUseActiveTalent();
                        TileData tileToDiscard = ChooseTileToDiscard();
                        _hand.Remove(tileToDiscard);
                        _server.SubmitAction(ClientAction.Discard(PlayerId, tileToDiscard));
                    }
                    else if (actionType == ClientActionType.MingGan)
                    {
                        ExecuteMingGanLocally(targetTile);
                        SortHand();
                    }
                }
                catch (OperationCanceledException)
                {
                    // 超时 — OnTimeout 会处理手牌同步
                }
            }
        }

        public void OnDrawGame()
        {
            Debug.Log($"[AI {PlayerId}] 收到流局广播");
        }

        public void OnPlayerWin(int winnerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
            WinKind winKind, int loserId, WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown)
        {
            Debug.Log($"[AI {PlayerId}] 确认玩家 {winnerId} 胡牌，番数：{totalFan}");
        }

        public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex)
        {
            _roundWind = prevalentWind;
            _seatWind = seatWind;
            Debug.Log($"[AI {PlayerId}] 第{roundNumber}局开始 - 圈风:{prevalentWind} 门风:{seatWind}");
        }

        public void OnSessionEnd(int[] finalScores)
        {
            Debug.Log($"[AI {PlayerId}] 对战结束");
        }

        public void OnTimeout(TileData autoDiscardedTile)
        {
            if (autoDiscardedTile != null)
            {
                var match = _hand.FirstOrDefault(
                    t => t.TileSuit == autoDiscardedTile.TileSuit
                      && t.Value == autoDiscardedTile.Value);
                if (match != null) _hand.Remove(match);
            }
        }

        public void OnTalentInfo(ScoringOptions scoringOptions)
        {
            _scoringOptions = scoringOptions == null
                ? new ScoringOptions()
                : new ScoringOptions
                {
                    BonusFan = scoringOptions.BonusFan,
                    RelaxedPureStraight = scoringOptions.RelaxedPureStraight
                };
        }

        public void OnPeekWallTiles(List<TileData> topTiles)
        {
            // AI 暂不使用窥探
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

        private void TryUseActiveTalent()
        {
            if (_server is GameServer gameServer)
                AiTalentDecisionPolicy.TrySubmitActiveAction(gameServer, PlayerId);
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
            return new Meld(meld.Type, (meld.Tiles ?? new List<TileData>()).Select(CloneTile).Where(tile => tile != null).ToList(),
                meld.SourcePlayerID, meld.IsConcealed);
        }

        private TileData ChooseTileToDiscard()
        {
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
