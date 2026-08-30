# SuperMajiang 架构概览 (Architectural Overview)

本文档作为项目的核心架构索引，详细记录了系统的设计模式、目录结构及职责。

## 1. 核心设计模式
*   **多场景架构 (Multi-Scene Architecture)**: 
    *   `00_Persistent`: 作为游戏持久入口，承载所有“不死”的单例管理器（Profile、Network 等）。
    *   `01_Login` & `02_MainLobby`: UI 专用子场景，通过 Additive 模式叠加加载。
    *   `03_Game`: 麻将核心 3D 对局场景。
    *   `00_ServerBootstrap`: Dedicated Server 唯一启动场景，不加载大厅、游戏表现、Camera 或 UI。
*   **MVC 架构**: 严格分离数据层 (Core)、表现层 (Controllers) 与 UI 层 (UI Toolkit)。
*   **胖客户端，瘦服务端 (Fat Client, Thin Server)**: 
    *   `GameServer` 负责洗牌、发牌、权威状态流转、动作校验和并发仲裁。
    *   `LocalPlayerClient` 和 `SimpleAIClient` 在本地计算吃碰权限和算番，并将意图发往服务端。
    *   网络客户端通过 `RemoteServerProxy` 接收服务端已排序状态，服务端通过 `RemotePlayerClient` 接收玩家意图。
*   **Strategy & Reflection (算番系统)**: 
    *   番种规则通过 `[FanRuleAttribute]` 标记并由 `FanRuleRegistry` 自动注册。
    *   支持多重触发 (`GetMatchCount`) 机制，兼容自定义牌库下的番数累加。
