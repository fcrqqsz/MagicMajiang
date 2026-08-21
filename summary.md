麻将 Roguelike 项目进度快照 (Project Snapshot)
日期: 2026-08-16 版本: Alpha - Talent Vertical Slice Complete 引擎: Unity (2022.3.61t9 / Tuanjie 1.6.8)

> **文档约定**: 已完成的任务统一记录在 `milestone.md`，`plan.md` 仅保留待办与未来规划。

1. 项目核心目标
开发一款基于 Unity 3D、统一使用 WebSocket 房间的一人/多人 Roguelike 国标麻将游戏。
*   **核心规则**: 以国标麻将 (MCR) 为基础，支持81番种计算。
*   **特色玩法**: Roguelike 天赋系统、自定义 34 张牌库及异化值机制。

2. 最近进展 (Recent Progress, snapshot 2026-08-19)
*   **大厅房间列表浏览面板 (Room Browser) 与重连抢占保护 (2026-08-19)**:
    *   实现了独立弹窗 `RoomListPanel` (UXML/USS/CS)，支持主动拉取、手动刷新与多局模式/可用状态筛选。
    *   房间卡片展示房主、模式、档位、席位实时人数及异化构筑适配预检（超标禁用加入并支持一键跳转工坊）。
    *   服务端新增 `QueryRoomList` 协议，过滤已关闭房间并下发摘要；直连加入支持房号解析与大厅一键创建直达。
    *   修复历史断线票据恢复失败时意外中断前台业务请求的问题；优化 UI Toolkit `sortingOrder`、防抖与生命周期。
*   **天赋玩法垂直切片与联机架构完成 (2026-08-16)**:
    *   一人游玩和多人游玩统一进入在线 `Room`，由 AI 补足空席；`GameManager` 只协调权威网络投影、恢复与场景，不持有服务端、会话或天赋 runtime。
    *   协议为 v4，携带构筑 schema 为 v3。服务端验证 34 张牌库、6 主槽 + 3 备选槽及 Low 40 / Standard 80 / High 120 档位；预算只计牌库与当前激活主天赋，精确值仅本家可见。
    *   `TalentMatchRuntime` 跨小局复用，统一管理生命周期、主动动作、防御/负面效果、公开充能、Peek、算番归因、恢复快照和匿名 JSONL 遥测。
    *   九个天赋已落地：点金手、窥探、如龙、厚积、快人一步、初始资金、定心、截流、藏锋。藏锋至少 1 层即可消耗全部锋，本局下次合法胡牌每层 +12 番。
    *   半庄/全庄第 4 小局后进入一次 45 秒中场备牌；真人、AI、断线和超时均走服务端权威锁定，后半场复用同一 runtime 且不重放比赛开始效果。
    *   客户端完成常驻天赋 chip、弱/中/强三级反馈、主动天赋目标选择、独立全屏备牌 UIDocument、恢复投影，以及“最终番置顶、基础番与天赋影响逐项”的结算界面。
    *   杠上开花等场况使用服务端权威胡牌上下文；牌型合法性、最终番、逐项天赋贡献和计分使用同一接受结果。
*   **卡组编辑器预算检查器完成 (2026-08-16)**:
    *   右侧栏固定显示圆形预算表盘、Low/Standard/High 三档直选、牌山成本、当前主天赋成本、备牌不计入和总计。
    *   牌张、天赋或档位变化只更新编辑草稿与“未保存”状态；牌库列表保存前保持旧值。超限构筑允许保存但阻止创建/加入不匹配房间，非 34 张不能保存。
    *   切换、新建、删除当前牌库和退出统一使用保存/放弃/取消保护，人工 Unity 布局与点击验收已通过。
*   **联机网络化框架 Phase A-E 最终验收 (2026-07-25)**:
    *   Dedicated Server 使用独立 `00_ServerBootstrap` 场景启动，正式服务端不依赖 `03_Game`、`GameManager.Instance`、`DeckManager.Instance` 或 UI。
    *   完成开发期 username 身份桥接、连接代次、房间/席位管理、四席构筑锁定、服务端天赋执行和多人 Ready 流程；当前协议与构筑版本已升级为 v4 / v3。
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
*   **结算系统**: `ResultPanel` 已完成 UI Toolkit 对接，权威最终番置顶，并逐条展示基础番与天赋影响；恢复结算时直接呈现，不重播反馈动画。
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
    *   *解法*: USS 的 `-unity-font-definition` 统一引用由 `MSYH.TTC` 生成的 TextCore `MSYH_UITK.asset`；不要直接引用 TTC 或 TMP 的 `MSYH_SDF.asset`。
