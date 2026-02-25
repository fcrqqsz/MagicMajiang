# GEMINI.md - SuperMajiang Project Context

## Project Overview
**SuperMajiang** is a single-player Roguelike Mahjong game built with Unity.
*   **Core Rules:** Based on Chinese Official Mahjong (MCR/Guobiao) with 81 scoring elements (Fan).
*   **Key Features:**
    *   **Roguelike Talents:** Ability to modify game rules via a talent system.
    *   **Deck Building:** Players customize a 34-tile deck; "Alienation Score" is calculated based on deviation from standard decks.
    *   **Visuals:** 3D Table + Modern UI Toolkit interface.

## Technical Stack
*   **Engine:** Unity 2022.3.61t9 (Tuanjie 1.6.8)
*   **UI System:** UI Toolkit (UXML/USS). **Legacy UGUI is NOT used.**
*   **Animation:** DOTween (Pro)
*   **Text:** TextMeshPro (SDF)

## Architecture & Structure
有关详细的架构模式、目录索引及实现范式，请优先参阅 **[struct.md](./struct.md)**。

### 核心约束 (Critical Constraints)
*   **UI**: 始终使用 UI Toolkit。除非绝对必要，否则不要引入 Canvas/UGUI。
*   **字体**: 确保使用兼容 SDF 的中文字体资产（如 MSYH.TTC）。
*   **单例**: 逻辑层核心管理器应优先使用纯 C# 单例，避免对场景 GameObject 的硬依赖。

## 开发与计划 (Development & Planning)
*   **进度跟踪**: 参阅 `summary.md` 获取最新快照、关键决策及排故日志。
*   **任务与优化**: 参阅 **[plan.md](./plan.md)** 获取 Backlog 及长期优化路线图。
*   **调试手牌**: 通过 `GameManager` 中的 `useDebugHand` 功能，在 Inspector 中快速配置测试牌型。
*   **算番开发**: 在 `FanRules_Common.cs` 中新增规则时，需实现 `GetMatchCount` 并考虑优先级与排斥逻辑。
