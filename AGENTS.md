# AGENTS.md - SuperMajiang Project Context

## Project Overview
**SuperMajiang** 是一款基于 Unity 的 Roguelike 国标麻将游戏，支持 WebSocket 联机；一人游玩同样使用在线 `Room`，由 AI 补足其余席位。
- **核心规则**: 国标麻将 (MCR/Guobiao)，支持 81 番种计算
- **特色系统**: Roguelike 天赋系统、34 张自定义牌库、异化值机制
- **当前阶段**: Alpha - UI、联机权威、天赋垂直切片与卡组预算编辑器已完成

## Technical Stack
- **Engine**: Unity 2022.3.61t9 (Tuanjie 1.6.8)
- **UI**: UI Toolkit (UXML/USS) — **禁止使用 Canvas/UGUI**
- **Animation**: DOTween (Pro)
- **Text**: UI Toolkit 的 `-unity-font-definition` 统一引用由 `Assets/Font/MSYH.TTC` 生成的 TextCore `Assets/Font/MSYH_UITK.asset`；不得直接引用 TTC 或 TMP 的 `MSYH_SDF.asset`
- **Language**: C#

## Architecture
**胖客户端，瘦服务端 (Fat Client, Thin Server)**:
- `GameServer`: 负责洗牌、发牌、状态流转、服务端动作校验与并发仲裁；维护 `ServerGameState` 手牌、副露和牌河权威状态
- `LocalPlayerClient` / `SimpleAIClient`: 本地计算吃碰杠胡权限和算番，将意图发往服务端
- 通过 `IPlayerClient` 接口统一本地玩家与 AI
- **超时取消机制**: 服务端通过 `CancellationToken` 取消客户端 async 操作，`ServerGameState` 提供真实手牌兜底出牌

**联机服务端与房间系统**:
- 正式服务端使用 Dedicated Server / Headless 构建，唯一启动场景为 `00_ServerBootstrap`；客户端默认首场景仍为 `00_Persistent`
- 服务端链路：`ServerBootstrap -> WebSocketService -> ConnectionRegistry -> RoomManager -> Room -> GameServer`
- `ConnectionRegistry` 分离物理 WebSocket、连接代次、开发期身份和逻辑席位；旧 endpoint 的迟到回调必须被代次校验丢弃
- `RoomManager` 管理房间生命周期，`Room` 持有四席构筑、`GameSession`、跨小局复用的 `TalentMatchRuntime`、`GameServer`、席位消息流及断线托管状态
- 协议版本为 v11，携带构筑 schema 为 v3；连接必须先完成 `Hello`，开发期以规范化 username 生成稳定 `playerId`
- 每个真人席位使用独立、连续递增的 `SeatMessageStream`；公共消息也按席序列化，私有手牌、牌库、天赋和窥探结果不得串席
- `RoomGameSnapshot` 只向本家暴露完整暗手牌；客户端使用纯 C# `ClientGameState` 原子应用快照和有序消息
- `PrivateTileKnowledgeTracker` 由每小局 `GameServer` 持有，按观察者隔离追踪窥探/洞若观火获得的已知对手暗手；客户端只收到不含物理牌 ID 的当前全量投影，重连不重放过期揭示弹窗
- 所有网络动作携带 `decisionId`；服务端拒绝过期、重复、错误阶段、错误席位和 AI 控制期间的人类动作
- 断线后保留逻辑席位并在安全决策边界由 AI 临时托管；重连通过 username + roomId + streamId 重绑新 endpoint
- 当前重连采用完整权威快照恢复；Dedicated Server 重启不恢复房间，客户端收到终止错误后清理本地票据

