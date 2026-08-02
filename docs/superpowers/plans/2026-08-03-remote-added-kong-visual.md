# Remote Added-Kong Visual Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade an opponent's existing pon visual when an added kong resolves instead of displaying the added tile as a separate meld.

**Architecture:** Introduce a pure `OpponentMeldState` model for the melds represented by one opponent view. `OpponentViewController` applies incremental actions to that model and rebuilds its meld visuals, while snapshots replace the model from authoritative public meld data.

**Tech Stack:** Unity 2022.3 C#, UI-independent core model, DOTween-linked 3D presentation, .NET network regression executable.

## Global Constraints

- Keep the server and network protocol unchanged; the bug is client presentation only.
- UI/presentation code remains in Unity `MonoBehaviour` classes; state transition logic remains pure C#.
- DOTween calls attached to dynamic objects must retain `.SetLink(gameObject)` where applicable.
- An added kong consumes one concealed tile and upgrades one matching pon without increasing meld count.
- A missing matching pon must not create an orphan meld visual.

---

### Task 1: Opponent meld-state upgrade and rendering

**Files:**
- Create: `Assets/Scripts/Core/OpponentMeldState.cs`
- Create: `Assets/Scripts/Core/OpponentMeldState.cs.meta`
- Modify: `Assets/Scripts/Core/OpponentViewController.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Test: `Tests/NetworkRegression/SnapshotReconnectTests.cs`

**Interfaces:**
- Consumes: `Meld`, `MeldType`, and `TileData` from the existing core model.
- Produces: `OpponentMeldState.Melds`, `Replace(IEnumerable<Meld>)`, `Clear()`, and `TryApply(MeldType, IEnumerable<TileData>)`.

- [x] **Step 1: Write the failing regression test**

Add `TestOpponentAddedKongMeldState` to `SnapshotReconnectTests.Run`. Construct a state containing a three-tile `Pon`, apply one matching `Kan_Added` tile, and assert a single four-tile `Kan_Added` remains. Apply a nonmatching added kong and assert it returns `false` without changing the meld count.

- [x] **Step 2: Run the test to verify RED**

Run:

```powershell
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore
```

Expected: compilation fails because `OpponentMeldState` does not exist.

- [x] **Step 3: Implement the pure meld state**

Create `OpponentMeldState` with cloned internal `Meld` instances. `TryApply` must special-case `Kan_Added`: locate a `Pon` whose `FirstTile` has the same suit and value, mutate that meld to `Kan_Added`, append one target tile, and return `true`. Return `false` without mutation when the input is empty or the pon is absent. Other valid meld types append one cloned meld.

- [x] **Step 4: Connect the state to the opponent view**

Store one `OpponentMeldState` in `OpponentViewController`. Clear it in `ClearHand`, replace it in `RebuildFromSnapshot`, and render from `Melds`. In `ExecuteMeld`, retain the existing concealed-tile removal policy, apply the action to the state, warn on failure, and rebuild all meld child objects from the state so an upgraded kong occupies the original pon slot.

- [x] **Step 5: Run regression verification**

Run:

```powershell
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore
```

Expected: `Network regression tests passed.`

- [x] **Step 6: Review the diff and Unity asset metadata**

Run:

```powershell
git diff --check
git status --short
git diff -- Assets/Scripts/Core/OpponentMeldState.cs Assets/Scripts/Core/OpponentViewController.cs Tests/NetworkRegression/NetworkRegression.csproj Tests/NetworkRegression/SnapshotReconnectTests.cs
```

Expected: no whitespace errors; only the planned source, test, metadata, and documentation files are changed.
