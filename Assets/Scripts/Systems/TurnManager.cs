using UnityEngine;
using System.Collections;
using MahjongGame.Core;
using MahjongGame.UI; // 引用 UI 命名空间
using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Systems
{
    public class TurnManager : MonoBehaviour
    {
        public static TurnManager Instance;

        [Header("State")]
        public int currentPlayerIndex = 0; // 0=玩家, 1-3=AI
        public bool isGameActive = false;

        [Header("Refs")]
        // 这里应该是一个数组，存4个玩家的控制器
        // 为了简化，我们暂时只引用玩家的 HandController
        public HandController playerController; 
        
        // 我们假设 AI 也有 HandController，实际项目中应该用基类或者接口
        // 这里暂时先只处理玩家逻辑

        // 标志位：是否正在等待 UI 输入
        private bool _isWaitingForUI = false;
        
        // 标志位：是否正在等待玩家出牌
        private bool _isWaitingForDiscard = false;

        // 标记：本回合的标准流程是否被打断 (因为吃碰杠)
        private bool _turnFlowInterrupted = false;

        // 标记：下个回合是否跳过摸牌 (吃碰后跳过，杠后不跳过)
        private bool _skipNextDraw = false;

        void Awake() { Instance = this; }

        /// <summary>
        /// 游戏初始化完成后调用
        /// </summary>
        public void StartGameLoop()
        {
            isGameActive = true;
            currentPlayerIndex = 0; // 庄家先手 (这里写死为 P0)
            
            StartCoroutine(TurnRoutine());
        }

        IEnumerator TurnRoutine()
        {
            // 简单的回合循环
            while (isGameActive)
            {
                Debug.Log($"--- 轮到玩家 {currentPlayerIndex} ---");

                // 1. 摸牌阶段
                // 如果被吃碰打断(_turnFlowInterrupted=true)，通常意味着跳过摸牌(_skipNextDraw=true)
                // 但如果是杠(_turnFlowInterrupted=true)，则需要摸牌(_skipNextDraw=false)
                
                // 重置打断标记 (它已经在上一轮末尾阻止了玩家切换，现在新回合开始，重置它)
                if (_turnFlowInterrupted)
                {
                    _turnFlowInterrupted = false;
                }

                if (!_skipNextDraw) 
                {
                    yield return StartCoroutine(DrawPhase());
                }
                else
                {
                    // 跳过摸牌 (吃/碰)
                    _skipNextDraw = false; 
                }

                // 2. 思考/出牌阶段 (Action Phase)
                // 在这里处理自摸、暗杠，或者打出一张牌
                TileData discardedTile = null;
                yield return StartCoroutine(ActionPhase(result => discardedTile = result));

                // 3. 响应阶段 (Response Phase)
                // 别人打牌了，其他人能不能吃碰杠胡？
                if (discardedTile != null)
                {
                    // 这里会根据玩家选择修改 _turnFlowInterrupted
                    yield return StartCoroutine(ResponsePhase(discardedTile));
                }
                
                // 4. 切换到下家 (如果没胡也没发生特殊流转)
                if (isGameActive && !_turnFlowInterrupted)
                {
                    currentPlayerIndex = (currentPlayerIndex + 1) % 4;
                }
            }
        }

        IEnumerator DrawPhase()
        {
            // 如果是玩家 (Index 0)
            if (currentPlayerIndex == 0)
            {
                // 调用玩家控制器的摸牌逻辑
                playerController.DrawCard();
                yield return new WaitForSeconds(0.5f); // 动画缓冲
            }
            else
            {
                // TODO: AI 摸牌
                Debug.Log($"AI {currentPlayerIndex} 摸牌 (模拟)");
                yield return new WaitForSeconds(0.2f);
            }
        }

        // --- 阶段 2: 出牌/自摸 ---
        // 使用 System.Action 回调传出打出的牌
        IEnumerator ActionPhase(System.Action<TileData> onDiscard)
        {
            if (currentPlayerIndex == 0)
            {
                // === 玩家回合 ===
                // 1. 检查自摸动作 (胡、暗杠、加杠)
                // 传入刚摸到的牌 (如果有的话)
                var lastDrawn = playerController.LastDrawnData;

                // 如果是刚吃碰完跳过摸牌进来的，lastDrawn 是 null (在 HandController 里吃碰后置空了)
                // 这种情况下不能自摸胡 (除非天胡，但天胡逻辑不同)，也不能暗杠
                if (lastDrawn != null) 
                {
                    yield return StartCoroutine(CheckSelfActions(lastDrawn));
                }

                // 如果 CheckSelfActions 触发了杠，流程会被中断（进入岭上开花）
                // 所以我们需要检查 _turnFlowInterrupted
                if (_turnFlowInterrupted) 
                {
                    // 杠牌后，TurnRoutine 会进入下一轮（但禁止换人）
                    // 从而触发 DrawPhase (岭上摸牌) -> 再次进入 ActionPhase
                    // 所以这里直接退出即可
                    yield break; 
                }

                // 2. 等待打牌
                playerController.SetInteractable(true);
                _isWaitingForDiscard = true;
                
                // 阻塞直到 OnPlayerDiscarded 被调用
                while (_isWaitingForDiscard) yield return null;
                
                playerController.SetInteractable(false);

                // 获取玩家刚才打出的牌 (从 River 获取)
                var river = playerController.myRiver; 
                var lastDiscardVisual = river.GetLastDiscard();
                
                if (lastDiscardVisual != null)
                    onDiscard?.Invoke(lastDiscardVisual.Data);
            }
            else
            {
                // === AI 回合 ===
                yield return new WaitForSeconds(0.5f); // 模拟思考

                // 模拟 AI 打出一张牌
                TileData aiDiscard = null;
                if (currentPlayerIndex == 1) aiDiscard = new TileData(Suit.Dragon, 2, currentPlayerIndex); // 发财
                else if (currentPlayerIndex == 2) aiDiscard = new TileData(Suit.Pin, 3, currentPlayerIndex); // 3饼
                else if (currentPlayerIndex == 3) aiDiscard = new TileData(Suit.Man, 5, currentPlayerIndex); // 9万

                Debug.Log($"AI {currentPlayerIndex} 打出了: {aiDiscard}");
                
                // 这里应该调用 AI 的 River 显示牌，这里简化直接返回数据
                onDiscard?.Invoke(aiDiscard);
            }
        }

        // --- 阶段 3: 响应 (吃碰杠胡检测) ---
        IEnumerator ResponsePhase(TileData discardedTile)
        {
            // 如果打牌的是玩家，AI 需要检测 (以后写)
            if (currentPlayerIndex == 0) 
            {
                yield break; 
            }

            // 如果打牌的是 AI，检测玩家(ID 0)能不能操作
            // 注意：只有当玩家不是当前打牌的人时才检测
            
            // 1. 获取玩家数据
            var handData = playerController.GetHandData();
            var melds = playerController.Melds;
            bool isNextPlayer = (currentPlayerIndex + 1) % 4 == 0; // 是否是我的上家

            // 2. 校验权限
            var actions = ActionValidator.CheckActions(handData, melds, discardedTile, isNextPlayer);

            if (actions.HasAction)
            {
                Debug.Log($"<color=yellow>检测到玩家有操作权限！(胡:{actions.CanHu} 碰:{actions.CanPon})</color>");
                
                // 3. 唤起 UI 并暂停
                _isWaitingForUI = true;

                ActionPanelController.Instance.Show(actions, (choice) => 
                {
                    // 如果我已经不再等待UI输入（说明已经处理过一次了），直接返回
                    if (!_isWaitingForUI) return;

                    Debug.Log($"玩家选择: {choice}");
                    
                    if (choice == "Skip")
                    {
                        // 玩家点过，什么都不做，流程继续
                    }
                    else if (choice == "Hu")
                    {
                        Debug.Log("玩家胡牌！游戏结束！");
                        // 玩家点炮胡
                        // discardedTile 是别人打的牌， isSelfDraw = false
                        PerformHu(discardedTile, false);
                    }
                    else if (choice == "Pon")
                    {
                        Debug.Log("玩家执行碰牌！");

                        PerformPon(discardedTile);
                    } else if (choice == "Chi")
                    {
                        // 1. 获取组合 (List<int[]>)
                            var combos = playerController.GetChiCombinations(discardedTile);
                            
                            if (combos.Count == 1)
                            {
                                PerformChi(discardedTile, combos[0]);
                            }
                            else if (combos.Count > 1)
                            {
                                // 多选一
                                Debug.Log($"combo数量： {combos.Count}");
                                var optionsStr = combos.Select(c => $"{c[0]}{discardedTile.GetSuitName()} {c[1]}{discardedTile.GetSuitName()}").ToList();
                                Debug.Log($"optionsStr数量： {optionsStr.Count}");
                                ActionPanelController.Instance.ShowChiSelection(optionsStr, (idx) => 
                                {
                                    if (!_isWaitingForUI) return;
                                    PerformChi(discardedTile, combos[idx]);
                                });
                                return; // 提前返回，等待二级回调
                            }
                    } else if (choice == "Gan")
                    {
                        PerformMingGan(discardedTile);
                    }

                    _isWaitingForUI = false; // 解除暂停
                    ActionPanelController.Instance.Hide();
                });

                // 4. 阻塞等待
                while (_isWaitingForUI) yield return null;

                // 如果玩家进行了操作（比如碰），那么原来的“下家摸牌”流程就被打断了
                // 这里需要特殊的流转控制，暂时不展开
            }
        }

        private void PerformChi(TileData target, int[] eatingValues)
        {
            playerController.ExecuteChi(target, eatingValues);
            playerController.myRiver.RemoveLastDiscard();
            
            // 核心流转修改：
            currentPlayerIndex = 0; // 轮到我
            _turnFlowInterrupted = true; // 标记流程中断 (阻止 TurnRoutine 最后那行 +1 操作)
            _skipNextDraw = true; // 吃牌后不摸牌，直接出牌
            _isWaitingForUI = false; // 解锁协程
            ActionPanelController.Instance.Hide();
        }

        private void PerformPon(TileData target)
        {
            playerController.ExecutePon(target); // 记得把 HandController 的 ExecutePon 参数也改一下或者重载
            playerController.myRiver.RemoveLastDiscard();
            
            currentPlayerIndex = 0;
            _turnFlowInterrupted = true;
            _skipNextDraw = true; // 碰牌后不摸牌，直接出牌
            _isWaitingForUI = false;
            ActionPanelController.Instance.Hide();
        }

        private void PerformMingGan(TileData target)
        {
            Debug.Log("玩家执行明杠！");
            
            // 1. 执行视觉与数据 (手牌-3, 副露+4)
            playerController.ExecuteMingGan(target);
            playerController.myRiver.RemoveLastDiscard();

            // 2. 状态机跳转
            currentPlayerIndex = -1;
            
            // 3. 【关键区别】杠牌后需要“岭上开花” (摸一张牌)
            _skipNextDraw = false; // 明杠需要摸牌
            _isWaitingForUI = false; // 解锁协程
            ActionPanelController.Instance.Hide();
        }

        // 【新增】检查自己的特殊操作
        IEnumerator CheckSelfActions(TileData currentTile)
        {
            // 1. 获取权限
            var handData = playerController.GetHandData();
            var melds = playerController.Melds;
            
            // 调用 Validator
            var actions = ActionValidator.CheckSelfActions(handData, melds, currentTile);
            
            if (actions.HasAction)
            {
                Debug.Log("检测到自摸/杠机会！");
                _isWaitingForUI = true;
                bool actionTaken = false;

                ActionPanelController.Instance.Show(actions, (choice) => 
                {
                    if (!_isWaitingForUI) return;

                    if (choice == "Hu")
                    {
                        // 自摸胡
                        PerformHu(currentTile, true);
                        actionTaken = true;
                    }
                    else if (choice == "Gan")
                    {
                        // 这里比较复杂：可能同时有 暗杠 和 加杠 的机会
                        // 1. 获取暗杠选项
                        var anGanOpts = playerController.GetAnGanOptions();
                        // 2. 获取加杠选项
                        var jiaGanOpts = playerController.GetJiaGangOptions();

                        var allGanOpts = new List<TileData>();
                        allGanOpts.AddRange(anGanOpts);
                        allGanOpts.AddRange(jiaGanOpts);

                        if (allGanOpts.Count == 1)
                        {
                            // 只有一种杠，直接执行
                            TileData target = allGanOpts[0];
                            // 判断是暗杠还是加杠
                            if (anGanOpts.Contains(target)) PerformAnGan(target);
                            else PerformJiaGang(target);
                        }
                        else
                        {
                            // 多种杠 (极罕见)，显示二级菜单
                            // 这里复用 ShowChiSelection 或者简单的 Log，实际项目需要 UI 支持
                            // 简单起见，默认杠第一个
                            TileData target = allGanOpts[0];
                            if (anGanOpts.Contains(target)) PerformAnGan(target);
                            else PerformJiaGang(target);
                        }
                        actionTaken = true;
                    }
                    else if (choice == "Skip")
                    {
                        // 玩家放弃自摸/杠，继续打牌
                    }

                    _isWaitingForUI = false;
                    ActionPanelController.Instance.Hide();
                });

                while (_isWaitingForUI) yield return null;

                if (actionTaken)
                {
                    // 标记中断，ActionPhase 会提前退出
                    // 1. 如果是胡 -> 游戏结束 (PerformHu 内部处理)
                    // 2. 如果是杠 -> _turnFlowInterrupted = true, _skipNextDraw = false
                    //    -> 下一轮 DrawPhase (岭上开花)
                    _turnFlowInterrupted = true;
                    if (isGameActive) _skipNextDraw = false; // 杠牌必摸牌
                }
            }
        }

        private void PerformAnGan(TileData target)
        {
            Debug.Log("执行暗杠！");
            playerController.ExecuteAnGan(target);

            // 状态流转：
            // 保持当前玩家
            currentPlayerIndex = 0; // 保持是自己 (因为 _turnFlowInterrupted 会阻止 +1)
            
            // 允许摸牌 (岭上开花)
            _skipNextDraw = false; 
            
            // 标记中断
            _turnFlowInterrupted = true;
        }

        // 新增 PerformJiaGang
        private void PerformJiaGang(TileData target)
        {
            Debug.Log("执行加杠！");
            playerController.ExecuteJiaGang(target);

            // 状态流转
            currentPlayerIndex = 0; // 保持当前玩家
            _skipNextDraw = false;   // 岭上开花
            _turnFlowInterrupted = true;
        }

        private void PerformHu(TileData winningTile, bool isSelfDraw)
        {
            Debug.Log("执行胡牌结算...");
            isGameActive = false; // 停止游戏循环
            _turnFlowInterrupted = true;
            ActionPanelController.Instance.Hide(); // 隐藏操作按钮

            // 1. 收集算番所需的数据
            var handTiles = playerController.GetHandData();
            var melds = playerController.Melds;
            
            // 2. 调用计算核心
            // 注意：我们直接调用 MahjongLogic.CheckWinWithFan 来获取番数详情
            // 虽然名字叫 Check... 但它内部会算出 totalFan 和 details
            
            int totalFan;
            List<string> fanDetails;
            
            // 这里的 CheckWinWithFan 需要稍微改一下 MahjongLogic，
            // 或者是直接使用 FanCalculator (如果你之前公开了)
            // 假设我们使用 MahjongLogic 的静态封装：
            
            bool canWin = MahjongLogic.CheckWinWithFan(
                handTiles, 
                melds, 
                winningTile, 
                isSelfDraw, 
                out totalFan, 
                out fanDetails
            );

            if (canWin || true) // || true 是为了测试方便，防止因为番数不足8番而不显示面板
            {
                // 3. 显示结算面板
                MahjongGame.UI.ResultPanelController.Instance.ShowWin(totalFan, fanDetails, isSelfDraw);
            }
            else
            {
                Debug.LogWarning("逻辑矛盾：UI允许胡牌，但结算校验失败（可能是番数不足）");
            }
        }

        /// <summary>
        /// 供外部调用的回调：玩家打牌完成
        /// </summary>
        public void OnPlayerDiscarded()
        {
            if (currentPlayerIndex == 0)
            {
                _isWaitingForDiscard = false; // 解除阻塞，进入下一回合
            }
        }
    }
}