**多局对战系统**:
- `GameSession`: 管理多局状态（圈风轮转、门风分配、累计分数）
- 支持 `GameMode`: Single(单局) / EastOnly(东风局4局) / HalfGame(半庄8局) / FullGame(全庄16局)
- 起始分由 `SessionScoreRules` 统一定义：Single 50 / EastOnly 100 / HalfGame 150 / FullGame 200；完整局末天赋结算后任一席 `<= 0` 即击飞终局，负分保留
- `SessionEnd` 是客户端唯一终局权威；服务端发送后立即解绑并移除房间、保留 WebSocket，终局房间不支持重连或结果补领
- `WindDirection` 枚举值与牌面 Value 对齐（East=1..North=4），番种规则无需转换
- 国标规则：无庄家概念，东家仅决定先摸牌和门风分配，计分无翻倍
- 计分：自摸三家各付(8+番数)；点炮放炮者付(8+番数)另两家各付底分8；流局不计分

**算番系统**: Strategy + Reflection 模式
- 规则通过 `[FanRuleAttribute]` 标记，由 `FanRuleRegistry` 自动注册
- 支持 `GetMatchCount` 多重触发，兼容自定义牌库番数累加
- 新增规则需考虑优先级与 `ExcludedRuleIds` 排斥逻辑

**天赋系统**: 纯 C# 管道架构，服务端统一执行
- 天赋通过 `[TalentRuleAttribute]` 标记，由 `TalentRegistry` 反射自动注册（镜像 `FanRuleRegistry` 模式）
- 四席构筑锁定后，`Room` 恰好创建一个 `TalentMatchRuntime`，并供所有 `GameServer` 小局复用；`TalentManager` 与 `SessionTalentPolicy` 已删除
- runtime 负责带类型的比赛/小局状态、生命周期、管道、事件、私有窥探、算分选项、揭示及小局结束效果
- 覆盖牌山构建、摸牌、出牌、动作校验和算番钩子；`OnDraw`/`OnDiscard` 返回修改后的 `TileData`，形成管道链式调用
- `OnActionCommitted` 只接收已经提交的权威动作与只读小局账本；候选、过期和被抢占动作不入账
- `OnDiscard` 的最终结果先写入权威牌河，再作为对手响应窗口的唯一目标；超时自动弃牌同样经过完整天赋管道
- 携带构筑为 6 个主槽（大×1 + 中×2 + 小×3）及 3 个备选槽；主槽可向下兼容装配
- 异化值档位为 Low 40 / Standard 80 / High 120。服务端重建并验证构筑：总成本 = 牌库异化值 + 当前激活主天赋成本，未激活的三个备选不计成本；精确总值仅本家可见
- 当前二十六个天赋均由规则类实现：点金手、窥探、如龙、厚积、快人一步、初始资金、定心、截流、藏锋、轻装上阵、归色、异彩成章、乘势、褪色、化劲、合围、背水阵、点将、循迹、洞若观火、定调、未雨绸缪、去芜、候潮、预判、障眼法
- 主动天赋使用服务端权威 `decisionId` 和目标投影；负面效果先经过目标席防御管道
- 半庄/全庄第 4 小局后进入一次中场备牌，45 秒内从携带的 6+3 天赋中重新锁定生效集合；AI、断线和超时由服务端提交合法方案
- 胡牌番数拆为基础番、天赋门槛/奖励/惩罚和最终番，最终值及逐项归因随权威结果与恢复快照下发
- Dedicated Server 将匿名玩法事件及 `session_end` 写入紧凑 JSONL；遥测失败不得中断房间或小局生命周期

**单例模式**: 逻辑层使用纯 C# 懒加载单例，不依赖 MonoBehaviour/场景状态

