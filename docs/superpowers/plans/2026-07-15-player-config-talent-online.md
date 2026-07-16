# Player Config + Talent Online Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upload, validate, lock, and server-authoritatively execute each network player’s deck and talent configuration during a room session.

**Architecture:** `PlayerLoadoutMessage` is an explicit JSON-safe DTO whose codec validates every fixed tile type and reconstructs fresh trusted `DeckConfig` and `TalentSlotConfig` instances. `RoomSeat` owns that trusted snapshot through the session lock; `Room` derives every round’s wall and talent configuration from seats rather than recreating standard loadouts. Client room state exposes only the server-confirmed alienation summary and drives an in-lobby four-seat room view.

**Tech Stack:** Unity 2022 UI Toolkit, C#, JsonUtility DTOs, dedicated WebSocket server, NetworkRegression console tests.

## Global Constraints

- Keep the dedicated server independent from `03_Game`, UI, `GameManager`, and `DeckManager`.
- Use explicit DTO arrays; never serialize `DeckConfig`’s dictionary with `JsonUtility`.
- Validate all loadouts before creating a room, occupying a seat, or binding `ConnectionRegistry`.
- The server returns only `totalAlienation` to other clients; never broadcast another player’s deck or talents.
- Lock four seat configurations before `LoadingGameScene`; no in-room loadout update protocol is added.
- Preserve Phase C heartbeat, disconnect, ready, and room-close rules; do not implement Phase E reconnect, sequence recovery, or endpoint rebinding.
- Keep all room UI in `MainLobby` with UI Toolkit; do not edit scene YAML or introduce Canvas/UGUI.

---

### Task 1: Loadout DTO, codec, and regression harness

**Files:**
- Create: `Assets/Scripts/Core/Network/PlayerLoadoutCodec.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

**Interfaces:**
- Produces `PlayerLoadoutCodec.CreateMessage(DeckConfig, TalentSlotConfig)` for clients.
- Produces `PlayerLoadoutCodec.TryDecode(PlayerLoadoutMessage, out TrustedPlayerLoadout, out string errorCode)` for the server.
- `TrustedPlayerLoadout` owns fresh `DeckConfig`, `TalentSlotConfig`, and `TotalAlienation` values.

- [ ] Add tests for standard/custom round trips, 33/35 totals, negative counts, duplicate/missing/illegal entries, all-34-one-type configuration, empty talents, wrong slot count, unknown/duplicate/tier-invalid talent IDs, missing loadout, and unsupported schema version.
- [ ] Run `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj` and confirm the new assertions fail because the codec is absent.
- [ ] Add `DeckTileCountMessage`, `PlayerLoadoutMessage`, `CreateRoomMessage.loadout`, and `JoinRoomMessage.loadout`; enumerate fixed legal tile types in the codec, clone accepted data, and return only `MissingLoadout`, `UnsupportedLoadoutVersion`, `InvalidDeck`, or `InvalidTalent`.
- [ ] Re-run the regression executable and confirm all tests pass.

### Task 2: Authoritative room-seat loadout lifecycle

**Files:**
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Modify: `Assets/Scripts/Core/Network/RoomManager.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Create: `Assets/Scripts/Core/Network/SessionTalentPolicy.cs`
- Modify: `Assets/Scripts/Core/Agents/SimpleAIClient.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

**Interfaces:**
- `Room.TryAddHuman(..., TrustedPlayerLoadout loadout, out int seatIndex)` stores a trusted copy.
- `RoomSeatMessage.totalAlienation` is the only shared loadout summary.
- `SessionTalentPolicy.ApplyStartingCapitalOnce(GameSession, IReadOnlyDictionary<int, TalentSlotConfig>)` awards each `starting_capital` seat exactly once.

- [ ] Add regression tests for no room/seat/binding on invalid create/join requests, seat removal if `BindRoomSeat` fails, owner-specific four-seat wall construction, locked loadout reuse across EastOnly rounds, pre-lock AI standard reset, post-lock AI retention, and one-time starting capital.
- [ ] Run the regression executable and confirm it fails against Phase C behavior.
- [ ] Validate before room/seat mutation, roll back a successful `TryAddHuman` when binding fails, store trusted configurations and alienation on seats, make `RoomJoined` include accepted schema/alienation, send full authoritative replacement seat snapshots, lock/fill all seats before loading, derive every round from locked seats, and apply starting capital once per session.
- [ ] Cache `ScoringOptions` in `SimpleAIClient` and pass them to action/win checks so AI decisions match its inherited server talent summary.
- [ ] Re-run the regression executable and confirm all assertions pass.

### Task 3: Client loadout submission and room-state propagation

**Files:**
- Modify: `Assets/Scripts/Core/Network/ClientRoomService.cs`
- Modify: `Assets/Scripts/Core/Network/ClientRoomState.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Modify: `Tests/NetworkRegression/Program.cs`

**Interfaces:**
- `ClientRoomService.CreateRoom/JoinRoom` build a loadout from the selected profile deck, or an explicit standard/empty fallback when no saved deck exists.
- An invalid selected saved deck stops the request and raises a user-facing `RoomError` locally.
- Client state mirrors server-provided `RoomSeatMessage` instances without inferring AI, ready, or alienation values.

- [ ] Add assertions that joined state retains accepted alienation/schema data and `PlayerJoined`/`PlayerLeft` replace seats from authoritative messages.
- [ ] Run the regression executable and confirm the state assertions fail.
- [ ] Implement selected-deck snapshot construction, local validation failure, standard fallback only for absent saves, and authoritative seat message replacement; have `RemoteServerProxy` ignore all room message types without warning.
- [ ] Re-run the regression executable and confirm all assertions pass.

### Task 4: Dedicated MainLobby room view

**Files:**
- Modify: `Assets/UI/MainLobby.uxml`
- Modify: `Assets/UI/MainLobbyStyles.uss`
- Modify: `Assets/UI/LobbyController.cs`

**Interfaces:**
- `LobbyController` responds to `RoomJoined`, `SeatSnapshotChanged`, `RoomClosed`, and room errors by switching Home/Room views.
- `ViewRoom` shows four stable rows and local deck name/server-confirmed alienation; it does not display schema or opponents’ private loadouts.

- [ ] Add `ViewRoom` with top status, fixed four-row seat list, local build summary, explicit leave/ready actions, and a bottom status bar.
- [ ] Add restrained UI Toolkit styling matching the lobby theme, with no nested card stack or radius above 8px.
- [ ] Replace the Home inline waiting controls with room-view transitions; lock navigation/deck selection while in a room, disable Ready after submission, restore Home on leave/close/disconnect, and display the concrete closure reason.
- [ ] Build the C# project to validate UXML controller references and inspect the changed documents for names used by controller queries.

### Task 5: Documentation and full verification

**Files:**
- Modify: `docs/network_overhaul_master_plan.md`
- Modify: `docs/network_loopback_verification.md`
- Modify: `Tests/NetworkRegression/Program.cs`

- [ ] Extend the Phase D manual guide with room-view transitions, 1/2/4 player loadouts, seat alienation, server owner/tile composition, PeekWall targeting, representative talents, AI takeover, EastOnly starting capital, rejection/no-leak, and no-private-loadout-broadcast checks.
- [ ] Mark Phase D as “implemented, pending manual acceptance” and record automated verification, manual checks remaining, and the explicit Phase E exclusions.
- [ ] Run `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj`, `dotnet restore Assembly-CSharp.csproj`, and `dotnet build Assembly-CSharp.csproj --no-restore`.
- [ ] Inspect `git diff --check` and `git status --short`; report only files changed for Phase D and any verification limitations.
