using UnityEngine;
using System.Collections.Generic;

namespace MahjongGame.Systems
{
    /// <summary>
    /// TurnManager 现在被降级为纯粹的表现层同步控制器 (View Controller)。
    /// 它的职责是：在旧版本中管理流程，而在新的 Fat Client/Thin Server 架构下，
    /// 这里的职责将逐步被 LocalPlayerClient 取代。
    /// 
    /// 当前版本：作为旧 API 兼容层保留，或作为纯表现动画触发器（监听 GameServer）。
    /// </summary>
    public class TurnManager : MonoBehaviour
    {
        public static TurnManager Instance;

        [Header("Refs")]
        public Core.HandController playerController; 

        void Awake() { Instance = this; }

        // [旧架构入口，已被 GameManager 中的 GameServer 替代，可在此打印警告]
        public void StartGameLoop()
        {
            Debug.LogWarning("[TurnManager] 警告：尝试启动旧版单机循环。此方法已被 GameServer 接管，不应再被调用。");
        }

        /// <summary>
        /// 供 HandController 调用的回调：玩家 3D 模型完成打牌动画。
        /// 在新架构下，事件通常由 Agent / Server 控制流转。
        /// 保留此方法仅为兼容现有 HandController 中的调用（它会在此通知我们动画结束）。
        /// </summary>
        public void OnPlayerDiscarded()
        {
            // 在新的胖客户端架构中，LocalPlayerClient 会通过监听 HandController 的 OnTileDiscardedEvent 
            // 获知打牌动作，并直接向 Server 提交 ClientAction.Discard。
            // 因此这里不再需要做业务流转。
            Debug.Log("[TurnManager] 检测到玩家完成打牌动作 (仅作表现层日志)");
        }
    }
}