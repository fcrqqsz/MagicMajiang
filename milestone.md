# SuperMajiang 已完成里程碑 (Completed Milestones)

本文档记录所有已完成的开发任务。新完成的任务追加到对应分类末尾。

## 房间与 AI v12

*   [x] **房间 v12、手动 AI 与房间 UI 重构 (2026-09-04)**:
    *   [x] 删除全部 `aiFill` 启动与协议路径，建立稳定房主、四个权威逻辑席位、永久 AI 增删改、可逆准备、120 秒房主保留与权威转移。
    *   [x] 永久 AI 公开 34 张牌库和 6+3 天赋，真人完整构筑保持私有；主动退出或超时转换出的永久 AI 默认新手并沿用锁定构筑。
    *   [x] 现有策略迁为确定性新手难度，新增标准向听、合法起胡与改良牌种评估；模板只改变构筑，不改变打法。
    *   [x] 先完成严格 UI Toolkit 子集的双 HTML/CSS 原型，再转译房间 UXML/USS；AI 与玩家复用同一套 `DeckEditorToolkit`，房间模式只使用隔离草稿且不写 Profile。
    *   [x] 房间 ViewModel、AI 草稿、权限、隐私、重连、策略与软截止均纳入纯 C# 回归。

## 核心循环与逻辑

*   [x] **"胖客户端，瘦服务端" 架构重构**: 实现了 `GameServer` 与 `ClientAgents` 的解耦，支持本地/AI 统一接口。
*   [x] **算番引擎基础构建**: 完成了 40+ 种核心番种判定，支持多路径拆解与番数最大化搜索。
*   [x] **基础 AI 实现**: `SimpleAIClient` 支持基本的基于孤张判定的出牌与吃碰杠胡逻辑。
*   [x] **多局对战系统**: 实现 `GameSession` 管理多局状态，支持单局/东风局/半庄/全庄模式，含圈风轮转、门风分配、国标计分。
*   [x] **模式起始分与击飞终局 (2026-09-02)**: Single/EastOnly/HalfGame/FullGame 分别从 50/100/150/200 分开始；完整局末结算后 `<= 0` 由服务端权威终止整场，协议 v11 同步终局原因、完成局数、最终分数与全部耗尽席位。终局房间立即解绑移除但保持 WebSocket，第 4 局击飞不进入备牌，匿名 JSONL 记录一次 `session_end`。
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
*   [x] **结算手牌缩略图 (2026-07-31)**:
    *   [x] `ResultPanel` 使用现有 2D 卡面展示服务端权威的赢家暗手、独立胡牌张及分组副露。
    *   [x] 所有席位均可在局结束后复盘赢家牌型，结算阶段断线重连可从权威快照恢复。
    *   [x] 单行牌型条按总牌数自适应缩放，暗杠使用“两侧牌背、中间明牌”的结算表现。
    *   [x] 结算结果补充 `WinKind` 与放炮席位，并通过统一 Codec 完成牌型快照创建、深拷贝、校验和规范化。
*   [x] **天赋战术 UI 与卡组预算检查器 (2026-08-16)**:
    *   [x] `GameHUD` 展示本家常驻天赋、已公开对手天赋、事件流及弱/中/强三级反馈；强反馈只用于主动天赋实际生效。
    *   [x] `ActionPanel` 与通用目标选择器支持服务端下发的主动天赋选项、取消、拒绝恢复、过期决策和重连恢复。
    *   [x] 中场备牌使用独立 `SideboardPanel` Scene Object / `UIDocument`，只在权威阶段显示，隐藏时不阻断手牌和吃碰杠胡输入。
    *   [x] `ResultPanel` 将权威最终番置顶，基础番与天赋正负影响逐条展示，恢复时不重播 toast/音效。
    *   [x] 卡组编辑器右侧栏固定预算表盘与 Low/Standard/High 直选，实时拆分牌山/主天赋/备牌/总计；超限可保存，非 34 张不可保存。
    *   [x] 未保存草稿在切换、新建、删除当前牌库和退出时统一提供保存/放弃/取消，保存前列表条目保持已保存值。
