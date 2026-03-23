# CLAUDE.md - SuperMajiang Project Context

## Project Overview
**SuperMajiang** 是一款基于 Unity 的单机 Roguelike 麻将游戏。
- **核心规则**: 国标麻将 (MCR/Guobiao)，支持 81 番种计算
- **特色系统**: Roguelike 天赋系统、34 张自定义牌库、异化值机制
- **当前阶段**: Alpha - 核心循环与规则已实现

## Technical Stack
- **Engine**: Unity 2022.3.61t9 (Tuanjie 1.6.8)
- **UI**: UI Toolkit (UXML/USS) — **禁止使用 Canvas/UGUI**
- **Animation**: DOTween (Pro)
- **Text**: TextMeshPro (SDF)，字体使用 `-unity-font-definition` 引用 SDF 资产
- **Language**: C#

## Architecture
**胖客户端，瘦服务端 (Fat Client, Thin Server)**:
- `GameServer`: 仅负责洗牌、发牌、状态流转、并发仲裁；维护 `ServerGameState` 手牌/副露快照
- `LocalPlayerClient` / `SimpleAIClient`: 本地计算吃碰杠胡权限和算番，将意图发往服务端
- 通过 `IPlayerClient` 接口统一本地玩家与 AI
- **超时取消机制**: 服务端通过 `CancellationToken` 取消客户端 async 操作，`ServerGameState` 提供真实手牌兜底出牌

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
- `TalentManager` 非单例，每局由 `GameServer` 创建，避免跨局状态残留
- 覆盖五个阶段钩子：牌山构建 / 摸牌 / 出牌 / 动作校验 / 算番
- `OnDraw`/`OnDiscard` 返回修改后的 `TileData`，形成管道链式调用
- 6 槽位配置（大×1 + 中×2 + 小×3），向下兼容（大槽可装中/小天赋）
- 异化值 = 牌库异化值 + 天赋异化值之和（`DeckConfig.CalculateTotalAlienation`）
- 天赋配置嵌入 `SavedDeck.Talents`，旧存档无此字段时默认空值，向后兼容

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
│   ├── HandController.cs    # 3D 手牌管理、布局、动画、交互
│   ├── RiverController.cs   # 牌河 3D 排布
│   ├── TileVisual.cs        # 单张牌视觉容器
│   ├── Network/
│   │   ├── Protocol.cs      # 通信数据结构 (ClientAction)
│   │   ├── GameServer.cs    # 异步核心循环 (含 CTS 管理)
│   │   ├── ServerGameState.cs # 服务端手牌/副露快照 (超时兜底/重连)
│   │   └── GameSession.cs   # 多局对战状态管理 (圈风/门风/计分)
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
│   ├── GameManager.cs       # 游戏初始化入口, 多局循环驱动
│   └── DeckManager.cs       # 牌山构建、洗牌、发牌
├── Talent/                  # Roguelike 天赋系统
│   ├── TalentRuleAttribute.cs # 标记属性 (id, displayName, description, tier, cost, phases)
│   ├── TalentRule.cs        # 运行时抽象基类 (阶段钩子)
│   ├── TalentContext.cs     # 上下文数据类
│   ├── TalentSlotConfig.cs  # 可序列化 6 槽位配置
│   ├── TalentRegistry.cs    # 纯 C# 懒加载单例注册中心
│   ├── TalentManager.cs     # 纯 C# 管道执行器 (每局创建)
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
- UI Toolkit 字体引用 `-unity-font-definition` (SDF 资产)，不用原始字体文件
- **超时取消**: 客户端 async 方法通过 `CancellationToken` 实现可取消（`ct.Register(() => tcs.TrySetCanceled())`），外层统一 `catch (OperationCanceledException)`
- **服务端快照**: `ServerGameState` 镜像每个玩家手牌/副露，超时时从快照取真实牌自动出牌，避免虚构兜底牌
- `HandController.ForceRemoveTile()`: 超时自动出牌专用，移除牌到牌河但不触发 `OnTileDiscardedEvent` 避免竞态

### Debugging
- `GameManager` 中 `useDebugHand` 可在 Inspector 配置测试牌型
- `GameManager` 中 `gameMode` 可在 Inspector 切换对局模式 (Single/EastOnly/HalfGame/FullGame)
- 算番开发: 在 `FanRules_Common.cs` 新增规则需实现 `GetMatchCount`，考虑优先级与排斥

### Design Principles
- 客户端不应直接访问 `GameManager.Session` 等全局状态，信息通过接口回调下发
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
- ~~天赋系统重构~~ (已完成: 纯 C# 管道架构、服务端执行、6 槽位 UI、MidasTouch 迁移)
- ~~牌河指针~~ (已完成: Emission 呼吸灯高亮最新出牌)
- ~~通用悬浮牌面板~~ (已完成: FloatingTilePanel 展示/选择双模式，窥探天赋接入)
- ~~天赋选择弹窗美化~~ (已完成: CSS class 重构，品阶颜色区分，结构化布局)

## Reference Docs
- `summary.md`: 项目进度快照与排故日志
- `plan.md`: 当前待办任务与长期优化路线图（仅未完成项）
- `milestone.md`: 已完成里程碑归档（完成的任务记录在此）
- `struct.md`: 详细架构索引
