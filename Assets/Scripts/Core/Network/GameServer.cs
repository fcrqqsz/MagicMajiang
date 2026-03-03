using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using MahjongGame.Core.Agents;
using MahjongGame.Systems; // 需要访问 DeckManager 或重构 DeckManager

namespace MahjongGame.Core.Network
{
    public interface IServer
    {
        void SubmitAction(ClientAction action);
    }

    /// <summary>
    /// 瘦服务端：只负责发牌、接收请求、仲裁响应、广播事件，不执行麻将逻辑
    /// </summary>
    public class GameServer : IServer
    {
        private List<IPlayerClient> _clients;
        private int _currentPlayerIndex = 0;
        private bool _isGameActive = false;

        private TaskCompletionSource<ClientAction> _pendingActionTcs;
        private Dictionary<int, ClientAction> _pendingResponses = new Dictionary<int, ClientAction>();
        private TaskCompletionSource<bool> _responsesTcs;

        // 临时流转控制
        private bool _skipNextDraw = false;

        public async void StartGame(List<IPlayerClient> clients, List<DeckConfig> configs)
        {
            _clients = clients;
            _currentPlayerIndex = 0;

            // 洗牌
            DeckManager.Instance.BuildWall(configs);

            // 发牌
            DealStartingHands();

            _isGameActive = true;

            try
            {
                await RunGameLoop();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameServer] 游戏循环异常: {ex}");
            }
        }

        private void DealStartingHands()
        {
            foreach (var client in _clients)
            {
                List<TileData> startingHand = new List<TileData>();
                
                bool useDebug = GameManager.Instance != null && GameManager.Instance.useDebugHand && client is LocalPlayerClient;

                if (useDebug)
                {
                    var debugHand = GameManager.Instance.debugHand;
                    for (int i = 0; i < Mathf.Min(debugHand.Count, 13); i++)
                    {
                        var t = debugHand[i];
                        startingHand.Add(new TileData(t.TileSuit, t.Value, t.OriginalOwnerID));
                    }
                    
                    int remaining = 13 - startingHand.Count;
                    for (int i = 0; i < remaining; i++)
                    {
                        startingHand.Add(DeckManager.Instance.DrawTile());
                    }
                }
                else
                {
                    for (int i = 0; i < 13; i++)
                    {
                        startingHand.Add(DeckManager.Instance.DrawTile());
                    }
                }
                
                client.OnGameStart(startingHand);
            }
        }

