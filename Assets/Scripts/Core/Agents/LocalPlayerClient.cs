using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core;
using MahjongGame.UI;

namespace MahjongGame.Core.Agents
{
    /// <summary>
    /// 本地玩家客户端。将服务端的事件映射到 UI 和 3D 表现层，并收集玩家输入发回服务端。
    /// </summary>
    public class LocalPlayerClient : IPlayerClient
    {
        public int PlayerId { get; private set; }
        public CancellationToken TurnCancellationToken { get; set; }

        private IServer _server;
        private HandController _handController;

        // 本地状态
        private bool _isWaitingForUI = false;
        private int _lastDiscarderId = -1; // 记录最后打牌的人

        // 风位信息
        private WindDirection _roundWind = WindDirection.East;
        private WindDirection _seatWind = WindDirection.East;

        // 天赋加成
        private ScoringOptions _scoringOptions;
        private CancellationTokenSource _presentationCancellation = new CancellationTokenSource();

        public LocalPlayerClient(int playerId, IServer server, HandController handController)
        {
            PlayerId = playerId;
            _server = server;
            _handController = handController;
        }

        public void SetServer(IServer server)
        {
            _server = server;
        }

        /// <summary>Cancels every UI wait owned by the old projection before a recovered table is rebuilt.</summary>
        public void CancelPendingInput()
        {
            var cancellation = _presentationCancellation;
            _presentationCancellation = new CancellationTokenSource();
            cancellation.Cancel();
            cancellation.Dispose();
            _isWaitingForUI = false;
            _lastDiscarderId = -1;
            ActionPanelController.Instance?.Hide();
            FloatingTilePanelController.Instance?.Hide();
            UI.WaitHintController.Instance?.HideHint();
            _handController?.SetInteractable(false);
            GameHUDController.Instance?.StopTimer();
        }

        private CancellationTokenSource CreateOperationCancellation()
        {
            return CancellationTokenSource.CreateLinkedTokenSource(TurnCancellationToken, _presentationCancellation.Token);
        }

        /// <summary>Rebuilds the client-only table presentation from one authoritative per-seat snapshot.</summary>
        public void RestoreFromSnapshot(RoomGameSnapshot snapshot)
        {
            if (snapshot == null || snapshot.requestingSeatIndex != PlayerId || _handController == null) return;

            CancelPendingInput();
            _roundWind = (WindDirection)snapshot.prevalentWind;
            _seatWind = (WindDirection)snapshot.requestingSeatWind;
            _handController.RoundWind = _roundWind;
            _handController.SeatWind = _seatWind;
            _scoringOptions = new ScoringOptions
            {
                BonusFan = snapshot.privateSeat?.scoringOptions?.bonusFan ?? 0,
                RelaxedPureStraight = snapshot.privateSeat?.scoringOptions?.relaxedPureStraight ?? false
            };
            _handController.ScoringOptions = _scoringOptions;

            _handController.RebuildFromSnapshot(
                ToTiles(snapshot.privateSeat?.concealedHand),
                ToMelds(snapshot.privateSeat?.melds),
                ToTiles(GetRiverTiles(snapshot.rivers, PlayerId)));

            foreach (var seat in snapshot.seats ?? Array.Empty<RoomSnapshotSeat>())
            {
                if (seat == null || seat.seatIndex == PlayerId) continue;
                var view = GameManager.Instance?.GetOpponentView(seat.seatIndex);
                view?.RebuildFromSnapshot(
                    seat.concealedTileCount,
                    ToMelds(seat.publicMelds),
                    ToTiles(GetRiverTiles(snapshot.rivers, seat.seatIndex)));
            }

            var localSeat = (snapshot.seats ?? Array.Empty<RoomSnapshotSeat>()).FirstOrDefault(seat => seat?.seatIndex == PlayerId);
            var activeDecision = snapshot.activeDecision;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (activeDecision != null
                && (NetworkDecisionPhase)activeDecision.phase == NetworkDecisionPhase.Response
                && activeDecision.discardingSeatIndex >= 0
                && activeDecision.discardingSeatIndex < 4
                && activeDecision.targetTile != null)
            {
                // Keep the rebuilt river aligned even when this recovering seat is not
                // eligible to respond but another player claims the current discard.
                _lastDiscarderId = activeDecision.discardingSeatIndex;
            }
            if (activeDecision == null || !ClientRecoveryInputPolicy.CanRestoreInput(activeDecision, localSeat, PlayerId, now)) return;

            float remainingSeconds = Mathf.Max(0.1f, (activeDecision.deadlineUnixMilliseconds - now) / 1000f);
            if ((NetworkDecisionPhase)activeDecision.phase == NetworkDecisionPhase.MainTurn)
            {
                ResumeMainTurnDecision(snapshot.mainTurnDrawnTile?.ToTileData(), remainingSeconds);
                return;
            }

            var targetTile = activeDecision.targetTile?.ToTileData();
            if (targetTile != null)
            {
                if ((NetworkDecisionPhase)activeDecision.phase == NetworkDecisionPhase.RobKong)
                    HandleAddedKongDeclared(activeDecision.discardingSeatIndex, targetTile, remainingSeconds);
                else
                    HandleOtherPlayerDiscarded(activeDecision.discardingSeatIndex, targetTile, false, remainingSeconds);
            }
        }

