# SuperMajiang 任务与优化计划 (Project Plan)

本文档记录了项目的开发进度、待办事项 (Backlog) 及长期的优化路线图。

## 1. 近期开发任务 (Backlog)

### High Priority (高优先级)
*   **[已完成] 结算 UI 对接**: 
    *   [x] 将 `MahjongLogic.CheckWinWithFan` 返回的详细番种列表渲染至 `ResultPanel`。
    *   [x] 扩大 UI 面板，确保一次性展示所有番种列表而无需滚动。
    *   [x] **得分动效**: 实现得分（番数）从 0 开始滚动增加的视觉特效。
*   **[已完成] AI 决策逻辑与网络架构重构**: 
    *   [x] 引入 "Fat Client, Thin Server" 架构，拆分 `GameServer` 与 Client Agents，支持未来的联机扩展。
    *   [x] 实现 `SimpleAIClient`，完成基于孤张判定的出牌与吃碰杠胡基础响应。
*   **[已完成] 流局与失败展示**:
    *   [x] 完善流局 (`ShowDraw`) 时各家听牌状态的展示。
    *   [x] 实现玩家输掉比赛（AI 胡牌）时的失败视觉反馈。

### Medium Priority (中优先级)
*   **番种规则精修**:
    *   目前已补全 40+ 种核心番种（含大三元、四暗刻、各种色别规则等）。
    *   **边缘大牌**: 补全四杠子、九莲宝灯、连七对等极罕见 88 番。
    *   **逻辑健壮性**: 针对异化牌库（如同种牌 8 张以上）的极端组合进行算番压力测试。
*   **手牌缩略图**: 
    *   在结算面板展示胡牌瞬间的 2D 手牌排布，方便玩家复盘。

## 2. 系统优化路线图 (Optimization)

### 低优先级 / 未来规划 (Low Priority / Future)
*   **表现层调度器 (View Coordinator) 重构**:
    *   鉴于原有的 `TurnManager` 已经被移除，未来如果需要处理复杂的跨动画协同（如处理多个玩家同时发生的特效、控制摄像机镜头切换、管理全局倒计时等），需要引入一个专门的 `MatchViewController` 或 `AnimationCoordinator` 来统筹。这不属于核心逻辑范畴。

### 性能优化
*   **对象池 (Object Pooling)**: 引入 `TilePool` 管理 `TileVisual` 的生成与回收，减少高频实例化带来的内存抖动。
*   **资源预加载**: 在 `DeckManager` 初始化时缓存 `TileResourceConfig` 中的所有 Sprite，消除摸牌时的瞬间卡顿。

### 视觉与交互增强
*   **发牌动画**: 实现牌从牌山飞入手牌位置的 DoTween 序列动画。
*   **牌河指针**: 动态显示一个“浮标”或“高亮”，标记当前最新的打牌位置。
*   **动态视觉反馈**: 
    *   根据 `TileData` 的异化状态（如被天赋修改过），通过 `TileVisual` 改变牌背颜色或增加发光特效。

### 核心算法演进
*   **优先级自动判定**: 在 `FanRule` 中引入更智能的排斥算法（如：基于 RuleGroup），减少手动维护 `ExcludedRuleIds` 的工作量。
*   **算番方案评价**: 优化多路径拆解的评分权重，确保不仅番数最高，且符合国标计分的拆解习惯。