## Directory Structure
```
Assets/Scripts/
├── Core/                    # 核心逻辑与表现层
│   ├── TileData.cs          # 牌数据结构 (Suit, Value, ID)
│   ├── MahjongEnums.cs      # Suit, MeldType, WindDirection, GameMode 等枚举
│   ├── Meld.cs              # 副露数据结构
│   ├── MahjongLogic.cs      # 核心算法 (回溯胡牌判定、多路径拆解、听牌分析)
│   ├── ActionValidator.cs   # 吃碰杠胡动作校验
│   ├── DeckConfig.cs        # 牌库配置与异化值计算
│   ├── AlienationPreset.cs  # Low 40 / Standard 80 / High 120 服务端预算
│   ├── HandController.cs    # 3D 手牌管理、布局、动画、交互
│   ├── RiverController.cs   # 牌河 3D 排布
│   ├── TileVisual.cs        # 单张牌视觉容器
│   ├── Network/
│   │   ├── ServerBootstrap.cs # Dedicated Server 启动入口与参数装配
│   │   ├── ConnectionRegistry.cs # 物理连接、身份、房间和席位映射
│   │   ├── RoomManager.cs   # 房间创建、加入、Ready、断线和清理
│   │   ├── Room.cs          # 单房间会话、席位消息流和 GameServer 生命周期
│   │   ├── GameServer.cs    # 服务端权威异步对局循环
│   │   ├── ServerGameState.cs # 服务端手牌/副露/牌河权威状态
│   │   ├── RoomGameSnapshot.cs # 按席隐私恢复快照
│   │   ├── ClientRoomService.cs # Hello、房间命令、排序、心跳和重连
│   │   ├── ClientGameState.cs # 客户端纯 C# 权威状态投影
│   │   └── GameSession.cs   # 多局对战管理 (圈风/门风/计分)
│   ├── Agents/
│   │   ├── IPlayerClient.cs # 客户端代理接口
│   │   ├── SimpleAIClient.cs
│   │   └── LocalPlayerClient.cs
│   └── Fan/                 # 算番系统
│       ├── FanContext.cs    # 拆解方案、听牌类型、场况上下文 (WindDirection 风位)
│       ├── FanRule.cs       # 规则基类
│       └── Rules/
│           ├── FanCalculator.cs
│           ├── FanRuleRegistry.cs
│           └── MCR/             # 国标麻将番种 (按番数分文件)
│               ├── MCR_1to6.cs  # 1-6番 (33种)
│               ├── MCR_8to24.cs # 8-24番 (28种)
│               └── MCR_32Plus.cs # 32+番 (18种)
├── Systems/                 # 全局管理
│   ├── GameManager.cs       # 客户端网络投影与场景协调，不持有服务端/会话/runtime
│   └── DeckManager.cs       # 牌山构建、洗牌、发牌
├── Talent/                  # Roguelike 天赋系统
│   ├── TalentRuleAttribute.cs # 标记属性 (id, displayName, description, tier, cost, phases)
│   ├── TalentRule.cs        # 运行时抽象基类 (阶段钩子)
│   ├── TalentContext.cs     # 上下文数据类
│   ├── TalentSlotConfig.cs  # 可序列化 6+3 携带构筑（主槽 + 备选槽）
│   ├── TalentRegistry.cs    # 纯 C# 懒加载单例注册中心
│   ├── TalentMetadata.cs    # 生命周期、公开策略与备选限制元数据
│   ├── TalentRuntimeState.cs # 跨小局的天赋状态
│   ├── TalentRuntimeEvent.cs # 结构化公开/私有天赋事件
│   ├── TalentActionModels.cs # 主动天赋目标、类型化选择、请求与结果
│   ├── TalentActionFacts.cs # 已提交权威动作事实与小局只读账本
│   ├── TalentImmutableFacts.cs # 起手与接受胡牌等不可变事实
│   ├── TalentPrivateTileReveal.cs # 脱敏私有牌面揭示结果
│   ├── TalentMatchRuntime.cs # Room 持有的跨小局生命周期、管道与揭示协调器
│   ├── TalentDefinition.cs  # SO 元数据 (UI 图标/描述, 可选)
│   └── Impl/                # 26 个具体天赋规则类；完整索引见 struct.md
└── Editor/
    └── TileConfigEditor.cs  # 编辑器扩展

Assets/UI/                   # UI Toolkit 面板
├── MainLobby.uxml/uss       # 大厅主界面 (含 DeckSelector 卡组切换器)
├── LobbyController.cs       # 大厅逻辑 (标签页切换、卡组选择、匹配入口)
├── RoomListPanel.uxml/uss   # 房间大厅独立浏览弹窗 UIDocument
├── RoomListController.cs    # 房间大厅控制器 (生命周期、防抖、自动层级提升)
├── RoomCardTemplate.uxml    # 房间卡片复用模板 (异化适配、状态徽章、加入)
├── RoomListPreview.html     # 房间大厅纯 HTML/CSS 原型预览
├── DeckEditorToolkit.cs     # 牌库编辑器（固定预算表盘、6+3天赋、未保存保护）
├── TalentSlotTemplate.uxml/uss # 天赋槽位模板与样式
├── TalentItemTemplate.uxml  # 天赋列表项模板
├── FloatingTilePanel.uxml/uss  # 通用悬浮牌面板 (窥探天赋等)
├── FloatingTilePanelController.cs # 悬浮面板控制器 (展示/选择双模式)
├── TileImageHelper.cs       # 牌面图片路径共享工具类
├── WaitHintPanel.uxml/uss   # 听牌提示面板
├── WaitHintController.cs    # 听牌提示控制器
├── ActionPanel.uxml/uss     # 基础动作与主动天赋操作面板
├── GameHUD/                 # 常驻天赋 chip、事件流和主动效果反馈
├── SideboardPanel.uxml/uss  # 独立中场备牌 UIDocument
├── SideboardPanelController.cs # 备牌草稿、倒计时与锁定表现
├── ResultPanel.uxml/uss     # 最终番置顶、基础番与天赋逐项结算
├── DeckEditor/              # 牌库编辑器视图与样式
└── Templates/               # 复用模板 (TileItemTemplate 等)
```