        private static List<TileData> ToTiles(IEnumerable<SimpleTileData> tiles)
        {
            return (tiles ?? Enumerable.Empty<SimpleTileData>())
                .Select(tile => tile?.ToTileData())
                .Where(tile => tile != null)
                .ToList();
        }

        private static List<Meld> ToMelds(IEnumerable<SnapshotMeld> melds)
        {
            var result = new List<Meld>();
            foreach (var meld in melds ?? Enumerable.Empty<SnapshotMeld>())
            {
                var tiles = ToTiles(meld?.tiles);
                if (tiles.Count == 0) continue;
                result.Add(new Meld((MeldType)meld.meldType, tiles, meld.sourceSeatIndex, meld.isConcealed));
            }
            return result;
        }

        private static SimpleTileData[] GetRiverTiles(SeatRiverSnapshot[] rivers, int index)
        {
            return rivers != null && index >= 0 && index < rivers.Length
                ? rivers[index]?.tiles ?? Array.Empty<SimpleTileData>()
                : Array.Empty<SimpleTileData>();
        }

        public void OnGameStart(List<TileData> startingHand)
        {
            _handController.ClearHand();
            foreach (var tile in startingHand)
            {
                _handController.AddTileDirectly(tile);
            }
            _handController.SortHand();

            // 初始化其他玩家的 13 张盖着的牌
            for (int i = 0; i < 4; i++)
            {
                if (i == PlayerId) continue;
                var view = GameManager.Instance.GetOpponentView(i);
                if (view != null) view.InitHand(13);
            }
            Debug.Log($"[LocalPlayer] 游戏开始，发牌完成");
        }

        public void OnPlayerDrawn(int playerId)
        {
            var view = GameManager.Instance.GetOpponentView(playerId);
            if (view != null) view.DrawCard();
        }

        public async void OnTileDrawn(TileData drawnTile)
        {
            using var operationCancellation = CreateOperationCancellation();
            var ct = operationCancellation.Token;
            try
            {
                // 表现层：摸牌动画
                _handController.DrawCardData(drawnTile);
                await Task.Delay(300, ct);
                await BeginMainTurnDecision(drawnTile, null, ct);
            }
            catch (OperationCanceledException)
            {
                // 超时取消 — OnTimeout 已处理 UI 清理和手牌同步
                _handController.SetInteractable(false);
                GameHUDController.Instance?.StopTimer();
            }
        }