*   [x] **大厅房间列表浏览面板 (Room Browser) (2026-08-19)**:
    *   [x] 独立弹窗 `RoomListPanel.uxml/uss` + `RoomListController.cs`，在大厅 Home 页呼出，支持多局模式及可用席位筛选。
    *   [x] 卡片化展示房主、模式、档位、席位人数，实时比对出战构筑异化值与房间上限（超标禁用加入并支持一键跳转工坊）。
    *   [x] 服务端 `QueryRoomList` 协议支持，下发开放房间摘要；支持房号直连与从大厅列表一键快速开房。
    *   [x] 修复历史断线票据失败时的前台抢占中断问题；实现 UI 幂等初始化、点击防抖、层级提升与生命周期调度清理。

*   [x] **客户端 BGM 与声音设置 (2026-09-03)**:
    *   常驻 AudioManager 与 Master/Music/SFX 混音；登录/大厅及对战分别循环对应 BGM，跨类别 1 秒淡变，同曲请求不重播。
    *   大厅设置支持三路音量、试听与恢复默认，通过独立 PlayerPrefs 保存本机偏好；既有天赋音效统一接入，不涉及服务器状态。
    *   Unity 原生生成 Mixer、脚本 meta 和场景引用；BGM 使用后台流式加载，客户端只保留常驻监听器。
    *   纯 C# 回归、Unity 编译及播放运行检查通过，用户已确认人工验收无问题；维护与回归说明见 `docs/audio_verification.md`。

*   [x] **对战菜单、退出与共享音频设置实现 (2026-09-04)**:
    *   独立 `UIDocument_BattleMenu` 接入 `03_Game`，排序 200；菜单首页、设置、二次确认和退出中状态齐备，HUD/备牌/结算都有入口，菜单不暂停对局。
    *   大厅与对战复用音频模板、绑定和保存逻辑；本地输入门禁不覆盖权威决策，面板隐藏时释放整个文档输入层。
    *   离房与大厅导航统一为幂等异步流程，5 秒发送上限，阻断旧房间/恢复回调，复用健康连接并支持导航失败重试。
    *   纯 C# 回归通过；UnityMCP 完成编译、场景保存、三页实际渲染及本地真实房间退出/再次开局验证。用户已确认完整键鼠、跨面板层级、窗口比例与听音验收无问题；证据见 `docs/battle_menu_verification.md`。

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
*   [x] **天赋主动动作、控制、防御与备牌 (2026-08-16)**:
    *   [x] 协议升级为 v4、携带构筑 schema 升级为 v3；主动天赋请求与结果均携带服务端权威 `decisionId`。
    *   [x] 新增定心、截流、藏锋；控制效果通过窄负面效果描述和防御管道执行，不向规则暴露任意回调。
    *   [x] 藏锋至少 1 层即可发动，发动时消耗当前全部锋，使本局下次合法胡牌每层 +12 番。
    *   [x] 半庄/全庄第 4 小局后恰好进入一次 45 秒备牌；AI 立即选择，断线/超时锁回合法方案，重连只恢复权威锁定状态。
    *   [x] 算番拆为基础、门槛、post-legal 奖励/惩罚和最终番；杠上开花等场况由统一权威胡牌上下文计算。
    *   [x] `TalentMatchRuntime` 输出匿名玩法遥测，Dedicated Server 以 JSONL 落盘，sink 异常不影响房间生命周期。
*   [x] **下一批天赋服务基础 (2026-08-21)**:
    *   [x] `TalentWinFacts` 以不可变实体牌、副露和场况快照贯穿 detached 候选、反事实、归因及最终接受；仅接受路径提交一次性状态。
    *   [x] `TalentActionCommittedFacts` 与 runtime 小局动作账本覆盖权威吃碰杠胡和自动兜底，按 `decisionId` 去重并在规则回调前入账。
    *   [x] 通用 Mode/Suit/Seat/Tile 选择事务完成服务端授权、本家私有快照、恢复、客户端 ID 回传、AI 默认项和执行前二次校验；协议升级为 v5，构筑 schema 保持 v3。
    *   [x] 新增 `InitialHandCompleted` 生命周期，在发牌写入 `ServerGameState` 后、Peek 与首个决策前，仅向激活规则提供本席不可变起手事实。
    *   [x] focused 纯 C#、完整 `NetworkRegression` 与真实 `GameServerTelemetryRegression` 覆盖生命周期、隐私、恢复、AI、算番归因和动作提交。
