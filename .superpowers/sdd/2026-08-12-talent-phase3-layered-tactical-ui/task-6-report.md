# Phase 3 Task 6 Report — Layered talent HUD and generic active sound

## Scope and implementation

- Added a UI Toolkit-only B-density talent HUD to the existing `GameHUD`: the own active row sits above the hand-safe region; opponent summaries sit at their seat edges; the event feed is side-aligned; the strong-feedback toast uses the central safe region.
- Added the reusable `TalentChipTemplate.uxml/.uss` with `NameLabel`, `ValueLabel`, and the authority-safe `ConsumedMarker` placeholder. State and polarity colors live only in USS (`active`, `inactive`, `known`, `consumed`, `positive`, `negative`); the controller does not infer consumed state from a numeric value.
- Extended Task 5's privacy-preserving projection with own-private collapsed items and authorized-known opponent expanded items. Opponent summaries remain limited to two plus `+N`; opponent active state is never projected or bound.
- Added table-click and new-decision drawer closing, four-row feed retention, linked chip/toast pulses, schedule cancellation, tween cleanup, and proxy event subscription/unsubscription.
- `ApplyRecoverySnapshot` clears transient drawers/events and rebuilds only chips/action-adjacent state. It does not append feed rows, show toast, pulse chips, or play sound.
- Added `TalentFeedbackHistory.TryBuild` as the single event acceptance gate. Null/button/reject input, blocked medium feedback, duplicates, and recovery produce zero play requests. Only accepted `active_talent_applied` strong feedback reaches `PlayOneShot`.
- Added serialized `_talentChipTemplate`, `_genericActiveTalentClip`, and `_talentAudioSource` fields. `03_Game.unity` binds the real generated clip and an `AudioSource` with `Spatialize: 0` and `m_PlayOnAwake: 0`; no `Resources.Load` path exists.

## RED → GREEN

### RED 1 — feedback consumption boundary

