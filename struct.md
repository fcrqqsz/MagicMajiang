# SuperMajiang 架构概览 (Architectural Overview)

本文档作为项目的核心架构索引，详细记录了系统的设计模式、目录结构及职责。

## 1. 核心设计模式
*   **MVC 架构**: 严格分离数据层 (Core)、表现层 (Controllers) 与 UI 层 (UI Toolkit)。
*   **FSM (有限状态机)**: `TurnManager` 驱动游戏主循环，管理 `Draw -> Action -> Response -> Turn End` 的流转。
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
    *   `MahjongEnums.cs`: 存储 Suit, MeldType 等核心枚举。
    *   `Meld.cs`: 副露数据结构及暗面标记。
*   **核心算法**:
    *   `MahjongLogic.cs`: 核心算法库（含回溯胡牌判定、手牌多路径拆解、听牌类型分析）。
    *   `ActionValidator.cs`: 静态校验类，判定玩家当前可进行的动作。
    *   `DeckConfig.cs`: 玩家自定义牌库配置及异化值计算。
*   **表现层控制器 (MonoBehaviour)**:
    *   `HandController.cs`: 管理 3D 手牌生成、布局、DoTween 动画及交互。
    *   `RiverController.cs`: 管理牌河的 3D 排布。
    *   `TileVisual.cs`: 单张牌的视觉容器，处理牌面图片切换。
    *   `TileResourceConfig.cs`: 基于 `ScriptableObject` 的资源索引表。
*   **算番系统 (`Core/Fan/`)**:
    *   `FanContext.cs`: 包含拆解方案、听牌类型、场况信息的上下文。
    *   `FanRule.cs`: 规则基类，定义优先级、排斥逻辑。
    *   `Rules/FanCalculator.cs`: 汇总番数核心类。
    *   `Rules/FanRuleRegistry.cs`: 纯 C# 单例，自动发现并注册规则类。
    *   `Rules/FanRules_Common.cs`: 具体番种规则集。

### B. `Assets/Scripts/Systems` (全局系统管理)
*   `GameManager.cs`: 游戏初始化入口，调试手牌注入。
*   `TurnManager.cs`: 回合流程控制器，管理游戏主循环及流局判定。
*   `DeckManager.cs`: 牌山构建、洗牌、发牌管理。
*   `TalentManager.cs`: 天赋系统的分发中转站。

### C. `Assets/Scripts/Talent` (Roguelike 天赋系统)
*   `TalentBase.cs`: 天赋抽象基类。
*   `Impl/`: 具体天赋实现。

### D. `Assets/Scripts/Editor` (编辑器扩展)
*   `TileConfigEditor.cs`: `TileResourceConfig` 自动化图片匹配工具。

### E. `Assets/UI` (UI 表现架构 - UI Toolkit)
每个主要面板由三部分组成：
*   **`.uxml` (布局/视图)**: 定义 UI 的层级结构。
*   **`.uss` (样式)**: 定义 UI 的视觉外观。
*   **`.cs` (控制器)**: 绑定元素并处理交互逻辑。

#### 核心面板与组件：
*   **操作面板 (`ActionPanel`)**: 按钮布局与可选吃牌组合逻辑。
*   **结算面板 (`ResultPanel`)**: 汇总算番详情，驱动流局或胡牌界面。
*   **牌库编辑器 (`DeckEditor`)**: 34 种牌选择界面与异化值计算提示。
*   **复用模板**: `TileItemTemplate.uxml` 等小组件。