*   **Lazy Singleton (纯 C# 单例)**: 
    *   逻辑层核心管理类（如 `FanRuleRegistry`）脱离 `MonoBehaviour`，采用懒加载确保全局唯一且不依赖场景状态。

## 2. 目录结构与功能索引

### A. `Assets/Scripts/Core` (核心逻辑与表现层控制器)
主要分为纯逻辑与 3D 控制两部分：
*   **基础数据**:
    *   `TileData.cs`: 牌的基础数据结构 (Suit, Value, ID)。
    *   `MahjongEnums.cs`: 存储 Suit, MeldType, WindDirection, GameMode 等核心枚举。
    *   `Meld.cs`: 副露数据结构及暗面标记。
*   **核心算法**:
    *   `MahjongLogic.cs`: 核心算法库（含回溯胡牌判定、手牌多路径拆解、听牌类型分析）。
    *   `ActionValidator.cs`: 静态校验类，判定玩家当前可进行的动作 (吃、碰、杠、胡)。
    *   `DeckConfig.cs`: 玩家自定义牌库配置及异化值计算。
*   **网络与代理 (`Core/Network/` & `Core/Agents/`)**:
    *   `Data/`: 玩家本地存档数据模型 (`PlayerProfile`, `SavedDeck`)。
    *   `Interfaces/ & Mock/`: 抽象网络接口层 (`IAuthService`, `IMatchmakingService`) 与对应 Mock 实现。
    *   `ServerBootstrap.cs` / `ServerBootstrapOptions.cs`: Headless 服务入口，解析端口、房间数、AI 补位、心跳、缓存和恢复窗口参数。
    *   `WebSocketService.cs`: `ws://0.0.0.0:{port}/game` 长连接传输层，限制单条客户端消息为 64 KiB。
    *   `ConnectionRegistry.cs`: 管理 connection ID、连接代次、endpoint、playerId、roomId 和 seatIndex；旧连接代次不得操作新绑定。
    *   `RoomManager.cs`: 处理 Hello 后的创建、加入、Ready、离开、断线、重连和过期房间清理。
    *   `Room.cs`: 单房间聚合根，持有席位、锁定构筑、`GameSession`、跨小局 `TalentMatchRuntime`、中场备牌 tracker、`GameServer`、`StableSeatController` 和每席消息流。
    *   `SeatMessageStream.cs`: 为每个逻辑真人席位提供连续 `seq`、最近 256 条序列化消息缓存及 endpoint 重绑。
    *   `GameServer.cs`: 权威异步对局循环，管理决策截止时间、`decisionId` 和并发仲裁。
    *   `ServerGameState.cs`: 权威记录四席手牌、副露和牌河；超时兜底与恢复快照均从该状态读取。
    *   `RoomGameSnapshot.cs`: 构建按席隐私快照；本家可见完整手牌，他家只包含暗牌数量和公开牌面。
    *   `TalentActionSnapshotCodec.cs`: 通用主动天赋目标与类型化选择集合的私有快照、深拷贝及恢复编解码。
    *   `ClientRoomService.cs`: 客户端协议入口，负责 Hello、房间命令、序号门、心跳、票据、自动重试和 Reconnect/Resync。
    *   `ClientGameState.cs`: 纯 C# 客户端投影，幂等应用有序消息并以完整快照原子替换旧状态。
    *   `RemoteServerProxy.cs`: 将 `ClientRoomService` 的有序游戏状态桥接到 Unity 对局表现，不直接订阅 WebSocket。
    *   `GameSession.cs`: 多局对战状态管理（圈风轮转、门风分配、国标计分、局数追踪）。
    *   `IPlayerClient.cs`: 客户端代理通用接口。含 `CancellationToken TurnCancellationToken` 属性供服务端设置取消令牌。
    *   `SimpleAIClient.cs`: 规则化 AI 客户端。async 方法支持 CancellationToken 取消。
    *   `LocalPlayerClient.cs`: 本地真实玩家客户端，负责桥接 UI 与输入。async 方法支持 CancellationToken 取消。

#### 联机数据流

```text
Dedicated Server
ServerBootstrap
  -> WebSocketService
  -> ConnectionRegistry
  -> RoomManager
  -> Room
  -> GameServer + ServerGameState
  -> SeatMessageStream (per logical human seat)

Client
WebSocketClient
  -> ClientRoomService (Hello / seq / heartbeat / reconnect)
  -> ClientGameState (authoritative projection)
  -> RemoteServerProxy
  -> Hand / River / HUD / Result presentation
```

协议版本为 v10，携带构筑 schema 为 v3。本家私有牌投影携带不透明实体 ID 与异化标记；`GameServer` 持有的 `PrivateTileKnowledgeTracker` 按观察者隔离保存窥探/洞若观火知识，已知对手暗手投影仅携带牌面与观察时可见的异化标记，公共牌河和副露会清洗私有字段。username 目前仅作为开发期身份桥接，经 `IAccountAuthenticator` 规范化为稳定 `playerId`；它不是正式鉴权。`Room` 锁定四席构筑后重建并验证 Low 40 / Standard 80 / High 120 异化值预算，公开消息只携带档位而非其他玩家精确总值。主动天赋、基础动作和备牌提交都使用权威 `decisionId`；类型化天赋选择集合仅进入本家私有快照，客户端只回传所选 `choiceId`，runtime 在执行前重新生成授权集合。半庄/全庄第 4 小局后恰好开放一次备牌阶段。断线时物理 endpoint 与逻辑席位分离，席位可进入 `OfflineReserved` / `AiControlled`，并只在安全决策边界切换控制者。重连使用 `{roomId, streamId}` 和已认证身份定位席位，当前始终请求完整权威快照；Dedicated Server 重启不恢复房间。
*   **表现层控制器 (MonoBehaviour)**:
    *   `HandController.cs`: 管理 3D 手牌生成、布局、DoTween 动画及交互。含 `ForceRemoveTile()` 超时出牌专用方法。
    *   `RiverController.cs`: 管理牌河的 3D 排布。
    *   `OpponentViewController.cs` / `OpponentKnownTileDisplayPolicy.cs`: 按权威暗手总数连续渲染未知牌背与末端排序明牌；布局根据槽位视觉类型恢复各席位正反朝向，避免状态刷新把明牌重置为牌背。
    *   `TileVisual.cs`: 单张牌的视觉容器，处理牌面图片切换。
    *   `TileResourceConfig.cs`: 基于 `ScriptableObject` 的资源索引表。
*   **算番系统 (`Core/Fan/`)**:
    *   `FanContext.cs`: 包含拆解方案、听牌类型、场况信息(WindDirection 风位)的上下文。
    *   `FanRule.cs`: 规则基类，定义优先级、排斥逻辑。
    *   `Rules/FanCalculator.cs`: 汇总番数核心类。
    *   `Rules/FanRuleRegistry.cs`: 纯 C# 单例，自动发现并注册规则类。
    *   `Rules/MCR/MCR_1to6.cs`: 国标 1-6 番种规则。
    *   `Rules/MCR/MCR_8to24.cs`: 国标 8-24 番种规则。
    *   `Rules/MCR/MCR_32Plus.cs`: 国标 32+ 番种规则。

### B. `Assets/Scripts/Systems` (全局系统管理)
*   `ProfileManager.cs`: 玩家本地存档数据管理者。
*   `NetworkManager.cs`: 服务接口与 Additive 多场景加载的枢纽。
*   `LoadingScreenController.cs`: UI Toolkit 加载遮罩控制。
*   `CameraManager.cs`: 多场景动态相机切换控制。
*   `GameManager.cs`: 客户端网络投影和场景协调器；不组装 Server/Clients，不拥有会话或 `TalentMatchRuntime`，也不驱动多局循环。
*   `DeckManager.cs`: 牌山构建、洗牌、发牌管理。`GetWallTiles()` 暴露牌山引用供天赋修改，`ShuffleWall()` 公开由 GameServer 在天赋处理后显式调用。

### C. `Assets/Scripts/Talent` (Roguelike 天赋系统)
纯 C# 规则与跨小局 runtime 架构，镜像算番系统的 Strategy + Reflection 模式。`Room` 在四席构筑锁定后恰好创建一个 `TalentMatchRuntime`，供该场比赛全部 `GameServer` 小局复用。

*   **基础设施**:
    *   `TalentRuleAttribute.cs`: 标记属性，定义天赋的 Id、DisplayName、Description、Tier、AlienationCost、Phases。
    *   `TalentRule.cs`: 运行时抽象基类，覆盖比赛/小局生命周期、起手完成、牌山/摸牌/出牌/动作/算番、主动动作、防御、公开快照和接受胡牌提交钩子。
    *   `TalentContext.cs`: 窄上下文数据类；起手完成时只向每条规则绑定本席不可变事实，不暴露四席权威聚合输入。
    *   `TalentImmutableFacts.cs`: `TalentWinFacts`、`TalentInitialHandFacts` 及实体牌/副露的不可变物理快照。
    *   `TalentActionFacts.cs`: 已提交权威动作事实和每小局只读动作账本；候选、过期及被抢占动作不入账。
    *   `TalentMetadata.cs`: 生命周期、公开策略与备选限制等不可变元数据。
    *   `TalentSlotConfig.cs`: 可序列化 6+3 携带构筑：主槽 index 0=大、1-2=中、3-5=小，另有三个备选槽；支持向下兼容装配。
    *   `TalentRegistry.cs`: 纯 C# 懒加载单例，反射自动发现 `[TalentRuleAttribute]` 标记的类，提供 `CreateInstance`、`GetDisplayName`、`GetDescription`、`GetCost`、`GetTier` 等查询方法。
    *   `TalentRuntimeState.cs`: 每席每个天赋的带类型比赛/小局计数器、标志和公开状态。
    *   `TalentRuntimeEvent.cs`: 带单调事件 ID 的结构化公开/私有天赋事件。
    *   `TalentMatchRuntime.cs`: Room 持有的唯一生命周期协调器，执行起手完成、动作账本、管道、私有 Peek、主动动作、防御/负面效果、公开充能、备牌生效集合、算番归因和小局结束效果；`TalentManager` 与 `SessionTalentPolicy` 已删除。
    *   `TalentActionModels.cs`: 服务端下发的主动天赋目标、Mode/Suit/Seat/Tile 类型化选择集合、请求和结果模型。
    *   `TalentNegativeEffect.cs`: 窄负面效果描述与公开充能能力边界，runtime 保证阻挡时不执行、放行时只执行一次。
    *   `TalentTelemetry.cs`: 匿名玩法记录与 Null/Memory/JSONL sink；Dedicated Server 默认写 `Logs/talent-playtest.jsonl`。
    *   `TalentPrivateTileReveal.cs`: 结构化、脱敏的只读私有牌面揭示结果数据模型。
    *   `TalentDefinition.cs`: ScriptableObject 元数据（图标/显示名/描述），仅供 UI 展示，运行时逻辑不依赖。
*   **具体实现 (`Impl/`)**:
    *   `MidasTouchTalent.cs`: 点金手——摸牌时将风牌/箭牌转化为发财。
    *   `PeekTalent.cs`: 窥探——发牌后通过 FloatingTilePanel 显示牌山顶部 4 张牌。
    *   `DragonAscentTalent.cs`: 如龙——宽松清龙判定。
    *   `DrawRewardTalent.cs`: 厚积——流局得分。
    *   `HeadStartTalent.cs`: 快人一步——降低起胡门槛并加番。
    *   `StartingCapitalTalent.cs`: 初始资金。
    *   `ComposureTalent.cs`: 定心——每小局首次受到的负面天赋效果无效。
    *   `InterceptionTalent.cs`: 截流——整场 3 次，削减一项对手已公开且仍生效的充能天赋。
    *   `TravelLightTalent.cs`: 轻装上阵——起手完成后将本席所有数牌 1/9 向内转为 2/8，并原子提交整手变更。
    *   `SuitConvergenceTalent.cs`: 归色——首个主回合选择万/饼/条，之后前两张非目标花色数牌摸牌转为目标花色。
    *   `ChromaticCompositionTalent.cs`: 异彩成章——合法胡牌含至少 4 张异化实体牌时，每张 +3 番，最多计算 8 张。
    *   `SheathedEdgeTalent.cs`: 藏锋——至少 1 层可发动，消耗全部锋，本局下次合法胡牌每层 +12 番。
    *   `GatherMomentumTalent.cs`: 乘势——吃碰杠积攒跨局公开势，主回合每局一次消耗全部势，本局合法胡牌每层 +8 post-legal 番。
    *   `FadingColorTalent.cs`: 褪色——每局首次打出异化牌获得公开墨，主回合每回合一次消耗 1 墨削减对手公开充能。
    *   `RedirectForceTalent.cs`: 化劲——每局首次阻挡公开充能削减，并将该次控制转化为本局合法胡牌 +4 post-legal 番。
    *   `EncirclementTalent.cs`: 合围——吃碰明杠来自至少两个不同对手后，当局合法胡牌 +4 post-legal 番。
    *   `LastStandFormationTalent.cs`: 背水阵——提交第 2 个新公开副露后，当局独立起胡门槛 +2，满足门槛的合法胡牌奖励 +12 post-legal 番。
    *   `CallTheMarkTalent.cs`: 点将——每局指定一名公开目标，下一次吃碰明杠来自目标时保留当局 +6 post-legal 番奖励。
    *   `FollowTheTrailTalent.cs`: 循迹——荣和时胡牌张与放铳者上一张弃牌同为相同数牌花色，奖励 +4 post-legal 番。
    *   `PiercingInsightTalent.cs`: 洞若观火——每小局一次，私下查看一名其他玩家当前暗手中的所有数牌。
    *   `SetTheToneTalent.cs`: 定调——首次主决策选择万/饼/条，以所选花色数牌胡牌时奖励 +4 post-legal 番。
    *   `PrepareForRiskTalent.cs`: 未雨绸缪——首次主决策选择防自摸或防放铳，并在基础保险或所选风险发生时返还 8 分。
    *   `PruneTheExcessTalent.cs`: 去芜——当局提交第 3 张幺九牌或字牌弃牌后，合法胡牌奖励 +3 post-legal 番。
    *   `BideTheTideTalent.cs`: 候潮——当局提交至少 6 次弃牌后，合法胡牌奖励 +2 post-legal 番。
    *   `ForetellOutcomeTalent.cs`: 预判——首次主决策选择自摸或荣和，胡牌方式匹配时奖励 +3 post-legal 番。
    *   `MisdirectionTalent.cs`: 障眼法——每局一次主动装备，使下一张权威弃牌按数牌花色环或字牌顺序环变换。

#### 天赋定义规范

新增天赋需要：

1. **创建实现类**（`Assets/Scripts/Talent/Impl/` 下），继承 `TalentRule`，添加 `[TalentRuleAttribute]`：
   ```csharp
   [TalentRule(
       "unique_id",          // 唯一标识符，snake_case
       "显示名称",            // 中文友好名称
       "天赋效果的简短描述",   // 一句话描述
       TalentTier.Medium,    // 品阶: Small / Medium / Large
       15,                   // 异化值消耗
       TalentPhase.OnDraw    // 生效阶段 (可多个, params)
   )]
   public class MyTalent : TalentRule
   {
       public override TalentScope Scope => TalentScope.Self; // Self=仅自己, Global=全局
       public override int Priority => 0; // 同阶段内执行优先级，越高越先

       public override TileData OnDraw(TalentContext ctx, TileData tile)
       {
           if (!ctx.IsOwnersTurn) return tile; // Self 天赋需检查
           // 修改 tile 并返回
           tile.IsModified = true;
           tile.SpecialEffectID = Id;
           return tile;
       }
   }
   ```

2. **自动注册**：无需手动注册，`TalentRegistry` 启动时反射扫描自动发现。

3. **可选 SO 资产**：如需图标，创建 `TalentDefinition` ScriptableObject（`Assets/ScriptableObjects/Talents/`），`talentId` 与 Attribute 的 Id 对应。

4. **阶段钩子签名**：
   | 阶段 | 方法签名 | 说明 |
   |------|---------|------|
   | Match / Round | `InitializeMatchState` / `OnRoundStarted` / `OnRoundEnded` | 初始化比赛状态、重置小局状态与结算跨局效果 |
   | InitialHandCompleted | `OnInitialHandCompleted` | 四席发牌进入权威状态后，只读取本席不可变起手事实 |
   | Committed Action | `OnActionCommitted` | 读取已提交动作及当前小局只读账本，候选动作不触发 |
   | WallBuilding | `void OnWallBuilding(TalentWallContext ctx)` | 通过窄上下文修改牌山 |
   | OnDraw | `TileData OnDraw(TalentContext ctx, TileData tile)` | 返回修改后的牌，管道链式 |
   | OnDiscard | `TileData OnDiscard(TalentContext ctx, TileData tile)` | 返回修改后的牌，管道链式 |
   | ActionValidation | `bool OnActionValidation(TalentContext ctx, ClientActionType, TileData)` | 返回 false 可禁止动作 |
   | Scoring | `void OnScoring(TalentContext ctx, FanContext fanCtx)` | 修改 FanContext 影响算番 |
   | Active Action | `GetAvailableActions` / `TryActivate` | 枚举服务器授权选项并执行携带 `decisionId` 的主动动作 |
   | Control Defense | `TryBlockNegativeEffect` | 只读取窄负面效果描述，不能直接执行被阻挡效果 |
   | Accepted Win | `GetPostLegalFanBonus/Penalty` / `OnAcceptedWin` | 合法胡牌后汇总贡献，最终接受时提交一次性状态 |

5. **设计约束**：
   - 天赋逻辑必须纯 C#，不依赖 MonoBehaviour 或 Unity 生命周期
   - `Scope.Self` 天赋在钩子中应检查 `ctx.IsOwnersTurn` 或 `ctx.CurrentPlayerId == ctx.TalentOwnerId`
   - `Scope.Global` 天赋影响所有玩家，应配高异化值作为代价
   - 修改 `TileData` 后应设置 `IsModified = true` 和 `SpecialEffectID = Id`
   - `Room` / `GameServer` 不得按具体 `talentId` 分支；具体效果和可选目标由规则多态与 runtime 统一调度
   - 候选算番、可见性反事实和归因必须使用 detached state 与 null event sink，不能改变权威状态或重复公开事件
   - 主动动作、负面效果与接受胡牌的消耗都必须在服务器权威边界恰好提交一次

### D. `Assets/Scripts/Editor` (编辑器扩展)
*   `TileConfigEditor.cs`: `TileResourceConfig` 自动化图片匹配工具。
*   `SceneSetupMenu.cs`: 一键构建多场景结构的编辑器工具。

### E. `Assets/UI` (UI 表现架构 - UI Toolkit)
每个主要面板由三部分组成：
*   **`.uxml` (布局/视图)**: 定义 UI 的层级结构。
*   **`.uss` (样式)**: 定义 UI 的视觉外观。
*   **`.cs` (控制器)**: 绑定元素并处理交互逻辑。

#### 核心面板与组件：
*   **LoginPanel & MainLobby**: `01_Login` 和 `02_MainLobby` 场景中的 UI 主体面板。Home 页包含 `DeckSelector`（左右箭头循环切换卡组）、异化值显示及匹配入口。
*   **房间大厅面板 (`RoomListPanel`)**: `RoomListPanel.uxml/uss` + `RoomListController.cs` + `RoomCardTemplate.uxml`，独立弹窗浏览当前在线房间。支持多局模式与可用性筛选、出战构筑异化值与房间上限实时预检、房号直连加入与一键快速创建。生命周期集成 `sortingOrder: 50` 自动提升、初始化幂等保护与点击防抖锁定。对应纯 HTML 原型为 `RoomListPreview.html`。
*   **操作面板 (`ActionPanel`)**: 基础吃碰杠胡与主动天赋按钮共存；只消费服务器下发的合法选项，pending/reject/过期/恢复均按 `decisionId` 隔离。
*   **常驻天赋 HUD (`GameHUD`)**: 展示本家生效天赋、已公开对手天赋、最近事件流及三级反馈；恢复快照只重建状态，不重播 toast/音效。
*   **独立备牌面板 (`SideboardPanel`)**: 独立 Scene Object / `UIDocument`，权威阶段到来时全屏显示，隐藏时整个文档 `display:none`，不拦截 ActionPanel 或 3D 手牌输入。
*   **结算面板 (`ResultPanel`)**: 最终番置顶，基础番与天赋门槛/奖励/惩罚逐项展示，使用服务端权威 `TalentFanBreakdown`。
*   **牌库编辑器 (`DeckEditor`)**: 34 种牌、6 主槽 + 3 备选槽和固定预算检查器。右侧表盘实时显示牌山/主天赋/备牌不计入/总计与 Low 40、Standard 80、High 120 档位；未保存草稿在切换、新建、删除当前牌库和退出时统一保护。
*   **天赋模板**: `TalentSlotTemplate.uxml/uss`（槽位显示）、`TalentItemTemplate.uxml`（列表项）。
*   **通用悬浮牌面板 (`FloatingTilePanel`)**: `FloatingTilePanel.uxml/uss` + `FloatingTilePanelController.cs`，支持展示模式（自动关闭+手动关闭）和选择模式（点击回调），用于窥探天赋等需要展示牌面信息的场景。屏幕上方居中，淡入动画，不阻挡底层交互。
*   **牌面图片工具**: `TileImageHelper.cs` 静态类，将 `Suit+Value` 映射为 `Resources` 路径，供 `WaitHintController`、`FloatingTilePanelController` 等共用。
*   **听牌提示面板 (`WaitHintPanel`)**: `WaitHintPanel.uxml/uss` + `WaitHintController.cs`，横向显示听牌列表及最大番数。
*   **复用模板**: `TileItemTemplate.uxml` 等小组件。

### F. `Tests` (长期纯 C# 回归)
*   **`NetworkRegression`**: 覆盖联机权威、恢复隐私、主动动作、备牌、AI、算番归因及全部天赋规则；`UniversalFillerTalentTests.cs` 长期保护定调、未雨绸缪、去芜、候潮、预判和障眼法的选择授权、计数、结算与组合行为。
*   **`GameServerTelemetryRegression`**: 直接编译生产 `GameServer.cs`，验证真实小局生命周期、接受胡牌、杠后流程及自动弃牌；障眼法用例确保变换后的牌同时成为权威牌河记录与响应窗口目标。