Focused command:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-presentation"
```

Expected failure: five `CS1061` errors because `TalentFeedbackHistory.TryBuild` did not exist. The minimal implementation combines recovery suppression, policy mapping, and event-ID acceptance without adding a UI dependency.

### RED 2 — drawer-safe projections

The next focused run failed with five `CS1061` errors because `TalentHudView.OwnCollapsed` and `TalentSeatSummary.Expanded` did not exist. The minimal policy extension returns only own private inactive entries and server-authorized known opponent entries. It does not infer hidden opponent loadout size or active state.

### RED 3 — artifacts

After the pure policy changes, focused regression failed only:

```text
layered talent HUD source and UI assets exist
deterministic talent placeholder generator and WAV exist
```

After adding UXML/USS/controller/proxy/scene/audio assets and the standalone HUD stub surface, the focused command printed `Network regression tests passed.`

## Layout, event flow, and lifecycle

- Persistent own chips render from `TalentHudView.OwnVisible`; the own `+N` drawer uses `OwnCollapsed` from the requesting seat's private snapshot.
- Each opponent row renders `TalentSeatSummary.Visible` (maximum two) and its `+N` drawer renders `Expanded`, both derived solely from `SnapshotKnownTalent.isKnown`. `ShowActiveState` is gated by `isOwn` in the chip binding.
- `RemoteServerProxy` binds/unbinds the current HUD and closes drawers at new main/response decision boundaries. Its existing ordered `TalentRuntimeEventReceived` event drives the HUD.
- Accepted public events are retained only for deterministic authorized recency ordering. `TalentFeedbackHistory` rejects null, non-positive, duplicate, and lower event IDs.
- Strong feedback updates chips, pulses the affected chip, appends one feed row, shows toast, and plays the generic clip. Medium feedback updates/pulses chips and appends feed with no toast/audio. Weak feedback only rebuilds values.
- `OnDestroy` unsubscribes both proxy events, pauses the toast schedule, and kills all three tweens. Static verification counted `DOVirtual` / `.SetLink(gameObject)` as `3/3`.

## Deterministic WAV

`Tools/GenerateTalentPlaceholderAudio.ps1` uses PowerShell 7 and `.NET BinaryWriter` only. It writes 48,000 Hz, 16-bit stereo PCM for exactly 0.70 seconds, with a fixed-seed xorshift filtered/noisy click during the first 80 ms, a 620→980 Hz glide with exponential decay, and a final 30 ms fade.

The committed asset and two independent temporary generations all produced:

```text
SHA256: 3CDE4C85FF1CA03AF255E3F79097B4CD0E080F535C1733722B75D8D448939EB3
ByteIdentical: True
Length: 134444 bytes
Peak: 29203 (-1.000184 dBFS)
```

The regression WAV reader verified `RIFF/WAVE`, PCM format 1, 48,000 Hz, 2 channels, 16 bits, block align 4, consistent RIFF/data sizes, and duration within 0.60–0.80 seconds.

## Scene and asset evidence

- Historical GameHUD controller/UXML scene GUID strings were not guessed or replaced. `git show d774b39` proved the long-form meta GUIDs and 32-hex scene references were created in the same original commit; without Unity import evidence, Task 6 preserved them.
- Task 6 added only the new template/clip/source bindings. The clip meta GUID is `eb50fc0dc9224ba8b782c22acfe0fa91`; the scene binds fileID `8300000`. The UI source binds the template fileID `9197481963319205126` and its new meta GUID.
- A guard checked all seven new asset/meta pairs and found exactly one unique GUID in each.

## Verification

Fresh focused and full commands:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-presentation"
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Both test commands printed `Network regression tests passed.` `git diff --check` exited successfully; Git emitted only line-ending conversion notices.

The stale ignored `Assembly-CSharp.csproj` was missing thirteen source items accumulated across prior Phase work. After temporarily adding those compile items, `dotnet build Assembly-CSharp.csproj --no-restore --nologo` succeeded with `0 Warning(s), 0 Error(s)`. Every temporary `<Compile>` item was then removed and the ignored file remained outside staging.

Unity/Tuanjie executable discovery found `D:\unity\2022.3.61t9\Editor\Tuanjie.exe`, but no batch import/scene compile was run during this task; no Unity batch success is claimed.

## Self-review

- No Canvas/UGUI, network access, external audio package, `Resources.Load`, or Task 7+ action/sideboard/result visual work was added.
- No server-provided event rich text is rendered; display names and feedback copy remain local/registry-backed.
- Opponent active state and hidden talent cardinality remain unavailable to the view.
- Missing clip/source/template references warn once and preserve HUD behavior.
- The template instance retains its `TemplateContainer`, so the referenced template stylesheet remains attached.
- `.superpowers/brainstorm/` remains untouched and unstaged. `Tools/` and `Assets/Audio/` contain only Task 6 artifacts.

## Commit

Initial Task 6 implementation: `6f6701472d3969e37c62d5782dd7a217583573e7`.

Fix Round 1 commit message: `fix: reset transient talent feedback on recovery`. Its final SHA is reported in the parent handoff (a commit cannot contain its own stable SHA).

## Concerns

- `Assembly-CSharp.csproj` is an ignored Unity-generated file. The temporary compile entries were removed exactly and it is not staged, but `apply_patch` may have changed local line endings in the touched area; no pre-edit byte copy existed, so byte-for-byte restoration was not guessed. Task 12 should let Unity Refresh regenerate this ignored project file authoritatively and then run Unity compilation.
- Actual layout and serialized reference import still require Unity/Tuanjie scene validation in Task 12; current evidence is source compilation plus regression/static/YAML verification.

## Fix Round 1 — recovery transient lifecycle, click teardown, and automated WAV regeneration

### Review findings verified

- `ResetForRecovery` previously stopped only the round timer/pulse; the four-row talent feed, visible toast, pending toast schedule, chip/toast tweens, and open drawer overlay could survive an authoritative recovery snapshot.
- `BindTalentElements` previously attached own/four-seat/dismiss click handlers using callbacks that were not all removable; `OnDestroy` did not detach those UI Toolkit `clicked` subscriptions.
- `RunTalentAudioAssetTests` previously inspected only the committed WAV. The two independent generations and byte comparison reported above were a manual verification, not an automated regression guard.

### RED → GREEN evidence

1. Recovery lifecycle RED added an executable pure `TalentTransientPresentationState` test covering a live strong event, atomic recovery reset, and a later higher-ID live event. Focused compilation failed as expected with `CS0246` because the state boundary did not exist. The minimal GREEN added the pure lifecycle model and connected `GameHUDController.ResetForRecovery` to `ResetTalentFeedbackForRecovery`; focused regression passed.
2. Click teardown RED added source assertions for stored own/four-seat/dismiss callbacks, matching `clicked -=` operations, nulling callback references, and the absence of anonymous `clicked += () =>` subscriptions. Focused regression failed only that new assertion. The GREEN stores `Action` references, calls `UnbindTalentElementCallbacks` first from `OnDestroy`, removes all six subscriptions, and nulls every callback, so repeated teardown is safe; focused regression passed.
3. WAV automation RED called a missing `RunTalentAudioGeneratorTwice` helper and failed as expected with `CS0103`. The GREEN launches PowerShell 7 twice with `ProcessStartInfo.ArgumentList` using `pwsh -NoLogo -NoProfile -File <generator> -OutputPath <explicit temp WAV>`, captures clear timeout/start/exit diagnostics, reads both files, and removes the GUID-scoped temporary directory in `finally`; focused regression passed.

### Recovery and lifecycle implementation

- Recovery pauses and nulls the pending toast schedule; kills/nulls only talent chip/toast tweens; restores the currently pulsed talent chip scale to `Vector2.one`; hides the toast, clears its text and opacity; clears the feed; closes all drawers and the dismiss overlay; and resets the pure transient lifecycle state.
- The unrelated round timer pulse remains on its existing `_pulseTween` recovery path. Talent cleanup does not kill unrelated animation state.
- Recovery clears accepted public presentation ordering only after transient cleanup and renders the authoritative snapshot directly. It does not call `TryBuild`, seed history, replay feed/toast/audio, or infer feedback from snapshot history.
- The pure boundary verifies a subsequent ordered live event (`eventId = 2`) is accepted and repopulates feed/toast/schedule/chip/toast channels normally after recovery.
- `OnDestroy` retains proxy event unsubscription, toast schedule pause/null, and linked tween cleanup. Own `+N`, four seat `+N`, and dismiss callbacks are also detached with null-safe stored delegates; calling the teardown path again is safe.

### Automated audio and final verification

The focused and full commands both completed with `Network regression tests passed.` after the final implementation. Each invocation now actually runs the generator twice in a temporary directory and checks:

```text
generated-a bytes == generated-b bytes == committed WAV bytes
SHA256 == 3CDE4C85FF1CA03AF255E3F79097B4CD0E080F535C1733722B75D8D448939EB3
RIFF/WAVE PCM, 48,000 Hz, stereo, 16-bit, 0.70 s, peak 29203 (<= -1 dBFS)
```

Static guards found `DOVirtual=3` and `.SetLink(gameObject)=3`; no anonymous talent `.clicked += () =>` remains. The scene still binds audio meta GUID `eb50fc0dc9224ba8b782c22acfe0fa91` at clip fileID `8300000`, with `m_PlayOnAwake: 0` and `Spatialize: 0`. `git diff --check` passed. Fix Round 1 did not touch `Assembly-CSharp.csproj`, any scene/audio/meta asset, Task 7+ UI, or `.superpowers/brainstorm/`.