*   **独立 UIDocument 透明遮挡输入**:
    *   *症状*: 备牌、目标选择等动态面板视觉上已隐藏，但出牌或吃碰杠胡按钮仍无法点击。
    *   *解法*: 隐藏时对整个 `UIDocument.rootVisualElement` 设置 `DisplayStyle.None`，显示/取消/超时/恢复/销毁统一清理 schedule、回调和选择状态；全屏阶段面板优先使用独立 Scene Object。
*   **Unity `.meta` / 生成工程误维护**:
    *   *原因*: 手写 GUID 或临时修改 `Assembly-CSharp.csproj` 会制造导入失败和无效编译证据。
    *   *解法*: 智能体只做纯 C# 回归；新增资产的 `.meta`、Unity Refresh、生成工程和视觉/音频 smoke 由人工 Unity 关口完成。
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
*   **历史断线恢复阻断前台操作 (Stale Reconnect Hijack)**:
    *   *症状*: 客户端存有旧局断线票据时，登录大厅后点击“查看房间列表”，突然弹出红色 `RoomNotFound` 报错且无法获取列表。
    *   *原因*: 握手成功后客户端优先尝试恢复旧房间，被服务端拒绝后作为致命错误直接关闭连接，丢弃了排队的 `QueryRoomList`。
    *   *解法*: 在 `HandleTerminalReconnectFailure` 中检查是否存在玩家前台新排队指令（`_pendingRoomCommandAfterHello`），若存在则静默清空失效票据，复用已认证连接直接派发新指令，不阻断前台交互。
*   **UI Toolkit 独立弹窗 `sortingOrder` 层级遮挡**:
    *   *症状*: 独立 UIDocument 弹窗设为 Flex 但在全屏主大厅上完全不可见。
    *   *原因*: 多个 UIDocument 默认 `sortingOrder` 均为 0，后绘制的大厅全屏深色背景遮挡了弹窗。
    *   *解法*: 弹窗 Controller 在 `Awake` 和 `Open` 中显式强制设置 `document.sortingOrder = 50`。
*   **UI Toolkit USS `cursor` 属性警告**:
    *   *症状*: 控制台每帧报 `Runtime cursors other than the default cursor need to be defined using a texture`。
    *   *原因*: USS 样式声明了 Web 端的 `cursor: link`，UI Toolkit 运行时缺少 Texture2D 资源。
    *   *解法*: 彻底从 USS 中移除非默认 `cursor` 属性。
*   **TextCore 字体不支持 Emoji 导致方框乱码**:
    *   *症状*: 按钮文本 `📋`、`🔄`、`👑`、`✓` 等显示为方框“豆腐块”。
    *   *解法*: 在未配置复合 Fallback 字体前，UI Toolkit 的文本一律使用纯文本或标准中文字符。
*   **UI 事件重复累加与网络请求防抖**:
    *   *症状*: 多次打开弹窗后点击事件重复触发，连击“加入房间”发送重复网络包。
    *   *解法*: 引入 `_isUIInitialized` 幂等标志，网络请求按钮增加 `_isJoining` 防抖锁并在 `Hide`/`OnDisable` 时安全注销 `schedule` 句柄。
*   **HTML 原型转译 UXML/USS 严格子集约束**:
    *   *原则*: 允许先写 HTML/CSS 预览文件，但 HTML/CSS 必须严格限定为 UI Toolkit 支持的子集。
    *   *禁令*: 严禁 `cursor`、`box-shadow`/`filter`、`transform`、`display: grid/inline/block`、`z-index`、伪元素 `::before/::after`、高级选择器 `:nth-child`、相对单位 `rem/vw` 及 Unicode Emoji。所有 Flex 容器必须显式写明 `flex-direction: column` 或 `row`（UI Toolkit 默认 column）。

