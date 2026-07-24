# Phase E Reconnect + Robustness Architecture

## Status

Design approved on 2026-07-17. Implementation is split into E1-E4 and must pass a review checkpoint after each step.

## Goals

- Recover from short network interruptions and same-device client restarts without pausing the table.
- Preserve server authority and Phase D loadout/talent privacy.
- Let AI temporarily control disconnected human seats at safe decision boundaries.
- Restore clients from an authoritative snapshot instead of replaying UI animations as game truth.

## Fixed Defaults

| Setting | Value |
| --- | --- |
| Client heartbeat interval | 3 seconds |
| Client/server liveness timeout | 10 seconds |
| Offline seat retention | 120 seconds |
| Per-human-seat message cache | 256 envelopes |
| Maximum client message size | 64 KiB |
| Reconnect retry delays | 0, 1, 2, 4, 8, 10 seconds, then every 10 seconds |

The server exposes `--reconnectWindowSeconds`, `--messageCacheSize`, and `--heartbeatTimeoutSeconds`; the values above are the defaults.

## Identity Boundary

Phase E does not implement a production account service. The development identity provider treats the normalized username as the temporary credential:

- Trim leading and trailing whitespace.
- Accept 1-32 characters.
- Compare active identities with `StringComparer.OrdinalIgnoreCase`.
- Preserve the accepted spelling for display.
- Reject a new connection with `IdentityInUse` while the same username is still online.
- Allow the same username to reclaim an offline reserved seat before it expires.

The connection/room layers use an abstract stable `playerId` supplied by `IAccountAuthenticator`. The development provider derives it from the normalized username. A production provider can later replace this source without changing room ownership, reconnect, snapshot, or message-stream contracts.

Username authentication is intentionally insecure and must not be described or deployed as production authentication.

## Architectural Boundaries

### Physical Connection

`ConnectionRegistry` owns WebSocket connection records, the authenticated player identity, connection generation, activity time, and the current endpoint. A stale endpoint or event whose generation no longer matches the active record cannot submit messages.

### Logical Seat

`RoomSeat` owns the stable player identity, room membership, loadout, readiness, control state, offline expiry, and a per-seat outbound stream. It must not treat a `GameEndpoint` as the seat identity.

Seat control states are:

- `Vacant`
- `OnlineHuman`
- `OfflineReserved`
- `AiControlled`
- `PermanentAi`

`isAi` continues to mean a permanent AI occupant. Temporary control is exposed separately so clients do not mistake a disconnected human for a vacant or permanent-AI seat.

### Per-Seat Message Stream

Every seat ever owned by a human has one logical `streamId` and one `SeatMessageStream` for the room lifetime:

- Server sequence numbers start at 1 and increase across endpoint replacement.
- The stream stores the last 256 serialized envelopes.
- Room broadcasts are serialized separately into each seat stream.
- Private hand, talent, and `PeekWall` payloads are written only to the owning seat stream.
- A live endpoint receives the same envelope that is placed in the cache.

The client applies envelopes through one sequencer in `ClientRoomService`. Duplicate sequences are ignored. A gap disables room input and starts resynchronization.

### Authoritative Snapshot

`RoomGameSnapshot` is generated from `Room`, `GameSession`, and `GameServer` state. It contains:

- Room state and seat occupancy/online/control information.
- Round number, prevalent/seat winds, dealer index, and scores.
- The requesting seat's complete concealed hand and melds.
- Opponent concealed counts and public melds only. In MCR, a declared concealed kong is a public meld and includes its four tile faces; only the rest of an opponent's concealed hand remains hidden.
- All public rivers and remaining wall count.
- The requesting seat's scoring options and private peek result.
- The active decision, eligible/submitted seats, controller, and absolute deadline.
- The current or most recent round/session result.

It never contains another player's complete hand, deck, or talent configuration.

`ClientGameState` is a pure C# projector. It applies ordered envelopes idempotently and atomically replaces itself from a full snapshot. Unity presentation rebuilds from this state; it does not infer authoritative state from existing GameObjects.

### Decision Boundary

