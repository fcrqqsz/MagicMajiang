# Room Lifecycle and Turn Synchronization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close Phase C rooms reliably when a human leaves or loses connectivity, prevent stale response actions from skipping a discard, and synchronize turn and wall state to online clients.

**Architecture:** The dedicated server remains authoritative. All leave causes flow through `RoomManager.RemoveMemberFromRoom`; a lightweight heartbeat detects connections that never emit WebSocket close. `GameServer` explicitly classifies valid actions by phase and broadcasts a no-draw turn and remaining wall count through `IPlayerClient`.

**Tech Stack:** Unity 2022.3 / C#, UI Toolkit, WebSocketSharp, existing `dotnet build Assembly-CSharp.csproj` validation.

## Global Constraints

- Do not add reconnect, AI takeover, room lists, matchmaking, or persistence.
- Dedicated-server code must not reference `GameManager`, UI, `HandController`, `LocalPlayerClient`, or `DeckManager`.
- Preserve the existing single-player code path; only online HUD count is server-driven.
- Do not modify Unity scene YAML by hand.

---

### Task 1: Test the authoritative action-phase classifier

**Files:**
- Create: `Tests/NetworkRegression/NetworkRegression.csproj`
- Create: `Tests/NetworkRegression/Program.cs`
- Create: `Assets/Scripts/Core/Network/TurnActionPolicy.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`

**Interfaces:**
- Produces: `TurnActionPolicy.IsMainTurnAction(ClientActionType)` and `IsResponseAction(ClientActionType)`.
- Consumes: `ClientActionType` in `Protocol.cs`.

- [x] **Step 1: Write the failing executable regression test**

```csharp
Assert(TurnActionPolicy.IsMainTurnAction(ClientActionType.Discard));
Assert(!TurnActionPolicy.IsMainTurnAction(ClientActionType.Skip));
Assert(TurnActionPolicy.IsResponseAction(ClientActionType.Skip));
Assert(!TurnActionPolicy.IsResponseAction(ClientActionType.Discard));
```

- [x] **Step 2: Verify RED**

Run: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj`

Expected: build failure because `TurnActionPolicy` does not yet exist.

- [x] **Step 3: Implement the minimal classifier and enforce it before completing either action TCS**

```csharp
if (_pendingActionTcs != null && action.PlayerId == _currentPlayerIndex)
{
    if (!TurnActionPolicy.IsMainTurnAction(action.ActionType)) return;
    _pendingActionTcs.TrySetResult(ValidateMainAction(action));
}
```

- [x] **Step 4: Verify GREEN**

Run: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj`

Expected: `Network regression tests passed.`

### Task 2: Unify normal leave, socket close, and heartbeat expiry

**Files:**
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/ConnectionRegistry.cs`
- Modify: `Assets/Scripts/Core/Network/RoomManager.cs`
- Modify: `Assets/Scripts/Core/Network/ServerBootstrap.cs`
- Modify: `Assets/Scripts/Core/Network/ClientRoomService.cs`
- Modify: `Assets/Scripts/Systems/NetworkManager.cs`
- Modify: `Assets/UI/ResultPanelController.cs`

**Interfaces:**
- Produces: `LeaveRoom`, `Heartbeat`, and `RoomClosed` messages.
- Produces: `ClientRoomService.LeaveRoom()` and `Tick(float now)`.
- Produces: `RoomManager.Tick(DateTime utcNow)` and one shared removal method.

- [x] **Step 1: Add serializable protocol messages and connection activity timestamp**

```csharp
public class LeaveRoomMessage { }
public class HeartbeatMessage { }
public class RoomClosedMessage { public string roomId; public string reason; }
```

- [x] **Step 2: Route leave, transport disconnect, and inactivity timeout through one service method**

```csharp
private void RemoveMemberFromRoom(string connectionId, string reason)
{
    // remove seat, broadcast RoomClosed while endpoints remain available,
    // then call room.Close() and unbind every member.
}
```

- [x] **Step 3: Send heartbeat every 3 seconds while in a room; expire after 10 seconds**

```csharp
public void Tick(float now)
{
    if (HasRoom && now >= _nextHeartbeatAt) Send("Heartbeat", new HeartbeatMessage());
}
```

- [x] **Step 4: Send LeaveRoom before the result page returns to lobby and reset client room state on RoomClosed**

### Task 3: Synchronize no-draw turns and wall count

**Files:**
- Modify: `Assets/Scripts/Core/Agents/IPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Agents/LocalPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Agents/SimpleAIClient.cs`
- Modify: `Assets/Scripts/Core/Network/RemotePlayerClient.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Modify: `Assets/UI/GameHUD/GameHUDController.cs`

**Interfaces:**
- Produces: `IPlayerClient.OnTurnWithoutDraw()` and `OnWallCountChanged(int)`.
- Produces: `TurnWithoutDrawMessage` and `WallCountMessage`.

- [x] **Step 1: Extend the client interface and remote message bridge**

```csharp
void OnTurnWithoutDraw();
void OnWallCountChanged(int remainingCount);
```

- [x] **Step 2: Broadcast remaining count after dealing and every wall draw**

```csharp
private void BroadcastWallCount()
{
    foreach (var client in _clients) client.OnWallCountChanged(_wallService.RemainingCount);
}
```

- [x] **Step 3: Notify only the current player after a Chi/Pon transition is fully established**

```csharp
currentPlayer.TurnCancellationToken = _turnCts.Token;
currentPlayer.OnTurnWithoutDraw();
```

- [x] **Step 4: Make the local client wait for a discard only from `OnTurnWithoutDraw`; remove the speculative Chi/Pon wait from `OnActionResolved`**

- [x] **Step 5: Have online HUD update from `WallCount`, while offline HUD continues polling `DeckManager`**

### Task 4: Build and manual verification

**Files:**
- Verify: `Assembly-CSharp.csproj`
- Verify: `Builds/DedicatedServer/server.log`

- [x] **Step 1: Run regression runner and project compile**

Run: `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj` and `dotnet build Assembly-CSharp.csproj`

Expected: regression runner exits 0 and the project has no C# errors.

- [ ] **Step 2: Manually verify two humans plus two AI**

1. Return one client to lobby: the peer receives a room-closed notice and returns to lobby.
2. Force-kill one client: the peer receives the same result within 10 seconds.
3. Let a main turn expire: one tile enters that player's river and the next turn starts.
4. Let a response expire: only a Skip is recorded; no main-turn discard is skipped.
5. Chi or Pon: the claimant must discard before the next player draws.
6. At round start HUD reads 84; every server wall draw lowers it by one.
