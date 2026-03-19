# SuperMajiang 架构概览 (Architectural Overview)

本文档作为项目的核心架构索引，详细记录了系统的设计模式、目录结构及职责。

## 1. 核心设计模式
*   **多场景架构 (Multi-Scene Architecture)**: 
    *   `00_Persistent`: 作为游戏持久入口，承载所有“不死”的单例管理器（Profile、Network 等）。
    *   `01_Login` & `02_MainLobby`: UI 专用子场景，通过 Additive 模式叠加加载。
    *   `03_Game`: 麻将核心 3D 对局场景。
*   **MVC 架构**: 严格分离数据层 (Core)、表现层 (Controllers) 与 UI 层 (UI Toolkit)。
*   **胖客户端，瘦服务端 (Fat Client, Thin Server)**: 
    *   `GameServer` 仅负责洗牌、发牌、状态流转和仲裁并发请求。
    *   `LocalPlayerClient` 和 `SimpleAIClient` 在本地计算吃碰权限和算番，并将意图发往服务端。
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
    *   `Protocol.cs`: 客户端与服务端通信的数据结构 (`ClientAction`)。
    *   `GameServer.cs`: 异步核心循环，管理单局状态与并发仲裁。集成 `CancellationTokenSource` 管理回合取消。
    *   `ServerGameState.cs`: 服务端手牌/副露快照。每次摸牌、出牌、副露时同步更新，超时时提供真实手牌自动出牌，未来可用于重连恢复。
    *   `GameSession.cs`: 多局对战状态管理（圈风轮转、门风分配、国标计分、局数追踪）。
    *   `IPlayerClient.cs`: 客户端代理通用接口。含 `CancellationToken TurnCancellationToken` 属性供服务端设置取消令牌。
    *   `SimpleAIClient.cs`: 规则化 AI 客户端。async 方法支持 CancellationToken 取消。
    *   `LocalPlayerClient.cs`: 本地真实玩家客户端，负责桥接 UI 与输入。async 方法支持 CancellationToken 取消。
*   **表现层控制器 (MonoBehaviour)**:
    *   `HandController.cs`: 管理 3D 手牌生成、布局、DoTween 动画及交互。含 `ForceRemoveTile()` 超时出牌专用方法。
    *   `RiverController.cs`: 管理牌河的 3D 排布。
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
*   `GameManager.cs`: 游戏初始化入口，组装 Server 与 Clients，驱动多局循环。
*   `DeckManager.cs`: 牌山构建、洗牌、发牌管理。`GetWallTiles()` 暴露牌山引用供天赋修改，`ShuffleWall()` 公开由 GameServer 在天赋处理后显式调用。

### C. `Assets/Scripts/Talent` (Roguelike 天赋系统)
纯 C# 管道架构，镜像算番系统的 Strategy + Reflection 模式。

*   **基础设施**:
    *   `TalentRuleAttribute.cs`: 标记属性，定义天赋的 Id、DisplayName、Description、Tier、AlienationCost、Phases。
    *   `TalentRule.cs`: 运行时抽象基类，提供 5 个阶段钩子虚方法（OnWallBuilding / OnDraw / OnDiscard / OnActionValidation / OnScoring）。
    *   `TalentContext.cs`: 上下文数据类，传递当前玩家 ID、牌山引用、GameState 快照等信息。
    *   `TalentSlotConfig.cs`: 可序列化 6 槽位配置（index 0=大, 1-2=中, 3-5=小），支持向下兼容装配。
    *   `TalentRegistry.cs`: 纯 C# 懒加载单例，反射自动发现 `[TalentRuleAttribute]` 标记的类，提供 `CreateInstance`、`GetDisplayName`、`GetDescription`、`GetCost`、`GetTier` 等查询方法。
    *   `TalentManager.cs`: 管道执行器（非单例，每局由 GameServer 创建）。维护 `_playerTalents` 和 `_phasePipelines`，按 Priority 排序、Scope 过滤后链式调用。
    *   `TalentDefinition.cs`: ScriptableObject 元数据（图标/显示名/描述），仅供 UI 展示，运行时逻辑不依赖。
*   **具体实现 (`Impl/`)**:
    *   `MidasTouchTalent.cs`: 点金手——摸牌时将风牌/箭牌转化为发财。

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
   | WallBuilding | `void OnWallBuilding(TalentContext ctx)` | 通过 `ctx.WallTiles` 直接修改牌山 |
   | OnDraw | `TileData OnDraw(TalentContext ctx, TileData tile)` | 返回修改后的牌，管道链式 |
   | OnDiscard | `TileData OnDiscard(TalentContext ctx, TileData tile)` | 返回修改后的牌，管道链式 |
   | ActionValidation | `bool OnActionValidation(TalentContext ctx, ClientActionType, TileData)` | 返回 false 可禁止动作 |
   | Scoring | `void OnScoring(TalentContext ctx, FanContext fanCtx)` | 修改 FanContext 影响算番 |

5. **设计约束**：
   - 天赋逻辑必须纯 C#，不依赖 MonoBehaviour 或 Unity 生命周期
   - `Scope.Self` 天赋在钩子中应检查 `ctx.IsOwnersTurn` 或 `ctx.CurrentPlayerId == ctx.TalentOwnerId`
   - `Scope.Global` 天赋影响所有玩家，应配高异化值作为代价
   - 修改 `TileData` 后应设置 `IsModified = true` 和 `SpecialEffectID = Id`

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
*   **操作面板 (`ActionPanel`)**: 按钮布局与可选吃牌组合逻辑。
*   **结算面板 (`ResultPanel`)**: 汇总算番详情，驱动流局或胡牌界面。
*   **牌库编辑器 (`DeckEditor`)**: 34 种牌选择界面与异化值计算提示。含天赋槽 UI（6 槽位选择、弹窗选择器、详情区域）。
*   **天赋模板**: `TalentSlotTemplate.uxml/uss`（槽位显示）、`TalentItemTemplate.uxml`（列表项）。
*   **复用模板**: `TileItemTemplate.uxml` 等小组件。