Each room owns a monotonically increasing `decisionId` across the session. A decision records its phase, acting/discarding seat, target tile, eligible and submitted seats, controller, and Unix-millisecond deadline.

Network actions include `decisionId`. `Room` rejects stale, duplicate, wrong-phase, or wrong-controller actions before forwarding them to `GameServer`.

The controller is latched when a decision starts:

- Disconnecting does not replace an already running human decision.
- Reconnection before its deadline may resume that human decision.
- After the deadline, existing authoritative fallback resolves it.
- Subsequent decisions may be latched to AI.
- A human returning during an AI decision observes until the next safe decision boundary.

## Reconnect Protocol

Protocol v2 adds:

- `Hello { protocolVersion, username }`
- `HelloAccepted { protocolVersion, playerId, displayName }`
- `Reconnect { roomId, streamId, lastSeq, hasProjection }`
- `Resync { roomId, streamId, lastSeq }`
- `ReconnectState { baselineSeq, snapshot, missedMessages }`
- `ReconnectRejected { code, message }`
- `HeartbeatAck`

The client never supplies a trusted seat index. The server finds the seat through the authenticated `playerId`, room ID, and non-secret stream lineage. While an `OfflineReserved` seat exists, that identity must reclaim it through `Reconnect`; the server rejects ordinary Create/Join requests from the same identity until the seat expires.

The current client deliberately sends `hasProjection=false` for every `Reconnect`, so recovery always starts from a fresh authoritative snapshot and baseline. The server retains contiguous cached replay support for `Resync` and future complete projections. While building a recovery response, the stream pauses delivery to that endpoint and flushes newer envelopes afterward in sequence order.

The client persists only `{ serverAddress, username, roomId, streamId }`. It does not persist a room recovery token. After login, it automatically starts recovery only when the entered development username normalizes to the ticket's same `playerId`; another username remains in the lobby. Process-local projection and `lastSeq` are retained only while the process lives.

## Room Lifecycle Rules

| Room stage | Disconnect behavior |
| --- | --- |
| Waiting for players/match ready | Reserve loadout and Ready state for 120 seconds; do not auto-ready an unready player; release to a vacant seat on expiry. |
| Loading game scene | If another human remains online, mark the seat scene-ready and allow temporary AI control. |
| In round | Preserve the current decision owner until its deadline; latch later decisions to AI as needed. |
| Waiting for next round | Temporarily controlled offline seats auto-ready and the session may continue. |
| Session completed | Allow result recovery and leave only; reject Ready and Action. |

Additional invariants:

- `aiFill=false` controls empty-seat fill before the match; it does not disable emergency control for a locked human seat.
- Explicit `LeaveRoom` immediately abandons reconnect rights. It releases a pre-match seat or converts an in-session seat to permanent AI.
- If no human remains online in any room state, close the room immediately. A 1-human + 3-AI room therefore cannot recover after that human disconnects.
- When 120 seconds expire, a pre-match seat becomes vacant and an in-session seat becomes permanent AI.
- Dedicated Server restart does not restore rooms. `RoomNotFound` and `SeatExpired` clear the client ticket and return safely to login/lobby.
- Final session results use a new `SessionCompleted` room state instead of closing immediately while humans remain online.

## Client Experience

A UI Toolkit reconnect overlay disables gameplay input and shows connecting, resynchronizing, restored, or terminal-failure state. It retries with the fixed backoff and offers an explicit leave action.

On successful restoration, the presenter clears stale animations, prompts, and timers; rebuilds hands, melds, rivers, HUD, and result UI; then restores an actionable prompt only if the local human owns the current unexpired decision.

At application startup, a saved room ticket triggers `Hello` and `Reconnect`. Waiting-room snapshots route to the lobby room view. Loading, in-round, between-round, and completed snapshots route to the game scene. Terminal recovery failure clears the ticket and leaves the user on login or returns an already-running client to the lobby.

## Out of Scope

- Production accounts, passwords, access tokens, bans, or cross-device identity.
- Persistent room recovery after Dedicated Server restart.
- WSS certificates and reverse-proxy deployment.
- Mid-session loadout changes.
- Changes to server-authoritative scoring, talent execution, or the current rob-win rule.
