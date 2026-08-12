# Phase 3 Task 4 Report — Standardized active-effect feedback

## Scope and changes

- Added `TalentActionResult.EffectApplied` and made `TalentActionResult.Success(bool effectApplied)` explicit, so acceptance is not implicitly treated as a strong effect success.
- `SheathedEdgeTalent` reports an applied effect after it arms.
- `InterceptionTalent` preserves its Phase 2 consumption/reveal order, captures `TalentNegativeEffectResult`, and reports an applied effect only when `WasApplied` is true. A blocked interception remains accepted and still spends its use/token.
- `TalentMatchRuntime.TryActivate` is the sole publisher of the public `active_talent_applied` runtime event. It only publishes for an accepted result with `EffectApplied`, and the event is owned by the source rule entry.
- Added `effectApplied` to the private `TalentActionResolvedMessage` envelope. `Room` copies the authoritative flag only; it does not infer talent IDs/effects or publish feedback.
- Expanded regression coverage for sheathed-edge arming, blocked/applied interception, rejected/duplicate/stale requests, the standardized public event's source ownership, and the resolved envelope field.

## RED

Command:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- TalentActionTests"
```

Output (expected): failed compilation with five `CS1061` errors because `TalentActionResult` did not define `EffectApplied` (at `TalentActionTests.cs` lines 397, 399, 401, 405, and 406).

## GREEN

Command:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- TalentActionTests"
```

Output:

```text
Network regression tests passed.
```

## Full verification

Commands:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Results:

- Full network regression suite: `Network regression tests passed.` (exit 0).
- Diff whitespace guard: exit 0, no diff errors.
- `rg` source guard confirmed every `TalentActionResult.Success` call supplies an explicit `effectApplied` value; only the two real active talents mark an applied effect.
- No Assembly-CSharp project-file diagnostic or temporary patch was required.

## Self-review

- No client/Room effect inference or `talentId`/`effectId` branching was added.
- No UI, audio, presentation, attribution, or Task 5+ code was changed.
- The existing runtime-event-before-resolved ordering is preserved: a successful GameServer submission raises the runtime event callback before `Room.SubmitTalentAction` writes its sole `TalentActionResolved` envelope.
- The only untracked workspace content is the pre-existing `.superpowers/brainstorm/`; it is intentionally neither modified nor staged.

## Commit

Committed with message `feat: mark applied active talent effects`. This report is included in the task commit; its final SHA is recorded in the task handoff.

## Concerns

None.
