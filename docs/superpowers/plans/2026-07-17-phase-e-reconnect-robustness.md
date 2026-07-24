# Phase E Reconnect + Robustness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:test-driven-development` for each checkpoint and `superpowers:verification-before-completion` before reporting it complete. Execute E1-E4 sequentially and stop for user acceptance after every checkpoint.

**Goal:** Add ordered room message streams, authoritative snapshot recovery, safe AI takeover, and same-device reconnect support without weakening server authority or Phase D privacy.

**Architecture:** Physical WebSocket connections authenticate into stable logical identities; human room seats own endpoint-independent message streams. `GameServer` exposes authoritative snapshot and decision state, while a pure client projector applies ordered deltas or a full snapshot before Unity presentation is rebuilt.

**Tech Stack:** Unity 2022/Tuanjie 1.6.8, C#, websocket-sharp, `JsonUtility` DTOs, UI Toolkit, Dedicated Server, NetworkRegression console tests.

## Global Constraints

- Read `docs/network_phase_e_reconnect_design.md` before editing code; it is the source of truth for decisions and defaults.
- Keep `GameServer` and Dedicated Server runtime independent from game scenes, UI, `GameManager`, and `DeckManager`.
- Keep single-player behavior working and preserve Phase D loadout/talent validation and privacy.
- Do not trust client seat/player IDs; resolve them from the authenticated connection and room binding.
- Username authentication is a development bridge only. Keep an `IAccountAuthenticator` boundary for future production identity.
- Use UI Toolkit only; do not add Canvas/UGUI or hand-edit Unity scene YAML.
- Do not implement a later checkpoint early. Each checkpoint ends with automated verification, a report, and user acceptance.

---

## E1: Identity, Connection, Sequence, and Liveness

### Task E1.1: Protocol v2 and identity policy

**Files:**

- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Create: `Assets/Scripts/Core/Network/IAccountAuthenticator.cs`
- Create: `Assets/Scripts/Core/Network/DevelopmentAccountAuthenticator.cs`
- Create: `Assets/Scripts/Core/Network/UsernameIdentityPolicy.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

**Produces:**

- `UsernameIdentityPolicy.TryNormalize(string, out string displayName, out string playerId, out string errorCode)`.
- `IAccountAuthenticator.TryAuthenticate(string username, out AuthenticatedIdentity identity, out string errorCode)`.
- `HelloAcceptedMessage`, protocol version 2, `HeartbeatAckMessage`, and stable protocol/identity errors.

- [x] Add failing tests for trim, empty/over-32 usernames, case-insensitive identity equality, protocol mismatch, and current online identity rejection.
- [x] Run `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore`; confirm the new policy/type assertions fail.
- [x] Add the policy, authenticator abstraction/development provider, and DTOs. Do not add passwords, account storage, or recovery tokens.
- [x] Re-run NetworkRegression and confirm the new policy tests pass.

### Task E1.2: Physical connection generations

**Files:**

- Modify: `Assets/Scripts/Core/Network/ConnectionRegistry.cs`
- Modify: `Assets/Scripts/Core/Network/RoomManager.cs`
- Modify: `Assets/Scripts/Core/Network/Transport/WebSocketService.cs`

**Produces:**

- A connection record containing endpoint, generation, authentication state, `playerId`, display name, activity time, room ID, and seat index.
- Validation that an incoming endpoint/generation is still the active physical connection.

- [x] Add failing pure-policy/registry tests for unauthenticated room commands, duplicate-online username, offline reclaim eligibility, and stale-generation rejection.
- [x] Make `Hello` the only pre-authentication room message; return `HelloAccepted` after authentication.
- [x] Reject all other pre-authentication messages with `AuthenticationRequired` and reject protocol mismatch without mutating room state.
- [x] Enforce the 64 KiB inbound text limit before deserializing an envelope.
- [x] Run NetworkRegression and `dotnet build Assembly-CSharp.csproj --no-restore`.

### Task E1.3: Per-seat ordered message stream

**Files:**

- Create: `Assets/Scripts/Core/Network/SeatMessageStream.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Modify: `Assets/Scripts/Core/Network/RoomManager.cs`
- Modify: `Assets/Scripts/Core/Network/RemotePlayerClient.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

**Produces:**

- `SeatMessageStream.Send(string type, object payload)` with room-lifetime sequence allocation.
- `SeatMessageStream.TryGetMessagesAfter(int lastSeq, out NetworkMessageEnvelope[] messages)`.
- `SeatMessageStream.RebindEndpoint(GameEndpoint endpoint)` without resetting sequence/cache.

- [x] Add failing tests for sequence 1..N, endpoint replacement continuity, 256-envelope eviction, replay cache boundaries, and isolation between two seat streams.
- [x] Implement a bounded serialized-envelope ring buffer with configurable capacity.
- [x] Route `Room.Broadcast`, joined/seat/ready messages, and `RemotePlayerClient` messages through the owning seat stream. Remove `RemotePlayerClient._seq` and room-bound `seq=0` sends.
- [x] Verify private `GameStart`, `TalentInfo`, and `PeekWall` payloads are never copied into another seat stream.
- [x] Run NetworkRegression and compile.

### Task E1.4: Client sequencing and bidirectional heartbeat

**Files:**

- Modify: `Assets/Scripts/Core/Network/ClientRoomService.cs`
- Modify: `Assets/Scripts/Core/Network/ClientRoomState.cs`
- Modify: `Assets/Scripts/Core/Network/Transport/WebSocketClient.cs`
- Modify: `Assets/Scripts/Core/Network/ConnectionLivenessPolicy.cs`
- Modify: `Assets/Scripts/Core/Network/ServerBootstrap.cs`
- Modify: `Tests/NetworkRegression/Program.cs`

**Produces:**

- One client-side sequence gate before room/game message dispatch.
- Duplicate ignore, contiguous accept, and gap/resync-required outcomes.
- Heartbeat acknowledgement and client watchdog state.
- Server options `ReconnectWindowSeconds`, `MessageCacheSize`, and `HeartbeatTimeoutSeconds` populated from command-line defaults 120/256/10.

- [x] Add failing tests for duplicate, contiguous, and gap outcomes plus the exact 10-second timeout boundary.
- [x] Apply all room-bound messages through the sequence gate; disable room command submission after a detected gap. E1 may expose a resync-required event but must not implement E3 reconnect/snapshot behavior.
- [x] Send heartbeat after Hello even outside a room, acknowledge it from the server, and disconnect/re-enter connection recovery state after 10 seconds without acknowledgement.
- [x] Parse the three new Dedicated Server arguments without changing existing `--port`, `--maxRooms`, or `--aiFill` behavior.
- [x] Run the E1 verification commands below and report results.

### E1 Checkpoint

> Status (2026-07-19): E1 accepted. E2 implementation is complete and awaiting acceptance. Phase E is not complete; E3-E4 remain unimplemented.

Run:

```powershell
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore
dotnet build Assembly-CSharp.csproj --no-restore
git diff --check
```

Expected:

- Network regression tests pass.
- Build completes with 0 errors.
- No whitespace errors.
- Create/join/Ready/single-round/EastOnly behavior remains usable.
- Every room-bound server envelope uses a positive continuous seat-stream sequence.

Report changed files, public contracts, verification output, Unity manual steps, and risks. Explicitly confirm E2-E4 were not implemented, then stop for acceptance.

---

## E2: Authoritative Snapshot and Decision Model

### Task E2.1: Complete server-visible table state

- Extend `ServerGameState` with per-seat rivers and immutable snapshot accessors.
- Update discard and claimed-discard transitions so hands, melds, and rivers remain mutually consistent.
- Add regression tests for discard, Chi/Pon/Kan consumption, claimed-river removal, and snapshot copy isolation.

### Task E2.2: Explicit network decision context

- Add session-wide monotonic `decisionId`, phase, eligible/submitted seats, controller, and Unix-millisecond deadline.
- Establish the decision before notifying clients and close it exactly once after action/timeout resolution.
- Add `decisionId` to network actions; reject stale, duplicate, wrong-phase, or wrong-controller actions in `Room`.
- Keep direct single-player actions compatible without trusting network payload player IDs.

### Task E2.3: Snapshot DTO and privacy builder

- Add `RoomGameSnapshot` DTOs and a per-requesting-seat builder.
- Include room/session/table/decision/result state and only the requesting seat's concealed/private information.
- Add tests proving another seat's hand, deck, talents, and peek result cannot appear.

### Task E2.4: Pure client projection

- Add `ClientGameState.ApplyEnvelope` and `ApplySnapshot` with idempotent sequence handling.
- Keep it independent from Unity scene objects and animations.
- Test duplicate application, atomic snapshot replacement, and result/decision restoration.

### E2 Checkpoint

> Status (2026-07-25): E2 accepted. E3-E4 were subsequently implemented and accepted.

Run the standard three verification commands, report snapshot/privacy and decision results, confirm E3-E4 are untouched, and stop for acceptance.

---

## E3: Rebind, Takeover, and Room Lifecycle

> Status (2026-07-25): E3 accepted. E4 was subsequently implemented and accepted.

### Task E3.1: Reconnect and resync protocol

- [x] Implement `Reconnect`, `Resync`, `ReconnectState`, and `ReconnectRejected`.
- [x] Resolve seats by authenticated `playerId`, room ID, and stream lineage; never trust seatIndex.
- [x] Keep server cached replay support, while the current client deliberately requests a full snapshot and baseline for every reconnect.
- [x] Pause endpoint delivery while composing recovery state, then flush newer messages in sequence.

### Task E3.2: Stable seat controller

- [x] Replace endpoint-bound `GameServer` clients with a stable seat router.
- [x] Latch human/AI ownership when each decision opens.
- [x] Preserve current human decisions until their deadline; route later offline decisions to `SimpleAIClient`; return control only at a later boundary.
- [x] Continue recording the human seat's outbound stream while AI controls it.

### Task E3.3: Offline retention and lifecycle

- [x] Implement the approved per-room-state behavior from the architecture document.
- [x] Apply temporary control regardless of `aiFill` once a human loadout is locked.
- [x] Expire pre-match seats to vacant and in-session seats to permanent AI after 120 seconds.
- [x] Treat explicit leave as immediate abandonment and close the room whenever no human remains online.
- [x] Add `SessionCompleted` and retain final results only while at least one human remains online.

### Task E3.4: Persisted client ticket

- [x] Persist `{ serverAddress, username, roomId, streamId }` only.
- [x] Keep process-local projection/lastSeq in memory.
- [x] Clear the ticket on leave, room close, `RoomNotFound`, `SeatExpired`, or final result exit.
- [x] After login, automatically begin `Reconnect` only when the entered development username matches the ticket's derived `playerId`.

### E3 Checkpoint

Run the standard commands plus focused loopback tests for cached replay, full snapshot, AI takeover, username reclaim, expiry, and all-humans-offline closure. Report and stop for acceptance.

---

## E4: Client Recovery Presentation and Integration

> 状态（2026-07-25）：E4 自动检查与 Unity 真人联机验收通过，Phase E 整体完成。

### Task E4.1: Single ordered client message path

- [x] Stop `RemoteServerProxy` from subscribing directly to WebSocket events.
- [x] Consume already ordered/projected game changes from `ClientRoomService`.
- [x] Ensure messages received during scene loading remain represented in `ClientGameState`.

### Task E4.2: Atomic table presenter

- [x] Rebuild local hand/melds/river, opponent counts/melds/rivers, HUD, wall count, winds, scores, and results from the projection.
- [x] Cancel old tweens, action callbacks, prompts, and timers before rebuilding.
- [x] Restore input only for an unexpired decision currently controlled by the local human.

### Task E4.3: Reconnect overlay and startup recovery

- [x] Add a UI Toolkit reconnect overlay with status, disabled gameplay input, retry progression, and explicit leave.
- [x] On startup, attempt `Hello` + `Reconnect` for a saved ticket and route the returned room state to lobby or game scene.
- [x] Handle terminal failure without scene overlap or stale room state.

### Task E4.4: Documentation and final verification

- [x] Add `docs/network_reconnect_verification.md` with the complete manual matrix.
- [x] Update the master plan and this checklist with implementation status and current limitations; do not mark Phase E complete before manual acceptance.
- [x] Run NetworkRegression, Assembly-CSharp build, `git diff --check`, Dedicated Server startup, and the full Unity matrix. 最终自动检查与 Unity 真人联机验收已于 2026-07-25 通过。

### Final Manual Matrix

- Two-human short disconnect with cached replay.
- Client process restart with full snapshot.
- Main-turn and response reconnect before/after deadlines.
- Loading, between-round, round-result, and final-result reconnect.
- `aiFill=false` four-human emergency takeover.
- Concurrent same-username rejection.
- One-human + three-AI disconnect closes immediately.
- All humans disconnect closes immediately.
- Dedicated Server restart returns clients safely.
- EastOnly four rounds without duplicate hand, meld, river, score, prompt, or action state.

Phase E was marked complete on 2026-07-25 after automated and manual acceptance passed.
