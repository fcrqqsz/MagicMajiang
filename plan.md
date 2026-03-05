# SuperMajiang 开发计划 (Development Plan)

本文档记录了项目的开发进度、当前重心及长期优化路径。

## 1. 项目里程碑 (Milestones - Completed)

### 核心循环与逻辑
*   [x] **"胖客户端，瘦服务端" 架构重构**: 实现了 `GameServer` 与 `ClientAgents` 的解耦，支持本地/AI 统一接口。
*   [x] **算番引擎基础构建**: 完成了 40+ 种核心番种判定，支持多路径拆解与番数最大化搜索。
*   [x] **基础 AI 实现**: `SimpleAIClient` 支持基本的基于孤张判定的出牌与吃碰杠胡逻辑。
*   [x] **多局对战系统**: 实现 `GameSession` 管理多局状态，支持单局/东风局/半庄/全庄模式，含圈风轮转、门风分配、国标计分。
*   [x] **风位 Bug 修复**: `FanContext` 风位类型从 `Suit` 改为 `WindDirection`，修复圈风刻/门风刻永远匹配西风的 bug。

### UI 与 交互
*   [x] **结算系统对接**: `ResultPanel` 已支持详细番种列表展示及得分滚动特效。
*   [x] **对局流转展示**: 完善了流局、玩家失败（AI胡牌）的视觉反馈。
*   [x] **多局结算面板**: `ResultPanel` 支持下一局/总结算切换，显示四家累计分数与排名。

---

## 2. 当前开发任务 (Active Backlog)

### High Priority (高优先级 - 游戏性反馈与 UX)
*   **[已完成] 听牌提示 (Wait Hint)**:
    *   [x] 核心算番支持：遍历手牌提供打出某张牌后的听牌列表及最大番数 (`MahjongLogic.GetWaitHints`)。
    *   [x] UI Toolkit 表现：实现 `WaitHintPanel` 横向列表，并在玩家点击选中牌时动态展示。
*   **多局对战 UI 完善**:
    *   [ ] 游戏界面显示当前圈风、门风、第几局的状态栏。
    *   [ ] DeckEditor 面板添加 GameMode 下拉选择框供玩家选择。
*   **异化牌视觉反馈**:
    *   [ ] 根据 `TileData` 的异化状态（天赋修改），通过 `TileVisual` 改变牌背颜色或增加发光特效。
*   **牌河指针 (River Pointer)**:
    *   [ ] 在 `RiverController` 中实现一个动态”浮标”，标记当前最新出的那张牌。

### Medium Priority (中优先级 - 视觉增强与复盘)
*   **结算手牌缩略图**: 
    *   [ ] 在 `ResultPanel` 中增加胡牌瞬间的 2D 手牌（含副露）排布，方便玩家复盘。

### Low Priority (低优先级 - 压力测试与表现细节)
*   **逻辑健壮性压力测试**: 
    *   [ ] 针对异化牌库（如同种牌 8 张以上）的极端组合进行算番拆解算法验证。
*   **发牌与摸牌动画**: 
    *   [ ] 使用 DoTween 实现牌从牌山飞入位置的序列动画，而非瞬间生成。
    *   [ ] 优化“新摸牌”与“已有手牌”之间的 `drawGap` 动态管理。

---

## 3. 长期优化路线图 (Optimization & Infrastructure)

### 性能优化
*   **对象池 (Object Pooling)**: 引入 `TilePool` 管理 `TileVisual` 的生成与回收，减少高频实例化。
*   **资源预加载**: 在 `DeckManager` 初始化时缓存所有牌面 Sprite，消除首帧加载卡顿。

### 架构演进
*   **算番排斥逻辑自动化**: 在 `FanRule` 中引入 `RuleGroup` 判定，减少手动维护 `ExcludedRuleIds` 的成本。
*   **表现层调度器 (View Coordinator)**: 引入 `AnimationCoordinator` 统筹管理复杂的跨角色动画序列（如：A家杠 -> B家补花 -> C家胡牌）。
