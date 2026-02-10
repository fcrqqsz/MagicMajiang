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

## Architecture & Conventions

### 1. Key Patterns
*   **MVC:** Strict separation of Data (Core), Visuals (Controllers), and UI (UI Toolkit).
*   **FSM (Finite State Machine):** Used in `TurnManager` to handle the flow: Draw -> Action -> Response -> Turn End.
*   **Strategy & Reflection:** Fan (scoring) rules are implemented as classes and automatically registered via `[FanRuleAttribute]`.
    *   **Multiple Triggers:** Rules use `GetMatchCount(ctx)` to support repeating Fan types (e.g., multiple Dragon Pungs in custom decks).
*   **Singletons:** Logic-heavy managers (`FanRuleRegistry`) use pure C# singleton patterns with lazy initialization to avoid Unity scene dependencies and `NullReferenceException`.

### 2. Core Directories
*   `Assets/Scripts/Core`: Pure C# logic (TileData, Meld, MahjongLogic, Fan calculation). **FanRuleRegistry is a pure C# class.**
*   `Assets/Scripts/Controllers`: `MonoBehaviour` scripts handling 3D objects (HandController, RiverController).
*   `Assets/Scripts/Systems`: Game loop managers (GameManager, TurnManager, TalentManager).
*   `Assets/Scripts/UI`: UI Toolkit specific scripts and assets (.uxml, .uss).

### 3. Critical Implementation Details
*   **Turn Flow:** `TurnManager` controls the loop.
    *   *Chi/Pon* sets `_skipNextDraw = true`.
    *   *Kan* (Kong) sets `_skipNextDraw = false` (triggers draw from dead wall).
*   **Hand Visualization:** `HandController` manages the "13+1" layout.
    *   The newest drawn tile (`_lastDrawnTile`) has a visual gap.
    *   This gap is removed after actions like Chi/Pon.
*   **UI Input:** `ActionPanelController` handles player choices.
    *   **Note:** Use strict event cleanup to prevent duplicate button clicks (see `ClearTempButtons`).
*   **Debug Tools:** `GameManager` includes a `useDebugHand` toggle and `debugHand` list for testing specific scenarios.

## Development & Usage
*   **Primary Documentation:** Refer to `summary.md` for the most up-to-date snapshot, backlog, and troubleshooting logs.
*   **Building:** Standard Unity Build settings.
*   **Fan Rules:** When adding new rules in `FanRules_Common.cs`, implement `GetMatchCount`. For custom decks, use `% 3` logic to detect sets.

## Important Constraints
*   **UI:** Always use UI Toolkit. Do not introduce Canvas/RectTransform based UI unless absolutely necessary for world-space UI that UI Toolkit cannot handle.
*   **Fonts:** Ensure `MSYH.TTC` or compatible SDF assets are used for Chinese character support.
