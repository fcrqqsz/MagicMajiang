麻将 Roguelike 项目进度快照 (Project Snapshot)
日期: 2026-09-03 版本: Alpha - Client Audio 引擎: Unity (2022.3.61t9 / Tuanjie 1.6.8)

> **文档约定**: 已完成的任务统一记录在 `milestone.md`，`plan.md` 仅保留待办与未来规划。

1. 项目核心目标
开发一款基于 Unity 3D、统一使用 WebSocket 房间的一人/多人 Roguelike 国标麻将游戏。
*   **核心规则**: 以国标麻将 (MCR) 为基础，支持81番种计算。
*   **特色玩法**: Roguelike 天赋系统、自定义 34 张牌库及异化值机制。

2. 最近进展 (Recent Progress, snapshot 2026-09-03)
*   **客户端 BGM 与声音设置接入 (2026-09-03)**:
    *   常驻音频管理器使用 Master/Music/SFX 分组；登录及大厅共用大厅曲，对战使用对战曲，跨场景 1 秒交叉淡变，同曲和同场景恢复不重播。
    *   大厅声音设置提供三路百分比滑条、试听与恢复默认，本机 PlayerPrefs 独立保存；既有天赋音效纳入 SFX，不修改服务器或协议。
    *   Unity 已生成 Mixer、音源和 meta，BGM 改为 Streaming，统一常驻监听器；修正编辑器处于 Server 构建目标时客户端音频被禁用的问题。
    *   纯 C# 回归、Unity 编译及音频运行检查通过；用户已确认人工验收无问题，验证与维护说明见 `docs/audio_verification.md`。
*   **模式起始分与击飞终局完成 (2026-09-02)**:
    *   `SessionScoreRules` 统一提供 Single 50 / EastOnly 100 / HalfGame 150 / FullGame 200 起始分；初始资金仍在比赛开始阶段额外叠加 30 分。
    *   完整小局严格在基础计分、全部局末天赋和事件投递后判定 `Scores <= 0`；负分不截断、多人可同时击飞，预定局数与击飞同局时以正常打满为主原因。
    *   协议升级为 v11；`SessionEnd` 下发最终分数、完成局数、终局原因和耗尽席位。客户端只接受该终局权威，保存只读姓名/结算状态并清理活动房间与重连票据。
    *   服务端终局消息后立即移除房间但保持 WebSocket；第 4 局击飞不进入备牌。大厅、房间卡、等待页、规则页和总结算已补齐起始分、终局原因及击飞标记。
    *   JSONL 新增幂等 `session_end` 记录；完整 `NetworkRegression` 与真实 `GameServerTelemetryRegression` 已通过，Unity UI 人工验收待执行。
*   **私有已知牌持续追踪完成 (2026-08-28)**:
    *   新增服务端按观察者隔离的知识账本：窥探牌随未暗改的对手摸牌迁移，洞若观火登记当时揭示的实体；公开出牌和实际副露贡献逐张消耗，隐藏变牌只使原知识失效。
    *   对手暗手按权威总数显示连续的“未知牌背 + 末端排序明牌”，重复牌保留，异常投影按总数截断；排序仅整理已知信息，不代表真实手牌位置。
    *   协议 v10 只下发花色、点数与可见异化标记的全量私有投影；完整快照和缓存重放均恢复最终知识状态，不重放可能过期的即时揭示弹窗。
    *   2026-08-30 完成 Unity 人工验收。明牌改为由槽位视觉类型决定正反朝向，并移除会被布局刷新中断的缩放翻牌动画，修复明牌呈侧向薄片及摸弃牌后整体翻回牌背的问题。
*   **天赋观察模式与状态 chip 完成 (2026-08-24)**:
    *   异彩成章、褪色和去芜的本家 chip 可切换纯客户端观察模式；匹配牌仅将 3D 底座染红，牌面保持清晰，暗杠同样可观察。模式默认关闭，再点取消，并在新小局或快照恢复时清空。
    *   chip 显示异化牌数量、资源/触发进度和服务端已确认的玩法模式。观察开关本身不进入网络、快照或天赋拆装状态。
    *   协议 v10 以本家私有实体 ID 保证同牌面普通牌/异化牌的精确出牌和副露，并新增按观察者隔离的已知对手暗手投影；该投影仅包含牌面与可见异化标记。他家消息、公共副露和牌河均清洗实体 ID 与内部效果字段。
