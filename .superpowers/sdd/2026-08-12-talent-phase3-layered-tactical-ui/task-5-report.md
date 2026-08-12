# Phase 3 Task 5 Report — Layered talent HUD and feedback policies

## Scope and changes

- Added pure C# `TalentHudProjectionPolicy` with `TalentHudView`, `TalentHudItem`, and `TalentSeatSummary` contracts for Task 6.
- The own persistent row includes only active own talents. All other own snapshot entries contribute only to `OwnCollapsedCount`.
- Opponent summaries consume only `SnapshotKnownTalent.isKnown` entries from the already-filtered snapshot, show at most two entries, and expose neither active state nor any inferred hidden-count value.
- Ordering is deterministic: public-at-match-start metadata entries first, then the latest accepted public event ID descending, then ordinal talent ID. Unknown or unregistered IDs are ignored instead of appearing in counts or ordering.
- Added `TalentEventPresentationPolicy`, local registry display-name lookup, safe fixed Chinese event copy, exact strong/medium/weak mapping, and generic weak/warning output for unknown event types. No server event text is rendered.
- Added `TalentFeedbackHistory` that rejects non-positive, duplicate, and lower per-match IDs, with a new-match reset. Recovery produces a completely silent feedback view and never appends feed rows.
- Added the two required Unity `.meta` files and included both policies in the standalone regression project.

## RED

Command:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-presentation"
```

Output before the policy files existed:

```text
CSC : error CS2001: Source file '...TalentHudProjectionPolicy.cs' could not be found.
CSC : error CS2001: Source file '...TalentEventPresentationPolicy.cs' could not be found.
```

This is the expected missing-policy failure after adding the focused behavior tests and project compile links.

## GREEN and verification

Focused GREEN command:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-presentation"
```

Output: `Network regression tests passed.`

Full verification commands:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Both exit successfully; the full suite output is `Network regression tests passed.` and the whitespace guard has no output. Meta guard confirms both new scripts have one `guid` entry.

## Self-review

- The policies depend only on snapshot DTOs, runtime-event DTOs, and `TalentRegistry`; they have no `MonoBehaviour`, scene, UI, audio, or service dependency.
- No service-side snapshot filter or protocol field changed. Opponent active state and hidden loadout cardinality are not inputs or outputs.
- `active_talent_applied` is the sole strong event. `blocked_negative_effect`, reveal, public charge, and public counter/use updates are medium. State refreshes and unknown types are weak.
- Server-provided talent/event text is never interpolated into `Copy`; only registry metadata and fixed Chinese strings are used.
- No talent-effect branches were added. The only untracked existing workspace content remains `.superpowers/brainstorm/`, which is not staged.

## Commit

Committed with `feat: define layered talent feedback policies`. The final SHA is recorded in the task handoff; it is intentionally not repeated here because this report itself is included in the commit.

## Concerns

None.

## Fix Round 1 — Review corrections

### Verified review findings

- `TalentContext.SetPublicCounter` previously used its arbitrary internal counter key as `TalentRuntimeEvent.EventType`. Real `SheathedEdgeTalent` emits `edge`; `InterceptionTalent` emits `uses_remaining`. These were neither stable presentation categories nor recognized by the feedback policy.
- Own active entries were filtered through `TalentRegistry.HasTalent`, then all non-rendered entries were counted as collapsed. An unknown active ID could therefore disappear from the persistent row and incorrectly increase the collapsed count.
- Every medium mapping set `ShowToast = true`, contrary to the feed-and-chip-only medium feedback contract.

### RED → GREEN evidence

1. Added the unknown-active HUD assertion before adding the item warning contract. Focused compilation failed with `CS1061`: `TalentHudItem` did not define `ShouldLogWarning`.
2. Added reveal/blocked/public-counter medium assertions requiring `AppendFeed` and `PulseChip`, while forbidding both toast and audio. After the minimal HUD implementation, focused presentation tests failed with `reveal blocking and public counter changes are feed-and-chip medium feedback without toast or audio`.
3. Added a real `TalentMatchRuntime` flow: a round advances Sheathed Edge’s public charge, and an Interception activation publishes its public remaining-use update. These events are converted to client DTOs and passed through the production presentation policy. After event source normalization, the existing Phase 2 tests still expected dynamic `uses_remaining`; they failed at the two stale assertions, which were updated to require `public_counter_changed` and to forbid the private counter key.

GREEN commands:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-presentation"
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-actions"
```

Both output `Network regression tests passed.`

### Implementation

- `SetPublicCounter` now emits the stable, structured category `public_counter_changed`; the visible public numeric value is retained, but the internal counter key is not transmitted as the event type. This is generic plumbing and does not branch on talent or effect IDs.
- Medium feedback (reveal, blocked, public-charge/counter/use change) is now feed + chip emphasis only: `ShowToast = false`, `PlayAudio = false`.
- Unknown active own talents become one visible active `未知天赋` item with `ShouldLogWarning = true` and an empty safe ID. They do not contribute to `OwnCollapsedCount`; no server name or rich text is rendered.
- The hidden-opponent fixture now uses registered `starting_capital` with `isKnown = false`; because it would otherwise be pinned, the test proves a hidden registered entry affects neither ordering nor count.

### Fix Round 1 verification and self-review

Full regression output: `Network regression tests passed.`

`git diff --check` succeeded. Source guard confirms the old `EmitPublic(key, value)` form and dynamic `edge`/`uses_remaining` event-type assertions are absent. Both original policy `.meta` files remain present.

No Assembly-CSharp temporary project was created. `.superpowers/brainstorm/` remains excluded.
