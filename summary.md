麻将 Roguelike 项目进度快照 (Project Snapshot)
日期: 2026-02-11 版本: Alpha - Core Loop & Rules Implemented 引擎: Unity (建议 2022.3+)

1. 项目核心目标
开发一款基于 Unity 3D 的单机麻将游戏。

核心规则：以 国标麻将 (MCR) 为基础，支持81番种计算。

特色玩法：

Roguelike 元素：天赋系统 (Talents) 影响牌局规则。

牌组构建 (Deck Building)：玩家自定义带入的 34 张牌，根据偏离标准牌库的程度计算“异化值”。

视觉风格：3D 桌面 + UI Toolkit 现代化界面。

2. 技术栈清单
核心引擎: Unity 3D (Tuanjie 1.6.8)

UI 系统: UI Toolkit (UI Elements) - 使用 UXML (结构) 和 USS (样式)，完全替代 UGUI。

动画库: DOTween (用于手牌移动、理牌、打牌动画)。

文本渲染: TextMeshPro (生成 SDF 字体资源，支持中文字符集)。

设计模式:

MVC: UI 与数据分离。

Observer (观察者): 天赋系统事件监听。

State Machine (FSM): 回合流程管理。

Strategy (策略): 番种计算规则。

Reflection (反射): 自动注册番种规则。

Pure C# Singleton: 逻辑层单例（如 FanRuleRegistry）不再继承 MonoBehaviour，采用懒加载机制，避免场景依赖。

3. 已达成的关键共识与决策
UI 架构：放弃 UGUI，全面采用 UI Toolkit，利用 Flexbox 布局解决多分辨率适配，使用 USS 管理样式。

数据驱动：天赋 (Talent) 和番种 (FanRule) 均设计为易扩展的类结构。番种通过 [FanRuleAttribute] 自动注册。

番种触发机制：支持多重触发 (`GetMatchCount`)。例如：在自定义牌库下，多组同类箭刻可累加计算番数。

自定义牌库支持：算番逻辑（如碰碰和）改用取模运算 (`% 3`)，以兼容单种牌数量超过 4 张的 Roguelike 情况。

调试增强：引入 `useDebugHand` 模式，支持在 Inspector 中预设起始手牌，方便规则测试。

手牌交互：采用 "13+1" 布局，新摸的牌在最右侧有独立间距，仅在打牌后触发重排序。

4. 代码架构概览
A. Core (核心逻辑)
TileData.cs: 牌的基础数据结构 (Suit, Value, ID)。

DeckConfig.cs: 玩家牌库配置，含异化值 (AlienationScore) 计算算法。

Meld.cs: 副露数据结构 (Chi, Pon, Kan_Exposed, Kan_Concealed, Kan_Added)。

MahjongLogic.cs: 静态工具类。含回溯算法判断胡牌 (CheckStandardWin)，转换手牌为频率数组。

ActionValidator.cs: 静态工具类。检测吃、碰、杠、胡权限 (CheckActions & CheckSelfActions)。

TurnContext.cs: 回合上下文，用于天赋系统修改规则（如本回合摸2张）。

B. Core/Fan (番种计算)
FanRule.cs: 番种基类。改为 `GetMatchCount` 计数模式。

FanContext.cs: 算番上下文。

FanRuleAttribute.cs: 用于标记番种元数据的特性。

FanRuleRegistry.cs: **纯 C# 单例**。利用反射自动加载所有带有 Attribute 的番种，支持懒加载。

FanCalculator.cs: 计算总番数，支持 `FanValue * MatchCount` 累加逻辑。

C. Controllers (表现层)
HandController.cs: 管理手牌生成、动画、交互。新增 `AddTileDirectly` 用于测试。

RiverController.cs: 管理打出的牌 (牌河)。

D. Systems (系统层)
GameManager.cs: 游戏入口。新增调试手牌配置字段。

DeckManager.cs: 负责洗牌、发牌、构建牌山。

TurnManager.cs: 核心状态机。管理流程。

TalentManager.cs: 管理天赋触发。

E. UI (界面)
DeckEditorToolkit.cs: 牌库构建器 UI。

ActionPanelController.cs: 吃碰杠胡操作面板。

ResultPanelController.cs: 结算面板。

5. 关键代码片段 (Core Logic)
1. FanRule - 多重触发支持
C#
public abstract class FanRule {
    public abstract int GetMatchCount(FanContext ctx); // 支持返回触发次数 (如 2 组箭刻)
}
2. AllPungs - 取模判定逻辑 (兼容自定义牌库)
C#
// 判定是否全由刻子+1个对子组成
int rem = count % 3;
if (rem == 2) totalPairs++;
else if (rem != 0) return 0; // 出现余 1 情况说明不是纯刻子结构
6. 待办事项 (Backlog)
High Priority (高优先级)
结算 UI 对接: 将算出的番种列表展示在 ResultPanel 界面。

AI 逻辑: 目前 AI 行为随机，需要基础的打牌与响应决策逻辑。

番种填充: 补全剩余的 80+ 种番种规则。

7. 避坑指南 (Troubleshooting Log)
FanRuleRegistry 空引用 (NullReferenceException):

原因: 原为 MonoBehaviour，未挂载到场景导致 Instance 为空。

解法: 转为纯 C# 单例，并在 Instance 访问器中通过属性进行懒加载初始化。

箭刻番数漏算:

原因: bool 判定无法表达“两组箭刻”。

解法: 架构重构为 `GetMatchCount`，由计算器负责 `次数 * 分值` 的汇总。

吃碰后手牌间隙错误:

解法: 引入 `_lastDrawnTile` 引用，仅对新摸的那张牌应用 `drawGap`。
