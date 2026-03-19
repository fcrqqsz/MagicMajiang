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
*   [x] **多场景架构与大厅枢纽**:
    *   [x] 实现了 `00_Persistent` 挂载 `NetworkManager`, `ProfileManager`, `LoadingScreenController`, `CameraManager` 等不死组件。
    *   [x] 基于 UI Toolkit 实现了 `01_Login`（登录）与 `02_MainLobby`（大厅功能枢纽）场景。
    *   [x] 基于 `SceneManager.LoadSceneAsync` Additive 模式实现了场景淡入淡出及过渡。
*   [x] **DeckEditor 模块化重构 (2026-03-12)**:
    *   [x] 将 `DeckEditorToolkit` 解耦为可复用模板，集成到大厅 Deck Workshop 标签页。
    *   [x] 新增侧边栏多卡组管理（最多 5 套），支持新建/切换/删除卡组。
    *   [x] 重构 DeckEditor 样式与布局（`DeckEditorStyles.uss` / `DeckEditorView.uxml`），优化视觉体验。
    *   [x] `LobbyController` 整合 DeckEditor 打开/关闭流程与卡组数据持久化。
*   [x] **天赋系统重构 (2026-03-17)**:
    *   [x] 拆除旧 MonoBehaviour 单例 `TalentManager` 和 ScriptableObject 基类 `TalentBase`。
    *   [x] 新建纯 C# 天赋架构：`TalentRuleAttribute` 标记 + `TalentRegistry` 反射注册 + `TalentRule` 抽象基类。
    *   [x] 实现 `TalentManager` 管道执行器（非单例，每局创建），覆盖 5 阶段钩子（牌山构建/摸牌/出牌/动作校验/算番）。
    *   [x] 服务端 `GameServer` 集成天赋管道注入点（牌山构建、摸牌、出牌）。
    *   [x] `TalentSlotConfig` 6 槽位系统（大×1 + 中×2 + 小×3），向下兼容。
    *   [x] 牌库编辑器 `DeckEditorToolkit` 新增天赋槽 UI（选择弹窗、详情区域、品阶竖线分隔）。
    *   [x] `SavedDeck` 嵌入天赋配置，异化值合计牌+天赋（`CalculateTotalAlienation`）。
    *   [x] `MidasTouchTalent` 迁移到新体系，`TalentRuleAttribute` 含 DisplayName/Description 字段。
    *   [x] Home 页异化值显示合计牌库+天赋。

---

## 2. 当前开发任务 (Active Backlog)

### High Priority (高优先级 - 游戏性反馈与 UX)
*   **[已完成] 听牌提示 (Wait Hint)**:
    *   [x] 核心算番支持：遍历手牌提供打出某张牌后的听牌列表及最大番数 (`MahjongLogic.GetWaitHints`)。
    *   [x] UI Toolkit 表现：实现 `WaitHintPanel` 横向列表，并在玩家点击选中牌时动态展示。
*   **[已完成] 大厅卡组与设置对接 (Lobby Integration)**:
    *   [x] 连通 `ProfileManager.CurrentProfile.Settings` 到 Settings 标签页。
    *   [x] 预留未来商业化展示与拓展（新增 Collection 页与对应的 Profile 结构）。
    *   [x] 将 `DeckEditor` 解耦为模块化模板，迁移到 `02_MainLobby` 的 Deck Workshop 标签页，支持侧边栏多卡组管理。
    *   [x] Home 页面卡组选择器：左右箭头循环切换 `SavedDecks`，即时持久化 `SelectedDeckIndex`，单卡组时箭头禁用。
*   **[已完成] 多局对战 UI 完善**:
    *   [x] 游戏界面显示当前圈风、门风、第几局的状态栏。
    *   [x] Home 页 GameMode 选择器：左右箭头循环切换四种模式（单局/东风局/半庄/全庄），即时持久化到 ProfileSettings。
*   **异化牌视觉反馈**:
    *   [ ] 根据 `TileData` 的异化状态（天赋修改），通过 `TileVisual` 改变牌背颜色或增加发光特效。
*   **[已完成] 牌河指针 (River Pointer)**:
    *   [x] 在 `TileVisual` 和 `RiverController` 中实现基于材质自发光 (Emission) 的高亮呼吸灯，始终标记当前最新出的那张牌。

### Medium Priority (中优先级 - 视觉增强与复盘)
*   **结算手牌缩略图**: 
    *   [ ] 在 `ResultPanel` 中增加胡牌瞬间的 2D 手牌（含副露）排布，方便玩家复盘。

### Low Priority (低优先级 - 压力测试与表现细节)
*   **天赋槽图标显示**:
    *   [ ] 为 `TalentDefinition` SO 资产配置天赋图标（Sprite），在牌库编辑器槽位中显示。
    *   [ ] 恢复 `TalentSlotTemplate.uxml` 中的 `IconContainer` 元素，`RefreshTalentSlots` 中绑定图标。
    *   [ ] 可选：Home 页卡组名下方显示已装配天赋的小图标。
*   **逻辑健壮性压力测试**:
    *   [ ] 针对异化牌库（如同种牌 8 张以上）的极端组合进行算番拆解算法验证。
*   **发牌与摸牌动画**:
    *   [ ] 使用 DoTween 实现牌从牌山飞入位置的序列动画，而非瞬间生成。
    *   [ ] 优化”新摸牌”与”已有手牌”之间的 `drawGap` 动态管理。

---

## 3. 长期优化路线图 (Optimization & Infrastructure)

### 性能优化
*   **对象池 (Object Pooling)**: 引入 `TilePool` 管理 `TileVisual` 的生成与回收，减少高频实例化。
*   **资源预加载**: 在 `DeckManager` 初始化时缓存所有牌面 Sprite，消除首帧加载卡顿。

### 架构演进
*   **算番排斥逻辑自动化**: 在 `FanRule` 中引入 `RuleGroup` 判定，减少手动维护 `ExcludedRuleIds` 的成本。
*   **表现层调度器 (View Coordinator)**: 引入 `AnimationCoordinator` 统筹管理复杂的跨角色动画序列（如：A家杠 -> B家补花 -> C家胡牌）。
