# Network Loopback Verification

## Current Local Endpoint

The current local WebSocket endpoint for SuperMajiang loopback testing is:

```text
ws://127.0.0.1:9876/game
```

Use this address when connecting a local Unity client to the Dedicated Server.

## 8080 Cache Note

Older cached verification notes may still mention port `8080`. That value is outdated for the current network baseline. Do not use `ws://127.0.0.1:8080/...` for current loopback verification.

Do not edit old cache documents under `C:/Users/fcrqq/.gemini/antigravity`; they are historical cache artifacts and are not the source of truth for this project.

## Minimal Verification Flow

1. In the Unity project, select `Tools > Build > Dedicated Server (Windows)`.
2. From the generated build output, start the server executable with `--port 9876 --maxRooms 1 --aiFill true`.
3. Confirm the Dedicated Server is listening for WebSocket clients.
4. Start the Unity client and confirm its `serverAddress` is `ws://127.0.0.1:9876/game`.
5. Connect the client, send Ready, and verify the current 1 remote player + 3 server AI loopback round can start.
6. Complete or force a round end and verify round-result sync still carries cumulative `scores` and `completedRounds`.

## Automated Coverage Boundary

`Tests/NetworkRegression` directly compiles DTOs, codecs, session and small pure policy classes. It includes a pure `RoomReadyPolicy` regression for rejecting MatchReady when AI fill is disabled and fewer than four humans are present.

It intentionally does **not** compile `Room` or `RoomManager`. It therefore does not automatically verify integrated seat rollback, room/connection binding cleanup, AI replacement, or loadout-lock behavior. Those flows remain part of the manual acceptance below.

## Phase D Manual Acceptance

Phase D completed manual acceptance on 2026-07-17. Retain this checklist for later regressions against a Dedicated Server started with `--port 9876 --maxRooms 1 --aiFill true`:

1. Create or join a room and confirm `MainLobby` switches to `ViewRoom`: the sidebar and all other lobby views are hidden, four stable seat rows are visible, and the local build summary shows the server-confirmed alienation value without showing a schema version or opponents' full loadouts.
2. Leave the room, let the room close, and force a client disconnect. Confirm each path returns to Home and presents the concrete leave/close/disconnect reason.
3. Run 1, 2, and 4 human-player rooms. Choose different local decks and talents before joining; verify the four rows show the server authority alienation value and the server wall contains exactly 34 tiles per owner with the selected composition.
4. Select an invalid local deck or talent configuration and attempt both Create and Join. Confirm the `RoomError` message remains visible instead of being replaced by the creating/joining status, and that the client has not entered a room.
5. Equip `peek` on one seat. Verify only that client receives and renders `PeekWall`; no other client receives the wall tiles.
6. Verify representative passive, scoring, and initial-capital talents. In an EastOnly match, confirm each `starting_capital` seat receives +30 only before the first small round, not again in rounds 2+.
7. During LoadingGameScene and between rounds, disconnect a human with AI fill enabled. Confirm the AI retains the locked deck, talents, alienation, and active effects. Confirm an AI created for a pre-lock vacancy uses the standard empty loadout.
8. Send malformed loadouts (missing, schema mismatch, invalid deck, invalid talent). Confirm the client receives `RoomError` and no room, occupied seat, or connection binding is left behind.
9. Inspect network traffic or server logging to confirm that `PlayerJoined` / `PlayerLeft` carry the authoritative seat summary only and never another player's full deck or talent IDs.

## Phase E Boundary

Reconnect, `lastSeq`, message caching/replay, endpoint rebinding, and in-room loadout updates are intentionally not part of this verification or Phase D.
