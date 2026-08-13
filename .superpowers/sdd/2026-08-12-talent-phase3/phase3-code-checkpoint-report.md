# Phase 3 Code Checkpoint Report

Date: 2026-08-14 (Asia/Shanghai)  
Status: **DONE_WITH_CONCERNS — stop before master**  
Baseline: `8658923`  
Candidate branch: `codex/talent-actions-ui-unified`  
Candidate HEAD: `bca8c54d8177f9860457601033d412cc5e2c5d7f`

This is the Task 12 code checkpoint only. No merge, push, master switch, Dedicated Server build, or real-player acceptance was performed.

## Blocking review finding

**Important — the Sideboard UI assets have invalid committed meta GUIDs, so Tuanjie ignores the assets and `GameHUD.uxml` fails semantic import.**

- `Assets/UI/SideboardPanel.uxml.meta` contains:
  `guid: t8bD64f7O7KqznzMHXmHWD79Tc7mV5cY+oTVsYfW9TsnPjFsj5ra5w=`
- `Assets/UI/SideboardPanelStyles.uss.meta` contains:
  `guid: 3SG2dfVYiPmA8VOXMEEmCnjn9DZ3yiNFjfF/JOeoUWFIicm8dMqbUA=`
- Both values are 55-character Base64-style values rather than valid Unity GUIDs. The files in the working tree are byte-for-byte represented by the same content at HEAD, so this is committed branch state rather than a local mutation.
- Tuanjie refresh log, lines 320–323, reports that neither GUID can be parsed and that both corresponding assets will be ignored.
- The same log then reports:
  - line 668: `GameHUD.uxml` has invalid dependency `00000000000000000000000000000000`;
  - line 673: the Sideboard URI refers to an invalid asset;
  - line 686: unknown template name `SideboardPanel`.

This blocks claiming that GameHUD/Sideboard instantiate successfully. Per the checkpoint brief, no unreviewed repair was made. The accepted finding must receive a RED regression, minimal fix, focused/full verification, and its own commit before this candidate can be treated as checkpoint-clean.

## Task 12 metadata repair (2026-08-14)

The accepted Sideboard metadata repair was completed after this checkpoint report's original stop point.

- A new `NetworkRegression` `unity-asset-meta` focused guard enumerates the Phase 3 UXML/USS assets. It requires every `.meta` GUID to be either a conventional 32-character lowercase hexadecimal Unity GUID or the 56-character Base64 GUID form emitted by Tuanjie, rejects the known-invalid 55-character truncated form explicitly, and rejects duplicate GUIDs within that asset set.
- The guard was first run against the original files and failed only for `SideboardPanel.uxml.meta` and `SideboardPanelStyles.uss.meta`, reproducing the importer defect before any repair.
- The two invalid values were replaced with fresh repository-unique 32-character hexadecimal GUIDs (`8adf1efb86fa42439fe14cc4870db222` and `78cf14e3a51e4fc8b12d213256d32640`). No serialized reference used the prior values: `GameHUD.uxml` imports the Sideboard template by `project://database/Assets/UI/...` path.
- Tuanjie batch refresh (`task12-unity-refresh-round1.log`) exited 0. It imported `SideboardPanelStyles.uss`, `SideboardPanel.uxml`, and `GameHUD.uxml` with the repaired GUIDs. The log contains none of the former invalid-GUID, invalid-Sideboard-asset, unknown-template, C# compile-error, or compilation-failed diagnostics.
- The original refresh's authoritative `TalentChipTemplate.uss.meta` normalization is intentionally retained: its GUID uses Tuanjie's valid 56-character form and its ScriptedImporter references fileID `12385` with `disableValidation: 0`.
- Fresh focused and full `NetworkRegression` runs passed; its build passed with 0 warnings and 0 errors. Post-refresh `dotnet restore Assembly-CSharp.csproj` and `dotnet build Assembly-CSharp.csproj --no-restore` exited 0 with 0 errors and the existing six package warnings (four Test Framework CS0618, two Timeline UNT0006).
- A repository-wide `Assets/**/*.meta` GUID duplicate scan returned no duplicates; a search for both obsolete Sideboard GUID values and the former TalentChip 32-hex GUID returned no in-project references.

The required visual/manual GameHUD matrix remains separate follow-up work; this repair verifies the import barrier only.

## Candidate commits (`8658923..HEAD`, oldest first)

