麻将 Roguelike 项目进度快照 (Project Snapshot)
日期: 2026-07-25 版本: Alpha - Online Framework & Reconnect 引擎: Unity (2022.3.61t9)

> **文档约定**: 已完成的任务统一记录在 `milestone.md`，`plan.md` 仅保留待办与未来规划。

1. 项目核心目标
开发一款基于 Unity 3D、支持单机与 WebSocket 联机的 Roguelike 国标麻将游戏。
*   **核心规则**: 以国标麻将 (MCR) 为基础，支持81番种计算。
*   **特色玩法**: Roguelike 天赋系统、自定义 34 张牌库及异化值机制。

2. 最近进展 (Recent Progress, snapshot 2026-08-10 / Talent Plan 1 complete)
*   **Talent Plan 1：Room 天赋基础、异化值与六天赋迁移 (2026-08-10)**:
    *   一人游玩统一进入普通在线 `Room` 并由 AI 补足席位；客户端没有隐式本地权威路径，`GameManager` 只负责网络投影与场景协调。
    *   协议升级为 v3，携带构筑 schema 为 v2。服务端重建并验证构筑；Low / Standard / High 异化值档位分别为 40 / 80 / 120，总成本为牌库异化值加当前激活主天赋成本，三个备选槽在启用前不计成本，精确总值仅本家可见。
    *   四席构筑锁定后，`Room` 恰好创建一个跨小局复用的 `TalentMatchRuntime`；它承载带类型的比赛/小局状态、生命周期、管道、事件、私有 Peek、算分选项、揭示与小局结束效果，`TalentManager` 与 `SessionTalentPolicy` 已删除。
    *   六个既有天赋均已迁入规则重写并完成跨两小局验证；Peek 读取发牌后的牌山，弃牌/副露/加杠/和牌边界使用权威实体牌公开，完成事件只发出一次，Room 异常时有终局兜底。
*   **联机网络化框架 Phase A-E 最终验收 (2026-07-25)**:
    *   Dedicated Server 使用独立 `00_ServerBootstrap` 场景启动，正式服务端不依赖 `03_Game`、`GameManager.Instance`、`DeckManager.Instance` 或 UI。
    *   完成开发期 username 身份桥接、连接代次、房间/席位管理、四席构筑锁定、服务端天赋执行和多人 Ready 流程；协议与构筑版本已由后续 Talent Plan 1 分别升级为 v3 / v2。
    *   `ServerGameState` 权威记录手牌、副露和牌河；`RoomGameSnapshot` 按席保护隐私，`ClientGameState` 幂等应用有序消息和完整快照。
    *   完成断线席位保留、决策边界 AI 托管、endpoint 重绑、心跳检测、自动重试、场景路由和客户端桌面原子恢复。
    *   已通过 1/2/3/4 真人组合、EastOnly 多小局、强退重连、心跳超时、加载/主回合/响应/局间/结算恢复和 Dedicated Server 验收。
    *   长期构建与测试流程见 `docs/network_verification.md`。
*   **通用悬浮牌面板 & 天赋选择弹窗美化 (2026-03-24)**:
    *   新增 `FloatingTilePanel`（UXML/USS/Controller 三件套），支持展示模式（自动关闭+手动关闭）和选择模式（点击选牌回调）。屏幕上方居中定位，CSS opacity 淡入动画，`picking-mode: Ignore` 不阻挡底层交互。
    *   提取 `TileImageHelper` 共享静态类，统一 `Suit+Value` 到 `Resources` 图片路径的映射，`WaitHintController` 和 `FloatingTilePanelController` 共用。
    *   窥探天赋 (`PeekTalent`) 从 `Debug.Log` 改为调用 `FloatingTilePanelController.Instance.ShowTiles()`，发牌后显示牌山顶部 4 张牌（原 3 张），8 秒自动关闭。
    *   天赋选择弹窗从内联样式重构为 CSS class 驱动（`DeckEditorStyles.uss`），新增结构化布局（天赋名/描述/品阶/异化值分行显示），品阶颜色区分（大=金/中=紫/小=蓝），hover 高亮和清空按钮独立样式，与编辑器整体深色+青色主题一致。
