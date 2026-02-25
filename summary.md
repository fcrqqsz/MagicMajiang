麻将 Roguelike 项目进度快照 (Project Snapshot)
日期: 2026-02-11 版本: Alpha - Core Loop & Rules Implemented 引擎: Unity (建议 2022.3+)

1. 项目核心目标
开发一款基于 Unity 3D 的单机麻将游戏。
*   **核心规则**: 以国标麻将 (MCR) 为基础，支持81番种计算。
*   **特色玩法**: Roguelike 天赋系统、自定义 34 张牌库及异化值机制。

2. 已达成的关键共识与决策
*   **UI 架构**: 全面采用 UI Toolkit，弃用 UGUI。
*   **算番系统**: 支持多重触发 (`GetMatchCount`)、优先级排序及自动排斥逻辑。
*   **逻辑单例**: 核心管理器转为纯 C# 实现，支持懒加载，解决场景依赖导致的空引用问题。
*   **多路径拆解**: `MahjongLogic` 会检索所有合法的胡牌组合，并按高分原则自动选取最优解。

3. 系统架构
有关详细的目录索引及职责划分，请参阅 **[struct.md](./struct.md)**。

4. 关键实现细节
### 手牌逻辑
*   **13+1 布局**: 刚摸的牌 (`_lastDrawnTile`) 会保持视觉间距，仅在打牌后重排。
*   **异化值兼容**: 算番逻辑（如碰碰和）使用 `% 3` 取模运算，兼容自定义牌库下同种牌超过 4 张的情况。
*   **多路径拆解**: `MahjongLogic` 遍历所有可能的胡牌组合，按“高分原则”选取最优番数方案。

### 流程控制
*   **中断机制**: `_turnFlowInterrupted` 用于标记吃碰杠导致的非标准顺序流转。
*   **跳过摸牌**: `_skipNextDraw` 用于处理吃碰后直接出牌的逻辑。

5. 核心代码范式
### 算番规则定义示例
```csharp
[FanRule("example", 8)]
public class Fan_Example : FanRule {
    public override int Priority => 10; // 优先级
    public override string[] ExcludedRuleIds => new[] { "lower_fan" }; // 排斥列表
    public override int GetMatchCount(FanContext ctx) => ...; // 返回触发次数
}
```

### Roguelike 天赋定义
```csharp
public class MyTalent : TalentBase {
    public override void OnTileDrawn(TileData tile) {
        // 修改摸到的牌的属性
    }
}
```

6. 避坑指南 (Troubleshooting Log)
*   **FanRuleRegistry 空引用**: 
    *   *解法*: 重构为纯 C# 单例，属性懒加载。
*   **胡牌计算不准确**: 
    *   *解法*: 引入手牌多路径拆解算法，遍历所有方案取番数最大值。
*   **编辑器资源丢失**:
    *   *解法*: 更新 `TileResourceConfig` 统一索引数组，并配套自动化扫描脚本。
*   **UI Toolkit 字体不显示**:
    *   *解法*: 确保 USS 引用 `-unity-font-definition` (SDF 资产) 而非原始字体文件。
