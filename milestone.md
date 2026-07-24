# SuperMajiang 已完成里程碑 (Completed Milestones)

本文档记录所有已完成的开发任务。新完成的任务追加到对应分类末尾。

## 核心循环与逻辑

*   [x] **"胖客户端，瘦服务端" 架构重构**: 实现了 `GameServer` 与 `ClientAgents` 的解耦，支持本地/AI 统一接口。
*   [x] **算番引擎基础构建**: 完成了 40+ 种核心番种判定，支持多路径拆解与番数最大化搜索。
*   [x] **基础 AI 实现**: `SimpleAIClient` 支持基本的基于孤张判定的出牌与吃碰杠胡逻辑。
*   [x] **多局对战系统**: 实现 `GameSession` 管理多局状态，支持单局/东风局/半庄/全庄模式，含圈风轮转、门风分配、国标计分。
*   [x] **风位 Bug 修复**: `FanContext` 风位类型从 `Suit` 改为 `WindDirection`，修复圈风刻/门风刻永远匹配西风的 bug。
*   [x] **超时取消机制 + 服务端快照 (2026-03-06)**: `CancellationToken` 取消 async 操作 + `ServerGameState` 镜像手牌超时兜底。

## UI 与交互

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
*   [x] **听牌提示 (Wait Hint)**:
    *   [x] 核心算番支持：遍历手牌提供打出某张牌后的听牌列表及最大番数 (`MahjongLogic.GetWaitHints`)。
    *   [x] UI Toolkit 表现：实现 `WaitHintPanel` 横向列表，并在玩家点击选中牌时动态展示。
*   [x] **大厅卡组与设置对接 (Lobby Integration)**:
    *   [x] 连通 `ProfileManager.CurrentProfile.Settings` 到 Settings 标签页。
    *   [x] 预留未来商业化展示与拓展（新增 Collection 页与对应的 Profile 结构）。
    *   [x] 将 `DeckEditor` 解耦为模块化模板，迁移到 `02_MainLobby` 的 Deck Workshop 标签页，支持侧边栏多卡组管理。
    *   [x] Home 页面卡组选择器：左右箭头循环切换 `SavedDecks`，即时持久化 `SelectedDeckIndex`，单卡组时箭头禁用。
*   [x] **多局对战 UI 完善**:
    *   [x] 游戏界面显示当前圈风、门风、第几局的状态栏。
    *   [x] Home 页 GameMode 选择器：左右箭头循环切换四种模式（单局/东风局/半庄/全庄），即时持久化到 ProfileSettings。
*   [x] **牌河指针 (River Pointer) (2026-03-19)**:
    *   [x] 在 `TileVisual` 和 `RiverController` 中实现基于材质自发光 (Emission) 的高亮呼吸灯，始终标记当前最新出的那张牌。

## 天赋系统

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
*   [x] **通用悬浮牌面板 (FloatingTilePanel) (2026-03-24)**:
    *   [x] 新建 `FloatingTilePanel.uxml/uss` + `FloatingTilePanelController.cs` 三件套，支持展示/选择双模式。
    *   [x] 提取 `TileImageHelper` 共享牌面图片路径工具类，`WaitHintController` 同步使用。
    *   [x] 窥探天赋 (`PeekTalent`) 接入 UI 面板（发牌后显示牌山顶部 4 张，8 秒自动关闭）。
*   [x] **天赋选择弹窗美化 (2026-03-24)**:
    *   [x] 弹窗从内联样式重构为 CSS class 结构化布局（名称/描述/品阶/异化值分行）。
    *   [x] 品阶颜色区分（大=金/中=紫/小=蓝），hover 高亮，与编辑器深色+青色主题一致。

## 联机框架

*   [x] **WebSocket 与服务端权威基础 (Phase A)**:
    *   [x] 完成远程客户端/服务端代理、动作校验、Ready 与多小局结果同步。
    *   [x] 网络服务端使用纯 C# `WallService`，不依赖 `DeckManager.Instance`。
*   [x] **Dedicated Server 解耦 (Phase B)**:
    *   [x] 新增无 UI 的 `00_ServerBootstrap` 场景和专用构建菜单。
    *   [x] Headless 服务端启动路径移除 `GameManager`、游戏场景和表现层依赖。
*   [x] **房间与多人席位 (Phase C)**:
    *   [x] 完成 `ConnectionRegistry`、`RoomManager`、`Room`、创建/加入/Ready/离开协议及 AI 补位。
    *   [x] 1/2/3/4 真人组合和 EastOnly 多小局联机验证通过。
*   [x] **玩家构筑与天赋联机 (Phase D)**:
    *   [x] 服务端验证并锁定四席 34 张牌库和 6 槽天赋，客户端只接收公开摘要。
    *   [x] 天赋由服务端统一执行，完成权威分数、响应优先级和截胡顺位同步。
*   [x] **重连与鲁棒性 (Phase E, 2026-07-25)**:
    *   [x] 协议 v2、稳定逻辑身份、连接代次、每席有序消息流、心跳和消息上限。
    *   [x] 权威隐私快照、`decisionId` 校验、客户端幂等投影和桌面原子恢复。
    *   [x] 断线席位保留、AI 临时托管、自动重试、完整快照重连和终止票据清理。
    *   [x] 自动回归、Dedicated Server、真人联机和 EastOnly 多局恢复最终验收通过。