*   **万金油填槽天赋完成 (2026-08-24)**:
    *   新增中品阶 **定调 (`set_the_tone`)** 与 **未雨绸缪 (`prepare_for_risk`)**：前者公开选择数牌花色并在对应胡牌张上获得 +4 post-legal 番；后者公开选择防自摸/防放铳，并在第三方荣和的基础保险或所选风险发生时最多返还一次 8 分。
    *   新增小品阶 **去芜 (`prune_the_excess`)**、**候潮 (`bide_the_tide`)** 与 **预判 (`foretell_outcome`)**：分别围绕第 3 张幺九/字牌弃牌、第 6 次弃牌及预先选择胡牌方式提供 +3/+2/+3 post-legal 番，均不改变 8 番起胡门槛。
    *   新增小品阶 **障眼法 (`misdirection`)**：每小局一次，在任意本家主回合主动装备；下一张实际弃牌按万→饼→条或东→南→西→北→中→发→白循环变换，保持实体 ID 与原始归属，并在进入牌河及响应阶段前完成权威变换。
    *   六个规则完全复用类型化选择、动作账本、post-legal 归因、`OnDiscard` 与小局结算分数增量；未新增协议、UI、专用服务分支或遥测字段。长期纯 C# 回归与真实 `GameServerTelemetryRegression` 覆盖 AI 默认选项、组合归因、自动弃牌权威顺序和小局重置。
*   **通用私有牌面揭示能力与洞若观火完成 (2026-08-23)**:
    *   **通用私有牌面揭示 (Universal Private Tile Reveal)**: 服务端提供通用的即时私有牌面揭示机制，对牌数据做权威脱敏（仅保留花色、数值与修改标记，抹除全局 ID、归属与特效）；揭示结果对外保持 detached、只在本次主动动作新生成时投递，并严格按查看席位隔离。重连不重放该瞬时弹窗，改由当前持续知识投影恢复。
    *   **协议升级为 v10**: 在 v9 本家精确牌投影基础上加入持续已知对手暗手的私有全量投影与快照恢复；公共牌河/副露继续清洗私有字段，并拒绝 v9 客户端。
    *   **洞若观火 (`piercing_insight`)**: 大品阶 26 成本，小局生效范围（`TalentStateScope.Round`），主回合主动激活（`TalentActivationWindow.MainTurn`），公开效果前隐藏（`TalentRevealPolicy.HiddenUntilPublicEffect`），备选灵活（`TalentSideboardPolicy.Flexible`）。每小局限 1 次，私下查看一名其他玩家当前暗手中的全部数牌（万/饼/条，保留重复牌与修改标记，排除字牌与花牌）；即使目标无数牌亦正常消耗当局次数；触发公开事件 `piercing_insight_target` 携带目标席位编号（1..4）；不改变算番。
    *   **UI 表现与交互**: `LocalPlayerClient` 收到揭示通知时通过 `FloatingTilePanelController` 弹窗展示目标玩家暗手数牌，空牌时友好展示“没有可展示的牌”；`TalentActionPanelPolicy` 拓展支持纯玩家目标选择（无需挂载目标天赋）。
    *   **全量回归通过**: 纯 C# 自动化回归、`NetworkRegression` 与真实 `GameServerTelemetryRegression` 均 100% 通过。
*   **开放副露与牌河运营天赋完成 (2026-08-23)**:
    *   **合围 (`encirclement`)**、**背水阵 (`last_stand_formation`)**、**点将 (`call_the_mark`)** 围绕吃碰明杠的来源和顺序形成开放副露构筑；**循迹 (`follow_the_trail`)** 则围绕放铳者的连续弃牌花色提供荣和奖励。四者复用权威小局动作账本、独立起胡门槛控制和 post-legal 算番归因，不按具体天赋 ID 接线。
    *   背水阵在第 2 个公开副露后同时提高 2 番起胡门槛并开放 +12 番奖励；点将公开目标、合围要求多来源、循迹要求胡牌张与放铳者上一张弃牌同花色，均提供明确的风险窗口与对手调整空间。