## Development Conventions

### Code Style
- C# 命名遵循 .NET 标准: PascalCase (类/方法/属性), camelCase (局部变量/参数)
- 逻辑层纯 C# 类优先，表现层才用 MonoBehaviour
- UI 面板三件套: `.uxml` (布局) + `.uss` (样式) + `.cs` (控制器)

### Key Patterns
- DoTween 动画绑定动态 GameObject 时，**必须**链式调用 `.SetLink(gameObject)` 防止销毁报错
- `FanRuleRegistry` / `TalentRegistry` 均为纯 C# 单例，属性懒加载，避免空引用
- 胡牌计算使用多路径拆解算法，遍历所有方案取番数最大值
- UI Toolkit 字体引用 `-unity-font-definition` 时，统一使用由 `Assets/Font/MSYH.TTC` 生成的 TextCore `Assets/Font/MSYH_UITK.asset`。不得直接引用 TTC；`MSYH_SDF.asset` 是 TMP_FontAsset，当前 UI Toolkit 不支持，禁止在 USS 中引用。共用 `PanelSettings.asset` 必须绑定 `SuperMajiangTextSettings.asset`
- **超时取消**: 客户端 async 方法通过 `CancellationToken` 实现可取消（`ct.Register(() => tcs.TrySetCanceled())`），外层统一 `catch (OperationCanceledException)`
- **服务端快照**: `ServerGameState` 镜像每个玩家手牌/副露，超时时从快照取真实牌自动出牌，避免虚构兜底牌
- `HandController.ForceRemoveTile()`: 超时自动出牌专用，移除牌到牌河但不触发 `OnTileDiscardedEvent` 避免竞态
- **对手已知牌表现**: `OpponentViewController` 必须按权威暗手总数连续排列“未知牌背 + 末端排序明牌”。布局刷新时须按槽位视觉类型分别恢复朝向；不得对明牌使用会被布局 `DOKill()` 截断的缩放翻牌动画，否则会出现侧向薄片或摸弃牌后全部翻回牌背
- **网络消息顺序**: 房间内服务端消息必须经过席位 `SeatMessageStream`；客户端只消费 `ClientRoomService` 完成排序、去重后的状态
- **快照隐私**: 新增快照字段时必须检查本家/他家可见性，禁止包含其他真人的完整手牌、牌库、天赋或私有窥探结果
- **决策边界**: AI 托管和真人交还只在新决策边界切换，不得中途抢占已经打开的决策
- **连接代次**: WebSocket 回调、房间命令和 endpoint 重绑必须校验当前连接代次，旧 socket 的迟到消息不得改变新连接状态