```text
2498c16 docs: plan phase 3 layered talent UI
4b7b273 feat: bind alienation presets to saved loadouts
d650997 feat: add per-deck alienation UI
0452e1e fix: correct deck editor duplicate presentation
e8fd580 feat: attribute talent fan contributions
719b26f feat: mark applied active talent effects
54e478b feat: define layered talent feedback policies
a17c7b9 fix: harden talent feedback presentation
6f67014 feat: add layered talent HUD feedback
99984c4 fix: reset transient talent feedback on recovery
a4fafa0 feat: integrate talent action controls
aa1aa4e fix: restore talent actions after scene recovery
8a8992e fix: publish recovery talent actions once
35299d8 feat: add halftime tactical sideboard UI
356819d fix: harden sideboard recovery and validation
c459262 feat: explain itemized talent fan results
5a2c459 fix: keep recovered final results immediate
204e4cc fix: compile result presentation policies in regression
b750a73 feat: add deterministic AI talent strategy
7080a9b fix: ignore inactive AI sideboard threats
a8298e8 test: isolate active AI sideboard threat
b90728d feat: add talent playtest telemetry
bca8c54 test: exercise real game server telemetry
```

## Automated regression and build evidence

### NetworkRegression

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

Exit 0; output: `Network regression tests passed.`

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet build Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

Exit 0; build succeeded with 0 warnings and 0 errors.

### Real GameServer telemetry regression

HEAD and the working tree both contain:

- `Tests/GameServerTelemetryRegression/GameServerTelemetryRegression.csproj`
- `Tests/GameServerTelemetryRegression/Program.cs`
- `Tests/GameServerTelemetryRegression/.gitignore`

The initial broad `rg --files` lookup did not show the project because that test directory's local ignore rules hid the `.csproj`; `git ls-tree` and a direct filesystem check confirmed that it was committed and present.

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet build Tests/GameServerTelemetryRegression/GameServerTelemetryRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/GameServerTelemetryRegression/GameServerTelemetryRegression.csproj --no-build --no-restore"
```

Both exited 0. Build result: 0 warnings, 0 errors. Run output: `Real GameServer telemetry regression tests passed.`

## Source and diff guards

The following were run against candidate HEAD:

```powershell
rg -n 'talentId\s*==|switch\s*\(.*talentId' Assets/Scripts/Core/Network Assets/Scripts/Talent
rg -n 'Canvas|UnityEngine\.UI\.' Assets/UI Assets/Scripts/Core
rg -n 'MSYH\.TTC|MSYH_SDF' Assets/UI
git diff --check 8658923..HEAD
git status --short
```

Results:

- all three `rg` guards returned no matches;
- `git diff --check 8658923..HEAD` exited 0;
- no generated/build/temp artifacts appeared in Git status;
- `.superpowers/brainstorm/` remains an untouched, untracked user/early-prototype directory and is excluded from checkpoint work.

`Assets/Scripts/Core/Network/GameServer.cs` appears as `.M` in porcelain status even though `git diff` is empty and its worktree hash equals the HEAD blob hash (`5448908ec123ae4ea4eb7ebcf9613011dcc3f0e7`). It was not checked out, reset, or modified by this checkpoint.

## Unity/Tuanjie refresh and Assembly-CSharp

Project version:

```text
Unity 2022.3.61t9
Tuanjie 1.6.8
revision 2e9db1b79d2d
```

Installed editor used:

```text
D:\unity\2022.3.61t9\Editor\Tuanjie.exe
```

No Unity/Tuanjie process or `Temp/UnityLockfile` existed before the first launch. A sandboxed launch stalled before log/lock creation because the editor could not reach its normal user/license environment; the checkpoint-owned headless PID was validated by executable path/start time and terminated before retry. The required batch refresh was then run with the normal license/user environment and a workspace-local log:

```powershell
D:\unity\2022.3.61t9\Editor\Tuanjie.exe `
  -projectPath D:\UnityPrj\SuperMajiang `
  -batchmode -quit `
  -logFile D:\UnityPrj\SuperMajiang\.superpowers\sdd\2026-08-12-talent-phase3\task12-unity-refresh.log
```

The editor completed AssetDatabase initial refresh, compiled scripts, reloaded assemblies, and ended with:

```text
Exiting batchmode successfully now!
Exiting without the bug reporter. Application will terminate with return code 0
```

`Library/ScriptAssemblies/Assembly-CSharp.dll` and the generated `Assembly-CSharp.csproj` were refreshed. All 16 C# files added in `8658923..HEAD` are present in the regenerated project; no temporary source inclusion was needed. This resolves the earlier Task 6 concern about stale generated-project contents/line endings.