        private async Task RunGameLoop()
        {
            while (_isGameActive)
            {
                if (DeckManager.Instance.RemainingCount == 0)
                {
                    HandleDrawGame();
                    break;
                }

                var currentPlayer = _clients[_currentPlayerIndex];

                // 1. 摸牌阶段
                if (!_skipNextDraw)
                {
                    TileData drawnTile = DeckManager.Instance.DrawTile();
                    // 这里发送给当前玩家具体牌数据
                    currentPlayer.OnTileDrawn(drawnTile);

                    // 广播给其他人，某人摸牌了 (不包含具体牌数据)
                    for (int i = 0; i < _clients.Count; i++)
                    {
                        if (i != _currentPlayerIndex)
                        {
                            _clients[i].OnPlayerDrawn(_currentPlayerIndex);
                        }
                    }
                }
                _skipNextDraw = false;

                // 2. 等待当前玩家出牌或自摸、暗杠
                _pendingActionTcs = new TaskCompletionSource<ClientAction>();
                
                // 【等待客户端调用 SubmitAction】
                ClientAction action = await _pendingActionTcs.Task;
                _pendingActionTcs = null;

                if (action.ActionType == ClientActionType.Hu)
                {
                    // 自摸胡
                    HandlePlayerWin(action, true);
                    break;
                }
                else if (action.ActionType == ClientActionType.AnGan || action.ActionType == ClientActionType.JiaGang)
                {
                    // 杠牌，广播动作，不下发牌直接重置回合（当前玩家继续摸岭上牌）
                    BroadcastAction(action);
                    continue; // 直接重新循环
                }
                else if (action.ActionType == ClientActionType.Discard)
                {
                    TileData discardedTile = action.TargetTile;

                    // 3. 广播他人打牌，并收集响应
                    _pendingResponses.Clear();
                    _responsesTcs = new TaskCompletionSource<bool>();
                    
                    // 通知其他玩家
                    for (int i = 0; i < _clients.Count; i++)
                    {
                        if (i != _currentPlayerIndex)
                        {
                            _clients[i].OnOtherPlayerDiscarded(_currentPlayerIndex, discardedTile);
                        }
                    }

                    // 等待所有其他3家回复（如果有AI可能瞬间完成，本地玩家会等UI）
                    await _responsesTcs.Task;
                    _responsesTcs = null;

                    // 4. 裁决响应优先级 (胡 > 碰/杠 > 吃)
                    ClientAction resolvedAction = ResolveResponses();

                    if (resolvedAction != null && resolvedAction.ActionType != ClientActionType.Skip)
                    {
                        if (resolvedAction.ActionType == ClientActionType.Hu)
                        {
                            HandlePlayerWin(resolvedAction, false);
                            break;
                        }
                        else
                        {
                            // 执行碰/杠/吃
                            BroadcastAction(resolvedAction);
                            
                            // 跳转回合
                            _currentPlayerIndex = resolvedAction.PlayerId;
                            
                            if (resolvedAction.ActionType == ClientActionType.MingGan)
                            {
                                // 杠牌需要摸岭上开花，不跳过摸牌
                                _skipNextDraw = false; 
                            }
                            else
                            {
                                // 吃/碰后直接出牌，跳过摸牌
                                _skipNextDraw = true;
                            }
                            continue; // 跳转到被截牌的玩家开始新的大循环
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[GameServer] 异常的主回合动作：{action.ActionType}");
                }

                // 5. 正常流转到下一家
                _currentPlayerIndex = (_currentPlayerIndex + 1) % _clients.Count;
            }
        }

        private ClientAction ResolveResponses()
        {
            var validResponses = _pendingResponses.Values.Where(r => r.ActionType != ClientActionType.Skip).ToList();
            if (validResponses.Count == 0) return null;

            // 检查一炮多响或截胡 (简化处理：优先级为位置顺位，或者优先级固定)
            var huResponse = validResponses.FirstOrDefault(r => r.ActionType == ClientActionType.Hu);
            if (huResponse != null) return huResponse;

            var ponGanResponse = validResponses.FirstOrDefault(r => r.ActionType == ClientActionType.Pon || r.ActionType == ClientActionType.MingGan);
            if (ponGanResponse != null) return ponGanResponse;

            var chiResponse = validResponses.FirstOrDefault(r => r.ActionType == ClientActionType.Chi);
            if (chiResponse != null) return chiResponse;

            return null;
        }

        /// <summary>
        /// 供客户端调用，提交决策
        /// </summary>
        public void SubmitAction(ClientAction action)
        {
            if (!_isGameActive) return;

            // 如果当前在等待主玩家出牌
            if (_pendingActionTcs != null && action.PlayerId == _currentPlayerIndex)
            {
                _pendingActionTcs.TrySetResult(action);
            }
            // 如果在等待其他人响应
            else if (_responsesTcs != null && action.PlayerId != _currentPlayerIndex)
            {
                _pendingResponses[action.PlayerId] = action;
                
                // 检查是否收集齐了 3 家的响应
                if (_pendingResponses.Count == _clients.Count - 1)
                {
                    _responsesTcs.TrySetResult(true);
                }
            }
        }

        private void BroadcastAction(ClientAction action)
        {
            foreach (var client in _clients)
            {
                client.OnActionResolved(action.PlayerId, action.ActionType, action.TargetTile, action.ChiCombinations);
            }
        }

        private void HandlePlayerWin(ClientAction winAction, bool isSelfDraw)
        {
            _isGameActive = false;
            foreach (var client in _clients)
            {
                client.OnPlayerWin(winAction.PlayerId, winAction.TotalFan, winAction.FanDetails, isSelfDraw);
            }
        }

        private void HandleDrawGame()
        {
            _isGameActive = false;
            foreach (var client in _clients)
            {
                client.OnDrawGame();
            }
        }
    }
}
