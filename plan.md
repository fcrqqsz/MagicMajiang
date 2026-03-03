# SuperMajiang 开发计划 (Development Plan)

本文档记录了项目的开发进度、当前重心及长期优化路径。

## 1. 项目里程碑 (Milestones - Completed)

### 核心循环与逻辑
*   [x] **"胖客户端，瘦服务端" 架构重构**: 实现了 `GameServer` 与 `ClientAgents` 的解耦，支持本地/AI 统一接口。
*   [x] **算番引擎基础构建**: 完成了 40+ 种核心番种判定，支持多路径拆解与番数最大化搜索。
*   [x] **基础 AI 实现**: `SimpleAIClient` 支持基本的基于孤张判定的出牌与吃碰杠胡逻辑。

### UI 与 交互
*   [x] **结算系统对接**: `ResultPanel` 已支持详细番种列表展示及得分滚动特效。
*   [x] **对局流转展示**: 完善了流局、玩家失败（AI胡牌）的视觉反馈。

---

## 2. 当前开发任务 (Active Backlog)

### High Priority (高优先级 - 逻辑与核心表现)
*   **[已完成] 副露 (Meld) 视觉精修**: 
    *   [x] 完善 `HandController.CreateMeldVisual` 中的 3D 旋转与堆叠逻辑（如加杠的叠放）。
    *   [x] 实现副露按国标规则从右向左排列，并精调横置防穿模逻辑。
*   **[已完成] 番种规则补全 (88番/罕见牌)**:
    *   [x] **88番大牌**: 补全四杠子、九莲宝灯、连七对、十三幺（国士无双）等。
    *   [ ] **逻辑健壮性**: 针对异化牌库（如同种牌 8 张以上）的极端组合进行算番压力测试。
*   **发牌与摸牌动画**: 
    *   [ ] 使用 DoTween 实现牌从牌山飞入位置的序列动画，而非瞬间生成。
    *   [ ] 优化“新摸牌”与“已有手牌”之间的 `drawGap` 动态管理。

### Medium Priority (中优先级 - 视觉增强与复盘)
*   **结算手牌缩略图**: 
    *   [ ] 在 `ResultPanel` 中增加胡牌瞬间的 2D 手牌（含副露）排布，方便玩家复盘。
*   **牌河指针 (River Pointer)**: 
    *   [ ] 在 `RiverController` 中实现一个动态“浮标”，标记当前最新出的那张牌。
*   **异化牌视觉反馈**: 
    *   [ ] 根据 `TileData` 的异化状态（天赋修改），通过 `TileVisual` 改变牌背颜色或增加发光特效。

---

## 3. 长期优化路线图 (Optimization & Infrastructure)

### 性能优化
*   **对象池 (Object Pooling)**: 引入 `TilePool` 管理 `TileVisual` 的生成与回收，减少高频实例化。
*   **资源预加载**: 在 `DeckManager` 初始化时缓存所有牌面 Sprite，消除首帧加载卡顿。

### 架构演进
*   **算番排斥逻辑自动化**: 在 `FanRule` 中引入 `RuleGroup` 判定，减少手动维护 `ExcludedRuleIds` 的成本。
*   **表现层调度器 (View Coordinator)**: 引入 `AnimationCoordinator` 统筹管理复杂的跨角色动画序列（如：A家杠 -> B家补花 -> C家胡牌）。
