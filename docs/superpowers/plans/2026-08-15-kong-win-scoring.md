# Kong Win Scoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make an immediate replacement draw after any successfully committed kong count as the authoritative 8-fan “杠上开花” situation in client admission, server validation, final scoring, and reconnect recovery.

**Architecture:** `GameServer` owns a two-stage pending/current replacement-draw state and copies the current value into the immutable main-turn decision. The decision is projected through existing per-seat messages and snapshots; clients consume the projected boolean instead of inferring from visual actions. `MahjongLogic` accepts an explicit `isKongWin` scoring input alongside the existing rob-kong input.

**Tech Stack:** C# 10+, .NET 10 pure regression executables, Unity 2022.3/Tuanjie production source (without automated Unity Refresh).

## Global Constraints

- A successful concealed, added, or exposed kong marks exactly the next actual wall draw as a replacement draw.
- The marker expires after that main decision and is re-established only by another successfully committed kong.
- A robbed or rejected kong never establishes the marker.
- Clients consume server-projected state; they do not infer it from animation or message history.
- No Unity/Tuanjie Refresh, `.meta` generation, or generated `Assembly-CSharp.csproj` modification is allowed.
- Preserve all unrelated dirty-worktree changes and stage only files from this plan.

---

### Task 1: Scoring API recognizes kong win

**Files:**
- Modify: `Assets/Scripts/Core/MahjongLogic.cs:23-103`
- Test: `Tests/NetworkRegression/ActionValidationTests.cs`

**Interfaces:**
- Produces: `CheckWinWithFan(..., ScoringOptions options = null, bool isRobKongWin = false, bool isKongWin = false)`
- Produces: `EvaluateBestFan(..., ScoringOptions options = null, bool isRobKongWin = false, bool isKongWin = false)`

- [ ] **Step 1: Write the failing scoring test**

Add a legal self-draw fixture whose ordinary fan is below 8 but whose kong-win evaluation is legal, and assert both the threshold and detail behavior:

```csharp
bool ordinary = MahjongLogic.CheckWinWithFan(
    hand, melds, winningTile, true, out _, out _,
    WindDirection.East, WindDirection.East, null,
    isRobKongWin: false, isKongWin: false);
bool afterKong = MahjongLogic.CheckWinWithFan(
    hand, melds, winningTile, true, out int fan, out List<string> details,
    WindDirection.East, WindDirection.East, null,
    isRobKongWin: false, isKongWin: true);

runner.Check(!ordinary && afterKong && fan >= 8
    && details.Any(detail => detail.StartsWith("杠上开花("))
    && details.All(detail => !detail.StartsWith("自摸(")),
    "kong replacement self-draw supplies the 8-fan threshold and excludes self-draw");
```

- [ ] **Step 2: Run RED**

Run: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore`

Expected: compile failure because the scoring APIs have no `isKongWin` parameter.

- [ ] **Step 3: Implement the minimal scoring propagation**

Thread the new argument from `CheckWinWithFan` into `EvaluateBestFan`, then set it on every decomposition context:

```csharp
var ctx = new Fan.FanContext(hand, melds, winTile, isSelfDraw, roundWind, seatWind, decomp)
{
    Wait = decomp.Wait,
    IsRobKongWin = isRobKongWin,
    IsKongWin = isKongWin
};
```

- [ ] **Step 4: Run GREEN**

Run the same NetworkRegression command and confirm the focused assertion passes without changing unrelated expected fan totals.

---

### Task 2: Main-turn decision carries replacement-draw authority

**Files:**
- Modify: `Assets/Scripts/Core/Network/NetworkDecisionTracker.cs`
- Modify: `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`
- Modify: `Assets/Scripts/Core/Network/ClientGameState.cs`
- Test: `Tests/NetworkRegression/NetworkAuthorityBoundaryTests.cs`
- Test: `Tests/NetworkRegression/SnapshotReconnectTests.cs`

**Interfaces:**
- Produces: `NetworkDecisionContext.IsKongReplacementDraw`
- Produces: `NetworkDecisionTracker.OpenMainTurn(int seat, long deadline, bool isKongReplacementDraw)`
- Produces: serialized `SnapshotDecision.isKongReplacementDraw`

- [ ] **Step 1: Write failing decision and snapshot tests**

Assert the immutable decision clone, message projection, client ordered projection, and reconnect snapshot retain the flag only for the current main turn:

```csharp
NetworkDecisionContext decision = tracker.OpenMainTurn(0, deadline, true);
runner.Check(decision.IsKongReplacementDraw && tracker.Active.IsKongReplacementDraw,
    "main decision clone preserves replacement-draw authority");

SnapshotDecision wire = RoomGameSnapshotBuilder.CreateDecisionSnapshot(decision);
runner.Check(wire.isKongReplacementDraw,
    "reconnect decision projects replacement-draw authority");
```

- [ ] **Step 2: Run RED**

Run: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore`

Expected: compile failures for the missing property, overload, and wire field.

- [ ] **Step 3: Implement immutable propagation**

Add the boolean to the constructor, `Clone`, and `WithSubmittedSeat`; response and rob-kong decisions always pass false. Copy it into `SnapshotDecision` and every client snapshot clone path.

- [ ] **Step 4: Run GREEN**

Run the full NetworkRegression executable and confirm ordered duplicate/gap behavior remains unchanged.

---

### Task 3: GameServer owns the kong replacement state machine