### Dynamic UI Toolkit Panel Input Ownership
- 每个独立 `UIDocument` 都是独立的面板输入层；`sortingOrder` 较高的文档即使内容透明或子面板已隐藏，也可能继续阻断较低文档中的按钮。规划排序时必须同时考虑显示顺序和输入所有权，不能只看视觉层级。
- 动态弹窗、目标选择器和阶段面板在隐藏时，必须让整个 `UIDocument.rootVisualElement` 使用 `DisplayStyle.None`，显示时再恢复为 `DisplayStyle.Flex`。只隐藏内部子节点、只降低透明度或只设置根节点 `PickingMode.Ignore`，都不足以保证跨 `UIDocument` 的输入穿透。
- 可见时需要拦截全屏输入的 overlay 应保持可拾取；仅用于布局、允许点击穿透的父容器才使用 `PickingMode.Ignore`。不要把 `PickingMode.Ignore` 递归施加到实际按钮或全屏拦截层。
- 控制网络订阅和权威状态的 `MonoBehaviour` 应在面板隐藏期间继续存活，以便接收呼出消息；优先切换文档根节点的 `display`，不要为了隐藏 UI 直接停用同时承担消息订阅的 GameObject。若确需禁用 `UIDocument` 组件，必须处理视觉树重建、元素重新查询和回调重新绑定。
- `Show`、`Hide`、取消、超时、恢复、回合切换和 `OnDestroy` 必须汇入同一套可见性与清理边界：停止 schedule/coroutine/tween，解绑临时回调，清除旧选择状态，并恢复下层面板输入。
- 新增或提高动态 `UIDocument` 的排序后，Unity 人工验收至少覆盖三种状态：隐藏时下层 ActionPanel/3D 手牌可操作；显示时本面板按钮、目标和取消可操作；关闭后输入立即归还下层。纯 C# 测试可检查状态策略和源码约束，但不能替代跨面板实际点击验证。

### UI Prototyping & HTML-to-UI Toolkit Workflow
- **工作流原则**: 设计复杂/新界面时，推荐先编写纯 HTML/CSS 预览文件（如 `xxxPreview.html`）供快速查看布局和迭代设计；用户确认后再 1:1 转译为 `.uxml`、`.uss` 和 `.cs` 控制器。
- **HTML/CSS 严格子集约束（严禁使用 UI Toolkit 不支持的语法）**:
  - **严禁 USS 不支持的 CSS 属性**:
    - ❌ `cursor`: 严禁写 `cursor: pointer/link/not-allowed`（Unity 运行时会报 `Runtime cursors need to be defined using a texture` 警告）。
    - ❌ `box-shadow` / `drop-shadow` / `filter`: USS 不支持阴影和滤镜，必须用 `border`、背景微调或容器色块实现层级。
    - ❌ `transform`: 严禁在基础 USS 中写 `transform: translate/rotate/scale`。
    - ❌ `display: grid / inline / block / table`: USS 仅支持 `flex` 与 `none`。
    - ❌ `z-index`: USS 无 `z-index`，层级严格由 DOM 节点先后顺序（后声明在上）或跨 `UIDocument` 的 `sortingOrder` 决定。
    - ❌ 不支持的高级选择器: 严禁 `:nth-child()`, `::before`, `::after`, `:first-child`, `:last-child`。仅允许类名选择器和 `:hover`, `:active`, `:focus`, `:checked`, `:disabled`。
    - ❌ 不支持的相对单位: 严禁 `rem`, `em`, `vh`, `vw`, `calc(...)`。仅允许 `px` 和 `%`。
    - ❌ CSS 复合简写 (Shorthands): USS 必须完全展开属性，如 `border-left-width`, `border-top-left-radius`, `margin-left`, `padding-top`。
  - **Flexbox 默认方向差异**:
    - HTML 浏览器 `display: flex` 默认是 **`row`（水平）**；Unity UI Toolkit `VisualElement` 默认是 **`column`（垂直）**。
    - HTML 原型中的所有容器**必须显式声明** `flex-direction: column` 或 `flex-direction: row`，保证转译后行为完全对称。
  - **字符集与 Emoji 绝对禁令**:
    - HTML 浏览器有系统彩色 Emoji 字库，但 Unity TextCore 字体（`MSYH_UITK.asset`）无 Emoji 字符集，会导致 `[□]` 乱码。
    - HTML 原型和 UXML 中**严禁包含任何 Unicode Emoji**（如 📋, 👑, 🔄, 🀄, ●, ✓, ✕），必须全部使用纯中文或标准安全 ASCII 字符（如 `[等待中]`, `X`, `>`, `<=`）。
  - **标签与控件映射**:
    - `<div> / <section> / <header>` ➡️ `<ui:VisualElement>`
    - `<span> / <p> / <label>` ➡️ `<ui:Label>`
    - `<button>` ➡️ `<ui:Button>`
    - `<input type="text">` ➡️ `<ui:TextField>`
    - `<input type="checkbox">` ➡️ `<ui:Toggle>`
    - 滚动容器 ➡️ `<ui:ScrollView>`
  - **字体与排版属性**:
    - USS 根节点统一引用 `-unity-font-definition: url('project://database/Assets/Font/MSYH_UITK.asset');`。
    - 文本居中与对齐使用 `-unity-text-align: middle-center;`，加粗使用 `-unity-font-style: bold;`。

