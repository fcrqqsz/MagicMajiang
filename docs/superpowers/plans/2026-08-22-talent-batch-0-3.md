# Talent Batch 0-3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the shared foundations plus 轻装上阵、归色、异彩成章 on `master`, with authoritative recovery, minimal data-driven AI, generic choice presentation, and no telemetry changes.

**Architecture:** Talent rules remain polymorphic and operate through `TalentMatchRuntime`; `Room`, `GameServer`, clients, and UI receive only generic mutation, choice, public-history, and AI-capability contracts. Starting-hand changes are staged against copied physical tiles and committed atomically before clients receive their hands. Scoring uses immutable `TalentWinFacts` and detached runtime state.

**Tech Stack:** Unity 2022.3 C#, UI Toolkit, .NET 10 pure-C# NetworkRegression harness.

**Spec:** Current task's approved gameplay and technical design; the user requested no separate temporary design document.

## Global Constraints

- Work directly on `master` as requested.
- Do not add, remove, or reshape gameplay telemetry in this batch.
- Do not add concrete `talentId` branches to `Room`, `GameServer`, clients, AI policy, or UI.
- Protocol moves from v5 to v6; loadout schema remains v3.
- Do not edit Unity-generated `.meta` files or `Assembly-CSharp.csproj`.
- Do not start Unity/Tuanjie; report the manual UI integration gate separately.
- Every production behavior follows RED, verified failure, minimal GREEN, and regression verification.

---

### Task 0: Sticky Public History and Minimal Data-Driven AI

**Files:**
- Modify: `Assets/Scripts/Core/Network/Protocol.cs`
- Modify: `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Modify: `Assets/Scripts/Talent/TalentActionModels.cs`
- Modify: `Assets/Scripts/Talent/TalentNegativeEffect.cs`
- Modify: `Assets/Scripts/Talent/Impl/InterceptionTalent.cs`
- Modify: `Assets/Scripts/Talent/Impl/ComposureTalent.cs`
- Modify: `Assets/Scripts/Core/Agents/AiTalentDecisionPolicy.cs`
- Modify: `Assets/Scripts/Core/Agents/SimpleAIClient.cs`
- Test: `Tests/NetworkRegression/SnapshotReconnectTests.cs`
- Test: `Tests/NetworkRegression/AiTalentPolicyTests.cs`

**Interfaces:**
- Produces: `SnapshotKnownTalent.isActive`, `TalentActionOption.AiPriority`, marker interfaces `IPublicChargeControlTalent` and `IPublicChargeDefenseTalent`, and bounded repeated AI submission.
- Consumes: existing authoritative action options, `DefaultChoiceId`, `TalentSnapshotEntry.IsActive`, `IPublicChargeTalent`.

- [x] **Step 1: Write sticky-history RED tests**

Add behavioral tests proving a revealed inactive talent remains in `knownTalents` with `isActive == false`, an active one has `isActive == true`, hidden talents remain absent, and target presentation rejects inactive history.

- [x] **Step 2: Run focused sticky-history tests and verify RED**

Run: `dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- SnapshotReconnect AiTalent`

Expected: failure because `SnapshotKnownTalent` has no `isActive` and `Room` filters inactive entries.

- [x] **Step 3: Implement protocol v6 and sticky public history**

Add `isActive`, include every revealed opponent snapshot entry, and require `isActive` when building actionable public targets. Keep the exact active flag through JSON snapshot recovery.

- [x] **Step 4: Write data-driven AI RED tests**

Use literal priorities to prove `AiPriority` descending order wins before charge/seat tie-breaks; prove the default choice is copied; prove accepted submissions are re-queried up to six times, repeated fingerprints stop, and rejection stops. Prove sideboard counters only active public threats and discovers counter/defense talents through marker interfaces rather than ids.

- [x] **Step 5: Run focused AI tests and verify RED**

Expected: failure because the option has no priority, submission is single-shot, and sideboard policy hardcodes ids.

- [x] **Step 6: Implement minimal AI contracts**

Set rule-authored priorities on options; clone them through internal/client snapshot conversions where needed. Sort by descending priority, then descending target charge, seat, target id, and talent id. Submit in a loop capped at six, stop on rejection, and stop when an option fingerprint repeats. Use `DefaultChoiceId` only. Mark existing 截流 as public-charge control and 定心 as public-charge defense; make the one-swap sideboard reaction depend on interfaces and `isActive`.

- [x] **Step 7: Run focused tests and commit**

Run the focused snapshot and AI groups, then commit `feat: make talent history and ai decisions data driven`.

### Task 1: Transactional Initial Hands and 轻装上阵

**Files:**
- Modify: `Assets/Scripts/Talent/TalentImmutableFacts.cs`
- Modify: `Assets/Scripts/Talent/TalentContext.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Modify: `Assets/Scripts/Core/Network/ServerGameState.cs`
- Modify: `Assets/Scripts/Core/Network/GameRoundSetupSequence.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Create: `Assets/Scripts/Talent/Impl/TravelLightTalent.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Test: `Tests/NetworkRegression/TalentServiceFoundationTests.cs`
- Test: `Tests/NetworkRegression/TalentFoundationTests.cs`
- Test: `Tests/NetworkRegression/RoomSessionTests.cs`

