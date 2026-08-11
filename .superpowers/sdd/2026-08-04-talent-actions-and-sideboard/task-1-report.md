# Phase 2 Task 1 Report — Supplemental Talent Actions

## Implementation

- Added `NetworkDecisionTracker.TryValidateSupplementalAction`, sharing active-decision, ID, and deadline validation with normal actions while leaving `SubmittedSeats` unchanged.
- Added serializable talent action request/resolution wire DTOs and talent action domain models with stable rejection codes.
- Added polymorphic `TalentRule.TryActivate`, `TalentActivationContext`, and `TalentMatchRuntime.TryActivate`. Runtime locates only the requesting seat's active carried instance by talent ID, checks the declared activation window, binds that entry's state/event sink, and invokes the rule override. There are no stable-talent-ID effect branches.
- Added `GameServer.SubmitNetworkTalentAction` and `Room.SubmitTalentAction`. Supplemental actions validate the active decision without completing it, recompute runtime availability, execute the rule polymorphically, publish filtered runtime events, and send the owner-only `TalentActionResolved` result. They do not close the decision or fulfill `_pendingActionTcs`.
- No RoomManager/WebSocket parsing route, client-side projection, UI, or debug entry was added, per task scope.

## RED / GREEN evidence

### RED 1 — tracker contract

Command:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- talent-actions"
```

Result: expected build failure. `TalentActionTests.cs` reported five `CS1061` errors because `NetworkDecisionTracker` did not define `TryValidateSupplementalAction`.

### GREEN 1 — tracker contract

Command:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- talent-actions"
```

Result: exit 0, `Network regression tests passed.`

### RED 2 — polymorphic runtime contract

Command:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- talent-actions"
```

Result: expected build failure. `TalentActionTests.cs` reported `CS0246` for the missing `TalentActivationContext`, `TalentActionRequest`, and `TalentActionResult` action contract types.

### GREEN 2 — polymorphic runtime contract and final focused run

Command:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- talent-actions"
```

Result: exit 0, `Network regression tests passed.`

The focused runner selector is restricted to `talent-actions`; the default invocation continues to run every existing regression suite.

## Final verification

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore"
```

Result: exit 0, `Network regression tests passed.`

```powershell
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Result: exit 0. Git printed only CRLF conversion warnings for pre-existing tracked text files; no whitespace errors were reported.

## Changed files

- `Assets/Scripts/Talent/TalentActionModels.cs` and `.meta`
- `Assets/Scripts/Talent/TalentRule.cs`
- `Assets/Scripts/Talent/TalentContext.cs`
- `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- `Assets/Scripts/Core/Network/NetworkDecisionTracker.cs`
- `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- `Assets/Scripts/Core/Network/Room.cs`
- `Assets/Scripts/Core/Network/GameServer.cs`
- `Tests/NetworkRegression/TalentActionTests.cs`
- `Tests/NetworkRegression/NetworkRegression.csproj`
- `Tests/NetworkRegression/Program.cs`
- `Tests/NetworkRegression/UnityEngineStubs.cs` (required to keep the standalone room tests' GameServer stub API synchronized)

## Self-review

- Supplemental validation does not call `WithSubmittedSeat`; normal actions retain the only base-decision consumption path.
- Decision validation returns the requested stable errors for wrong controller, expired, stale, and wrong phase tests.
- Runtime activation is identity lookup plus a virtual rule call; Room and GameServer do not dispatch effects using talent IDs.
- The only owner-specific resolution message is sent via that seat's `SeatMessageStream`; talent runtime events retain their existing per-seat visibility filter.
- Full room-to-server-to-runtime live integration was intentionally not added or exercised, matching the stated Phase 2 boundary.

## Concerns

- The standalone NetworkRegression project stubs `GameServer`, so its compile-time coverage includes the Room-facing method signature but does not compile the real Unity `GameServer.cs`. The focused runtime/tracker contracts and the complete existing NetworkRegression suite passed; Unity editor/Dedicated Server compilation was not run because it was outside the task's requested verification commands.
