using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Interfaces;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    public class GameServerOptions
    {
        public int ActionTimeoutMs = 30000;
        public int ResponseTimeoutMs = 10000;
        public bool UseDebugHand = false;
        public List<TileData> DebugHand = new List<TileData>();
        public NetworkDecisionTracker DecisionTracker;
    }
    public interface IServer
    {
        void SubmitAction(ClientAction action);
    }

    /// <summary>
    /// 瘦服务端：只负责发牌、接收请求、仲裁响应、广播事件，不执行麻将逻辑
    /// </summary>
    public class GameServer : IServer
    {
        private readonly IWallService _wallService;
        private readonly TalentMatchRuntime _talentRuntime;
        private readonly GameRoundCompletionLatch _roundCompletionLatch = new GameRoundCompletionLatch();
        private GameServerOptions _options;
        private NetworkDecisionTracker _decisionTracker;

        public GameServer(
            IWallService wallService,
            TalentMatchRuntime talentRuntime) : this(wallService, talentRuntime, null)
        {
        }

        public GameServer(
            IWallService wallService,
            TalentMatchRuntime talentRuntime,
            GameServerOptions options)
        {
            _wallService = wallService ?? throw new ArgumentNullException(nameof(wallService));
            _talentRuntime = talentRuntime ?? throw new ArgumentNullException(nameof(talentRuntime));
            Configure(options);
        }

        public void Configure(GameServerOptions options)
        {
            _options = options ?? new GameServerOptions();
            if (_options.DebugHand == null)
                _options.DebugHand = new List<TileData>();

            ActionTimeoutMs = _options.ActionTimeoutMs;
            ResponseTimeoutMs = _options.ResponseTimeoutMs;
            _decisionTracker = _options.DecisionTracker ?? new NetworkDecisionTracker();
        }

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
        public TalentFanResolution WinFanResolution { get; private set; } = new TalentFanResolution();
        public TalentFanBreakdownMessage WinTalentFanBreakdown { get; private set; }
        public List<string> WinFanDetails { get; private set; } = new List<string>();
        public bool WinIsSelfDraw { get; private set; }
        public WinKind WinResultKind { get; private set; } = WinKind.Unknown;
        public WinningHandSnapshot WinningHandSnapshot { get; private set; }
        public int LoserId { get; private set; } = -1; // 放炮者
        public bool IsDrawGame { get; private set; }
        public NetworkDecisionContext ActiveDecision => _decisionTracker?.Active;

        private TaskCompletionSource<ClientAction> _pendingActionTcs;
        private Dictionary<int, ClientAction> _pendingResponses = new Dictionary<int, ClientAction>();
        private TaskCompletionSource<bool> _responsesTcs;

        // 临时流转控制
        private bool _skipNextDraw = false;
        private bool _pendingKongReplacementDraw;
        private bool _currentDrawIsKongReplacement;
        private TileData _lastDrawnTile; // 记录当前摸牌，供超时自动出牌使用

        /// <summary>The draw that opened the current main decision, if this main turn drew a tile.</summary>
        public TileData LastDrawnTile => _lastDrawnTile;

        // 服务端场面快照 + 取消令牌
        private ServerGameState _gameState;
        private AuthoritativePublicTileTransition _publicTileTransition;
        private CancellationTokenSource _turnCts;

        private Dictionary<int, DeckConfig> _deckConfigs;

        // 服务端验证用
        private Dictionary<int, ScoringOptions> _scoringOptions = new Dictionary<int, ScoringOptions>();
        private readonly int[] _drawCounts = new int[4];
        private readonly Dictionary<int, List<TileData>> _privateWallPreviewTiles =
            new Dictionary<int, List<TileData>>();
        private TileData _lastDiscardedTile; // 响应阶段：被打出的那张牌
        private TileData _pendingRobKongTile; // 加杠声明阶段：尚未落副露、可被抢胡的牌

        // 局结束事件，GameManager 监听此事件驱动多局循环
        public event System.Action<GameRoundCompletion> OnRoundFinished;
        public event System.Action OnTalentEventsAvailable;

        // 回合切换事件 (供 HUD 倒计时使用)
        public event System.Action<int, float> OnTurnStarted;  // (playerIndex, timeoutSeconds)
        public event System.Action OnTurnEnded;

        public int RemainingWallCount => _wallService?.RemainingCount ?? 0;

        public List<TileData> GetHandSnapshot(int seatIndex) => _gameState?.GetHand(seatIndex) ?? new List<TileData>();
        public List<Meld> GetMeldSnapshot(int seatIndex) => _gameState?.GetMelds(seatIndex) ?? new List<Meld>();
        public List<TileData> GetRiverSnapshot(int seatIndex) => _gameState?.GetRiver(seatIndex) ?? new List<TileData>();
        public int[] GetDrawCountsSnapshot() => (int[])_drawCounts.Clone();

        public ScoringOptions GetScoringOptionsSnapshot(int seatIndex)
        {
            if (!_scoringOptions.TryGetValue(seatIndex, out var options)) return new ScoringOptions();
            return new ScoringOptions
            {
                BonusFan = options.BonusFan,
                MinimumFan = options.MinimumFan,
                RelaxedPureStraight = options.RelaxedPureStraight
            };
        }

        public List<TileData> GetPeekWallSnapshot(int seatIndex)
        {
            return _privateWallPreviewTiles.TryGetValue(seatIndex, out var tiles)
                ? CloneTiles(tiles)
                : new List<TileData>();
        }

        public async void StartGame(
            List<IPlayerClient> clients,
            List<DeckConfig> configs,
            GameSession session)
        {
            if (clients == null) throw new ArgumentNullException(nameof(clients));
            if (configs == null) throw new ArgumentNullException(nameof(configs));
            if (session == null) throw new ArgumentNullException(nameof(session));
            _clients = clients;
            _session = session;
            _currentPlayerIndex = session != null ? session.DealerIndex : 0;

            // 重置局状态
            if (_session != null) _session.ResetRoundState();
            WinnerId = -1;
            WinFan = 0;
            WinFanResolution = new TalentFanResolution();
            WinTalentFanBreakdown = null;
            WinFanDetails = new List<string>();
            WinIsSelfDraw = false;
            WinResultKind = WinKind.Unknown;
            WinningHandSnapshot = null;
            LoserId = -1;
            IsDrawGame = false;
            _pendingKongReplacementDraw = false;
            _currentDrawIsKongReplacement = false;
            _privateWallPreviewTiles.Clear();
            Array.Clear(_drawCounts, 0, _drawCounts.Length);

            // 缓存牌库配置
            _deckConfigs = new Dictionary<int, DeckConfig>();
            for (int i = 0; i < configs.Count; i++)
                _deckConfigs[i] = configs[i];

            // 初始化服务端场面快照
            _gameState = new ServerGameState(clients.Count);
            _publicTileTransition = new AuthoritativePublicTileTransition(
                _gameState,
                _talentRuntime,
                _session);

            try
            {
                _talentRuntime.BeginRound(new TalentRoundContext(_session));

                GameRoundSetupSequence.BuildShuffleDealAndCapturePeek(
                    buildWall: () => _wallService.BuildWall(configs),
                    applyWallTalents: () => _talentRuntime.ApplyWallBuilding(new TalentWallContext(
                        _session,
                        _wallService.GetWallTiles(),
                        _gameState,
                        _deckConfigs)),
                    shuffleWall: _wallService.ShuffleWall,
                    dealStartingHands: DealStartingHands,
                    completeInitialHands: () => _talentRuntime.CompleteInitialHands(
                        new TalentInitialHandsContext(_session, _gameState, _deckConfigs)),
                    publishStartingHands: PublishStartingHands,
                    capturePeek: () => _talentRuntime.ResolvePostShuffle(new TalentPostShuffleContext(
                        _session,
                        _wallService.GetWallTiles())));

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

                // 构建并缓存各玩家天赋加成信息（服务端验证 + 通知客户端）
                _scoringOptions.Clear();
                for (int i = 0; i < _clients.Count; i++)
                {
                    ScoringOptions options = _talentRuntime.BuildScoringOptions(
                        new TalentScoringContext(_session, i));
                    _scoringOptions[i] = options;
                    _clients[i].OnTalentInfo(options);
                }

                BroadcastWallCount();

                SendPrivatePeekResults();

                _isGameActive = true;

                await RunGameLoop();
            }
            catch (TaskCanceledException)
            {
                Debug.Log("[GameServer] 游戏已被强制终止。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameServer] 游戏循环异常: {ex}");
                AbortRound(ex);
            }
        }

        public void StopGame()
        {
            _isGameActive = false;
            _pendingKongReplacementDraw = false;
            _currentDrawIsKongReplacement = false;
            CloseActiveDecision();
            
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

                bool useDebug = _options.UseDebugHand && client.PlayerId == 0;

                if (useDebug)
                {
                    var debugHand = _options.DebugHand;
                    for (int i = 0; i < Mathf.Min(debugHand.Count, 13); i++)
                    {
                        var t = debugHand[i];
                        startingHand.Add(new TileData(t.TileSuit, t.Value, t.OriginalOwnerID));
                    }

                    int remaining = 13 - startingHand.Count;
                    for (int i = 0; i < remaining; i++)
                    {
                        startingHand.Add(_wallService.DrawTile());
                    }
                }
                else
                {
                    for (int i = 0; i < 13; i++)
                    {
                        startingHand.Add(_wallService.DrawTile());
                    }
                }

                _gameState.InitHand(ci, startingHand);
            }
        }

        private void PublishStartingHands()
        {
            for (int seatIndex = 0; seatIndex < _clients.Count; seatIndex++)
                _clients[seatIndex].OnGameStart(_gameState.GetHand(seatIndex));
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
                if (_wallService.RemainingCount == 0)
                {
                    HandleDrawGame();
                    break;
                }

                var currentPlayer = _clients[_currentPlayerIndex];
                _pendingActionTcs = new TaskCompletionSource<ClientAction>();
                _currentDrawIsKongReplacement = !_skipNextDraw && _pendingKongReplacementDraw;
                if (!_skipNextDraw) _pendingKongReplacementDraw = false;
                var mainDecision = _decisionTracker.OpenMainTurn(
                    _currentPlayerIndex,
                    GetDeadlineUnixMilliseconds(ActionTimeoutMs),
                    _currentDrawIsKongReplacement);
                _talentRuntime.OpenMainDecision(_currentPlayerIndex, mainDecision.DecisionId);
                SetRemoteDecision(currentPlayer, mainDecision);

                // 1. 摸牌阶段
                if (!_skipNextDraw)
                {
                    _lastDrawnTile = _wallService.DrawTile();
                    if (_currentPlayerIndex >= 0 && _currentPlayerIndex < _drawCounts.Length)
                        _drawCounts[_currentPlayerIndex]++;
                    BroadcastWallCount();
                    _lastDrawnTile = _talentRuntime.ApplyDraw(
                        new TalentDrawContext(_session, _currentPlayerIndex, _gameState, _deckConfigs),
                        _lastDrawnTile);
                    _gameState.AddTile(_currentPlayerIndex, _lastDrawnTile);

                    // 创建 CTS 并设置到当前玩家（在 OnTileDrawn 之前，让客户端拿到 token）
                    _turnCts?.Dispose();
                    _turnCts = new CancellationTokenSource();
                    currentPlayer.TurnCancellationToken = _turnCts.Token;

                    OnTurnStarted?.Invoke(_currentPlayerIndex, ActionTimeoutMs / 1000f);

                    // 这里发送给当前玩家具体牌数据
                    currentPlayer.OnTileDrawn(_lastDrawnTile, _currentDrawIsKongReplacement);

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
                    currentPlayer.OnTurnWithoutDraw();
                }
                _skipNextDraw = false;
                OnTalentEventsAvailable?.Invoke();

                // 2. 等待当前玩家出牌或自摸、暗杠（带超时）
                TileData _autoDiscardCache = null;
                bool mainActionWasAutomatic = false;
                ClientAction action = await AwaitWithTimeout(
                    _pendingActionTcs,
                    ActionTimeoutMs,
                    onTimeout: () =>
                    {
                        mainActionWasAutomatic = true;
                        _autoDiscardCache = _gameState.GetAutoDiscardTile(_currentPlayerIndex, _lastDrawnTile);
                        Debug.LogWarning($"[GameServer] 玩家 {_currentPlayerIndex} 主回合超时，自动出牌: {_autoDiscardCache}");
                        _turnCts.Cancel();
                        currentPlayer.OnTimeout(_autoDiscardCache);
                    },
                    fallbackFactory: () => ClientAction.Discard(_currentPlayerIndex, _autoDiscardCache)
                );
                _pendingActionTcs = null;
                CloseDecision(mainDecision.DecisionId);

                if (action.ActionType == ClientActionType.Hu)
                {
                    // 自摸胡
                    HandlePlayerWin(
                        action,
                        true,
                        decisionId: mainDecision.DecisionId);
                    break;
                }
                else if (action.ActionType == ClientActionType.AnGan)
                {
                    // 暗杠不能被抢胡，立即生效。
                    if (!_publicTileTransition.TryCommitConcealedKong(
                            _currentPlayerIndex,
                            action.TargetTile,
                            action.ChiCombinations,
                            BroadcastAction,
                            out ClientAction committedConcealedKong))
                    {
                        Debug.LogError($"[GameServer] Could not commit concealed kong for player {_currentPlayerIndex}.");
                        continue;
                    }
                    CommitTalentAction(
                        mainDecision.DecisionId,
                        committedConcealedKong,
                        sourceSeatIndex: null,
                        wasAutomatic: mainActionWasAutomatic);
                    _pendingKongReplacementDraw = true;
                    OnTalentEventsAvailable?.Invoke();
                    continue; // 直接重新循环
                }
                else if (action.ActionType == ClientActionType.JiaGang)
                {
                    // 加杠先公开声明，但在抢杠窗口关闭前不能修改权威副露。
                    if (!_publicTileTransition.TryPrepareAddedKong(
                            _currentPlayerIndex,
                            action.TargetTile,
                            out TileData authoritativeAddedKongTile))
                    {
                        Debug.LogError($"[GameServer] Could not resolve added kong declaration for player {_currentPlayerIndex}.");
                        continue;
                    }
                    ClientAction declaration = new ClientAction(
                        action.PlayerId,
                        action.ActionType,
                        authoritativeAddedKongTile,
                        action.ChiCombinations);
                    ResolvedResponseAction robKongResolution = await CollectRobKongResponses(declaration);
                    ClientAction robKongWin = robKongResolution.Action;
                    bool wasRobbed = robKongWin != null && robKongWin.ActionType == ClientActionType.Hu;
                    if (wasRobbed)
                    {
                        HandlePlayerWin(
                            robKongWin,
                            false,
                            _currentPlayerIndex,
                            authoritativeAddedKongTile,
                            true,
                            robKongResolution.DecisionId);
                        break;
                    }

                    // 所有人过或超时：此时才真正将碰升级为加杠，然后进入岭上补牌。
                    if (!_publicTileTransition.TryCommitAddedKong(
                            _currentPlayerIndex,
                            authoritativeAddedKongTile,
                            action.ChiCombinations,
                            BroadcastAction,
                            out ClientAction committedAddedKong))
                    {
                        Debug.LogError($"[GameServer] Could not commit added kong for player {_currentPlayerIndex}.");
                        continue;
                    }
                    CommitTalentAction(
                        mainDecision.DecisionId,
                        committedAddedKong,
                        sourceSeatIndex: null,
                        wasAutomatic: mainActionWasAutomatic);
                    _pendingKongReplacementDraw = true;
                    continue;
                }
                else if (action.ActionType == ClientActionType.Discard)
                {
                    TileData discardedTile = _gameState.GetHand(action.PlayerId)
                        .FirstOrDefault(tile => tile.TileSuit == action.TargetTile.TileSuit
                                                && tile.Value == action.TargetTile.Value);
                    _gameState.RemoveTile(action.PlayerId, discardedTile);
                    discardedTile = _talentRuntime.ApplyDiscard(
                        new TalentDiscardContext(_session, action.PlayerId),
                        discardedTile);
                    _gameState.RecordDiscard(action.PlayerId, discardedTile);
                    CommitTalentAction(
                        mainDecision.DecisionId,
                        ClientAction.Discard(action.PlayerId, discardedTile),
                        sourceSeatIndex: null,
                        wasAutomatic: mainActionWasAutomatic);

                    // 3. 广播他人打牌，并收集响应
                    _lastDiscardedTile = discardedTile; // 缓存，供响应阶段验证使用
                    _pendingResponses.Clear();
                    _responsesTcs = new TaskCompletionSource<bool>();
                    var responseDecision = _decisionTracker.OpenResponse(
                        _currentPlayerIndex,
                        discardedTile,
                        Enumerable.Range(0, _clients.Count).Where(index => index != _currentPlayerIndex),
                        GetDeadlineUnixMilliseconds(ResponseTimeoutMs));

                    // 创建响应阶段 CTS，设置到所有非当前玩家
                    _turnCts?.Dispose();
                    _turnCts = new CancellationTokenSource();
                    for (int i = 0; i < _clients.Count; i++)
                    {
                        SetRemoteDecision(_clients[i], responseDecision);
                        if (i != _currentPlayerIndex)
                        {
                            _clients[i].TurnCancellationToken = _turnCts.Token;
                        }
                    }

                    // The discard is a public table event, so write it to every seat stream,
                    // including the discarder. The discarder client ignores the response prompt.
                    for (int i = 0; i < _clients.Count; i++)
                    {
                        _clients[i].OnOtherPlayerDiscarded(_currentPlayerIndex, discardedTile);
                    }
                    NotifyTileBecamePublic(_currentPlayerIndex, discardedTile);

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
                    CloseDecision(responseDecision.DecisionId);

                    // 4. 裁决响应优先级 (胡 > 碰/杠 > 吃)
                    ClientAction resolvedAction = ResolveResponses();

                    if (resolvedAction != null && resolvedAction.ActionType != ClientActionType.Skip)
                    {
                        if (resolvedAction.ActionType == ClientActionType.Hu)
                        {
                            HandlePlayerWin(
                                resolvedAction,
                                false,
                                _currentPlayerIndex,
                                decisionId: responseDecision.DecisionId);
                            break;
                        }
                        else
                        {
                            List<TileData> handTilesBecomingPublic =
                                GetHandTilesBecomingPublic(resolvedAction, _lastDiscardedTile);
                            // 执行碰/杠/吃，更新快照
                            if (!_gameState.TryClaimDiscard(_currentPlayerIndex, _lastDiscardedTile))
                            {
                                Debug.LogError($"[GameServer] Could not consume claimed discard from player {_currentPlayerIndex}.");
                                continue;
                            }
                            _gameState.ApplyMeld(resolvedAction.PlayerId, resolvedAction.ActionType,
                                _lastDiscardedTile, resolvedAction.ChiCombinations);
                            resolvedAction = new ClientAction(resolvedAction.PlayerId, resolvedAction.ActionType,
                                _lastDiscardedTile, resolvedAction.ChiCombinations);
                            BroadcastAction(resolvedAction);
                            NotifyTilesBecamePublic(
                                resolvedAction.PlayerId,
                                handTilesBecomingPublic);
                            CommitTalentAction(
                                responseDecision.DecisionId,
                                resolvedAction,
                                sourceSeatIndex: _currentPlayerIndex,
                                wasAutomatic: false);

                            // 跳转回合
                            _currentPlayerIndex = resolvedAction.PlayerId;

                            if (resolvedAction.ActionType == ClientActionType.MingGan)
                            {
                                // 杠牌需要摸岭上开花，不跳过摸牌
                                _skipNextDraw = false;
                                _pendingKongReplacementDraw = true;
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
            return ResponseActionPolicy.SelectHighestPriorityResponse(
                _pendingResponses.Values,
                _currentPlayerIndex,
                _clients.Count);
        }

        private async Task<ResolvedResponseAction> CollectRobKongResponses(ClientAction declaration)
        {
            TileData targetTile = declaration.TargetTile;
            _pendingRobKongTile = targetTile;
            _pendingResponses.Clear();
            _responsesTcs = new TaskCompletionSource<bool>();
            var robKongDecision = _decisionTracker.OpenRobKong(
                _currentPlayerIndex,
                targetTile,
                Enumerable.Range(0, _clients.Count).Where(index => index != _currentPlayerIndex),
                GetDeadlineUnixMilliseconds(ResponseTimeoutMs));

            _turnCts?.Dispose();
            _turnCts = new CancellationTokenSource();
            for (int i = 0; i < _clients.Count; i++)
            {
                SetRemoteDecision(_clients[i], robKongDecision);
                if (i != _currentPlayerIndex)
                {
                    _clients[i].TurnCancellationToken = _turnCts.Token;
                }
            }

            // 该牌是公开的加杠声明，不是弃牌：客户端只能据此打开胡/过响应。
            _publicTileTransition.PublishAddedKongDeclaration(
                _currentPlayerIndex,
                targetTile,
                publicTile =>
                {
                    for (int i = 0; i < _clients.Count; i++)
                        _clients[i].OnAddedKongDeclared(_currentPlayerIndex, publicTile);
                });
            OnTalentEventsAvailable?.Invoke();

            try
            {
                await AwaitWithTimeout(
                    _responsesTcs,
                    ResponseTimeoutMs,
                    onTimeout: () =>
                    {
                        Debug.LogWarning("[GameServer] 抢杠响应超时，为未回复玩家自动填充 Skip");
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
                    fallbackFactory: () => true);

                return new ResolvedResponseAction(
                    ResolveResponses(),
                    robKongDecision.DecisionId);
            }
            finally
            {
                _responsesTcs = null;
                CloseDecision(robKongDecision.DecisionId);
                _pendingRobKongTile = null;
            }
        }

        /// <summary>
        /// 供客户端调用，提交决策
        /// </summary>
        public void SubmitAction(ClientAction action)
        {
            SubmitActionInternal(action, false);
        }

        private void SubmitActionInternal(ClientAction action, bool isValidatedNetworkAction)
        {
            if (!_isGameActive) return;
            var activeDecision = _decisionTracker?.Active;
            if (action == null || action.PlayerId < 0 || _clients == null || action.PlayerId >= _clients.Count) return;
            bool requiresDirectAiAuthorization = _clients[action.PlayerId] is IDirectActionAuthorizer;
            bool isDirectAiAuthorized = !requiresDirectAiAuthorization
                || ((_clients[action.PlayerId] as IDirectActionAuthorizer)?.CanSubmitDirectAction(activeDecision) ?? false);
            if (!NetworkActionSubmissionPolicy.CanProceedToActionHandling(
                    isValidatedNetworkAction, requiresDirectAiAuthorization, isDirectAiAuthorized))
            {
                Debug.LogWarning($"[GameServer] Rejected direct action from seat {action.PlayerId} because AI control is not latched for the active decision.");
                return;
            }
            if (!isValidatedNetworkAction && !NetworkActionSubmissionPolicy.CanProcessDirectAction(
                    isDirectAiAuthorized, RecordDirectActionSubmission(action)))
            {
                Debug.LogWarning($"[GameServer] Rejected direct action from seat {action.PlayerId} because the active decision did not admit it.");
                return;
            }

            // 如果当前在等待主玩家出牌
            if (_pendingActionTcs != null && action.PlayerId == _currentPlayerIndex)
            {
                if (!TurnActionPolicy.IsMainTurnAction(action.ActionType))
                {
                    Debug.LogWarning($"[GameServer] Ignoring late or invalid main-turn action from player {action.PlayerId}: {action.ActionType}");
                    return;
                }
                var validated = ValidateMainAction(action);
                _pendingActionTcs.TrySetResult(validated);
            }
            // 如果在等待其他人响应
            else if (_responsesTcs != null && CanRespondToActiveResponse(action.PlayerId))
            {
                if (_pendingResponses.ContainsKey(action.PlayerId)) return;

                bool isRobKong = IsRobKongResponseActive;
                bool isAllowedResponse = isRobKong
                    ? TurnActionPolicy.IsRobKongResponseAction(action.ActionType)
                    : TurnActionPolicy.IsResponseAction(action.ActionType);
                if (!isAllowedResponse)
                {
                    Debug.LogWarning($"[GameServer] Ignoring late or invalid response action from player {action.PlayerId}: {action.ActionType}");
                    return;
                }
                var validated = isRobKong ? ValidateRobKongResponseAction(action) : ValidateResponseAction(action);
                _pendingResponses[action.PlayerId] = validated;

                if (validated.ActionType == ClientActionType.Hu)
                {
                    CompleteHuResponseCollection();
                }
                // 检查是否收集齐了 3 家的响应
                else if (_pendingResponses.Count == _clients.Count - 1)
                {
                    _responsesTcs.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Validates a network action against the currently published decision before
        /// passing it to the existing authoritative action validation path.
        /// </summary>
        public bool SubmitNetworkAction(int boundSeatIndex, long decisionId, ClientAction action, out string errorCode)
        {
            errorCode = null;
            if (action == null || action.PlayerId != boundSeatIndex)
            {
                errorCode = NetworkErrorCodes.WrongController;
                return false;
            }

            if (_decisionTracker == null || !_decisionTracker.TrySubmitNetworkAction(
                    decisionId, boundSeatIndex, action.ActionType, out errorCode))
            {
                return false;
            }

            SubmitActionInternal(action, true);
            return true;
        }

        /// <summary>
        /// Executes a carried talent action alongside, rather than in place of, the active decision.
        /// </summary>
        public bool SubmitNetworkTalentAction(
            int boundSeatIndex,
            TalentActionMessage message,
            out TalentActionResult result)
        {
            result = null;
            if (message == null)
            {
                result = TalentActionResult.Reject(NetworkErrorCodes.InvalidAction);
                return false;
            }

            if (!TalentActionAdmissionPolicy.TryValidateMainTurn(
                    _decisionTracker,
                    message.decisionId,
                    boundSeatIndex,
                    out string decisionError))
            {
                result = TalentActionResult.Reject(decisionError);
                return false;
            }

            result = _talentRuntime.TryActivate(
                boundSeatIndex,
                new TalentActionRequest
                {
                    DecisionId = message.decisionId,
                    TalentId = message.talentId,
                    TargetSeatIndex = message.targetSeatIndex,
                    TargetTalentId = message.targetTalentId,
                    ChoiceId = message.selectedChoiceId
                },
                new TalentActivationContext(
                    _session,
                    boundSeatIndex,
                    TalentActionAdmissionPolicy.RequiredActivationWindow,
                    message.decisionId));
            if (result.Accepted)
            {
                RefreshScoringOptions();
                OnTalentEventsAvailable?.Invoke();
            }
            return result.Accepted;
        }

        public IReadOnlyList<TalentActionOption> GetAvailableTalentActionsSnapshot(int seatIndex)
        {
            NetworkDecisionContext decision = ActiveDecision;
            if (decision == null
                || !TalentActionAdmissionPolicy.TryValidateMainTurn(
                    _decisionTracker,
                    decision.DecisionId,
                    seatIndex,
                    out _))
            {
                return Array.Empty<TalentActionOption>();
            }

            return _talentRuntime.GetAvailableActions(
                seatIndex,
                new TalentActionQueryContext(
                    _session,
                    seatIndex,
                    TalentActionAdmissionPolicy.RequiredActivationWindow,
                    decision.DecisionId));
        }

        private void CompleteHuResponseCollection()
        {
            var potentialHuPlayerIds = GetPotentialHuPlayerIds();
            for (int playerId = 0; playerId < _clients.Count; playerId++)
            {
                if (playerId == _currentPlayerIndex || potentialHuPlayerIds.Contains(playerId) || _pendingResponses.ContainsKey(playerId)) continue;

                _pendingResponses[playerId] = ClientAction.Skip(playerId);
                _clients[playerId].OnTimeout(null);
            }

            if (ResponseActionPolicy.AllPotentialHuRespondersAnswered(potentialHuPlayerIds, _pendingResponses.Keys))
                _responsesTcs.TrySetResult(true);
        }

        private List<int> GetPotentialHuPlayerIds()
        {
            var potentialHuPlayerIds = new List<int>();
            TileData targetTile = IsRobKongResponseActive ? _pendingRobKongTile : _lastDiscardedTile;
            if (targetTile == null) return potentialHuPlayerIds;

            for (int playerId = 0; playerId < _clients.Count; playerId++)
            {
                if (playerId == _currentPlayerIndex) continue;

                if (TryResolveWin(
                        playerId,
                        targetTile,
                        isSelfDraw: false,
                         isRobKongWin: IsRobKongResponseActive,
                         out _,
                         out _,
                         out _))
                {
                    potentialHuPlayerIds.Add(playerId);
                }
            }

            return potentialHuPlayerIds;
        }

        private bool IsRobKongResponseActive => _decisionTracker?.Active?.Phase == NetworkDecisionPhase.RobKong;

        private bool CanRespondToActiveResponse(int playerId)
        {
            return IsRobKongResponseActive
                ? ResponseActionPolicy.CanRobAddedKong(playerId, _currentPlayerIndex)
                : ResponseActionPolicy.CanRespondToDiscard(playerId, _currentPlayerIndex);
        }

        /// <summary>
        /// 验证主回合动作（出牌/自摸胡/暗杠/加杠）
        /// </summary>
        private ClientAction ValidateMainAction(ClientAction action)
        {
            int pid = action.PlayerId;
            var hand = _gameState.GetHand(pid);
            var melds = _gameState.GetMelds(pid);

            // 需要 TargetTile 的动作类型，null 直接判定失败
            if (action.TargetTile == null && (action.ActionType == ClientActionType.Discard
                || action.ActionType == ClientActionType.AnGan || action.ActionType == ClientActionType.JiaGang))
            {
                Debug.LogWarning($"[ServerValidation] 玩家{pid} {action.ActionType} 目标牌为空，自动出牌");
                var fallback = _gameState.GetAutoDiscardTile(pid, _lastDrawnTile);
                return ClientAction.Discard(pid, fallback);
            }

            switch (action.ActionType)
            {
                case ClientActionType.Discard:
                    // 验证手牌中确实有这张牌
                    if (!HandContainsTile(hand, action.TargetTile))
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 出牌验证失败: 手中没有 {action.TargetTile}，自动出牌");
                        var fallback = _gameState.GetAutoDiscardTile(pid, _lastDrawnTile);
                        return ClientAction.Discard(pid, fallback);
                    }
                    break;

                case ClientActionType.Hu:
                    // 验证自摸胡合法性
                    if (!TryResolveWin(
                            pid,
                            _lastDrawnTile,
                            isSelfDraw: true,
                             isRobKongWin: false,
                             out _,
                             out _,
                             out _))
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 自摸胡验证失败，自动出牌");
                        var fallback = _gameState.GetAutoDiscardTile(pid, _lastDrawnTile);
                        return ClientAction.Discard(pid, fallback);
                    }
                    // 番数由 HandlePlayerWin 服务端重算，此处放行
                    break;

                case ClientActionType.AnGan:
                {
                    int count = hand.Count(t => t.TileSuit == action.TargetTile.TileSuit && t.Value == action.TargetTile.Value);
                    if (count < 4)
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 暗杠验证失败: {action.TargetTile} 仅有{count}张，自动出牌");
                        var fallback = _gameState.GetAutoDiscardTile(pid, _lastDrawnTile);
                        return ClientAction.Discard(pid, fallback);
                    }
                    break;
                }

                case ClientActionType.JiaGang:
                {
                    bool hasPon = melds.Any(m => m.Type == MeldType.Pon
                        && m.FirstTile.TileSuit == action.TargetTile.TileSuit
                        && m.FirstTile.Value == action.TargetTile.Value);
                    bool hasInHand = hand.Any(t => t.TileSuit == action.TargetTile.TileSuit && t.Value == action.TargetTile.Value);
                    if (!hasPon || !hasInHand)
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 加杠验证失败: {action.TargetTile}，自动出牌");
                        var fallback = _gameState.GetAutoDiscardTile(pid, _lastDrawnTile);
                        return ClientAction.Discard(pid, fallback);
                    }
                    break;
                }
            }

            return action;
        }

        /// <summary>
        /// 验证响应动作（胡/碰/明杠/吃/过）
        /// </summary>
        private ClientAction ValidateResponseAction(ClientAction action)
        {
            if (action.ActionType == ClientActionType.Skip) return action;

            int pid = action.PlayerId;
            var hand = _gameState.GetHand(pid);
            var melds = _gameState.GetMelds(pid);
            var roundWind = _session?.PrevalentWind ?? WindDirection.East;
            var seatWind = _session?.GetSeatWind(pid) ?? WindDirection.East;
            bool isNextPlayer = ((_currentPlayerIndex + 1) % _clients.Count) == pid;

            // 响应阶段必须有被打出的牌
            if (_lastDiscardedTile == null)
            {
                Debug.LogWarning($"[ServerValidation] 响应阶段无被打出的牌，玩家{pid} {action.ActionType} 自动Skip");
                return ClientAction.Skip(pid);
            }

            switch (action.ActionType)
            {
                case ClientActionType.Hu:
                    // 验证点炮胡
                    if (!TryResolveWin(
                            pid,
                            _lastDiscardedTile,
                            isSelfDraw: false,
                             isRobKongWin: false,
                             out _,
                             out _,
                             out _))
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 点炮胡验证失败，自动Skip");
                        Debug.LogWarning($"[ServerValidation] 点炮胡快照: 目标={_lastDiscardedTile}, 手牌=[{string.Join(", ", hand)}], 副露数={melds.Count}, 圈风={roundWind}, 门风={seatWind}");
                        return ClientAction.Skip(pid);
                    }
                    break;

                case ClientActionType.Pon:
                {
                    int count = hand.Count(t => t.TileSuit == _lastDiscardedTile.TileSuit && t.Value == _lastDiscardedTile.Value);
                    if (count < 2)
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 碰验证失败: {_lastDiscardedTile} 仅有{count}张，自动Skip");
                        return ClientAction.Skip(pid);
                    }
                    break;
                }

                case ClientActionType.MingGan:
                {
                    int count = hand.Count(t => t.TileSuit == _lastDiscardedTile.TileSuit && t.Value == _lastDiscardedTile.Value);
                    if (count < 3)
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 明杠验证失败: {_lastDiscardedTile} 仅有{count}张，自动Skip");
                        return ClientAction.Skip(pid);
                    }
                    break;
                }

                case ClientActionType.Chi:
                    if (!isNextPlayer)
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 吃牌验证失败: 非上家，自动Skip");
                        return ClientAction.Skip(pid);
                    }
                    if (action.ChiCombinations == null || action.ChiCombinations.Length != 2)
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 吃验证失败: 数据不完整，自动Skip");
                        return ClientAction.Skip(pid);
                    }
                    // 验证花色非字牌
                    if (_lastDiscardedTile.TileSuit == Suit.Wind || _lastDiscardedTile.TileSuit == Suit.Dragon)
                    {
                        Debug.LogWarning($"[ServerValidation] 玩家{pid} 吃验证失败: 字牌不能吃，自动Skip");
                        return ClientAction.Skip(pid);
                    }
                    // 验证三张牌构成连续顺子
                    {
                        var vals = new List<int> { _lastDiscardedTile.Value, action.ChiCombinations[0], action.ChiCombinations[1] };
                        vals.Sort();
                        if (vals[1] - vals[0] != 1 || vals[2] - vals[1] != 1)
                        {
                            Debug.LogWarning($"[ServerValidation] 玩家{pid} 吃验证失败: {vals[0]},{vals[1]},{vals[2]} 不构成顺子，自动Skip");
                            return ClientAction.Skip(pid);
                        }
                        // 验证手牌中确实有吃的那两张牌
                        foreach (int val in action.ChiCombinations)
                        {
                            if (!hand.Any(t => t.TileSuit == _lastDiscardedTile.TileSuit && t.Value == val))
                            {
                                Debug.LogWarning($"[ServerValidation] 玩家{pid} 吃验证失败: 手中没有 {_lastDiscardedTile.TileSuit}{val}，自动Skip");
                                return ClientAction.Skip(pid);
                            }
                        }
                    }
                    break;

                default:
                    // 响应阶段不应出现 Discard/AnGan/JiaGang 等主回合动作
                    Debug.LogWarning($"[ServerValidation] 玩家{pid} 响应阶段提交了非法动作类型 {action.ActionType}，自动Skip");
                    return ClientAction.Skip(pid);
            }

            return action;
        }

        /// <summary>验证抢杠声明的响应；该阶段不允许吃、碰或任何类型的杠。</summary>
        private ClientAction ValidateRobKongResponseAction(ClientAction action)
        {
            if (action.ActionType == ClientActionType.Skip) return action;
            if (action.ActionType != ClientActionType.Hu || _pendingRobKongTile == null)
            {
                Debug.LogWarning($"[ServerValidation] 玩家{action.PlayerId} 提交了非法抢杠响应 {action.ActionType}，自动Skip");
                return ClientAction.Skip(action.PlayerId);
            }

            int pid = action.PlayerId;
            if (!TryResolveWin(
                    pid,
                    _pendingRobKongTile,
                    isSelfDraw: false,
                     isRobKongWin: true,
                     out _,
                     out _,
                     out _))
            {
                Debug.LogWarning($"[ServerValidation] 玩家{pid} 抢杠胡验证失败，自动Skip");
                return ClientAction.Skip(pid);
            }

            return action;
        }

        /// <summary>
        /// 检查手牌列表中是否包含指定花色和数值的牌
        /// </summary>
        private bool HandContainsTile(List<TileData> hand, TileData tile)
        {
            return hand.Any(t => t.TileSuit == tile.TileSuit && t.Value == tile.Value);
        }

        private void BroadcastWallCount()
        {
            foreach (var client in _clients)
            {
                client.OnWallCountChanged(_wallService.RemainingCount);
            }
        }

        private void BroadcastAction(ClientAction action)
        {
            foreach (var client in _clients)
            {
                client.OnActionResolved(action.PlayerId, action.ActionType, action.TargetTile, action.ChiCombinations);
            }
        }

        private void SendPrivatePeekResults()
        {
            for (int seatIndex = 0; seatIndex < _clients.Count; seatIndex++)
            {
                IReadOnlyList<TileData> privateTiles = _talentRuntime.GetPrivatePeekTiles(seatIndex);
                if (privateTiles.Count == 0) continue;

                List<TileData> snapshot = CloneTiles(privateTiles);
                _privateWallPreviewTiles[seatIndex] = snapshot;
                _clients[seatIndex].OnPeekWallTiles(CloneTiles(snapshot));
            }
        }

        private List<TileData> GetHandTilesBecomingPublic(ClientAction action, TileData targetTile)
        {
            List<TileData> hand = _gameState.GetHand(action.PlayerId);
            var selected = new List<TileData>();
            if (targetTile == null) return selected;

            if (action.ActionType == ClientActionType.Chi)
            {
                foreach (int value in action.ChiCombinations ?? Array.Empty<int>())
                {
                    int index = hand.FindIndex(tile => tile.TileSuit == targetTile.TileSuit
                                                      && tile.Value == value);
                    if (index < 0) continue;
                    selected.Add(hand[index]);
                    hand.RemoveAt(index);
                }
                return selected;
            }

            int count = action.ActionType switch
            {
                ClientActionType.Pon => 2,
                ClientActionType.MingGan => 3,
                ClientActionType.JiaGang => 1,
                _ => 0
            };
            for (int index = hand.Count - 1; index >= 0 && selected.Count < count; index--)
            {
                TileData tile = hand[index];
                if (tile.TileSuit == targetTile.TileSuit && tile.Value == targetTile.Value)
                    selected.Add(tile);
            }
            return selected;
        }

        private void NotifyTileBecamePublic(int ownerSeatIndex, TileData tile)
        {
            _talentRuntime.NotifyTileBecamePublic(
                new TalentPublicTileContext(_session, ownerSeatIndex),
                tile);
            OnTalentEventsAvailable?.Invoke();
        }

        private void NotifyTilesBecamePublic(int ownerSeatIndex, IEnumerable<TileData> tiles)
        {
            foreach (TileData tile in tiles ?? Enumerable.Empty<TileData>())
            {
                _talentRuntime.NotifyTileBecamePublic(
                    new TalentPublicTileContext(_session, ownerSeatIndex),
                    tile);
            }
            OnTalentEventsAvailable?.Invoke();
        }

        private void CommitTalentAction(
            long decisionId,
            ClientAction action,
            int? sourceSeatIndex,
            bool wasAutomatic,
            TalentWinFacts winFacts = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            bool committed = _talentRuntime.CommitAction(TalentActionCommittedFacts.Create(
                decisionId,
                action.PlayerId,
                sourceSeatIndex,
                action.ActionType,
                action.TargetTile,
                action.ChiCombinations,
                wasAutomatic,
                winFacts));
            if (committed)
            {
                RefreshScoringOptions();
                OnTalentEventsAvailable?.Invoke();
            }
        }

        private void RefreshScoringOptions()
        {
            if (_clients == null || _session == null) return;

            for (int seatIndex = 0; seatIndex < _clients.Count; seatIndex++)
            {
                ScoringOptions next = _talentRuntime.BuildScoringOptions(
                    new TalentScoringContext(_session, seatIndex));
                if (_scoringOptions.TryGetValue(seatIndex, out ScoringOptions current)
                    && HaveSameScoringOptions(current, next))
                {
                    continue;
                }

                _scoringOptions[seatIndex] = next;
                _clients[seatIndex].OnTalentInfo(next);
            }
        }

        private static bool HaveSameScoringOptions(ScoringOptions first, ScoringOptions second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null) return false;
            return first.BonusFan == second.BonusFan
                   && first.MinimumFan == second.MinimumFan
                   && first.RelaxedPureStraight == second.RelaxedPureStraight;
        }

        private static long GetDeadlineUnixMilliseconds(int timeoutMilliseconds)
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Math.Max(0, timeoutMilliseconds);
        }

        private static void SetRemoteDecision(IPlayerClient client, NetworkDecisionContext decision)
        {
            if (client is INetworkDecisionClient remoteClient)
            {
                remoteClient.SetActiveDecision(decision);
            }
        }

        private void CloseDecision(long decisionId)
        {
            _decisionTracker?.Close(decisionId);
            if (_clients == null) return;
            foreach (var client in _clients.OfType<INetworkDecisionClient>()) client.CloseDecision(decisionId);
        }

        private void CloseActiveDecision()
        {
            var activeDecision = _decisionTracker?.Active;
            if (activeDecision != null)
            {
                CloseDecision(activeDecision.DecisionId);
            }
        }

        private bool RecordDirectActionSubmission(ClientAction action)
        {
            if (action == null) return false;
            var activeDecision = _decisionTracker?.Active;
            if (activeDecision == null) return false;

            return _decisionTracker.TrySubmitNetworkAction(activeDecision.DecisionId, action.PlayerId, action.ActionType, out _);
        }

        private static List<TileData> CloneTiles(IEnumerable<TileData> tiles)
        {
            return (tiles ?? Enumerable.Empty<TileData>()).Select(tile => new TileData(tile.TileSuit, tile.Value, tile.OriginalOwnerID)
            {
                ID = tile.ID,
                IsModified = tile.IsModified,
                SpecialEffectID = tile.SpecialEffectID
            }).ToList();
        }

        private bool TryResolveWin(
            int playerId,
            TileData winTile,
            bool isSelfDraw,
            bool isRobKongWin,
            out TalentFanResolution fanResolution,
            out List<string> fanDetails,
            out TalentWinFacts winFacts)
        {
            fanResolution = new TalentFanResolution();
            fanDetails = null;
            winFacts = null;
            if (winTile == null) return false;

            List<TileData> hand = _gameState.GetHand(playerId);
            List<Meld> melds = _gameState.GetMelds(playerId);
            ScoringOptions options = _scoringOptions.TryGetValue(playerId, out ScoringOptions configured)
                ? configured
                : null;
            WindDirection roundWind = _session?.PrevalentWind ?? WindDirection.East;
            WindDirection seatWind = _session?.GetSeatWind(playerId) ?? WindDirection.East;
            if (!MahjongLogic.CheckWinWithFan(
                    hand,
                    melds,
                    winTile,
                    isSelfDraw,
                    out int eligibilityFan,
                    out fanDetails,
                    roundWind,
                    seatWind,
                    options,
                    isRobKongWin,
                    isKongWin: isSelfDraw && _currentDrawIsKongReplacement))
            {
                return false;
            }

            winFacts = TalentWinFacts.Create(
                _session,
                playerId,
                isSelfDraw ? (int?)null : _currentPlayerIndex,
                hand,
                melds,
                winTile,
                isSelfDraw,
                isRobKongWin,
                isKongReplacement: isSelfDraw && _currentDrawIsKongReplacement);
            fanResolution = _talentRuntime.ResolvePostLegalFan(
                new TalentWinContext(_session, playerId, winFacts),
                eligibilityFan);
            return true;
        }

        private void HandlePlayerWin(
            ClientAction winAction,
            bool isSelfDraw,
            int loserId = -1,
            TileData winTileOverride = null,
            bool isRobKongWin = false,
            long decisionId = 0)
        {
            _isGameActive = false;
            CloseActiveDecision();
            OnTurnEnded?.Invoke();

            int pid = winAction.PlayerId;
            var hand = _gameState.GetHand(pid);
            var melds = _gameState.GetMelds(pid);
            var roundWind = _session?.PrevalentWind ?? WindDirection.East;
            var seatWind = _session?.GetSeatWind(pid) ?? WindDirection.East;

            // 确定胡的那张牌
            TileData winTile = winTileOverride ?? (isSelfDraw ? _lastDrawnTile : _lastDiscardedTile);
            bool isKongWin = isSelfDraw && _currentDrawIsKongReplacement;

            // 服务端权威重算番数
            bool acceptedLegal = TryResolveWin(
                pid,
                winTile,
                isSelfDraw,
                isRobKongWin,
                out TalentFanResolution serverResolution,
                out List<string> serverDetails,
                out TalentWinFacts acceptedWinFacts);
            int serverFan = acceptedLegal ? serverResolution.FinalFan : 0;

            if (acceptedLegal)
            {
                try
                {
                    TalentFanResolution attribution = _talentRuntime.ResolveAcceptedWinFan(
                        new TalentAcceptedWinAttributionContext(
                            _session,
                            pid,
                            serverFan,
                            acceptedWinFacts,
                            candidateOptions => MahjongLogic.EvaluateBestFan(
                                hand,
                                melds,
                                winTile,
                                isSelfDraw,
                                roundWind,
                                seatWind,
                                candidateOptions,
                                isRobKongWin,
                                isKongWin)));
                    if (attribution.FinalFan == serverFan)
                    {
                        serverResolution = attribution;
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[GameServer] Accepted-win talent attribution disagreed with server final: " +
                            $"seat={pid}, attributed={attribution.FinalFan}, server={serverFan}.");
                        serverResolution.FinalFan = serverFan;
                        serverResolution.Contributions = Array.Empty<TalentFanContribution>();
                    }
                }
                catch (Exception attributionError)
                {
                    Debug.LogWarning(
                        $"[GameServer] Accepted-win talent attribution failed for seat {pid}: " +
                        attributionError);
                    serverResolution.FinalFan = serverFan;
                    serverResolution.Contributions = Array.Empty<TalentFanContribution>();
                }
            }

            if (acceptedLegal)
            {
                TalentWinEvaluation acceptedEvaluation = new TalentWinEvaluation(
                    isLegal: true,
                    finalFan: serverFan);
                _talentRuntime.ResolveAcceptedWinVisibility(new TalentAcceptedWinContext(
                    _session,
                    pid,
                    acceptedWinFacts,
                    acceptedEvaluation,
                    counterfactualOptions =>
                    {
                        int counterfactualFan = 0;
                        bool isLegal = MahjongLogic.CheckWinWithFan(
                            hand,
                            melds,
                            winTile,
                            isSelfDraw,
                            out counterfactualFan,
                            out _,
                            roundWind,
                            seatWind,
                            counterfactualOptions,
                            isRobKongWin,
                            isKongWin);
                        if (!isLegal) return new TalentWinEvaluation(false, 0);

                        TalentFanResolution counterfactualResolution =
                            _talentRuntime.ResolvePostLegalFan(
                                new TalentWinContext(_session, pid, acceptedWinFacts),
                                counterfactualFan,
                                counterfactualOptions);
                        return new TalentWinEvaluation(
                            true,
                            counterfactualResolution.FinalFan);
                    }));
                _talentRuntime.ConfirmAcceptedWin(
                    new TalentWinContext(_session, pid, acceptedWinFacts));
                CommitTalentAction(
                    decisionId,
                    new ClientAction(pid, ClientActionType.Hu, winTile),
                    isSelfDraw ? (int?)null : loserId,
                    wasAutomatic: false,
                    winFacts: acceptedWinFacts);
                _talentRuntime.RecordAcceptedWinTelemetry(pid, serverResolution, GetDrawCountsSnapshot());
            }
            // 断言比对：记录客户端与服务端计算差异（Phase 0 验证用）
            if (winAction.TotalFan != serverFan)
            {
                Debug.LogWarning($"[ServerValidation] 番数不一致! 玩家{pid} 客户端={winAction.TotalFan} 服务端={serverFan}");
                Debug.LogWarning($"  客户端番种: {(winAction.FanDetails != null ? string.Join(", ", winAction.FanDetails) : "null")}");
                Debug.LogWarning($"  服务端番种: {(serverDetails != null ? string.Join(", ", serverDetails) : "null")}");
                Debug.LogWarning($"  手牌: [{string.Join(", ", hand.Select(t => t.ToString()))}]");
                Debug.LogWarning($"  副露: [{string.Join(", ", melds.Select(m => $"{m.Type}:{m.FirstTile}"))}]");
                Debug.LogWarning($"  胡牌: {winTile} 自摸={isSelfDraw} 抢杠={isRobKongWin} 圈风={roundWind} 门风={seatWind}");
            }
            else
            {
                Debug.Log($"[ServerValidation] 番数验证通过: 玩家{pid} 番数={serverFan}");
            }

            // 使用服务端权威计算结果
            WinnerId = pid;
            WinFan = serverFan;
            WinFanResolution = serverResolution;
            WinTalentFanBreakdown = TalentFanBreakdownMessage.FromResolution(serverResolution);
            WinFanDetails = serverDetails?.ToList() ?? new List<string>();
            WinIsSelfDraw = isSelfDraw;
            WinResultKind = isSelfDraw
                ? WinKind.SelfDraw
                : isRobKongWin ? WinKind.RobKong : WinKind.Discard;
            WinningHandSnapshot = WinningHandSnapshotCodec.Create(hand, melds, winTile, isSelfDraw);
            if (!WinningHandSnapshotCodec.TryValidate(WinningHandSnapshot, out string snapshotError))
            {
                Debug.LogWarning($"[GameServer] Winning-hand snapshot validation failed: {snapshotError}");
            }
            LoserId = loserId;

            // 计分
            if (_session != null)
            {
                _session.ApplyScore(pid, serverFan, isSelfDraw, loserId);
            }

            _publicTileTransition.PublishWinningResult(pid, () =>
            {
                foreach (var client in _clients)
                {
                    client.OnPlayerWin(pid, serverFan, serverDetails, isSelfDraw,
                        WinResultKind, loserId, WinningHandSnapshotCodec.Clone(WinningHandSnapshot),
                        TalentFanBreakdownMessage.Clone(WinTalentFanBreakdown));
                }
            });
            OnTalentEventsAvailable?.Invoke();

            _pendingKongReplacementDraw = false;
            _currentDrawIsKongReplacement = false;

            TryCompleteRound(GameRoundCompletionKind.Win);
        }

        private void HandleDrawGame()
        {
            _isGameActive = false;
            _pendingKongReplacementDraw = false;
            _currentDrawIsKongReplacement = false;
            CloseActiveDecision();
            OnTurnEnded?.Invoke();
            IsDrawGame = true;

            foreach (var client in _clients)
            {
                client.OnDrawGame();
            }

            TryCompleteRound(GameRoundCompletionKind.Draw);
        }

        private void AbortRound(Exception error)
        {
            _isGameActive = false;
            _pendingKongReplacementDraw = false;
            _currentDrawIsKongReplacement = false;
            CloseActiveDecision();
            try
            {
                OnTurnEnded?.Invoke();
            }
            catch (Exception callbackError)
            {
                Debug.LogError($"[GameServer] 回合异常清理回调失败: {callbackError}");
            }
            TryCompleteRound(GameRoundCompletionKind.Aborted, error);
        }

        private bool TryCompleteRound(GameRoundCompletionKind kind, Exception error = null)
        {
            if (!_roundCompletionLatch.TryComplete(kind, error, out GameRoundCompletion completion))
                return false;
            OnRoundFinished?.Invoke(completion);
            return true;
        }

        private sealed class ResolvedResponseAction
        {
            public ClientAction Action { get; }
            public long DecisionId { get; }

            public ResolvedResponseAction(ClientAction action, long decisionId)
            {
                Action = action;
                DecisionId = decisionId;
            }
        }
    }
}