**Interfaces:**
- Produces: owner-local `TalentInitialHandContext.TryTransformTile(...)`, staged physical-tile copies, and an atomic `ServerGameState.TryReplaceInitialHands(...)` boundary.
- Consumes: immutable starting-hand facts, physical tile ids, `OnInitialHandCompleted`, and the setup sequence.

- [x] **Step 1: Write initial-hand transaction RED tests**

Prove a rule can transform only a physical tile from its owner's staged hand; identity and original owner remain unchanged; `IsModified` and `SpecialEffectID` are applied; later rules see the staged result; an invalid id or invalid suit/value aborts without changing any authoritative hand.

- [x] **Step 2: Verify RED**

Expected: compile/test failure because the mutation request API and atomic replacement do not exist.

- [x] **Step 3: Implement the staged mutation boundary**

Clone all hands, rebuild immutable facts before each rule, restrict values to suited 1..9 or valid honors, preserve physical id/owner, stage events until validation succeeds, then atomically replace all hands. Do not expose `ServerGameState` or mutable `TileData` to rules.

- [x] **Step 4: Write GameServer publication-order RED test**

Record client `OnGameStart` hands and assert they match the post-transaction authoritative hands, not the originally dealt hands.

- [x] **Step 5: Reorder setup and verify GREEN**

Split dealing from publication: deal into authority, run `CompleteInitialHands`, publish final per-seat hands, then capture Peek. Keep `GameRoundSetupSequence` deterministic.

- [x] **Step 6: Write 轻装上阵 RED tests**

Use literal fixtures containing suited 1, 9, inner tiles, winds, and dragons. Assert only starting suited 1→2 and 9→8, all transformed tiles are modified by `travel_light`, later draws are untouched, reserves do not trigger, and no public transformed-count event is emitted.

- [x] **Step 7: Implement 轻装上阵 and verify GREEN**

Register `travel_light` as Medium, cost 16, phase `InitialHandCompleted`, round state, flexible sideboard, hidden until a normal public tile transition reveals its concrete modification.

- [x] **Step 8: Run focused tests and commit**

Commit `feat: add transactional starting-hand talents`.

### Task 2: Generic Choice Presentation and 归色

**Files:**
- Modify: `Assets/Scripts/Core/TalentActionPanelPolicy.cs`
- Modify: `Assets/Scripts/Core/Agents/LocalPlayerClient.cs`
- Modify: `Assets/UI/ActionPanelController.cs`
- Modify: `Assets/UI/ActionPanelStyles.uss` only if existing button classes cannot express the nested choice row
- Create: `Assets/Scripts/Talent/Impl/SuitConvergenceTalent.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`
- Test: `Tests/NetworkRegression/TalentActionTests.cs`
- Test: `Tests/NetworkRegression/AiTalentPolicyTests.cs`
- Test: `Tests/NetworkRegression/SnapshotReconnectTests.cs`

**Interfaces:**
- Produces: generic policy state for beginning/canceling a choice and a rule-authored suit choice for `suit_convergence`.
- Consumes: existing `TalentChoiceSet`, `TalentActionPanelPolicy.SelectChoice`, authoritative action transactions, `OnDraw`, runtime public events.

- [x] **Step 1: Write generic choice-policy RED tests**

Prove opening a choice preserves base actions, valid selection returns a cloned option with `SelectedChoiceId`, cancel restores the action list, rejection clears pending choice state, and recovery clears stale choice state.

- [x] **Step 2: Verify RED and implement the pure policy**

Add generic choice-selection state without inspecting talent ids or choice display text.

- [x] **Step 3: Implement the ActionPanel renderer**

