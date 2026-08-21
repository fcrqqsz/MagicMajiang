# SuperMajiang (超级国标麻将 Roguelike)

**SuperMajiang** 是一款基于 Unity 引擎开发的单机 Roguelike 国标麻将游戏（未来设计支持网络联机）。游戏将传统国标麻将与现代 Roguelike 机制相结合，玩家可以自定义牌库并通过天赋系统影响对局规则，体验独特的策略与变化。

---

## 🎮 核心玩法与特色

1. **国标麻将规则 (Guobiao MCR)**
   - 严格遵循中国官方国标麻将规则，支持全部 81 种番种（番）的精确计算与多路径最优拆解。
   - 采用 Strategy & Reflection 模式自动加载番种规则。

2. **卡组构建机制 (Deck Building)**
   - 玩家可自定义 34 张基础牌的配比构成牌库。
   - 系统将根据牌库配置与标准麻将库的偏差自动计算**“异化值”**，影响可装备的天赋。

3. **Roguelike 天赋系统 (Talent System)**
   - 采用纯 C# 管道架构，可在牌山构建、摸牌、出牌、动作校验及结算五个不同阶段动态修改游戏规则。
   - 提供 6 个天赋槽位（1 大、2 中、3 小），支持向下兼容装配。

4. **对局模式与多局流转 (Multi-Round Match)**
   - 支持多种对局模式（GameMode）：单局（Single）、东风局（EastOnly - 4局）、半庄（HalfGame - 8局）和全庄（FullGame - 16局）。
   - 包含圈风轮转、门风分配、累计计分及积分排行榜展示。

---

## 🛠️ 技术栈

*   **游戏引擎**: Unity 2022.3.61t9 (Tuanjie 1.6.8)
*   **UI 系统**: UI Toolkit (UXML/USS) — *完全禁用 Canvas/UGUI*
*   **动画系统**: DOTween (Pro)
*   **文本渲染**: TextMeshPro (SDF)
*   **编程语言**: C# (支持 async/await 异步多线程机制)

---

## 📂 项目目录结构说明