*   [x] **首批新玩法天赋 0–3 (2026-08-22)**:
    *   [x] 协议升级为 v6；已公开但换入备牌的天赋保留公共历史并标记非激活，主动授权不会把非激活历史当作可用目标。
    *   [x] AI 按服务端下发的通用优先级执行同一主决策内的补充天赋动作；中场备牌只做一次基于公开能力标记的明显克制替换，不按具体天赋 ID 分支。
    *   [x] 起手变更使用复制、规则链和整手原子提交事务；轻装上阵将起手数牌 1/9 转为 2/8，失败时权威手牌保持不变。
    *   [x] ActionPanel 支持通用类型化选择；归色在首个主回合选择花色，并把之后前两张非目标花色数牌转为目标花色。
    *   [x] 异彩成章按唯一实体牌 ID 统计合法胡牌中的异化牌，4 张起每张提供 +3 post-legal 番，最多计算 8 张，不降低起胡门槛。
    *   [x] 未新增遥测布局；纯 C# 聚焦回归、完整 `NetworkRegression` 与真实 `GameServerTelemetryRegression` 通过。
*   [x] **第二批新玩法天赋 4–6 (乘势、褪色、化劲) (2026-08-22)**:
    *   [x] **乘势 (`gather_momentum`)**: 大品阶、成本 26、跨小局保留。每次吃碰杠动作提交积攒 1 层【势】（最多 3 层）；摸牌出牌阶段可主动消耗全部【势】强化（每小局限 1 次），合法胡牌每消耗 1 层结算额外 +8 post-legal 番。完整实现 `IPublicChargeTalent`。
    *   [x] **褪色 (`fading_color`)**: 小品阶、成本 8、跨小局保留。每小局本家首次提交打出异化牌积攒 1 点【墨】（最多 2 点，墨满时仍消耗本局充能机会）；摸牌出牌阶段可主动消耗 1 点【墨】削减指定对手 1 点公开充能并准确同步剩余墨点。实现 `IPublicChargeTalent` 与 `IPublicChargeControlTalent`。
    *   [x] **化劲 (`redirect_force`)**: 中品阶、成本 12、小局重置。优先级 10（高于定心 0），每小局首次受到削减公开充能效果时优先触发化劲格挡并强化本小局胡牌 +4 post-legal 番；实现 `IPublicChargeDefenseTalent`。
    *   [x] 纯 C# TDD 完整覆盖充能边界、墨满机会消耗、控制削减、多层防御优先级链条、起胡门槛保持与跨小局备牌恢复。完整 `NetworkRegression` 与 `GameServerTelemetryRegression` 通过。
*   [x] **万金油填槽天赋批次 (2026-08-24)**:
    *   [x] 中品阶定调 (`set_the_tone`, 12) 与未雨绸缪 (`prepare_for_risk`, 12) 复用首次主决策类型化选择；定调匹配胡牌张花色奖励 +4 番，未雨绸缪按基础第三方荣和保险及所选防自摸/防放铳条件最多返还 8 分。
    *   [x] 小品阶去芜 (`prune_the_excess`, 6)、候潮 (`bide_the_tide`, 4)、预判 (`foretell_outcome`, 6) 复用权威动作账本与 detached post-legal 归因，提供有条件的 +3/+2/+3 番且保持独立 8 番门槛。
    *   [x] 小品阶障眼法 (`misdirection`, 8) 在任意本家主回合每局一次主动装备，下一张实际弃牌按数牌花色环或东南西北中发白顺序环变换；超时自动弃牌同样在进入权威牌河和响应窗口前应用。
    *   [x] 未新增协议、UI、遥测或具体 talentId 服务分支；长期纯 C# 回归、完整 `NetworkRegression` 与真实 `GameServerTelemetryRegression` 覆盖选择授权、AI 默认、结算返还、组合归因、小局重置和自动弃牌顺序。