        private async void ResumeMainTurnDecision(TileData drawnTile, float? recoveryTimerSeconds)
        {
            using var operationCancellation = CreateOperationCancellation();
            try
            {
                await BeginMainTurnDecision(drawnTile, recoveryTimerSeconds, operationCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                _handController.SetInteractable(false);
                GameHUDController.Instance?.StopTimer();
            }
        }

        /// <summary>Offers self-actions when this main turn drew a tile, then waits for the authoritative discard.</summary>
        private async Task BeginMainTurnDecision(TileData drawnTile, float? recoveryTimerSeconds, CancellationToken ct)
        {
            if (drawnTile != null)
            {
                var handData = _handController.GetHandData();
                var melds = _handController.Melds;
                var kongOptions = _handController.GetSelfTurnKongOptions();
                var actions = ActionValidator.CheckSelfActions(handData, melds, drawnTile, _scoringOptions, _roundWind, _seatWind, kongOptions);
                if (actions.HasAction)
                {
                    bool actionTaken = false;
                    _isWaitingForUI = true;

                    ActionPanelController.Instance.Show(actions, (choice) =>
                    {
                        if (!_isWaitingForUI) return;

                        if (choice == ActionPanelChoice.Hu)
                        {
                            int totalFan;
                            List<string> fanDetails;
                            MahjongLogic.CheckWinWithFan(handData, melds, drawnTile, true, out totalFan, out fanDetails, _roundWind, _seatWind, _scoringOptions);

                            var action = new ClientAction(PlayerId, ClientActionType.Hu, drawnTile);
                            action.SetHuDetails(totalFan, fanDetails);
                            _server.SubmitAction(action);
                            actionTaken = true;
                        }
                        else if (choice == ActionPanelChoice.AnGan || choice == ActionPanelChoice.JiaGang)
                        {
                            var targets = choice == ActionPanelChoice.AnGan
                                ? kongOptions.AnGangTargets
                                : kongOptions.JiaGangTargets;
                            var actionType = choice == ActionPanelChoice.AnGan
                                ? ClientActionType.AnGan
                                : ClientActionType.JiaGang;

                            if (targets.Count == 1)
                            {
                                _server.SubmitAction(new ClientAction(PlayerId, actionType, targets[0]));
                                actionTaken = true;
                            }
                            else if (targets.Count > 1)
                            {
                                ActionPanelController.Instance.ShowKongSelection(choice, targets, selectedTile =>
                                {
                                    if (!_isWaitingForUI) return;
                                    _server.SubmitAction(new ClientAction(PlayerId, actionType, selectedTile));
                                    actionTaken = true;
                                    _isWaitingForUI = false;
                                });
                                return;
                            }
                        }

                        _isWaitingForUI = false;
                        ActionPanelController.Instance.Hide();
                    });

                    while (_isWaitingForUI)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }

                    if (actionTaken) return;
                }
            }

            await WaitForDiscardAfterAction(recoveryTimerSeconds, ct);
        }

        public void OnOtherPlayerDiscarded(int discarderId, TileData discardedTile)
        {
            HandleOtherPlayerDiscarded(discarderId, discardedTile, true, null);
        }

        public void OnAddedKongDeclared(int declaringPlayerId, TileData targetTile)
        {
            HandleAddedKongDeclared(declaringPlayerId, targetTile, null);
        }

        private async void HandleAddedKongDeclared(int declaringPlayerId, TileData targetTile, float? recoveryTimerSeconds)
        {
            if (!Network.ResponseActionPolicy.CanRobAddedKong(PlayerId, declaringPlayerId)) return;

            using var operationCancellation = CreateOperationCancellation();
            var ct = operationCancellation.Token;
            try
            {
                var handData = _handController.GetHandData();
                var melds = _handController.Melds;
                bool canHu = MahjongLogic.CheckWinWithFan(handData, melds, targetTile, false,
                    out _, out _, _roundWind, _seatWind, _scoringOptions, true);
                if (!canHu)
                {
                    _server.SubmitAction(ClientAction.Skip(PlayerId));
                    return;
                }

                _isWaitingForUI = true;
                if (recoveryTimerSeconds.HasValue)
                {
                    GameHUDController.Instance?.StartTimer(recoveryTimerSeconds.Value, PlayerId);
                }
                else if (GameManager.Instance != null)
                {
                    GameHUDController.Instance?.StartTimer(GameManager.Instance.responseTimeout, PlayerId);
                }

                var actions = new AllowedActions { CanHu = true };
                bool actionTaken = false;
                ActionPanelController.Instance.Show(actions, (choice) =>
                {
                    if (!_isWaitingForUI) return;

                    if (choice == ActionPanelChoice.Hu)
                    {
                        MahjongLogic.CheckWinWithFan(handData, melds, targetTile, false,
                            out int totalFan, out List<string> fanDetails, _roundWind, _seatWind, _scoringOptions, true);
                        var action = new ClientAction(PlayerId, ClientActionType.Hu, targetTile);
                        action.SetHuDetails(totalFan, fanDetails);
                        _server.SubmitAction(action);
                        actionTaken = true;
                    }

                    _isWaitingForUI = false;
                    ActionPanelController.Instance.Hide();
                    GameHUDController.Instance?.StopTimer();
                });

                while (_isWaitingForUI)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                if (!actionTaken)
                    _server.SubmitAction(ClientAction.Skip(PlayerId));
            }
            catch (OperationCanceledException)
            {
                GameHUDController.Instance?.StopTimer();
            }
        }

