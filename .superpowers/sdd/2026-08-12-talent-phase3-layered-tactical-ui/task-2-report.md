# Task 2 Report — 6+3 deck editor and explicit room admission UI

## Scope delivered

- Added Unity-free alienation gauge and room admission presentation policies.
- Replaced the editor's single alienation score with a Low/Standard/High draft selector, fill gauge, exact deck/main-talent breakdown, and saveable over-cap warning.
- Added distinct 6 main + 3 reserve talent containers. Main and reserve selection use `CanEquip` and `CanEquipReserve`; duplicate, tier-incompatible, and metadata-disallowed choices remain visible but disabled.
- Save atomically writes the draft config, talents, deck preset, and total. Deck switching reloads the saved preset and therefore discards the previous unsaved draft.
- Added an independent pending room preset selector, client-side create blocker, authoritative join-error blocker, public room preset copy, and private own total/limit copy.
- Opponent rows show only the shared room tier, never opponent totals.

## TDD evidence

### Cycle 1 — pure policies

- RED: `pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"`
- Result: exit 1; CS2001 for missing `AlienationGaugePolicy.cs` and `RoomLoadoutAdmissionPresentationPolicy.cs` only.
- GREEN: same command.
- Result: exit 0; `Network regression tests passed.`

### Cycle 2 — 6+3 UI and explicit create-room preset

- RED: same regression command.
- Result: exit 1 with the four intended assertions: editor gauge/main-reserve containers, `CanEquip`/`CanEquipReserve`, lobby selector/blocker, and explicit room preset create call.
- GREEN: same command.
- Result: exit 0; `Network regression tests passed.`

### Cycle 3 — authoritative join rejection presentation

- RED: same regression command.
- Result: exit 1 only for `authoritative join-room rejections open the explicit admission blocker`.
- GREEN: same command after routing `HandleRoomError` through the blocker.
- Result: exit 0; `Network regression tests passed.`

## Final verification

- Full regression (fresh): exit 0; `Network regression tests passed.`
- `git diff --check`: exit 0; only line-ending conversion notices.
- UXML static validation: both modified UXML files parsed successfully as XML.
- Unity asset static validation: both new `.cs` files have `.meta`; all scanned Assets meta GUIDs are unique.
- Assembly validation: initial generated `Assembly-CSharp.csproj` was stale and failed on 29 unrelated missing Task 1/3 source references. After temporarily adding those existing source references plus the two new policies, `dotnet build Assembly-CSharp.csproj --no-restore --verbosity minimal` succeeded with 0 warnings and 0 errors. Temporary generated-project edits were removed before commit.

## Diff and self-review

- Changes are confined to the two policies, deck editor/lobby UXML-USS-controllers, regression test project/test file, and this report.
- `.superpowers/brainstorm/` remains untracked and unstaged.
- No Canvas/UGUI was introduced; both styles continue using `Assets/Font/MSYH_UITK.asset` and do not reference TTC/TMP assets.
- Presentation policies do not send commands or mutate profiles. The server remains authoritative for JoinRoom because the room preset is unknown locally.
- Save remains gated only by exactly 34 tiles, not by over-cap state.

## Commit

- Message: `feat: add per-deck alienation UI`

## Concerns

- Unity batch/editor execution was unavailable in the environment; compilation used the Unity-generated C# project with temporary source-reference repair, plus XML/meta static validation.
- Client create validation is intentionally presentation-only and can be stale if a saved total was externally corrupted; the server still performs authoritative validation.

## Fix Round 1

### Findings addressed

- Corrected duplicate selection semantics so only the current slot in the edited partition is skipped. The opposite main/reserve partition is always checked, including the same numeric index.
- Removed the room alienation field from every seat row. The room view now has one public preset summary and one private local total/limit summary only.
- Added pure presentation policies so the regression suite exercises the real duplicate and visibility behavior without Unity mocks.

### RED evidence

- Command: `pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"`
- Result: exit 1; CS2001 only for the intentionally missing `TalentPickerDuplicatePolicy.cs` and `RoomAlienationPresentationPolicy.cs`.
- Covered cases: current main slot remains selectable; same-index reserve duplicate blocks the main item; current reserve slot remains selectable; public preset is singular and seat summaries are empty.

### GREEN and final verification

- GREEN: same regression command; exit 0, `Network regression tests passed.`
- Fresh full regression after self-review: exit 0, `Network regression tests passed.`
- Assembly: temporarily repaired the stale generated project references and ran `dotnet build Assembly-CSharp.csproj --no-restore --verbosity minimal`; build succeeded with 0 errors and 6 existing Unity package warnings. Temporary generated-project edits were removed.
- Static assets: modified UXML parsed; both new `.cs` assets have `.meta`; removed seat-row and old duplicate-helper source shapes are absent from production UI.
- `git diff --check`: exit 0; only line-ending conversion notices.

### Fix Round 1 self-review

- Main edit skips only `SlotTalentIds[slotIndex]`; every reserve index remains eligible to mark a duplicate.
- Reserve edit skips only `ReserveTalentIds[slotIndex]`; every main index remains eligible to mark a duplicate.
- Seat rows contain number, name, kind, and ready only. They expose neither shared tier copies nor exact opponent totals.
- `.superpowers/brainstorm/` remains untracked and will not be staged.

### Fix Round 1 concerns

- Unity Editor execution remains unavailable; validation uses the Unity-generated assembly project plus UXML/meta static checks.