*   [x] **窥探与洞若观火的私有已知牌持续追踪 (2026-08-28)**:
    *   [x] 服务端按观察者隔离保存物理牌知识，客户端投影只含花色、点数和可见异化标记；协议升级至 v10，构筑 schema 保持 v3。
    *   [x] 窥探牌按实际摸牌迁移，洞若观火登记重复实体；公开离手逐张消耗，隐藏变牌原子失效，小局与会话边界清空。
    *   [x] 对手暗手连续显示牌背与末端排序明牌，不增加分隔线或常驻牌山面板；完整快照和缓存重放恢复当前知识，不重放即时弹窗。
    *   [x] Unity 人工验收通过（2026-08-30）：上、左、右三家明牌朝向、排序、摸弃牌后的持续显示与重连重建均确认；修复了缩放 tween 被布局刷新截断造成的侧向薄片，以及统一牌背旋转覆盖明牌朝向的问题。

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
*   [x] **Room 唯一比赛权威与一人 AI 补位 (2026-08-10)**:
    *   [x] 一人游玩与多人游玩统一进入普通在线 `Room`；AI 填充空席，客户端不存在隐式本地比赛权威路径。
    *   [x] `GameManager` 收敛为网络投影与场景协调器，不再拥有 Server、会话、runtime 或多局循环。
*   [x] **Talent Plan 1：基础、异化值与六天赋迁移 (2026-08-10)**:
    *   [x] 协议升级为 v3、携带构筑 schema 升级为 v2；此前 Phase E 的 v2 记录为历史实现，已由本条目取代。
    *   [x] 服务端重建和验证 Low 40 / Standard 80 / High 120 异化值预算；6 个主槽加 3 个备选槽，未激活备选不计成本，精确总值保持本家私有。
    *   [x] 四席构筑锁定后 `Room` 恰好创建一个 `TalentMatchRuntime` 并跨小局复用；`TalentManager` 与 `SessionTalentPolicy` 已删除。
    *   [x] 既有六天赋迁移到规则重写并跨两小局验证；Peek 读取发牌后牌山，权威实体牌在公开边界揭示，终局完成一次且 Room 具备异常安全回退。
*   [x] **联机天赋垂直切片最终验收 (2026-08-16)**:
    *   [x] `Room -> GameServer -> TalentMatchRuntime` 成为天赋生命周期、主动动作、备牌、算番归因和遥测的唯一权威链路。
    *   [x] `RoomGameSnapshot` / `ClientGameState` 恢复本家天赋、公开对手信息、可用主动动作、备牌锁定和最终番明细，且不串席泄露私有状态。
    *   [x] `SimpleAIClient` 使用合法 6+3 构筑，按公开权威信息使用主动天赋并完成中场备牌，不读取隐藏对手状态。
    *   [x] 联机、天赋、备牌、算番、重连和真实 `GameServer` 纯 C# 回归通过；Unity UI、输入、字体、音效及卡组编辑器人工验收通过。
*   [x] **新玩法天赋 4–6：乘势、褪色、化劲 (2026-08-22)**:
    *   [x] 乘势 (`gather_momentum`): 跨小局蓄力（上限 3），吃碰明暗加杠入账时充能（被抢胡加杠不充能），主动强化当局合法胡牌每层 +8 番；实现 `IPublicChargeTalent`。
    *   [x] 褪色 (`fading_color`): 保持 `HiddenUntilPublicEffect` 隐蔽策略；跨小局积墨（上限 2），每局首次提交打出异化牌（含超时自动出牌）充能，主动消耗 1 墨定向削减对手公开充能，被防御阻挡不退款；实现 `IPublicChargeTalent` 与 `IPublicChargeControlTalent`。
    *   [x] 化劲 (`redirect_force`): 小局重置；以 Priority 10 优先于定心 (0) 拦截公开充能削减，格挡后强化当局合法胡牌 +4 番；实现 `IPublicChargeDefenseTalent`。
    *   [x] 网络快照反序列化与 `ClientGameState` 投影全面验证本家/他家隐私隔离与断线恢复；`NetworkRegression` 与 `GameServerTelemetryRegression` 自动化套件 100% 通过。
*   [x] **天赋观察模式与 chip 状态 (2026-08-24)**:
    *   [x] 默认不改变牌面；点击本家可观察天赋 chip 后，仅将匹配牌的 3D 底座改为红色，再次点击取消，牌面 Sprite、牌河与他家视觉不受影响。
    *   [x] 异彩成章与褪色复用异化牌观察，去芜复用幺九/字牌观察；观察状态纯客户端本地、快照恢复和新小局都会清空，备牌拆装不保存该状态。
    *   [x] chip 展示异化牌数量、墨/弃牌/副露进度及已选花色/胡牌方式/防护模式；协议 v9 仅向本家下发实体 ID、异化标记和精确副露，公共牌河与副露严格清洗。
