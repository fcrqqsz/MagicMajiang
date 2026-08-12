# Task 8 Report — original full-screen halftime tactical sideboard

## Scope delivered

- Added a pure immutable `SideboardDraftPolicy` that accepts only the nine carried slots, preserves canonical main-then-reserve slot order, keeps locked-main talents active, allows any valid active count, and separates editable over-cap drafts from lock eligibility.
- Added a pure `SideboardPanelStatePolicy` for editable, submission-pending, authoritative locked, public progress, closed, and recovery states.
- Added the approved full-screen UI Toolkit sideboard with separate active and reserve grids, tier/state/cost/lock card copy, private own budget gauge, four-seat lock progress, known-opponent intel, and a server-deadline display.
- Routed ordered `SideboardStarted`, `SideboardLocked`, and `SideboardProgress` envelopes through `RemoteServerProxy` into the production `GameHUD` tree.
- Kept the local draft presentation-only. `ClientGameState` and `ClientRoomState` remain authoritative projections and never receive draft edits.

## TDD evidence

### Cycle 1 — immutable draft policy

- RED: focused sideboard regression failed with `CS2001` because `SideboardDraftPolicy.cs` did not exist.
- GREEN: focused sideboard regression exited 0 after implementing immutable add/disable/replace, locked-main protection, unknown/duplicate/uncarried rejection, canonical ordering, non-six active counts, over-cap editability, deep copying, and readonly recovery.
- One first GREEN attempt correctly exposed an invalid test fixture that placed a Medium talent in a Small slot; the fixture was corrected before accepting GREEN.

### Cycle 2 — panel state and ordered proxy events

- RED: focused sideboard regression failed with `CS2001` because `SideboardPanelStatePolicy.cs` did not exist.
- GREEN: focused sideboard regression exited 0 after implementing submit-once pending state, authoritative lock finality, wrong-seat rejection, public progress, own-locked recovery, and sequence-gated proxy publication.

### Cycle 3 — full-screen UI artifacts

- RED: focused sideboard regression failed only for `full-screen sideboard UI assets exist`.
- GREEN: focused sideboard regression exited 0 after adding UXML/USS/controller assets and mounting the template under `GameHUD`.

### Cycle 4 — malformed source and recovery edges

- RED: malformed private Started IDs were initially canonicalized without a blocking error.
- GREEN: unknown, duplicate, and uncarried source IDs now produce non-lockable drafts; unlocked recovery waits readonly until the ordered private Started arrives, while own-locked recovery remains readonly.

## Authority, privacy, and lifecycle review

- Live private Started is tagged with the current local seat by `RemoteServerProxy`; the panel refuses a mismatched seat.
- Locked always discards the private draft. A duplicate or stale Started for the same locked decision cannot restore editing, including after an invalid server submission locked the original set.
- Progress contains and renders only four public locked booleans. It never carries active selections, hidden talents, validation details, or another seat's exact budget.
- Opponent intel is built only through Task 5 `TalentHudProjectionPolicy`; the controller does not read `knownTalents`, `ownTalents`, opponent active state, or hidden/private values directly.
- Lock submission transitions locally to readonly before calling the proxy and cannot be emitted twice.
- The deadline schedule only renders `deadlineUnixMilliseconds - UtcNow`. At zero it displays `等待服务器`; it never submits, locks, or decides timeout.
- Proxy events, the deadline schedule, the lock button, and every dynamic card callback are unsubscribed or paused on unbind/dispose. Leaving the game destroys the HUD and the proxy cleanup removes the upstream ordered-envelope subscription.

## Final verification

- Focused sideboard regression: exit 0, `Network regression tests passed.`
- Full network regression: exit 0, `Network regression tests passed.`
- Both UXML files parse as XML.
- All five new Unity assets have metas; each new GUID appears in exactly one meta.
- Source guards confirm paired proxy subscriptions, schedule/button cleanup, known-opponent projection-only access, and no local sideboard timeout path.
- `git diff --check`: exit 0; only repository line-ending conversion notices.
- Ignored Unity-generated `Assembly-CSharp.csproj` was not modified, per Task 8 instructions.

## Commit

- Message: `feat: add halftime tactical sideboard UI`

## Concerns

- Unity/Tuanjie Editor batch execution was unavailable in the environment, so the Unity presentation controller was verified through pure policy/client integration tests plus UXML/meta/source checks rather than an Editor play-mode run.
- An internal read-only review task did not return within the completion window and was interrupted; the parent coordinator will run the formal review.
- The pre-existing untracked `.superpowers/brainstorm/` directory remains untouched and unstaged.

## Fix Round 1 — formal review corrections

### Corrections

- Added `RoomState.WaitingForSideboard -> Game` to `ClientRecoverySceneRoutingPolicy`; regression coverage preserves `WaitingForPlayers -> Lobby`, `WaitingForNextRound -> Game`, and terminal behavior.
- Changed complete `SideboardProgress` from an implicit close into a public four-seat lock/`IsComplete` merge. The locked result remains visible and readonly until the ordered next `RoundStart`, inactive recovery snapshot, unbind, or explicit reset closes it.
- Extracted `TalentLoadoutSlotPolicy.TryBuild` from server admission and made `PlayerLoadoutCodec` plus client `SideboardStarted` validation share the exact strict 6+3 shape, duplicate, main-slot tier, reserve-slot tier, and reserve-`Flexible` rules.
- Required every carried `MainOnlyLocked` talent to be initially active. Malformed Started payloads retain an explanatory error but are readonly, cannot submit, and cannot make an unknown talent reach metadata rendering.
- Removed the unused `TrustedPlayerLoadout` parameter from `SetActive`/`ReplaceActive`; the production controller no longer passes a fake `null` argument.
- Exposed readonly deck and current-active-talent alienation partitions and added private-own UI labels for deck cost, active talent cost, total, and named preset limit. No opponent budget field or projection was added.

### Fix Round 1 TDD and verification

- RED: focused sideboard build failed on the new no-fake-loadout API, budget partitions, `IsComplete`, and explicit `Reset` until production behavior existed.
- GREEN: strict shape/main-tier/reserve-policy/locked-active fixtures are now checked against the same trusted server admission policies; malformed panels are visible readonly and cannot begin submission.
- GREEN: `Locked -> complete Progress` remains visible readonly with all four seats confirmed; a real ordered `RoundStart` publishes exactly one reset, and proxy cleanup prevents later callbacks.
- Fresh focused sideboard regression: exit 0, `Network regression tests passed.`
- Fresh full network regression: exit 0, `Network regression tests passed.`
- UXML parse, unique new policy meta GUID, source guards, ignored Unity csproj guard, and `git diff --check`: exit 0.

### Fix Round 1 concerns

- Unity/Tuanjie Editor batch execution remains unavailable; presentation changes are covered by pure policies, ordered proxy integration, source guards, and parsed UXML. Unity project refresh remains Task 12.
- The pre-existing untracked `.superpowers/brainstorm/` directory remains untouched and is excluded from the commit.
