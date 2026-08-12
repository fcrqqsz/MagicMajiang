# Phase 3 Task 3 Implementation Report

## Scope and baseline

- Requirement source: `task-3-brief.md` only.
- Baseline/current starting commit: `0452e1ef70b44df8279469b8a62ea810d7f11eeb`.
- Branch: `codex/talent-actions-ui-unified`, ordinary checkout.
- Scope stayed within Task 3. `ResultPanelController` and the new pure presentation boundary were added only to close the required live/recovery data chain; no contribution-row visuals or Task 4+ behavior were implemented.
- The pre-existing untracked `.superpowers/brainstorm/` tree was excluded from every stage/commit command.

## Delivered behavior

- Added one ungated `MahjongLogic.EvaluateBestFan` path. `CheckWinWithFan` delegates to it and still exclusively enforces the public 8-fan eligibility threshold.
- Added `TalentFanContribution`, stable categories/sequence, `TalentFanResolution.BaseFan`, completeness state, and contributions.
- Added accepted-final-aware `TalentAcceptedWinAttributionContext` and stable sequence marginal attribution in `TalentMatchRuntime`.
- All scoring and post-legal counterfactual calls use detached runtime state and null event sinks. Attribution is polymorphic/runtime-entry-based; it has no talent/effect ID execution branches.
- Positive eligibility/post-legal deltas and effective negative clamping reconcile `BaseFan + sum(rows)` to the already accepted final. Zero rows are omitted.
- Evaluator/scoring/post-legal failures and reconciliation failures are server diagnostics only: the accepted final is retained, the resolution is incomplete, and no fake/empty UI breakdown is produced.
- Added deep-copied transport through `IPlayerClient`, `PlayerWinMessage`, `Room` snapshot source/builder, `ClientGameState`, `RemoteServerProxy`, `LocalPlayerClient`, and `ResultPanelController` recovery.
- Added a stable, pure `LocalResultPresentationBridge`/`TalentFanPresentationState` boundary. It deliberately stores data only; later UI work may render rows.

## TDD evidence

### Round 1: ungated evaluation

- RED command: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-attribution`.
- RED result: compile failed because `FanEvaluation` and `MahjongLogic.EvaluateBestFan` did not exist.
- GREEN: extracted the single decomposition/rule/relaxed/bonus evaluation path and retained the 8-fan gate only in `CheckWinWithFan`.
- GREEN commands: the same focused command, followed by `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore`.
- GREEN result: both printed `Network regression tests passed.`

### Round 2: stable attribution

- RED command: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-attribution`.
- RED result: compile failed because `TalentAcceptedWinAttributionContext`, contribution types, and `ResolveAcceptedWinFan` were missing.
- GREEN: implemented stable cumulative runtime-sequence marginals, real relaxed-pure-straight counterfactual coverage, source-owned negative clamp rows, non-commutative order coverage, and detached/no-event protection.
- GREEN commands: focused and full regression commands above; both printed `Network regression tests passed.`

### Round 3: live/recovery wire chain

