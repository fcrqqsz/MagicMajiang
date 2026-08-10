# AGENTS.md - SuperMajiang Project Context

## Project Overview
**SuperMajiang** 是一款基于 Unity 的 Roguelike 国标麻将游戏，支持 WebSocket 联机；一人游玩同样使用在线 `Room`，由 AI 补足其余席位。
- **核心规则**: 国标麻将 (MCR/Guobiao)，支持 81 番种计算
- **特色系统**: Roguelike 天赋系统、34 张自定义牌库、异化值机制
- **当前阶段**: Alpha - 核心循环与规则已实现

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
- 协议版本为 v3，携带构筑 schema 为 v2；连接必须先完成 `Hello`，开发期以规范化 username 生成稳定 `playerId`
- 每个真人席位使用独立、连续递增的 `SeatMessageStream`；公共消息也按席序列化，私有手牌、牌库、天赋和窥探结果不得串席
- `RoomGameSnapshot` 只向本家暴露完整暗手牌；客户端使用纯 C# `ClientGameState` 原子应用快照和有序消息
- 所有网络动作携带 `decisionId`；服务端拒绝过期、重复、错误阶段、错误席位和 AI 控制期间的人类动作
- 断线后保留逻辑席位并在安全决策边界由 AI 临时托管；重连通过 username + roomId + streamId 重绑新 endpoint
- 当前重连采用完整权威快照恢复；Dedicated Server 重启不恢复房间，客户端收到终止错误后清理本地票据

**多局对战系统**:
- `GameSession`: 管理多局状态（圈风轮转、门风分配、累计分数）
- 支持 `GameMode`: Single(单局) / EastOnly(东风局4局) / HalfGame(半庄8局) / FullGame(全庄16局)
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
- 携带构筑为 6 个主槽（大×1 + 中×2 + 小×3）及 3 个备选槽；主槽可向下兼容装配
- 异化值档位为 Low 40 / Standard 80 / High 120。服务端重建并验证构筑：总成本 = 牌库异化值 + 当前激活主天赋成本，未激活的三个备选不计成本；精确总值仅本家可见
- 现有六个天赋均已迁入规则重写，并在跨两小局回归中验证

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
│   ├── TalentMatchRuntime.cs # Room 持有的跨小局生命周期、管道与揭示协调器
│   ├── TalentDefinition.cs  # SO 元数据 (UI 图标/描述, 可选)
│   └── Impl/                # 具体天赋实现
│       ├── MidasTouchTalent.cs
│       ├── PeekTalent.cs        # 窥探——发牌后显示牌山顶部4张
│       ├── DragonAscentTalent.cs # 龙腾——宽松清龙判定
│       ├── DrawRewardTalent.cs   # 摸牌奖励
│       ├── HeadStartTalent.cs    # 先发制人——固定加番
│       └── StartingCapitalTalent.cs # 启动资金
└── Editor/
    └── TileConfigEditor.cs  # 编辑器扩展

Assets/UI/                   # UI Toolkit 面板
├── MainLobby.uxml/uss       # 大厅主界面 (含 DeckSelector 卡组切换器)
├── LobbyController.cs       # 大厅逻辑 (标签页切换、卡组选择、匹配入口)
├── DeckEditorToolkit.cs     # 牌库编辑器 (含天赋槽 UI 与天赋选择弹窗)
├── TalentSlotTemplate.uxml/uss # 天赋槽位模板与样式
├── TalentItemTemplate.uxml  # 天赋列表项模板
├── FloatingTilePanel.uxml/uss  # 通用悬浮牌面板 (窥探天赋等)
├── FloatingTilePanelController.cs # 悬浮面板控制器 (展示/选择双模式)
├── TileImageHelper.cs       # 牌面图片路径共享工具类
├── WaitHintPanel.uxml/uss   # 听牌提示面板
├── WaitHintController.cs    # 听牌提示控制器
├── ActionPanel/             # 操作按钮面板
├── ResultPanel/             # 结算面板 (番种详情)
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
- **网络消息顺序**: 房间内服务端消息必须经过席位 `SeatMessageStream`；客户端只消费 `ClientRoomService` 完成排序、去重后的状态
- **快照隐私**: 新增快照字段时必须检查本家/他家可见性，禁止包含其他真人的完整手牌、牌库、天赋或私有窥探结果
- **决策边界**: AI 托管和真人交还只在新决策边界切换，不得中途抢占已经打开的决策
- **连接代次**: WebSocket 回调、房间命令和 endpoint 重绑必须校验当前连接代次，旧 socket 的迟到消息不得改变新连接状态

### Debugging
- `GameManager` 中 `useDebugHand` 可在 Inspector 配置测试牌型
- `GameManager` 中 `gameMode` 可在 Inspector 选择请求的对局模式 (Single/EastOnly/HalfGame/FullGame)
- 算番开发: 在 `FanRules_Common.cs` 新增规则需实现 `GetMatchCount`，考虑优先级与排斥
- Dedicated Server 使用 `Tools > Build > Dedicated Server (Windows)` 构建，不得修改客户端 Build Settings 首场景
- 联机自动回归：`dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore`
- 完整联机验证步骤见 `docs/network_verification.md`

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
- 结算手牌缩略图复盘
- 对象池性能优化
- ~~超时取消机制~~ (已完成: CancellationToken + ServerGameState)
- ~~Home 页卡组选择器~~ (已完成: DeckSelector 左右箭头循环切换)
- ~~多局对战 UI 完善~~ (已完成: 风位显示、分数面板、GameMode 选择器)
- ~~天赋系统重构~~ (已完成: Room 持有跨小局 runtime、服务端异化预算、6+3 构筑与六天赋迁移)
- ~~牌河指针~~ (已完成: Emission 呼吸灯高亮最新出牌)
- ~~通用悬浮牌面板~~ (已完成: FloatingTilePanel 展示/选择双模式，窥探天赋接入)
- ~~天赋选择弹窗美化~~ (已完成: CSS class 重构，品阶颜色区分，结构化布局)

## Reference Docs
- `summary.md`: 项目进度快照与排故日志
- `plan.md`: 当前待办任务与长期优化路线图（仅未完成项）
- `milestone.md`: 已完成里程碑归档（完成的任务记录在此）
- `struct.md`: 详细架构索引
- `docs/network_verification.md`: Dedicated Server、多人联机、多局与重连验证指南