*   **牌河指针/高亮功能 (2026-03-19)**: 为 `TileVisual` 增加了基于 DOTween 和材质 `_EmissionColor` 的呼吸灯高亮效果（强青色 `Color(0.3f, 1.0f, 1.0f)`）。`RiverController` 实现了状态机制，确保全场永远只有**最新打出的那张牌**保持高亮，且在牌被吃碰杠拿走时安全销毁动画，提升了玩家的视觉跟踪体验。
*   **Home 页卡组选择器 (2026-03-12)**: 在 Home 页面新增左右箭头循环切换卡组功能（`DeckSelector` 容器 + `BtnDeckPrev`/`BtnDeckNext`），切换即时持久化 `SelectedDeckIndex`，单卡组时箭头禁用。修复了 `RefreshHomeDeckInfo` 中索引越界修正未写回 `profile.SelectedDeckIndex` 的潜在问题。
*   **多场景架构与大厅 UI (2026-03-07)**: 重构了项目的入口流程，引入了 `00_Persistent` 持久化层及 `ProfileManager`, `NetworkManager`, `LoadingScreenController`, `CameraManager`。实现了基于 UI Toolkit 的 `01_Login` 登录界面和 `02_MainLobby` 大厅枢纽，支持模拟登录、模拟匹配房间以及多场景加载与无缝过渡机制。
*   **超时取消机制 + 服务端快照 (2026-03-06)**: 解决超时出牌三大问题：(1) 手牌不同步 — 服务端 `ServerGameState` 镜像手牌，超时从快照取真实牌出牌；(2) async void 无法取消 — 通过 `CancellationToken` + `ct.Register(() => tcs.TrySetCanceled())` 实现可取消的 async 操作；(3) 虚构兜底牌 — `AwaitWithTimeout` 的 fallback 改为 `Func<T>` 延迟求值。新增 `HandController.ForceRemoveTile()` 移除牌到牌河但不触发事件。
*   **多局对战系统 (2026-03-05)**: 实现 `GameSession` 多局状态管理，支持单局/东风局/半庄/全庄。含圈风轮转、门风分配、国标计分（底分+番数制）。修复了 `FanContext` 风位 bug（圈风刻/门风刻永远匹配西风）。`ResultPanel` 支持多局结算流程。
*   **架构重构**: 完成了 "Fat Client, Thin Server" 架构，拆分了 `GameServer` 与 Client Agents，为本地/AI 统一逻辑打下基础。
*   **AI 基础**: 实现了 `SimpleAIClient`，支持基础的出牌、吃碰杠胡决策。
*   **结算系统**: `ResultPanel` 已完成 UI Toolkit 对接，支持番种列表展示及得分滚动动画。
*   **副露系统**: `HandController` 已实现吃碰杠的基础 3D 模型生成逻辑（正在精修旋转与堆叠细节）。

3. 避坑指南 (Troubleshooting Log)
*   **DontDestroyOnLoad 警告**:
    *   *症状*: `DontDestroyOnLoad only works for root GameObjects...`
    *   *解法*: 在调用 `DontDestroyOnLoad` 前，确保执行 `transform.SetParent(null);` 使对象成为根节点。
*   **多场景 Camera 蒙版与 AudioListener 冲突**:
    *   *症状*: Additive 加载游戏场景后，画面出现底色蒙版且报 AudioListener 数量错误。
    *   *解法*: 新增 `CameraManager`，监听场景加载。进入游戏时禁用 Persistent UI Camera；并在游戏场景的主摄像机上移除多余的 AudioListener。
*   **风位 Bug (FanContext RoundWind/SeatWind 硬编码)**:
    *   *症状*: 圈风刻/门风刻番种永远只匹配西风 (Value=3)。
    *   *原因*: `RoundWind`/`SeatWind` 类型为 `Suit`，创建时硬编码 `Suit.Wind`（枚举值=3）。
    *   *解法*: 新增 `WindDirection` 枚举 (East=1..North=4)，替换 `FanContext` 中的 `Suit` 类型。
*   **SortHand 理牌后位置不更新**:
    *   *症状*: 发牌后手牌数据有序但 3D 位置未更新。
    *   *解法*: `SortHand()` 末尾追加 `UpdateHandPositions()` 调用。
*   **多局牌河残留**:
    *   *症状*: 下一局开始时上局牌河未清理。
    *   *解法*: 在 `MahjongHandViewBase.ClearHand()` 基类中统一调用 `myRiver.Clear()`。
*   **FanRuleRegistry 空引用**: 
    *   *解法*: 重构为纯 C# 单例，属性懒加载。
*   **胡牌计算不准确**: 
    *   *解法*: 引入手牌多路径拆解算法，遍历所有方案取番数最大值。
*   **UI Toolkit 字体不显示**:
    *   *解法*: 确保 USS 引用 `-unity-font-definition` (SDF 资产) 而非原始字体文件。
*   **DoTween 序列同步**:
    *   *解法*: 在 `HandController` 中处理并发动作时，需使用 `Sequence` 确保动画不冲突。
*   **超时出牌手牌不同步 (+1 bug)**:
    *   *症状*: 服务端超时自动出牌后客户端手牌未移除，每次超时手牌数+1。
    *   *原因*: `OnTimeout()` 仅清理 UI 状态，未移除手牌数据；`async void` 方法无法被外部中断，超时后代码继续跌落。
    *   *解法*: (1) `ServerGameState` 维护手牌快照，超时时取真实牌；(2) `CancellationToken` 取消 async 操作；(3) `OnTimeout(TileData)` 传入自动出的牌，客户端调 `ForceRemoveTile` 同步。
*   **超时虚构兜底牌 (new TileData 不在手牌中)**:
    *   *症状*: 吃碰后超时用 `new TileData(Suit.Wind, 1, ...)` 作 fallback，该牌不在手牌中，状态腐坏。
    *   *解法*: `AwaitWithTimeout` 的 fallback 改为 `Func<T>` 延迟求值，超时时从 `ServerGameState.GetAutoDiscardTile()` 取真实手牌。
*   **DoTween 对象销毁报错 (Target or field is missing/null)**:
    *   *症状*: 当 AI 极速连击（如瞬间打牌后被瞬间吃牌），导致刚执行动画的牌被立即 `Destroy` 时，DOTween 尝试访问已销毁的 Transform。
    *   *解法*: 对所有针对动态生成/销毁的 GameObject 进行的 DoTween 动画（如 `DOLocalMove`），必须链式调用 `.SetLink(gameObject)`，强制绑定动画生命周期与 GameObject。

