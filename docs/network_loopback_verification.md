# Network Loopback Verification

## Current Local Endpoint

The current local WebSocket endpoint for SuperMajiang loopback testing is:

```text
ws://127.0.0.1:9876/game
```

Use this address when running the temporary in-Editor server path and connecting a local client.

## 8080 Cache Note

Older cached verification notes may still mention port `8080`. That value is outdated for the current network baseline. Do not use `ws://127.0.0.1:8080/...` for current loopback verification.

Do not edit old cache documents under `C:/Users/fcrqq/.gemini/antigravity`; they are historical cache artifacts and are not the source of truth for this project.

## Minimal Verification Flow

1. Start the server-side Unity instance with `isNetworkMode` enabled and `isServer` enabled.
2. Confirm the server is listening for WebSocket clients.
3. Start the client-side Unity instance with `isNetworkMode` enabled and `isServer` disabled.
4. Confirm the client `serverAddress` is `ws://127.0.0.1:9876/game`.
5. Connect the client, send Ready, and verify the current 1 remote player + 3 server AI loopback round can start.
6. Complete or force a round end and verify round-result sync still carries cumulative `scores` and `completedRounds`.

## Phase A Boundary

This document only covers the current local loopback baseline. Formal headless bootstrap, room management, connection registry, and reconnect verification belong to later phases.