        private async void HandleOtherPlayerDiscarded(int discarderId, TileData discardedTile, bool applyDiscardVisual, float? recoveryTimerSeconds)
        {
            if (!Network.ResponseActionPolicy.CanRespondToDiscard(PlayerId, discarderId)) return;

            using var operationCancellation = CreateOperationCancellation();
            var ct = operationCancellation.Token;
            try
            {
                // A recovery snapshot already rebuilt this river, but the same discard still
                // must be removed if a later Chi/Pon/MingGan claims it.
                _lastDiscarderId = discarderId;

                // 表现层：其他玩家打出牌，渲染到其牌河
                if (applyDiscardVisual)
                {
                    var view = GameManager.Instance.GetOpponentView(discarderId);
                    if (view != null) view.DiscardTile(discardedTile);
                }

                var handData = _handController.GetHandData();
                var melds = _handController.Melds;
                bool isNextPlayer = (discarderId + 1) % 4 == PlayerId;

                var actions = ActionValidator.CheckActions(handData, melds, discardedTile, isNextPlayer, _scoringOptions, _roundWind, _seatWind);

                if (actions.HasAction)
                {
                    _isWaitingForUI = true;
                    bool actionTaken = false;
                    if (recoveryTimerSeconds.HasValue)
                    {
                        GameHUDController.Instance?.StartTimer(recoveryTimerSeconds.Value, PlayerId);
                    }
                    else if (GameManager.Instance != null)
                    {
                        GameHUDController.Instance?.StartTimer(GameManager.Instance.responseTimeout, PlayerId);
                    }

                    ActionPanelController.Instance.Show(actions, (choice) =>
                    {
                        if (!_isWaitingForUI) return;

                        if (choice == ActionPanelChoice.Hu)
                        {
                            int totalFan;
                            List<string> fanDetails;
                            MahjongLogic.CheckWinWithFan(handData, melds, discardedTile, false, out totalFan, out fanDetails, _roundWind, _seatWind, _scoringOptions);
                            Debug.Log($"[LocalPlayer] 请求点炮胡: 玩家{PlayerId}, 目标={discardedTile}, 手牌=[{string.Join(", ", handData)}], 副露数={melds.Count}, 番数={totalFan}");

                            var action = new ClientAction(PlayerId, ClientActionType.Hu, discardedTile);
                            action.SetHuDetails(totalFan, fanDetails);
                            _server.SubmitAction(action);
                            actionTaken = true;
                        }
                        else if (choice == ActionPanelChoice.Pon)
                        {
                            _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Pon, discardedTile));
                            actionTaken = true;
                        }
                        else if (choice == ActionPanelChoice.MingGan)
                        {
                            _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.MingGan, discardedTile));
                            actionTaken = true;
                        }
                        else if (choice == ActionPanelChoice.Chi)
                        {
                            var chiOptions = ActionValidator.GetChiCombinations(handData, discardedTile);

                            if (chiOptions.Count == 1)
                            {
                                _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Chi, discardedTile, chiOptions[0]));
                                actionTaken = true;
                            }
                            else if (chiOptions.Count > 1)
                            {
                                List<string> optionStrs = chiOptions.Select(arr => $"{arr[0]},{arr[1]}").ToList();

                                ActionPanelController.Instance.ShowChiSelection(optionStrs, (selectedIndex) =>
                                {
                                    _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Chi, discardedTile, chiOptions[selectedIndex]));
                                    _isWaitingForUI = false;
                                    GameHUDController.Instance?.StopTimer();
                                });
                                return;
                            }
                        }

                        _isWaitingForUI = false;
                        ActionPanelController.Instance.Hide();
                        GameHUDController.Instance?.StopTimer();
                    });

                    while (_isWaitingForUI)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }

                    if (actionTaken) return;
                }

                _server.SubmitAction(ClientAction.Skip(PlayerId));
            }
            catch (OperationCanceledException)
            {
                // 响应超时 — 服务端自动填充 Skip
                GameHUDController.Instance?.StopTimer();
            }
        }

        public void OnActionResolved(int actionPlayerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations)
        {
            // 从打出该牌的玩家牌河中移除这张牌 (不论是谁吃碰杠)
            if (actionType != ClientActionType.AnGan && actionType != ClientActionType.JiaGang && _lastDiscarderId != -1)
            {
                if (_lastDiscarderId == PlayerId) 
                    _handController.myRiver?.RemoveLastDiscard();
                else 
                    GameManager.Instance.GetOpponentView(_lastDiscarderId)?.myRiver?.RemoveLastDiscard();
                
                _lastDiscarderId = -1; // 重置
            }

            // 收到全局动作广播，更新表现层
            if (actionPlayerId == PlayerId)
            {
                // 如果是自己执行的动作，调用 HandController 播放对应动画并更新数据
                if (actionType == ClientActionType.Pon) _handController.ExecutePon(targetTile);
                else if (actionType == ClientActionType.Chi) _handController.ExecuteChi(targetTile, chiCombinations);
                else if (actionType == ClientActionType.MingGan) _handController.ExecuteMingGan(targetTile);
                else if (actionType == ClientActionType.AnGan) _handController.ExecuteAnGan(targetTile);
                else if (actionType == ClientActionType.JiaGang) _handController.ExecuteJiaGang(targetTile);
                
            }
            else
            {
                // 别人执行动作，更新别人的副露和手牌数量
                var view = GameManager.Instance.GetOpponentView(actionPlayerId);
                if (view != null)
                {
                    List<TileData> meldTiles = new List<TileData> { targetTile };
                    
                    if (actionType == ClientActionType.Pon) { meldTiles.Add(targetTile); meldTiles.Add(targetTile); }
                    else if (actionType == ClientActionType.MingGan || actionType == ClientActionType.AnGan) { meldTiles.Add(targetTile); meldTiles.Add(targetTile); meldTiles.Add(targetTile); }
                    else if (actionType == ClientActionType.Chi)
                    {
                        meldTiles.Add(new TileData(targetTile.TileSuit, chiCombinations[0], targetTile.OriginalOwnerID));
                        meldTiles.Add(new TileData(targetTile.TileSuit, chiCombinations[1], targetTile.OriginalOwnerID));
                        meldTiles.Sort((a,b) => a.Value.CompareTo(b.Value));
                    }

                    if (actionType == ClientActionType.Pon) view.ExecuteMeld(MeldType.Pon, meldTiles);
                    else if (actionType == ClientActionType.Chi) view.ExecuteMeld(MeldType.Chi, meldTiles);
                    else if (actionType == ClientActionType.MingGan) view.ExecuteMeld(MeldType.Kan_Exposed, meldTiles);
                    else if (actionType == ClientActionType.AnGan) view.ExecuteMeld(MeldType.Kan_Concealed, meldTiles);
                    else if (actionType == ClientActionType.JiaGang) view.ExecuteMeld(MeldType.Kan_Added, meldTiles);
                }
                Debug.Log($"[LocalPlayer] 观察到玩家 {actionPlayerId} 执行了 {actionType}");
            }
        }

        public void OnTurnWithoutDraw()
        {
            ResumeMainTurnDecision(null, null);
        }

        private async Task WaitForDiscardAfterAction(float? recoveryTimerSeconds, CancellationToken ct)
        {
            _handController.SetInteractable(true);
            if (recoveryTimerSeconds.HasValue)
            {
                GameHUDController.Instance?.StartTimer(recoveryTimerSeconds.Value, PlayerId);
            }
            else if (GameManager.Instance != null)
            {
                GameHUDController.Instance?.StartTimer(GameManager.Instance.actionTimeout, PlayerId);
            }

            var tcs = new TaskCompletionSource<TileData>();
            Action<TileData> onDiscard = (tile) => tcs.TrySetResult(tile);
            using (ct.Register(() => tcs.TrySetCanceled()))
            {
                _handController.OnTileDiscardedEvent += onDiscard;
                try
                {
                    var discardedTile = await tcs.Task;
                    _handController.OnTileDiscardedEvent -= onDiscard;
                    _handController.SetInteractable(false);
                    GameHUDController.Instance?.StopTimer();
                    _server.SubmitAction(ClientAction.Discard(PlayerId, discardedTile));
                }
                catch (TaskCanceledException)
                {
                    _handController.OnTileDiscardedEvent -= onDiscard;
                    GameHUDController.Instance?.StopTimer();
                    throw;
                }
            }
        }

        public void OnDrawGame()
        {
            _isWaitingForUI = false;
            ActionPanelController.Instance?.Hide();
            _handController.SetInteractable(false);
            GameHUDController.Instance?.StopTimer();
            ResultPanelController.Instance?.SetSessionInfo(GameManager.Instance?.Session);
            ResultPanelController.Instance.ShowDraw(new List<string> { "流局" });
        }

        public void OnPlayerWin(int winnerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
            WinKind winKind, int loserId, WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown)
        {
            _isWaitingForUI = false;
            ActionPanelController.Instance?.Hide();
            _handController.SetInteractable(false);
            GameHUDController.Instance?.StopTimer();
            ResultPanelController.Instance?.SetSessionInfo(GameManager.Instance?.Session);

            new LocalResultPresentationBridge(ResultPanelController.Instance).ShowLiveWin(
                PlayerId, winnerId, totalFan, fanDetails, isSelfDraw, winningHand,
                talentFanBreakdown);
        }

        public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex)
        {
            _roundWind = prevalentWind;
            _seatWind = seatWind;
            // 同步到 HandController 供听牌提示使用
            _handController.RoundWind = prevalentWind;
            _handController.SeatWind = seatWind;
            Debug.Log($"[LocalPlayer] 第{roundNumber}局开始 - 圈风:{prevalentWind} 门风:{seatWind}");
        }

        public void OnSessionEnd(int[] finalScores)
        {
            Debug.Log($"[LocalPlayer] 对战结束 - 分数: {string.Join(",", finalScores)}");
        }

        public void OnWallCountChanged(int remainingCount)
        {
            GameHUDController.Instance?.UpdateRemainingCount(remainingCount);
        }

        public void OnTimeout(TileData autoDiscardedTile)
        {
            _isWaitingForUI = false;
            ActionPanelController.Instance.Hide();
            _handController.SetInteractable(false);

            // 同步手牌：移除被自动出的牌（视觉+数据）
            if (autoDiscardedTile != null)
            {
                _handController.ForceRemoveTile(autoDiscardedTile);
            }

            Debug.LogWarning($"[LocalPlayer] 操作超时，自动出牌: {autoDiscardedTile}");
        }

        public void OnTalentInfo(ScoringOptions scoringOptions)
        {
            _scoringOptions = scoringOptions;
            _handController.ScoringOptions = scoringOptions;
            if (scoringOptions != null && scoringOptions.BonusFan > 0)
                Debug.Log($"[LocalPlayer] 天赋加成: 番数+{scoringOptions.BonusFan}");
            if (scoringOptions != null && scoringOptions.RelaxedPureStraight)
                Debug.Log($"[LocalPlayer] 天赋加成: 宽松清龙判定");
        }

        public void OnPeekWallTiles(List<TileData> topTiles)
        {
            if (FloatingTilePanelController.Instance != null)
            {
                FloatingTilePanelController.Instance.ShowTiles(
                    $"窥探 - 牌山顶部 {topTiles.Count} 张", topTiles, 8f);
            }
        }
    }
}