### Debugging
- `GameManager` 中 `useDebugHand` 可在 Inspector 配置测试牌型
- `GameManager` 中 `gameMode` 可在 Inspector 选择请求的对局模式 (Single/EastOnly/HalfGame/FullGame)
- 算番开发: 在 `FanRules_Common.cs` 新增规则需实现 `GetMatchCount`，考虑优先级与排斥
- Dedicated Server 使用 `Tools > Build > Dedicated Server (Windows)` 构建，不得修改客户端 Build Settings 首场景
- 联机自动回归：`dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore`
- 真实 `GameServer` 算番/遥测回归：`dotnet run --project Tests\GameServerTelemetryRegression\GameServerTelemetryRegression.csproj --no-restore`
- 完整联机验证步骤见 `docs/network_verification.md`

### Automated Validation and Unity Integration Boundary
- 智能体的日常自动验证以纯 C# 为主：优先运行与改动相关的 focused regression，再运行必要的完整 `NetworkRegression` 或其他纯 C# 回归工程。
- 对 `.uxml` / `.uss` 的日常静态检查仅限 XML 结构、资源路径、源码约束和纯策略测试；实际布局、中文字体、动画、音效及场景实例化通过 UnityMCP 或人工在 Unity 中验收，静态检查不能替代实际运行验证。
- 已完成人工验收的 UI 不长期保留 UXML/USS/Scene 源码形状、`.meta` GUID 或占位音频字节级测试；长期测试只保护玩法、网络权威、隐私、恢复和纯展示策略。
- **禁止智能体手写、猜测、复制或修复 Unity `.meta` GUID**。新增 Unity 资产时允许暂时没有 `.meta`，必须等待 Unity/Tuanjie Refresh 权威生成。
- **UnityMCP 持续授权**：用户已授权本项目任务范围内的 MCP 操作，无需逐项或每个任务再次询问。UnityMCP 已连接时，智能体可直接执行任务所需的 Refresh、编译状态/Console 检查、场景与资源编辑和保存、Play Mode 操作及视觉/音频 smoke test。
- 如果实现需要在场景、Prefab、UXML 或其他序列化资产中引用新资产 GUID，必须先由 Unity/Tuanjie Refresh 权威生成；不得预造 GUID。UnityMCP 已连接时直接执行 Refresh 并等待导入完成；MCP 不可用时再请求人工执行。
- **禁止智能体编辑、临时补项或以其他方式修补 Unity 生成的 `Assembly-CSharp.csproj` 等工程文件**，也不得把未 Refresh 导致的生成工程缺项视为源码编译失败。
- Unity/Tuanjie Refresh、Unity Console 导入与编译确认、`Assembly-CSharp` 权威生成及场景视觉/音频 smoke test 优先通过已连接的 UnityMCP 完成；仅 MCP 不可用或无法验证的实际交互交由人工验收。执行前仍需确认目标实例、编辑器就绪状态和待修改对象，执行后检查结果与资源副作用。
- 人工或 UnityMCP Refresh 完成后，智能体可以只读检查并提交 Unity 实际生成的 `.meta` 或其他必要资产变更，但不得重写其 GUID 或用自生成内容替换。
- MCP 持续授权不代表可以手写 Unity 生成资产，也不授权绕开 MCP 通过命令行启动 Unity/Tuanjie batch mode 或构建 Unity 生成的 `Assembly-CSharp.csproj`；这些非 MCP 操作仍需分别明确授权。
- 人工 Unity 关口尚未完成时，智能体必须明确报告“纯 C# 验证通过，Unity 集成/视觉验收待人工执行”，不得宣称完整 Unity 验证通过。
- 新增全屏模态流程、独立阶段面板或跨场景表现组件时，智能体必须先评估其布局、输入拦截、排序、生命周期、恢复和网络绑定归属；不得仅为了少建 Scene Object、少做一次 Unity 配置或复用现有入口，就默认挂载到已有 HUD/Controller。
- 如果新界面具有独立显示阶段、全屏输入所有权或独立恢复状态，应优先设计为独立 `UIDocument` / Scene Object。复用现有宿主与新建独立宿主之间存在架构取舍时，必须在实现前向用户说明方案和影响并取得确认，不得自行选择便利方案后再以局部样式补丁修复耦合问题。