- RED command: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-attribution`.
- RED result: compile failed because breakdown DTOs and the expanded `OnPlayerWin` contract did not exist.
- Intermediate compile failures identified required test stubs/usings and the room fixture's seat stream capacity; those fixture issues were corrected without weakening assertions.
- GREEN: implemented deep-copied live/wire/snapshot/client/proxy flow plus four-seat public result visibility, private-field isolation, duplicate handling, gap atomicity, and reconnect preservation.
- GREEN commands: focused and full regression commands above; both printed `Network regression tests passed.`

### Self-review reconciliation guard

- RED command: focused regression command above.
- RED result: failed assertion because an intentionally unreconciled `TalentFanResolution` still created wire rows.
- GREEN result: after `TalentFanBreakdownMessage.FromResolution` rejected incomplete/non-closing resolutions, the focused command passed.

### Internal review follow-up (three Important items plus Minor)

1. Accepted-final/runtime exception contract:
   - RED command: focused regression command above.
   - RED result: compile failed because the attribution context lacked the accepted-final constructor value; throwing evaluator/post-legal fixtures therefore could not satisfy the contract.
   - GREEN: context explicitly carries `AlreadyAcceptedFinalFan`; runtime catches scoring/evaluator/post-legal exceptions, logs errors, retains that final, and returns an incomplete empty contribution result. Zero-final failure cannot masquerade as a legitimate empty breakdown.
2. Presentation live/recovery boundary:
   - RED command: focused regression command above.
   - RED result: `CS0246` for missing `RecordingResultPresentation` and `LocalResultPresentationBridge`.
   - GREEN: `LocalPlayerClient` live and `ResultPanelController` recovery both call the same deep-copying bridge; tests assert independent live/recovery data and getter copies. Draw clears stale presentation state.
3. Real recovery source/no rerun:
   - RED command: focused regression command above with the new real-`Room` fixture.
   - RED result: the fixture could not inject/read a stored server breakdown until the test `GameServer` stub exposed the same result-source contract.
   - GREEN: a real `Room` is brought through match/game-scene ready states, supplied the already stored breakdown via its `GameServer` result source, snapshotted, and applied to `ClientGameState`; an evaluator counter proves snapshot/reconnect does not rerun attribution/runtime evaluation.
4. Minor null-row normalization:
   - GREEN command/result: the fresh focused regression passed with a new assertion proving clone filters null contribution rows and deep-copies retained rows.

## Fresh verification

- Focused: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-attribution` -> `Network regression tests passed.`
- Full: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore` -> `Network regression tests passed.`
- `git diff --check` -> exit 0.
- Source guard `talentId\s*==|switch\s*\(.*talentId` over `Assets/Scripts/Core` and `Assets/Scripts/Talent` -> no matches.
- Authority guard (`GameManager.Instance`, `DeckManager.Instance`, `ResultPanelController`, `HandController`) over `TalentMatchRuntime.cs` and `GameServer.cs` -> no matches.
- New Assets meta GUID `716eab8ab3d149cb8da0c78789717efd` occurs exactly once.

## Assembly-CSharp diagnostic and restoration

- Direct fresh `dotnet build Assembly-CSharp.csproj --no-restore` first failed with 31 missing-type errors.
- Diagnosis: Unity's generated project was stale. It omitted ten already-existing Phase 2 source files (`TalentActionModels`, `TalentNegativeEffect`, `TalentFanModifierPolicy`, sideboard/admission/alienation/picker policy files) and the new `TalentFanPresentationState.cs`.
- Temporarily added those eleven compile entries with `apply_patch`, then reran the same command: build succeeded with 0 warnings and 0 errors.
- Removed every temporary project entry with a reverse `apply_patch`; `ASSEMBLY_DIAGNOSTIC_RESTORED` guard passed and `Assembly-CSharp.csproj` has no worktree change.

## Diff and self-review

- Reviewed the complete Task 3 diff against the brief and the internal review findings.
- Confirmed server score/application remains based on the original accepted Phase 2 eligibility/post-legal result. Attribution only explains that value and can only suppress the optional breakdown on failure.
- Confirmed runtime entry identity/exclusion behavior used by `ResolveAcceptedWinVisibility` remains intact; attribution uses its own included-entry identity set and does not alter reveal/post-legal semantics.
- Confirmed all DTO/snapshot/projection/presentation boundaries clone arrays and rows; clone filters null rows.
- Confirmed four-seat snapshots share only the completed public breakdown, never hidden carried lists or private talent state.
- Confirmed no visual styling or contribution-row rendering was added.

## Commit

- Commit message: `feat: attribute talent fan contributions`.
- Initial verified commit was created as `871399a`; the report-only post-commit evidence update was folded into the same commit with `--amend`.
- Final hash is reported by the task handoff (`git log` is the non-self-referential source because this report is included in that same commit).
- Post-commit `git status --short --branch` showed the correct branch and only `?? .superpowers/brainstorm/`; no Task 3 tracked or untracked implementation files remained. This is the explicitly excluded pre-existing brainstorm tree, so the Task 3 worktree is clean modulo that exclusion.

## Concerns

- Unity Editor/PlayMode was not launched in this shell. Assembly compilation was nevertheless proven after temporarily repairing the stale Unity-generated project list, and the generated file was restored afterward.
- The new presentation boundary intentionally does not render contribution rows; visual rendering belongs to a later task.