When a selected option has an unresolved `Choice`, render its prompt/options as dynamic buttons plus cancel inside the existing talent container. Submit only after `SelectChoice` succeeds. Route clear, timeout, rejection, recovery, and destruction through existing cleanup.

- [x] **Step 4: Write 归色 RED tests**

Prove the first main decision offers 万/饼/条 with a default based on starting-hand suit counts and Man/Pin/Sou tie order. After acceptance, the next two suited draws outside the target suit preserve value and change suit; target suit and honors do not consume; further draws stay unchanged; state resets next round; inactive reserve neither chooses nor transforms.

- [x] **Step 5: Add public-state and recovery RED tests**

Prove selection reveals target suit with remaining 2, each actual transform publishes the remaining count, snapshots recover target/remaining without private leakage, and an inactive revealed history cannot be activated.

- [x] **Step 6: Implement 归色 and verify GREEN**

Register `suit_convergence` as Small, cost 8, `ActionValidation + OnDraw`, round state, main-turn activation, flexible sideboard. Set a high expiring-action `AiPriority`; store target suit and remaining count in round state; emit stable per-suit public event types with remaining as the public value.

- [x] **Step 7: Run focused tests and commit**

Commit `feat: add generic suit choice and convergence talent`.

### Task 3: 异彩成章

**Files:**
- Create: `Assets/Scripts/Talent/Impl/ChromaticCompositionTalent.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Test: `Tests/NetworkRegression/TalentResultAttributionTests.cs`
- Test: `Tests/NetworkRegression/TalentFoundationTests.cs`

**Interfaces:**
- Produces: `chromatic_composition` post-legal fan contribution.
- Consumes: immutable `TalentWinFacts`, detached runtime state, existing attribution pipeline.

- [ ] **Step 1: Write fan-boundary RED tests**

Use hand-authored physical ids across concealed tiles, melds, and winning tile. Assert 0 bonus at 0–3 unique modified tiles, +12 at 4, +15 at 5, and +24 at 8 or more. Assert one physical tile appearing as both concealed/winning data counts once, while distinct copies with equal suit/value count separately.

- [ ] **Step 2: Verify RED**

Expected: registry cannot find `chromatic_composition` or returns no contribution.

- [ ] **Step 3: Implement the rule**

Register Large, cost 26, `Scoring`, match state, public at match start, flexible sideboard. Count unique non-empty physical ids with `IsModified`; use a stable fallback key only for malformed empty-id facts so one fact object cannot be counted twice. Return `count >= 4 ? min(count, 8) * 3 : 0` from `GetPostLegalFanBonus`; do not lower eligibility.

- [ ] **Step 4: Write attribution/detachment RED tests**

Prove a six-fan base hand remains illegal despite the bonus, a legal accepted win receives one post-legal contribution, repeated candidate and counterfactual evaluations are deterministic, and authoritative state/event history does not change before final acceptance.

- [ ] **Step 5: Verify GREEN and commit**

Run focused fan and attribution tests, then commit `feat: add chromatic composition talent`.

### Task 4: Cross-Cutting Verification and Handoff

**Files:**
- Modify only if necessary: `plan.md`, `milestone.md`, `struct.md`, `summary.md`
- Create: no telemetry files and no temporary Unity assets

**Interfaces:**
- Produces: verified 0–3 implementation and a standalone child-task prompt for designs 4–6.
- Consumes: all prior task outputs.

- [ ] **Step 1: Run focused regressions**

Run the changed test groups and any necessary real `GameServer` regression. Fix every defect through a new failing regression first.

- [ ] **Step 2: Run complete NetworkRegression**

Run: `dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore`

Expected: exit 0 with no failed checks.

- [ ] **Step 3: Review architectural constraints**

Search production code to confirm no concrete new talent ids appear outside rule classes, registry-derived tests/fixtures, loadout presets, or player-facing metadata. Confirm other-seat private hands/choices remain absent from snapshots, no telemetry code changed, and no `.meta`/generated csproj changed.

- [ ] **Step 4: Update durable project docs and commit**

Record only completed behavior and the remaining 4–6 design handoff. Commit `docs: record first new talent batch progress` if durable docs actually need changes.

- [ ] **Step 5: Prepare the child-task prompt**

Include exact 乘势/褪色/化劲 rules, ids, costs, lifecycle, sideboard behavior, shared interfaces now available, RED tests, ordering, forbidden branches, no telemetry scope, validation commands, Unity boundary, and instructions to stop for review after implementation.