The batch log contains no `error CS...`, `Scripts have compiler errors`, or compilation-failed marker. It does contain the Sideboard UXML/meta import failure documented above. Early licensing-client signature/access-token messages were followed by successful entitlement resolution and do not change the editor's final return code.

The first post-refresh build required by the brief:

```powershell
dotnet build Assembly-CSharp.csproj --no-restore
```

stopped before compilation with NETSDK1004 because refresh had regenerated the project and `Temp/obj/Assembly-CSharp/project.assets.json` did not yet exist. The documented prerequisite from `docs/network_verification.md` was then run. In the filesystem sandbox, restore first failed solely because `%APPDATA%\NuGet\NuGet.Config` was access-denied; it was rerun with normal user-config access:

```powershell
dotnet restore Assembly-CSharp.csproj
dotnet build Assembly-CSharp.csproj --no-restore
```

Restore succeeded. The fresh no-restore build then exited 0 with **0 errors and 6 package warnings**:

- four CS0618 warnings from `com.unity.test-framework@1.1.33` for obsolete Lumin/UWP APIs;
- two UNT0006 warnings from `com.unity.timeline@1.7.7` for package `OnSceneGUI` signatures.

No candidate source compile errors were reported.

## Unity smoke evidence

There is no existing Editor smoke harness or Unity test assembly for the requested visual/interactive matrix. No temporary committed harness was added because this checkpoint may change source only for an accepted RED/GREEN review fix.

### Machine-observed

- **ActionPanel**: `Assets/UI/ActionPanel.uxml` and `ActionPanelStyles.uss` imported successfully in the Tuanjie log without an independent importer error.
- **ResultPanel**: `Assets/UI/ResultPanel.uxml` and `ResultPanelStyles.uss` imported successfully without an independent importer error.
- **GameHUD/Sideboard**: failed actual Unity import for the reasons in the blocking finding. Therefore panel instantiation is not accepted at either resolution.
- **WAV source**: `talent_active_generic.wav` is a valid RIFF/WAVE PCM file: format 1, stereo, 48,000 Hz, 16-bit, 134,444 bytes. Unity imported it successfully using GUID `eb50fc0dc9224ba8b782c22acfe0fa91` with no audio import error. Its importer preloads the data and the scene serializes the same GUID into the HUD clip field. The scene AudioSource is non-spatial, `m_PlayOnAwake: 0`, and `Loop: 0`.
- **One-shot request policy**: the fresh NetworkRegression run covered that one accepted strong event requests audio, a duplicate requests none, blocked/rejected/recovery paths request none, and the HUD uses `PlayOneShot`. Audible playback itself was not observed in batch mode.
- **Fonts**: UI styles reference `Assets/Font/MSYH_UITK.asset`; the forbidden TTC/TMP-font guard had no matches. Shared scenes reference `PanelSettings.asset`, whose TextSettings reference is `SuperMajiangTextSettings.asset`. Refresh logged no independent font or PanelSettings import error. Visible Chinese glyph rendering was not observable in batch mode.
- **Deck preset and over-cap semantics**: fresh NetworkRegression covered legacy preset migration, preset preservation/normalization, separate create/join preset mismatch versus over-cap errors, over-cap decks remaining saveable, and the required deck-editor query/source bindings. This is behavioral/static coverage, not a human click-through.
- All scanned UI UXML remains XML-well-formed at source level; that does not override the actual GameHUD semantic-import failure.

### Still requires visible manual smoke after the Sideboard meta fix

- open deck editor, cycle the deck preset, save a 34-tile over-cap deck, and visually confirm the warning;
- instantiate GameHUD, ActionPanel, Sideboard, and ResultPanel at 16:9 and the project's narrowest supported resolution, checking all runtime queries and layout;
- confirm `MSYH_UITK.asset` renders the required Chinese text;
- trigger the HUD test path and audibly confirm the generic WAV plays once.

## Deferred verification and stop point

- Independent whole-branch Critical/Important review is owned by the parent checkpoint workflow and is not duplicated by this report.
- Dedicated Server build/run, WebSocket loopback, multiplayer matrix, multi-round real-client behavior, reconnect matrix, and real-player acceptance remain deferred exactly as required.
- No merge, push, branch switch, or report commit was performed.
- Stop here for Phase 3 review. Do not merge to master until the Sideboard meta/UXML import finding is fixed through the required review-fix workflow and the visible Unity smoke matrix is completed.
