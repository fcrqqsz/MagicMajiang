# Scene Three Table, HUD, and Backdrop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild scene three around the physical tile dimensions so four rivers, all hands, contextual UI, and a modern Chinese-style room backdrop remain legible without overlapping.

**Architecture:** A pure C# `GameTableLayoutPolicy` owns deterministic hand, river, seat, and screen-safe-zone math. Unity-facing controllers consume those values once during scene initialization; gameplay, networking, and message ordering remain unchanged. The generated room backdrop is imported as a project asset, while a lit 3D table surface provides contact and shadow grounding.

**Tech Stack:** Unity 2022.3/Tuanjie 1.6.8, C#, UI Toolkit, DOTween Pro, built-in render pipeline, pure .NET regression tests, built-in ImageGen.

**Spec:** [`Approved Design`](#approved-design) in this document.

## Global Constraints

- Preserve the user's existing uncommitted changes in `Assets/Scenes/03_Game.unity` and `Assets/UI/GameHUD/GameHUDStyles.uss` as the baseline.
- Do not modify unrelated font, package, ignore, or local instruction files.
- UI remains UI Toolkit; do not add Canvas/UGUI UI.
- Use `Assets/Font/MSYH_UITK.asset` for UI Toolkit text.
- Never write or guess Unity `.meta` files or GUIDs. Pause for a human Unity Refresh before new assets are referenced by serialized scene data.
- Do not launch Unity/Tuanjie batch mode or modify generated `Assembly-CSharp.csproj`.
- Do not commit unless the user explicitly requests a commit.

---

## Approved Design

- Tile physical basis: the approved rounded replacement is `0.78 x 1.16 x 0.48` world units, derived from the original `0.8 x 1.2 x 0.5` footprint.
- Balanced camera starting point: position near `(0, 15, -17)`, target `(0, 0, 0)`, vertical FOV near `50`.
- Hands are centered with approximately `0.84` step and `0.32` extra drawn-tile separation.
- Rivers use six centered columns, approximately `0.93` column step and `1.24-1.28` row step, growing away from the table center for up to four rows.
- Central HUD is a non-rotated high-contrast command panel sized `164 x 104` logical pixels.
- A centered wait strip above the local hand grows from `196` to `790` logical pixels with the wait count, remains `96px` high, and must show all thirteen Thirteen Orphans waits without scrolling or compressing tile faces.
- Own talent controls occupy the lower-left; action controls occupy the right-side band above the wait strip.
- The room backdrop is modern Chinese style: deep teal, dark wood, brass, warm light, shallow depth of field, low-detail center, no people, tiles, text, logo, or central highlight.

**Unity-calibrated result:** The final Game view uses camera target height `-1.5`, a `164 x 104` logical-pixel non-rotated HUD positioned at `45.4%` viewport height, and a dynamic `196-790 x 96` wait strip anchored `12%` above the bottom. Wait tiles render at `44 x 62`; the taller strip may cover melds but remains clear of the fourth river row, hand, and action band. The rounded tile mesh has a measured inclined lower bound of `-0.627`, so hand roots sit at `y=0.397` over the table surface at `y=-0.26`. Flat meld bodies use local `y=-0.417`, and their anchors offset `1.3` units toward the table center so four kongs remain fully framed.

---

### Task 1: Pure layout policy and regression tests

**Files:**
- Create: `Assets/Scripts/Core/GameTableLayoutPolicy.cs`
- Create: `Tests/NetworkRegression/GameTableLayoutPolicyTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

**Interfaces:**
- Produce `TableLayoutPoint`, `TableSeatLayout`, and static `GameTableLayoutPolicy` methods:
  - `GetCenteredHandX(int tileCount, int index, float tileStep, bool hasDrawGap, float drawGap)`
  - `GetRiverLocalPosition(int index, int tilesPerRow, float columnStep, float rowStep)`
  - `RotateForSeat(TableLayoutPoint point, int seatIndex)`
  - `CanFitWaitItems(float panelWidth, float titleWidth, float horizontalPadding, int itemCount, float itemWidth)`
  - `GetWaitHintWidth(int itemCount, float itemWidth, float fixedWidth, float minimumWidth, float maximumWidth)`

- [x] Add tests with hand-derived literals for centered 13/14-tile hands, drawn-tile separation, six-column river symmetry, fourth-row direction, four-seat rotation, and thirteen wait items fitting in 820 logical pixels.
- [x] Run `dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore` and confirm the new tests fail because the policy does not exist.
- [x] Implement the minimal pure C# policy without UnityEngine dependencies.
- [x] Re-run the regression project and confirm all tests pass.

### Task 2: Apply policy to hand and river views

**Files:**
- Modify: `Assets/Scripts/Core/HandController.cs`
- Modify: `Assets/Scripts/Core/OpponentViewController.cs`
- Modify: `Assets/Scripts/Core/RiverController.cs`
- Modify: `Assets/Scripts/Core/MahjongHandViewBase.cs`

**Interfaces:**
- Consume `GameTableLayoutPolicy.GetCenteredHandX` for animated and immediate hand rebuild paths.
- Consume `GameTableLayoutPolicy.GetRiverLocalPosition` from both animated and immediate discard paths.
- Preserve all public gameplay methods and server snapshot behavior.

- [x] Extend policy tests for zero/one tile and partial final river rows, then watch the focused regression fail for the new expectation.
- [x] Center concealed hands including the drawn-tile gap so drawing and discarding do not drift the whole seat.
- [x] Center all river columns around the river Transform and keep rows on local negative Z so seat rotation makes them grow outward.
- [x] Set scene-facing defaults to hand step `0.84`, draw gap `0.32`, river column step `0.93`, row step `1.26`, and six tiles per row.
- [x] Re-run NetworkRegression.

### Task 3: Scene layout controller and scene-three anchors

**Files:**
- Create: `Assets/Scripts/Core/GameTableLayoutController.cs`
- Modify after Unity Refresh: `Assets/Scenes/03_Game.unity`

**Interfaces:**
- Serialized references: main camera, four hand roots, four river roots, four meld anchors, and a table-center Transform.
- `ApplyLayout()` runs once from `Awake` and applies policy-backed seat anchors, rotations, camera position, target, and FOV.

- [x] Add pure policy tests for the four world-space seat/river anchor results before creating the controller.
- [x] Implement the Unity adapter without gameplay or networking dependencies.
- [x] Pause for Unity Refresh so the script receives an authoritative `.meta` GUID.
- [x] Attach the controller in `03_Game`, bind existing objects, center the four river roots independently of hand roots, set the local hand near `z=-10`, and start the camera near `(0,15,-17)` looking at the table center with FOV `50`.
- [x] Confirm DOTween calls on dynamic tile objects remain linked where already required.

### Task 4: HUD, wait strip, talent, and action safe zones

**Files:**
- Modify: `Assets/UI/GameHUD/GameHUDStyles.uss`
- Modify: `Assets/UI/WaitHintPanel.uxml`
- Modify: `Assets/UI/WaitHintPanelStyles.uss`
- Modify: `Assets/UI/ActionPanel.uxml`
- Modify: `Assets/UI/ActionPanelStyles.uss`
- Modify only if necessary for non-scrolling layout: `Assets/UI/WaitHintController.cs`

**Interfaces:**
- Retain existing element names used by controllers.
- Preserve separate UIDocuments and whole-root `display:none` input ownership.

- [x] Add a regression assertion through `CanFitWaitItems` that thirteen `52px` items plus title and padding fit within `820px`.
- [x] Shrink the central diamond to its measured safe size, timer and typography proportionally while preserving four winds and scores.
- [x] Move own talent controls to the lower-left and make drawers grow upward; cap the top-right feed to four compact rows.
- [x] Rebuild the wait panel as a centered dynamic-width `196-790 x 96` strip above the hand, with `44 x 62` tile faces and all thirteen items visible without scrolling.
- [x] Lift the action band to `194px` bottom padding so the taller wait strip may overlap melds without blocking action buttons.
- [x] Move and compact the action panel into the right-side band above the wait strip; allow a compact single row or controlled two-row wrap without crossing the wait strip.
- [x] Parse all UXML as XML and scan USS for forbidden UI Toolkit syntax and unsupported emoji.

### Task 5: Generate and import environment art

**Files:**
- Create via built-in ImageGen: `Assets/Art/Environment/GameRoomBackdrop.png`
- Create in Unity after Refresh: backdrop and table materials referenced by `Assets/Scenes/03_Game.unity`

**Interfaces:**
- Backdrop Quad uses an Unlit material and remains behind all 3D play objects.
- Table surface uses a Standard/Lit material and receives tile shadows.

- [x] Generate one production backdrop using the approved modern Chinese room prompt and inspect it for subject, composition, center detail, forbidden objects, text, and logo artifacts.
- [x] Inspect the selected output and confirm no targeted regeneration is required.
- [x] Copy the selected output into `Assets/Art/Environment/GameRoomBackdrop.png` without creating a `.meta` file.
- [x] Pause for Unity Refresh, then set import properties to sRGB, Clamp, mipmaps off, and max size 2048.
- [x] In Unity create a deep-teal table surface with dark-wood/brass rim, a camera-facing RoomBackdrop Quad with Unlit material, and warm key plus subtle cool fill lighting.

### Task 6: Verification and human Unity gate

**Files:**
- Review all files changed by Tasks 1-5.

- [x] Run `dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore` and confirm zero failures.
- [x] Run XML parsing and USS constraint scans for the modified UI files.
- [x] Inspect `git diff` and preserve unrelated dirty files without reverting or overwriting them.
- [ ] Human Unity verification at `1920 x 1080` and `960 x 600`: empty/three-row/four-row rivers, thirteen waits, maximum action choices, talent drawers, timer, backdrop, shadows, and hidden/visible/closed input ownership.
- [x] Report automated verification separately from the pending or completed Unity visual gate.

### Task 7: Rounded tile model, vertical clearance, and four-kong stress case

**Files:**
- Create in Unity: `Assets/Art/Tiles/RoundedMahjongTileBody.asset`
- Create in Unity: `Assets/Art/Tiles/MahjongTileIvory.mat`
- Modify in Unity: `Assets/Resources/TilePrefab.prefab`
- Modify: `Assets/Scripts/Core/GameTableLayoutPolicy.cs`
- Modify: `Assets/Scripts/Core/GameTableLayoutController.cs`
- Modify: `Assets/UI/GameHUD/GameHUDStyles.uss`
- Modify: `Tests/NetworkRegression/GameTableLayoutPolicyTests.cs`

- [x] Add policy coverage for surface-aligned roots, child-local flat meld placement, four-kong horizontal span, and the enlarged HUD safe zone.
- [x] Replace the scaled cube with a procedural rounded ivory mesh while preserving the existing root, `TileVisual`, `SpriteRenderer`, and collider interfaces.
- [x] Recalibrate inclined hands to clear the felt by `0.03` world units and keep flat meld bodies flush with it.
- [x] Move all meld anchors `1.3` world units toward the table center so four full kongs remain visible and separated from fourth-row rivers.
- [x] Rebuild the central HUD as a `164 x 104` high-contrast panel with larger wind, score, and timer typography.
- [x] Generate a real four-seat stress snapshot with four kongs and 24 river tiles per seat, measure all renderer bounds, capture the Game view, then remove all 168 temporary tile objects before saving.
