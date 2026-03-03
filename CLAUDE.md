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
- `GameServer`: 仅负责洗牌、发牌、状态流转、并发仲裁
- `LocalPlayerClient` / `SimpleAIClient`: 本地计算吃碰杠胡权限和算番，将意图发往服务端
- 通过 `IPlayerClient` 接口统一本地玩家与 AI

**算番系统**: Strategy + Reflection 模式
- 规则通过 `[FanRuleAttribute]` 标记，由 `FanRuleRegistry` 自动注册
- 支持 `GetMatchCount` 多重触发，兼容自定义牌库番数累加
- 新增规则需考虑优先级与 `ExcludedRuleIds` 排斥逻辑

**单例模式**: 逻辑层使用纯 C# 懒加载单例，不依赖 MonoBehaviour/场景状态

## Directory Structure
```
Assets/Scripts/
├── Core/                    # 核心逻辑与表现层
│   ├── TileData.cs          # 牌数据结构 (Suit, Value, ID)
│   ├── MahjongEnums.cs      # Suit, MeldType 等枚举
│   ├── Meld.cs              # 副露数据结构
│   ├── MahjongLogic.cs      # 核心算法 (回溯胡牌判定、多路径拆解、听牌分析)
│   ├── ActionValidator.cs   # 吃碰杠胡动作校验
│   ├── DeckConfig.cs        # 牌库配置与异化值计算
│   ├── HandController.cs    # 3D 手牌管理、布局、动画、交互
│   ├── RiverController.cs   # 牌河 3D 排布
│   ├── TileVisual.cs        # 单张牌视觉容器
│   ├── Network/
│   │   ├── Protocol.cs      # 通信数据结构 (ClientAction)
│   │   └── GameServer.cs    # 异步核心循环
│   ├── Agents/
│   │   ├── IPlayerClient.cs # 客户端代理接口
│   │   ├── SimpleAIClient.cs
│   │   └── LocalPlayerClient.cs
│   └── Fan/                 # 算番系统
│       ├── FanContext.cs    # 拆解方案、听牌类型、场况上下文
│       ├── FanRule.cs       # 规则基类
│       └── Rules/
│           ├── FanCalculator.cs
│           ├── FanRuleRegistry.cs
│           └── MCR/             # 国标麻将番种 (按番数分文件)
│               ├── MCR_1to6.cs  # 1-6番 (33种)
│               ├── MCR_8to24.cs # 8-24番 (28种)
│               └── MCR_32Plus.cs # 32+番 (18种)
├── Systems/                 # 全局管理
│   ├── GameManager.cs       # 游戏初始化入口
│   ├── DeckManager.cs       # 牌山构建、洗牌、发牌
│   └── TalentManager.cs    # 天赋系统分发
├── Talent/                  # Roguelike 天赋
│   ├── TalentBase.cs
│   └── Impl/               # 具体天赋实现
└── Editor/
    └── TileConfigEditor.cs  # 编辑器扩展

Assets/UI/                   # UI Toolkit 面板
├── ActionPanel/             # 操作按钮面板
├── ResultPanel/             # 结算面板 (番种详情)
├── DeckEditor/              # 牌库编辑器
└── Templates/               # 复用模板 (TileItemTemplate 等)
```

## Development Conventions

### Code Style
- C# 命名遵循 .NET 标准: PascalCase (类/方法/属性), camelCase (局部变量/参数)
- 逻辑层纯 C# 类优先，表现层才用 MonoBehaviour
- UI 面板三件套: `.uxml` (布局) + `.uss` (样式) + `.cs` (控制器)

### Key Patterns
- DoTween 动画绑定动态 GameObject 时，**必须**链式调用 `.SetLink(gameObject)` 防止销毁报错
- `FanRuleRegistry` 是纯 C# 单例，属性懒加载，避免空引用
- 胡牌计算使用多路径拆解算法，遍历所有方案取番数最大值
- UI Toolkit 字体引用 `-unity-font-definition` (SDF 资产)，不用原始字体文件

### Debugging
- `GameManager` 中 `useDebugHand` 可在 Inspector 配置测试牌型
- 算番开发: 在 `FanRules_Common.cs` 新增规则需实现 `GetMatchCount`，考虑优先级与排斥

## Current Priorities
参阅 `plan.md` 获取完整任务列表。当前重点:
- 发牌与摸牌 DoTween 动画
- 结算手牌缩略图复盘
- 异化牌视觉反馈
- 对象池性能优化

## Reference Docs
- `summary.md`: 项目进度快照与排故日志
- `plan.md`: 开发任务与优化路线图
- `struct.md`: 详细架构索引
