麻将 Roguelike 项目进度快照 (Project Snapshot)
日期: 2026-03-01 版本: Alpha - Core Loop & Rules Implemented 引擎: Unity (2022.3.61t9)

1. 项目核心目标
开发一款基于 Unity 3D 的单机麻将游戏。
*   **核心规则**: 以国标麻将 (MCR) 为基础，支持81番种计算。
*   **特色玩法**: Roguelike 天赋系统、自定义 34 张牌库及异化值机制。

2. 最近进展 (Recent Progress)
*   **多局对战系统 (2026-03-05)**: 实现 `GameSession` 多局状态管理，支持单局/东风局/半庄/全庄。含圈风轮转、门风分配、国标计分（底分+番数制）。修复了 `FanContext` 风位 bug（圈风刻/门风刻永远匹配西风）。`ResultPanel` 支持多局结算流程。
*   **架构重构**: 完成了 "Fat Client, Thin Server" 架构，拆分了 `GameServer` 与 Client Agents，为本地/AI 统一逻辑打下基础。
*   **AI 基础**: 实现了 `SimpleAIClient`，支持基础的出牌、吃碰杠胡决策。
*   **结算系统**: `ResultPanel` 已完成 UI Toolkit 对接，支持番种列表展示及得分滚动动画。
*   **副露系统**: `HandController` 已实现吃碰杠的基础 3D 模型生成逻辑（正在精修旋转与堆叠细节）。

3. 避坑指南 (Troubleshooting Log)
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
    *   *解法*: 确保 USS 引用 `-unity-font-definition` (SDF 资产) 而非原始字体文件。
*   **DoTween 序列同步**:
    *   *解法*: 在 `HandController` 中处理并发动作时，需使用 `Sequence` 确保动画不冲突。
*   **DoTween 对象销毁报错 (Target or field is missing/null)**:
    *   *症状*: 当 AI 极速连击（如瞬间打牌后被瞬间吃牌），导致刚执行动画的牌被立即 `Destroy` 时，DOTween 尝试访问已销毁的 Transform。
    *   *解法*: 对所有针对动态生成/销毁的 GameObject 进行的 DoTween 动画（如 `DOLocalMove`），必须链式调用 `.SetLink(gameObject)`，强制绑定动画生命周期与 GameObject。
