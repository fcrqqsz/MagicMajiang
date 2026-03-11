using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private GameSession _session;

        // 超时配置 (毫秒)
        public int ActionTimeoutMs { get; set; } = 30000;
        public int ResponseTimeoutMs { get; set; } = 10000;

        // 当前局结果 (供 GameManager 读取)
        public int WinnerId { get; private set; } = -1;
        public int WinFan { get; private set; }
        public bool WinIsSelfDraw { get; private set; }
        public int LoserId { get; private set; } = -1; // 放炮者
        public bool IsDrawGame { get; private set; }

        private TaskCompletionSource<ClientAction> _pendingActionTcs;
        private Dictionary<int, ClientAction> _pendingResponses = new Dictionary<int, ClientAction>();
        private TaskCompletionSource<bool> _responsesTcs;

        // 临时流转控制
        private bool _skipNextDraw = false;
        private TileData _lastDrawnTile; // 记录当前摸牌，供超时自动出牌使用

        // 服务端场面快照 + 取消令牌
        private ServerGameState _gameState;
        private CancellationTokenSource _turnCts;

        // 局结束事件，GameManager 监听此事件驱动多局循环
        public event System.Action OnRoundFinished;

        // 回合切换事件 (供 HUD 倒计时使用)
        public event System.Action<int, float> OnTurnStarted;  // (playerIndex, timeoutSeconds)
        public event System.Action OnTurnEnded;

        public async void StartGame(List<IPlayerClient> clients, List<DeckConfig> configs, GameSession session = null)
        {
            _clients = clients;
            _session = session;
            _currentPlayerIndex = session != null ? session.DealerIndex : 0;

            // 重置局状态
            if (_session != null) _session.ResetRoundState();
            WinnerId = -1;
            WinFan = 0;
            WinIsSelfDraw = false;
            LoserId = -1;
            IsDrawGame = false;

            // 初始化服务端场面快照
            _gameState = new ServerGameState(clients.Count);

            // 洗牌
            DeckManager.Instance.BuildWall(configs);

            // 广播圈风/门风信息
            if (_session != null)
            {
                for (int i = 0; i < _clients.Count; i++)
                {
                    _clients[i].OnRoundStart(
                        _session.TotalRoundsPlayed + 1,
                        _session.PrevalentWind,
                        _session.GetSeatWind(i),
                        _session.DealerIndex
                    );
                }
            }

            // 发牌
            DealStartingHands();

            _isGameActive = true;

            try
            {
                await RunGameLoop();
            }
            catch (TaskCanceledException)
            {
                Debug.Log("[GameServer] 游戏已被强制终止。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameServer] 游戏循环异常: {ex}");
            }
        }

        public void StopGame()
        {
            _isGameActive = false;
            
            // 取消当前的回合等待 CTS
            if (_turnCts != null && !_turnCts.IsCancellationRequested)
            {
                _turnCts.Cancel();
            }

            // 强制完成或者取消正在等待的 TaskCompletionSource
            if (_pendingActionTcs != null && !_pendingActionTcs.Task.IsCompleted)
            {
                _pendingActionTcs.TrySetCanceled();
            }

            if (_responsesTcs != null && !_responsesTcs.Task.IsCompleted)
            {
                _responsesTcs.TrySetCanceled();
            }
        }

        private void DealStartingHands()
        {
            for (int ci = 0; ci < _clients.Count; ci++)
            {
                var client = _clients[ci];
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
                _gameState.InitHand(ci, startingHand);
            }
        }

        /// <summary>
        /// 带超时的 TCS 等待辅助方法。超时时调用 onTimeout 回调并用 fallback 值完成 TCS。
        /// </summary>
        private async Task<T> AwaitWithTimeout<T>(TaskCompletionSource<T> tcs, int timeoutMs, Action onTimeout, Func<T> fallbackFactory)
        {
            if (timeoutMs <= 0)
                return await tcs.Task;

            using (var cts = new CancellationTokenSource())
            {
                var timeoutTask = Task.Delay(timeoutMs, cts.Token);
                var completed = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completed == timeoutTask && !tcs.Task.IsCompleted)
                {
                    onTimeout?.Invoke();
                    tcs.TrySetResult(fallbackFactory());
                }
                else
                {
                    cts.Cancel(); // 正常完成，取消定时器避免泄漏
                }
            }

            return await tcs.Task;
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
                    _lastDrawnTile = DeckManager.Instance.DrawTile();
                    _gameState.AddTile(_currentPlayerIndex, _lastDrawnTile);

                    // 创建 CTS 并设置到当前玩家（在 OnTileDrawn 之前，让客户端拿到 token）
                    _turnCts?.Dispose();
                    _turnCts = new CancellationTokenSource();
                    currentPlayer.TurnCancellationToken = _turnCts.Token;

                    OnTurnStarted?.Invoke(_currentPlayerIndex, ActionTimeoutMs / 1000f);

                    // 这里发送给当前玩家具体牌数据
                    currentPlayer.OnTileDrawn(_lastDrawnTile);

                    // 广播给其他人，某人摸牌了 (不包含具体牌数据)
                    for (int i = 0; i < _clients.Count; i++)
                    {
                        if (i != _currentPlayerIndex)
                        {
                            _clients[i].OnPlayerDrawn(_currentPlayerIndex);
                        }
                    }
                }
                else
                {
                    _lastDrawnTile = null; // 吃碰后没有摸牌

                    // 吃碰后也需要创建 CTS（等待出牌）
                    _turnCts?.Dispose();
                    _turnCts = new CancellationTokenSource();
                    currentPlayer.TurnCancellationToken = _turnCts.Token;

                    OnTurnStarted?.Invoke(_currentPlayerIndex, ActionTimeoutMs / 1000f);
                }
                _skipNextDraw = false;

                // 2. 等待当前玩家出牌或自摸、暗杠（带超时）
                _pendingActionTcs = new TaskCompletionSource<ClientAction>();

                TileData _autoDiscardCache = null;
                ClientAction action = await AwaitWithTimeout(
                    _pendingActionTcs,
                    ActionTimeoutMs,
                    onTimeout: () =>
                    {
                        _autoDiscardCache = _gameState.GetAutoDiscardTile(_currentPlayerIndex, _lastDrawnTile);
                        Debug.LogWarning($"[GameServer] 玩家 {_currentPlayerIndex} 主回合超时，自动出牌: {_autoDiscardCache}");
                        _turnCts.Cancel();
                        currentPlayer.OnTimeout(_autoDiscardCache);
                    },
                    fallbackFactory: () => ClientAction.Discard(_currentPlayerIndex, _autoDiscardCache)
                );
                _pendingActionTcs = null;

                if (action.ActionType == ClientActionType.Hu)
                {
                    // 自摸胡
                    HandlePlayerWin(action, true);
                    break;
                }
                else if (action.ActionType == ClientActionType.AnGan || action.ActionType == ClientActionType.JiaGang)
                {
                    // 杠牌，更新快照并广播动作
                    _gameState.ApplyMeld(_currentPlayerIndex, action.ActionType, action.TargetTile, action.ChiCombinations);
                    BroadcastAction(action);
                    continue; // 直接重新循环
                }
                else if (action.ActionType == ClientActionType.Discard)
                {
                    TileData discardedTile = action.TargetTile;
                    _gameState.RemoveTile(action.PlayerId, discardedTile);

                    // 3. 广播他人打牌，并收集响应
                    _pendingResponses.Clear();
                    _responsesTcs = new TaskCompletionSource<bool>();

                    OnTurnEnded?.Invoke();

                    // 创建响应阶段 CTS，设置到所有非当前玩家
                    _turnCts?.Dispose();
                    _turnCts = new CancellationTokenSource();
                    for (int i = 0; i < _clients.Count; i++)
                    {
                        if (i != _currentPlayerIndex)
                            _clients[i].TurnCancellationToken = _turnCts.Token;
                    }

                    // 通知其他玩家
                    for (int i = 0; i < _clients.Count; i++)
                    {
                        if (i != _currentPlayerIndex)
                        {
                            _clients[i].OnOtherPlayerDiscarded(_currentPlayerIndex, discardedTile);
                        }
                    }

                    // 等待所有其他3家回复（带超时）
                    await AwaitWithTimeout(
                        _responsesTcs,
                        ResponseTimeoutMs,
                        onTimeout: () =>
                        {
                            Debug.LogWarning("[GameServer] 响应收集超时，为未回复玩家自动填充 Skip");
                            _turnCts.Cancel();
                            for (int i = 0; i < _clients.Count; i++)
                            {
                                if (i != _currentPlayerIndex && !_pendingResponses.ContainsKey(i))
                                {
                                    _clients[i].OnTimeout(null);
                                    _pendingResponses[i] = ClientAction.Skip(i);
                                }
                            }
                        },
                        fallbackFactory: () => true
                    );
                    _responsesTcs = null;

                    // 4. 裁决响应优先级 (胡 > 碰/杠 > 吃)
                    ClientAction resolvedAction = ResolveResponses();

                    if (resolvedAction != null && resolvedAction.ActionType != ClientActionType.Skip)
                    {
                        if (resolvedAction.ActionType == ClientActionType.Hu)
                        {
                            HandlePlayerWin(resolvedAction, false, _currentPlayerIndex);
                            break;
                        }
                        else
                        {
                            // 执行碰/杠/吃，更新快照
                            _gameState.ApplyMeld(resolvedAction.PlayerId, resolvedAction.ActionType,
                                resolvedAction.TargetTile, resolvedAction.ChiCombinations);
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
                OnTurnEnded?.Invoke();
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

        private void HandlePlayerWin(ClientAction winAction, bool isSelfDraw, int loserId = -1)
        {
            _isGameActive = false;
            OnTurnEnded?.Invoke();
            WinnerId = winAction.PlayerId;
            WinFan = winAction.TotalFan;
            WinIsSelfDraw = isSelfDraw;
            LoserId = loserId;

            // 计分
            if (_session != null)
            {
                _session.ApplyScore(winAction.PlayerId, winAction.TotalFan, isSelfDraw, loserId);
            }

            foreach (var client in _clients)
            {
                client.OnPlayerWin(winAction.PlayerId, winAction.TotalFan, winAction.FanDetails, isSelfDraw);
            }

            OnRoundFinished?.Invoke();
        }

        private void HandleDrawGame()
        {
            _isGameActive = false;
            OnTurnEnded?.Invoke();
            IsDrawGame = true;

            foreach (var client in _clients)
            {
                client.OnDrawGame();
            }

            OnRoundFinished?.Invoke();
        }
    }
}