### Design Principles
- 客户端没有隐式本地权威路径；`GameManager` 仅协调网络投影和场景，不持有服务端、会话或天赋 runtime
- 客户端不得自行推导联机权威分数、轮次、牌河或决策状态；统一从 `ClientGameState` 投影读取
- `RemoteServerProxy` 不直接订阅原始 WebSocket，只消费 `ClientRoomService` 的有序消息和恢复快照
- 服务端逻辑不得依赖 `GameManager.Instance`、`DeckManager.Instance`、HUD、手牌表现层或游戏场景对象
- 牌河清理等公共操作放在 `MahjongHandViewBase` 基类，避免各客户端重复实现
- `ClearHand()` 基类会自动清理关联的牌河 (`myRiver.Clear()`)

## Current Priorities
参阅 `plan.md` 获取完整任务列表。当前重点:
- 异化牌视觉反馈
- 发牌与摸牌 DoTween 动画
- 对象池性能优化
- ~~超时取消机制~~ (已完成: CancellationToken + ServerGameState)
- ~~Home 页卡组选择器~~ (已完成: DeckSelector 左右箭头循环切换)
- ~~多局对战 UI 完善~~ (已完成: 风位显示、分数面板、GameMode 选择器)
- ~~天赋系统重构与玩法扩充~~（已完成：Room 持有跨小局 runtime、服务端异化预算、6+3 构筑与二十六天赋）
- ~~天赋主动动作、中场备牌与战术 UI~~（已完成：独立备牌面板、HUD 三级反馈、番数归因、AI 与遥测）
- ~~卡组预算检查器~~（已完成：固定表盘、三档直选、实时拆分、未保存离开保护）
- ~~牌河指针~~ (已完成: Emission 呼吸灯高亮最新出牌)
- ~~通用悬浮牌面板~~ (已完成: FloatingTilePanel 展示/选择双模式，窥探天赋接入)
- ~~天赋选择弹窗美化~~ (已完成: CSS class 重构，品阶颜色区分，结构化布局)

## Reference Docs
- `summary.md`: 项目进度快照与排故日志
- `plan.md`: 当前待办任务与长期优化路线图（仅未完成项）
- `milestone.md`: 已完成里程碑归档（完成的任务记录在此）
- `struct.md`: 详细架构索引
- `unity_mcp_guide.md`: UnityMCP 连接、Refresh、场景操作、资源副作用与排障经验
- `docs/network_verification.md`: Dedicated Server、多人联机、多局与重连验证指南