*   **第二批新玩法天赋 4–6 完成 (2026-08-22)**:
    *   **乘势 (`gather_momentum`)**: 大品阶 26 成本，跨小局保留最多 3 层【势】。吃/碰/明杠/暗杠/加杠入账时充能，摸牌出牌阶段主动全额消耗强化（每局限 1 次），合法胡牌每层提供 +8 post-legal 番；完整实现 `IPublicChargeTalent`。
    *   **褪色 (`fading_color`)**: 小品阶 8 成本，跨小局保留最多 2 点【墨】。每局首次打出异化牌充能（满墨亦消耗当局充能机会），主动消耗 1 墨削减对手公开充能，并保持剩余墨量公开同步；实现 `IPublicChargeTalent` 与 `IPublicChargeControlTalent`。
    *   **化劲 (`redirect_force`)**: 中品阶 12 成本，小局重置。Priority = 10，优先于定心 (0) 拦截削减公开充能效果，格挡后强化当局胡牌 +4 post-legal 番；实现 `IPublicChargeDefenseTalent`。
    *   纯 C# TDD 测试全面覆盖动作账本充能、满墨机会消耗、主动控制削减、多层防御优先级顺序（化劲 -> 定心 -> 生效）、算番归因与跨小局备牌恢复；完整 `NetworkRegression` 与真实 `GameServerTelemetryRegression` 通过。
*   **首批新玩法天赋 0–3 完成 (2026-08-22)**:
    *   协议升级为 v6；公开天赋历史保留激活状态，AI 主动决策优先级与备牌克制判断均使用通用数据和能力标记。
    *   起手变更使用可回滚的整手事务；轻装上阵将起手数牌 1/9 向内转为 2/8，不公开额外变化计数。
    *   ActionPanel 支持服务端下发的通用类型化选择；归色首回合选择万/饼/条，并转换之后前两张非目标花色数牌摸牌。
    *   异彩成章为合法胡牌中 4–8 张唯一异化实体牌各提供 +3 post-legal 番，不改变起胡门槛。
    *   当前十五个天赋均保持规则类多态实现；未新增遥测布局。
*   **下一批天赋服务基础完成 (2026-08-21)**:
    *   新增不可变 `TalentWinFacts`，候选算番、反事实、归因和最终接受共享同一事实对象；仅最终接受胡牌提交一次性消耗。
    *   新增 `TalentActionCommittedFacts` 与 runtime 小局动作账本，只记录已提交的权威动作，覆盖吃碰杠胡、自动兜底、决策 ID 和接受胡牌事实。
    *   通用天赋选择事务支持 Mode/Suit/Seat/Tile 类型化候选、本家私有快照、断线恢复、客户端只回传 `choiceId`、runtime 二次授权及 AI 默认选择；该阶段协议升级为 v5，构筑 schema 仍为 v3。
    *   新增严格的起手完成生命周期：四席起手写入 `ServerGameState` 后、窥探捕获和首个决策前，规则只获得本席不可变 `TalentInitialHandFacts`。
*   **大厅房间列表浏览面板 (Room Browser) 与重连抢占保护 (2026-08-19)**:
    *   实现了独立弹窗 `RoomListPanel` (UXML/USS/CS)，支持主动拉取、手动刷新与多局模式/可用状态筛选。
    *   房间卡片展示房主、模式、档位、席位实时人数及异化构筑适配预检（超标禁用加入并支持一键跳转工坊）。
    *   服务端新增 `QueryRoomList` 协议，过滤已关闭房间并下发摘要；直连加入支持房号解析与大厅一键创建直达。
    *   修复历史断线票据恢复失败时意外中断前台业务请求的问题；优化 UI Toolkit `sortingOrder`、防抖与生命周期。
*   **天赋玩法垂直切片与联机架构完成 (2026-08-16)**:
    *   一人游玩和多人游玩统一进入在线 `Room`，由 AI 补足空席；`GameManager` 只协调权威网络投影、恢复与场景，不持有服务端、会话或天赋 runtime。
    *   当前协议为 v6，携带构筑 schema 为 v3。服务端验证 34 张牌库、6 主槽 + 3 备选槽及 Low 40 / Standard 80 / High 120 档位；预算只计牌库与当前激活主天赋，精确值仅本家可见。
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
    *   完成开发期 username 身份桥接、连接代次、房间/席位管理、四席构筑锁定、服务端天赋执行和多人 Ready 流程；当前协议与构筑版本为 v6 / v3。
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
