using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using MahjongGame.Core.Network;
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
        
        private IServer _server;
        private HandController _handController;

        // 本地状态
        private bool _isWaitingForUI = false;
        private int _lastDiscarderId = -1; // 记录最后打牌的人

        // 风位信息
        private WindDirection _roundWind = WindDirection.East;
        private WindDirection _seatWind = WindDirection.East;

        public LocalPlayerClient(int playerId, IServer server, HandController handController)
        {
            PlayerId = playerId;
            _server = server;
            _handController = handController;
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
            for (int i = 1; i < 4; i++)
            {
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
            // 表现层：摸牌动画
            _handController.DrawCardData(drawnTile); 
            await Task.Delay(300); // 稍微等待动画

            var handData = _handController.GetHandData();
            var melds = _handController.Melds;

            // 1. 检查自摸、暗杠
            var actions = ActionValidator.CheckSelfActions(handData, melds, drawnTile);
            if (actions.HasAction)
            {
                bool actionTaken = false;
                _isWaitingForUI = true;
                
                ActionPanelController.Instance.Show(actions, (choice) => 
                {
                    if (!_isWaitingForUI) return;

                    if (choice == "Hu")
                    {
                        int totalFan;
                        List<string> fanDetails;
                        MahjongLogic.CheckWinWithFan(handData, melds, drawnTile, true, out totalFan, out fanDetails, _roundWind, _seatWind);
                        
                        var action = new ClientAction(PlayerId, ClientActionType.Hu, drawnTile);
                        action.SetHuDetails(totalFan, fanDetails);
                        _server.SubmitAction(action);
                        actionTaken = true;
                    }
                    else if (choice == "Gan")
                    {
                        // 简化：默认发送暗杠
                        var anGanOpts = _handController.GetAnGanOptions();
                        if (anGanOpts.Count > 0)
                        {
                            _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.AnGan, anGanOpts[0]));
                        }
                        else
                        {
                            var jiaGanOpts = _handController.GetJiaGangOptions();
                            _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.JiaGang, jiaGanOpts[0]));
                        }
                        actionTaken = true;
                    }
                    
                    _isWaitingForUI = false;
                    ActionPanelController.Instance.Hide();
                });

                // 等待 UI 操作
                while (_isWaitingForUI) await Task.Yield();

                if (actionTaken) return; // 如果玩家选择了胡或杠，就直接返回，不进入打牌阶段
            }

            // 2. 等待玩家打出牌
            _handController.SetInteractable(true);
            
            // 这里我们需要一种机制来等待玩家在 3D 场景中点击某张牌并打出
            // 暂用一个轮询来模拟等待（在实际重构中，可以让 HandController 在打牌时触发事件）
            TileData discardedTile = null;
            
            // 为了保持接口简单，我们假设向 HandController 注册了一个一次性的回调
            var tcs = new TaskCompletionSource<TileData>();
            Action<TileData> onDiscard = (tile) => tcs.TrySetResult(tile);
            
            _handController.OnTileDiscardedEvent += onDiscard;
            discardedTile = await tcs.Task;
            _handController.OnTileDiscardedEvent -= onDiscard;

            _handController.SetInteractable(false);
            
            _server.SubmitAction(ClientAction.Discard(PlayerId, discardedTile));
        }

        public async void OnOtherPlayerDiscarded(int discarderId, TileData discardedTile)
        {
            _lastDiscarderId = discarderId; // 记录最后出牌人
            
            // 表现层：其他玩家打出牌，渲染到其牌河
            var view = GameManager.Instance.GetOpponentView(discarderId);
            if (view != null) view.DiscardTile(discardedTile);

            var handData = _handController.GetHandData();
            var melds = _handController.Melds;
            bool isNextPlayer = (discarderId + 1) % 4 == PlayerId;

            var actions = ActionValidator.CheckActions(handData, melds, discardedTile, isNextPlayer);

            if (actions.HasAction)
            {
                _isWaitingForUI = true;
                bool actionTaken = false;

                ActionPanelController.Instance.Show(actions, (choice) => 
                {
                    if (!_isWaitingForUI) return;

                    if (choice == "Hu")
                    {
                        int totalFan;
                        List<string> fanDetails;
                        MahjongLogic.CheckWinWithFan(handData, melds, discardedTile, false, out totalFan, out fanDetails, _roundWind, _seatWind);
                        
                        var action = new ClientAction(PlayerId, ClientActionType.Hu, discardedTile);
                        action.SetHuDetails(totalFan, fanDetails);
                        _server.SubmitAction(action);
                        actionTaken = true;
                    }
                    else if (choice == "Pon")
                    {
                        _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Pon, discardedTile));
                        actionTaken = true;
                    }
                    else if (choice == "Gan")
                    {
                        _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.MingGan, discardedTile));
                        actionTaken = true;
                    }
                    else if (choice == "Chi")
                    {
                        // 获取所有能吃的组合
                        var chiOptions = ActionValidator.GetChiCombinations(handData, discardedTile);
                        
                        if (chiOptions.Count == 1)
                        {
                            // 只有一种吃法，直接提交
                            _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Chi, discardedTile, chiOptions[0]));
                            actionTaken = true;
                        }
                        else if (chiOptions.Count > 1)
                        {
                            // 多种吃法，显示二级菜单
                            // 将 int[] 转换为显示字符串，例如 "2,3"
                            List<string> optionStrs = chiOptions.Select(arr => $"{arr[0]},{arr[1]}").ToList();
                            
                            ActionPanelController.Instance.ShowChiSelection(optionStrs, (selectedIndex) => 
                            {
                                _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.Chi, discardedTile, chiOptions[selectedIndex]));
                                // 注意：这里是回调内部，不能直接设置外部的 actionTaken 变量来跳出外部循环
                                // 但因为 ShowChiSelection 会关闭面板，我们可以在这里直接结束等待
                                _isWaitingForUI = false; 
                            });
                            return; // 退出当前的 lambda，等待二级菜单回调
                        }
                    }

                    _isWaitingForUI = false;
                    ActionPanelController.Instance.Hide();
                });

                while (_isWaitingForUI) await Task.Yield();

                if (actionTaken) return;
            }

            _server.SubmitAction(ClientAction.Skip(PlayerId));
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
                
                // 本地玩家吃碰后需要立即打出一张牌，我们可以在这里直接调用 OnTileDrawn 的打牌逻辑
                // 或者在 GameServer 中由下一个状态驱动。为了简化，在胖客户端自行控制
                if (actionType == ClientActionType.Pon || actionType == ClientActionType.Chi)
                {
                    _handController.SetInteractable(true);
                    // 实际需要注册事件等待出牌，代码类似 OnTileDrawn
                    WaitForDiscardAfterAction();
                }
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

        private async void WaitForDiscardAfterAction()
        {
            var tcs = new TaskCompletionSource<TileData>();
            Action<TileData> onDiscard = (tile) => tcs.TrySetResult(tile);
            
            _handController.OnTileDiscardedEvent += onDiscard;
            TileData discardedTile = await tcs.Task;
            _handController.OnTileDiscardedEvent -= onDiscard;

            _handController.SetInteractable(false);
            _server.SubmitAction(ClientAction.Discard(PlayerId, discardedTile));
        }

        public void OnDrawGame()
        {
            ResultPanelController.Instance.ShowDraw(new List<string> { "流局" });
        }

        public void OnPlayerWin(int winnerId, int totalFan, List<string> fanDetails, bool isSelfDraw)
        {
            if (winnerId == PlayerId)
            {
                ResultPanelController.Instance.ShowWin(totalFan, fanDetails, isSelfDraw);
            }
            else
            {
                ResultPanelController.Instance.ShowLose(winnerId, totalFan, fanDetails);
            }
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
    }
}