项目主要逻辑均位于 [Assets/Scripts](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/) 及 [Assets/UI](file:///d:/UnityPrj/SuperMajiang/Assets/UI/) 目录下。以下是详细结构：

### 1. 逻辑与控制器层: [Assets/Scripts](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/)
*   **[Core/](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/) (核心对局逻辑)**
    *   [TileData.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/TileData.cs): 牌的基础数据结构，包含花色 (Suit)、数值 (Value) 以及唯一 ID。
    *   [MahjongEnums.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/MahjongEnums.cs): 核心枚举定义，如 `Suit`、`MeldType`、`WindDirection`、`GameMode` 等。
    *   [Meld.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Meld.cs): 副露数据结构。
    *   [MahjongLogic.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/MahjongLogic.cs): 回溯胡牌判定、手牌多路径拆解及听牌分析核心算法。
    *   [ActionValidator.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/ActionValidator.cs): 玩家动作吃、碰、杠、胡的合法性校验。
    *   [DeckConfig.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/DeckConfig.cs): 玩家自定义牌库配置及异化值计算。
    *   **[Network/](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Network/)**: 
        *   [GameServer.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Network/GameServer.cs): 异步核心对局服务器，驱动回合流转与并发仲裁。
        *   [ServerGameState.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Network/ServerGameState.cs): 服务端状态快照，用于超时出牌兜底及未来断线重连。
        *   [GameSession.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Network/GameSession.cs): 追踪对战圈风、门风及多局分数结算。
    *   **[Agents/](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Agents/)**:
        *   [IPlayerClient.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Agents/IPlayerClient.cs): 客户端接口，统一玩家与 AI 代理。
        *   [LocalPlayerClient.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Agents/LocalPlayerClient.cs): 本地玩家客户端代理，桥接 UI 与输入。
        *   [SimpleAIClient.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Agents/SimpleAIClient.cs): AI 客户端代理，基于规则决策出牌与碰杠。
    *   **[Fan/](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Fan/) (国标算番系统)**:
        *   [FanContext.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Fan/FanContext.cs): 听牌状态与风位场况上下文。
        *   [FanRule.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Fan/FanRule.cs): 算番规则基类。
        *   [Rules/MCR/](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Fan/Rules/MCR/): 按番数归档的具体国标算番规则（MCR_1to6.cs, MCR_8to24.cs, MCR_32Plus.cs）。
    *   **表现控制器**:
        *   [HandController.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/HandController.cs): 3D 手牌生成、理牌、布局及 DOTween 动画。
        *   [RiverController.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/RiverController.cs): 3D 牌河排布及最新出牌高亮高亮指示器。
        *   [TileVisual.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/TileVisual.cs): 单张牌的 Mono 表现层，含材质自发光呼吸灯。
*   **[Systems/](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Systems/) (全局系统管理器)**
    *   [GameManager.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Systems/GameManager.cs): 场景内游戏初始化入口，整合 Server 与 Clients。
    *   [DeckManager.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Systems/DeckManager.cs): 负责牌山构建、洗牌、发牌，支持天赋对牌山的改写。
*   **[Talent/](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Talent/) (Roguelike 天赋机制)**
    *   [TalentRule.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Talent/TalentRule.cs): 天赋抽象基类，定义了 5 个生效阶段的管道钩子。
    *   [TalentManager.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Talent/TalentManager.cs): 管道执行器，负责按优先级执行当前生效的天赋链。
    *   [Impl/](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Talent/Impl/): 包含具体天赋实现（如 `MidasTouchTalent`、`PeekTalent`、`DragonAscentTalent` 等）。

### 2. UI 表现层: [Assets/UI](file:///d:/UnityPrj/SuperMajiang/Assets/UI/)
*   **大厅 UI (MainLobby)**: 基于 UI Toolkit 开发的匹配、卡组构建（Deck Workshop）、系统设置页面。
*   **房间列表浏览面板 (RoomListPanel)**: 独立 UIDocument 弹窗，支持多局模式与可用性过滤、构筑异化预算实时校验、房号直连加入与一键快速开房。
*   **牌库编辑器 (DeckEditor)**: 可编辑 34 张牌库并配置 6 个天赋槽位。支持天赋详情悬浮框及品阶高亮弹窗。
*   **通用悬浮牌面板 (FloatingTilePanel)**: 屏幕顶端淡入淡出面板，支持展示模式（如窥探牌山）及选择模式。
*   **听牌提示面板 (WaitHintPanel)**: 在玩家选中手牌时实时反馈打出该牌后的听牌及最大番数。

---

## 💡 架构设计亮点

### 1. 胖客户端，瘦服务端 (Fat Client, Thin Server)
服务端 [GameServer.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Network/GameServer.cs) 运行在纯异步的回合流转循环中，只负责维护牌山、出牌验证、仲裁客户端的并发动作（如吃碰杠胡冲突），并记录 [ServerGameState.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Network/ServerGameState.cs) 状态快照。算番和复杂的打牌推荐逻辑在客户端执行，保证了逻辑的模块化与未来的网络联机扩展性。

### 2. 超时取消与快照兜底机制
游戏中的玩家/AI 回合采用可取消的异步 `Task` 实现。
- 当玩家超时未出牌时，服务端通过 `CancellationToken` 强行取消客户端当前回合的 async 动作。
- 服务端随即调用 [ServerGameState.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Network/ServerGameState.cs) 的快照获取玩家真实的物理手牌，触发自动出牌，并由客户端执行 `ForceRemoveTile` 进行理牌状态同步，防止虚构兜底牌导致的客户端数据腐坏。

### 3. Strategy + Reflection 算番与天赋注册
所有番种规则与天赋均解耦为纯 C# 类：
- 算番规则使用 `[FanRuleAttribute]` 标记，天赋使用 `[TalentRuleAttribute]` 标记。
- 在程序启动时，通过 [FanRuleRegistry.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Core/Fan/Rules/FanRuleRegistry.cs) 和 [TalentRegistry.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Talent/TalentRegistry.cs) 以反射方式自动加载并注册，新增番种或天赋无需手动修改注册表，极大提高了开发效率。

---

## ⚡ 天赋开发规范

要开发一个全新的 Roguelike 天赋，请按照以下步骤进行：

1. 在 [Assets/Scripts/Talent/Impl/](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Talent/Impl/) 目录下创建新天赋的 C# 文件，继承 [TalentRule](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Talent/TalentRule.cs)，并标记 `[TalentRuleAttribute]`。
2. 实现所需的阶段钩子函数。天赋框架支持的 5 大生命周期阶段：
   - **WallBuilding**: 在发牌前，通过修改 `ctx.WallTiles` 重新排列或替换牌山里的牌。
   - **OnDraw**: 摸牌钩子，支持修改刚摸到牌的属性。
   - **OnDiscard**: 出牌钩子，修改玩家刚打出的牌。
   - **ActionValidation**: 吃碰杠胡动作校验，返回 `false` 可在规则层直接禁止某种动作。
   - **Scoring**: 结算阶段，可修改 `FanContext` 直接增加基础番数或激活特定隐藏番种。

**示例代码**：
```csharp
[TalentRule(
    "midas_touch",         // 唯一标识 ID (snake_case)
    "点金手",              // 界面显示名称
    "摸牌时将风牌或箭牌转化为发财", // 效果描述
    TalentTier.Medium,     // 天赋品阶 (Small / Medium / Large)
    15,                    // 异化值消耗 (Alienation Cost)
    TalentPhase.OnDraw     // 触发阶段 (可传入多个 Phase)
)]
public class MidasTouchTalent : TalentRule
{
    public override TalentScope Scope => TalentScope.Self; // 仅对自己生效
    public override int Priority => 0; // 执行优先级

    public override TileData OnDraw(TalentContext ctx, TileData tile)
    {
        // 验证是否为拥有者的回合
        if (!ctx.IsOwnersTurn) return tile;

        // 如果是风牌或三元牌，转换为发财 (Fa)
        if (tile.Suit == Suit.Wind || (tile.Suit == Suit.Dragon && tile.Value != 3))
        {
            tile.Suit = Suit.Dragon;
            tile.Value = 3; // 发财在 Dragon 花色中的对应 Value
            tile.IsModified = true;
            tile.SpecialEffectID = Id;
        }
        return tile;
    }
}
```

---

## 💡 开发与避坑指南 (Troubleshooting)

在继续开发本项目时，请格外注意以下前人总结的避坑经验：

*   **DoTween 动作销毁报错**: 当 AI 连续快速执行吃碰动作时，刚生成并处于移动动画中的 3D 牌可能瞬间被销毁，导致 DOTween 报空引用。针对所有动态生成/销毁的 3D GameObject 执行 DOTween 动画时，**必须**链式调用 `.SetLink(gameObject)`，使动画生命周期与 GameObject 绑定。
*   **DontDestroyOnLoad 报错**: `DontDestroyOnLoad only works for root GameObjects`。在对单例/持久化组件调用该方法前，确保先执行 `transform.SetParent(null);` 使其脱离父级，变为根节点。
*   **多场景 Camera 与 AudioListener 冲突**: Additive 模式叠加加载对局场景时，可能会报 AudioListener 数量错误且画面出现底色蒙版。使用 [CameraManager.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Systems/CameraManager.cs) 监听场景加载，进入游戏场景时自动禁用 Persistent 场景的 UI 摄像机，并移除游戏场景主相机上多余的 AudioListener。
*   **理牌后位置不更新**: 在对客户端手牌进行 `SortHand()` 理牌排序后，必须显式调用 `UpdateHandPositions()` 才能更新 3D 牌物体的空间坐标。
*   **多局牌河残留**: 开启多局游戏时，每一局初始化必须清理上局留在桌面的牌。需在 `MahjongHandViewBase.ClearHand()` 中统一调用 `myRiver.Clear()`。
*   **UI Toolkit 字体引用**: 为防止字体在部分分辨率下模糊或不显示，USS 样式中对字体的配置必须引用 **SDF 资产** (`-unity-font-definition`)，严禁直接引用原始的 `.ttf`/`.ttc` 字体文件。

---

## 📅 项目路线图 (Roadmap)

### 当前开发中任务 (Backlog)
*   **[ ] 异化牌视觉反馈 (高)**: 根据 `TileData` 的异化状态（被天赋修改），改变 3D 牌背颜色或增加粒子发光效果。
*   **[ ] 结算手牌缩略图 (中)**: 在结算界面 `ResultPanel` 中展示胡牌瞬间的 2D 缩略手牌及副露排布，便于玩家复盘。
*   **[ ] 天赋槽图标显示 (低)**: 为 `TalentDefinition` 配置 Sprite 图标，显示在 DeckEditor 天赋槽及主页卡组信息中。
*   **[ ] 发牌与摸牌动画 (低)**: 使用 DOTween 制作牌从牌山飞入手牌位置的平滑动画。
*   **[ ] 极限牌库压力测试 (低)**: 验证回溯算法在极端重复牌库下（例如单花色超过 8 张）的算番及听牌判定稳定性。

### 长期优化目标
*   **对象池 (Object Pooling)**: 引入 `TilePool` 统一管理 3D 牌视觉体的生成与回收，提升频繁吃碰杠时的性能表现。
*   **算番排斥逻辑自动化**: 在 `FanRule` 中引入 `RuleGroup` 规则组，代替目前手写的排斥列表 `ExcludedRuleIds`。

---

## 🚀 调试与开发环境运行

1. **场景入口**: 
   - 必须先加载并运行 **`00_Persistent`** 场景，作为整个游戏的初始化入口。
   - 调试时，可以使用编辑器工具 `SceneSetupMenu.cs` 一键构建并加载叠加的多场景结构。
2. **调试手牌 (Debug Hand)**:
   - 在 [GameManager.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Systems/GameManager.cs) 的 Inspector 中开启 `useDebugHand`，可以直接在面板上配置起手测试手牌，便于快速验证算番和胡牌拆解规则。
3. **切换局数模式**:
   - 在 [GameManager.cs](file:///d:/UnityPrj/SuperMajiang/Assets/Scripts/Systems/GameManager.cs) 的 Inspector 中调整 `gameMode`，以在 Single(单局) / EastOnly(东风局) 等不同游戏模式间自由切换。

---

> [!NOTE]
> 详细的项目修改历史和更多架构演进的上下文，请参阅根目录下的 [summary.md](file:///d:/UnityPrj/SuperMajiang/summary.md)、[plan.md](file:///d:/UnityPrj/SuperMajiang/plan.md)、[milestone.md](file:///d:/UnityPrj/SuperMajiang/milestone.md) 和 [struct.md](file:///d:/UnityPrj/SuperMajiang/struct.md)。