**Files:**
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Test: `Tests/GameServerTelemetryRegression/Program.cs`

**Interfaces:**
- Consumes: `OpenMainTurn(..., bool isKongReplacementDraw)` from Task 2
- Consumes: scoring `isKongWin` input from Task 1
- Produces: successful `AnGan`, `JiaGang`, and `MingGan` each arm exactly one subsequent wall draw

- [ ] **Step 1: Write real GameServer failing tests**

Extend the production-`GameServer` regression fixture with deterministic clients/walls. For each kong type, drive the accepted action through the real server and assert the next main decision is marked. Also assert rejected/robbed kong and the following normal turn are not marked.

The decisive assertions are against production state and final scoring, not a duplicate test state machine:

```csharp
runner.Check(server.ActiveDecision?.IsKongReplacementDraw == true,
    "successful kong marks the immediate replacement-draw decision");
runner.Check(server.WinFanDetails.Any(detail => detail.StartsWith("杠上开花(")),
    "real GameServer final scoring includes kong win");
```

- [ ] **Step 2: Run RED**

Run: `dotnet run --project Tests/GameServerTelemetryRegression/GameServerTelemetryRegression.csproj --no-restore`

Expected: compile failure for the missing decision property or behavioral failure because successful kongs do not arm a replacement draw.

- [ ] **Step 3: Implement the two-stage state machine**

Add private pending/current booleans. Before opening a main decision, consume pending only when an actual draw will occur:

```csharp
_currentDrawIsKongReplacement = !_skipNextDraw && _pendingKongReplacementDraw;
if (!_skipNextDraw) _pendingKongReplacementDraw = false;
var mainDecision = _decisionTracker.OpenMainTurn(
    _currentPlayerIndex,
    GetDeadlineUnixMilliseconds(ActionTimeoutMs),
    _currentDrawIsKongReplacement);
```

Set pending only after each authoritative kong commit succeeds. Pass `isKongWin: isSelfDraw && _currentDrawIsKongReplacement` through candidate, final, attribution, and visibility evaluations. Clear both fields on start/stop/win/draw/abort.

- [ ] **Step 4: Run GREEN**

Run the real GameServer regression, then NetworkRegression. Confirm all three kong paths and negative controls pass.

---

### Task 4: Local and AI clients use projected authority

**Files:**
- Modify: `Assets/Scripts/Core/Agents/IPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Agents/LocalPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Agents/SimpleAIClient.cs`
- Modify: `Assets/Scripts/Core/ActionValidator.cs`
- Modify: `Assets/Scripts/Core/Network/RemotePlayerClient.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Modify: `Assets/Scripts/Core/Network/StableSeatController.cs`
- Modify: `Tests/NetworkRegression/UnityEngineStubs.cs`
- Test: `Tests/NetworkRegression/SnapshotReconnectTests.cs`
- Test: `Tests/NetworkRegression/ActionValidationTests.cs`

**Interfaces:**
- Produces: `IPlayerClient.OnTileDrawn(TileData tile, bool isKongReplacementDraw)`
- Produces: `ActionValidator.CheckSelfActions(..., SelfTurnKongOptions kongOptions = null, bool isKongWin = false)`

- [ ] **Step 1: Write failing client admission tests**

Assert the same below-threshold hand has no Hu on a normal draw and has Hu on a projected replacement draw. Assert reconnect calls the local decision path with the snapshot boolean, while false remains the backward-compatible default.

- [ ] **Step 2: Run RED**

Run: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore`

Expected: compile failures for the new client callback and validator input.

- [ ] **Step 3: Implement minimal client propagation**

Forward the decision boolean through remote, stable-seat, local, and AI clients. `LocalPlayerClient.BeginMainTurnDecision` and `SimpleAIClient.OnTileDrawn` pass it to both admission and local detail calculation. Recovery reads `snapshot.activeDecision.isKongReplacementDraw`. No client derives the flag from `OnActionResolved`.

- [ ] **Step 4: Run GREEN**

Run NetworkRegression and verify ordinary main turns, response turns, timeout cleanup, and reconnect ordering still pass.

---

### Task 5: Final verification and scoped commit

**Files:**
- Verify only; no additional production files are introduced by this task.

- [ ] **Step 1: Run fresh pure C# verification**

Run sequentially:

```powershell
dotnet build Tests/NetworkRegression/NetworkRegression.csproj --no-restore
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-build --no-restore
dotnet build Tests/GameServerTelemetryRegression/GameServerTelemetryRegression.csproj --no-restore
dotnet run --project Tests/GameServerTelemetryRegression/GameServerTelemetryRegression.csproj --no-build --no-restore
git diff --check
```

Expected: both builds exit 0 with 0 errors, both executables pass, and diff check reports no errors.

- [ ] **Step 2: Inspect scope and privacy**

Confirm no Unity-generated project/meta files changed and the new boolean appears only in the requesting seat's main decision projection:

```powershell
git status --short
rg -n "IsKongReplacementDraw|isKongReplacementDraw" Assets/Scripts Tests
```

- [ ] **Step 3: Commit only the kong-win files**

Stage the exact files listed above, excluding all pre-existing UI/font/scene/brainstorm changes, then commit:

```powershell
git commit -m "fix: score authoritative kong replacement wins"
```

- [ ] **Step 4: Report the manual boundary**

State: pure C# verification passed; Unity integration and visual validation remain for the user's manual Unity Refresh gate.
