# Task 9 Report — authoritative final fan and itemized talent contributions

## Scope delivered

- Added `TalentResultPresentationPolicy`, a pure message-to-view mapping that keeps the accepted final fan authoritative, places the base fan first, orders non-zero talent rows by server `sequence`, and formats explicit signed deltas.
- Unknown talent IDs render only the fixed local label `未知天赋` and request a fixed warning; protocol text is never rendered or logged.
- Mismatch checks are diagnostic-only. Neither contribution sums nor MCR detail strings can replace the authoritative final fan.
- Restructured the UI Toolkit result panel with `FinalFanHero`, `BaseFanRow`, and a bounded `TalentContributionList`; the independent MCR `FanListContainer` scrolls while the winning hand and continue button remain fixed below it.
- Live win/lose and recovery round results converge on one `RenderRoundResult` entry point and one public breakdown view. Draw and session-final views hide the hero and breakdown.
- Removed the old `fanDetails` parenthesis parser, rolling sum coroutine, and derived total label. Recovery reveals the stored result directly without replaying toast or audio feedback.
- Added teardown for button and winning-hand geometry callbacks.

## TDD evidence

### Cycle 1 — pure result view

- RED: focused regression failed with ten expected `CS0246`/`CS0103` errors because `TalentResultView` and `TalentResultPresentationPolicy` did not exist.
- GREEN: focused regression exited 0 after implementing authoritative hero/base rows, stable sequence ordering, signed deltas, zero omission, unknown local copy/warning, mismatch diagnostics, hidden null view, and live/recovery equality.

### Cycle 2 — accepted-final fallback

- RED: focused regression failed with two expected `CS0117` errors because `BuildAcceptedWin` did not exist.
- GREEN: a win without complete attribution now still shows the accepted final hero and hides explanatory rows. When the duplicate accepted field disagrees with an available breakdown, the Task 3 breakdown `finalFan` remains authoritative and only the diagnostic flag changes.

### Cycle 3 — result panel artifacts and source boundaries

- Added executable UXML/source guards for exact top-level result hierarchy, base-before-talent rows, bounded talent and independent MCR scroll views, supported TextCore font, common live/recovery render entry, draw/session-final hiding, and teardown.
- Guards explicitly reject `RollScoreRoutine`, `LastIndexOf`, `StartCoroutine`, and `TotalScoreLabel` in the result presentation path.

## Authority and lifecycle review

- The policy never parses `fanDetails` and never assigns a computed sum to `FinalFan`.
- Contributions are display explanations only; reconciliation uses `long` solely to set `HasMismatchDiagnostic`.
- `TalentRegistry.HasTalent` gates every display name lookup. Unknown protocol IDs cannot enter UI copy or warning text.
- `LocalResultPresentationBridge` and `TalentFanPresentationState` remain the Task 3 transport/copy boundary; Task 9 adds no duplicate wire or projection model.
- Recovery receives the already-projected `GameSession` from `GameManager`, uses the same result renderer as live win/lose, and directly applies the visible overlay state.
- `OnDestroy` stops pending work, unregisters the restart button, unregisters the winning-hand geometry callback, and clears the singleton. No DOTween animation is used.

## Verification

- Focused `talent-presentation` regression: exit 0, `Network regression tests passed.`
- Full network regression: exit 0, `Network regression tests passed.`
- `Assembly-CSharp` source compile with a temporary validation-only inclusion target: build succeeded with 0 errors and 6 pre-existing Unity package obsolete/analyzer warnings.
- Result UXML parses and exposes the required hierarchy; legacy total-derivation source guards pass.
- New policy meta GUID is valid and unique across `Assets`.
- `git diff --check`: exit 0; only repository line-ending conversion notices.
- Ignored Unity-generated `.csproj` files were not modified. Temporary validation targets were deleted.

## Commit

- Message: `feat: explain itemized talent fan results`

## Concerns

- Unity/Tuanjie Editor batch execution is deferred to Task 12. Presentation compilation was checked through the existing generated project plus temporary validation-only missing-source inclusion, and behavior/layout through pure regression tests, parsed UXML, meta, and source guards.
- The pre-existing untracked `.superpowers/brainstorm/` directory remains untouched and excluded from the commit.
