# Room-Only Match Authority Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 删除客户端进程内的隐式本地比赛权威，让 `Room` 成为唯一比赛编排者，同时保留一真人加三 AI 的在线房间体验和四种 `GameMode`。

**Architecture:** `GameManager` 只在已有有效房间绑定时创建 `LocalPlayerClient + RemoteServerProxy`，缺少房间时安全返回 Persistent，不再创建 `GameServer`、AI、权威构筑或推进局数。Dedicated Server 的 `Room` 继续持有整场 `GameSession`、跨局 `NetworkDecisionTracker` 和每小局 `GameServer`；HUD 与结算只消费服务端消息和客户端投影。

**Tech Stack:** Unity 2022.3.61t9 / Tuanjie 1.6.8、C#、Dedicated Server、WebSocket 协议 v2、UI Toolkit、`Tests/NetworkRegression` 控制台回归。

## Global Constraints

- 设计真源为 `docs/superpowers/specs/2026-08-04-room-authority-remove-local-mode-design.md`。
- 删除的是客户端进程内本地权威，不得删除 `GameMode.Single`、`LocalPlayerClient` 或 `SimpleAIClient`。
- 不实现 `MatchSessionHost`、`LocalMatchBootstrap`、`GameLaunchMode.LocalMatch`、Listen Server 或本地 WebSocket 替身。
- `Room` 是唯一权威比赛编排者；客户端 `GameSession` 只是服务端消息/快照的展示投影。
- 默认 `aiFill=true` 时，一名真人必须可以准备并由三名 AI 补齐；`aiFill=false` 时仍要求四名真人。
- 无有效房间直接进入 `03_Game` 时，Editor 与构建都记录 `MissingNetworkRoomForGameScene` 并返回 `00_Persistent`，不得静默回落本地比赛。
- `GameHUDController` 的牌山余量只来自服务端事件或恢复快照，不再轮询 `DeckManager`。
- 所有 PowerShell 命令使用 `pwsh -NoLogo -NoProfile -Command`；联机回归命令为 `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore`。
- 每项任务执行红—绿—重构：先增加会失败的精确回归，再写最小实现，跑相关验证，最后单独提交。

---

## File and Interface Map

**新增文件**

- `Assets/Scripts/Systems/NetworkGameSceneEntryPolicy.cs`：纯 C# 游戏场景入口判定，不依赖 Unity 场景 API。
- `Tests/NetworkRegression/NetworkAuthorityBoundaryTests.cs`：入口、AI 补位、局数模式和源码所有权守卫。

**主要修改文件**

- `Assets/Scripts/Core/GameManager.cs`：删除客户端本地权威，只保留在线表现装配与恢复投影。
- `Assets/Scripts/Systems/NetworkManager.cs`：增加无房间游戏场景返回 Persistent/Login 流程的公开协调方法。
- `Assets/UI/GameHUD/GameHUDController.cs`：删除 `DeckManager` 权威余量轮询。
- `Assets/UI/ResultPanelController.cs`：下一局只发送 ready，总结算只显示投影，异常回退不再加载 Game。
- `Assets/Scenes/03_Game.unity`：移除已经删除或早已失效的本地/网络模式序列化字段，保留客户端表现超时字段。
- `Tests/NetworkRegression/NetworkRegression.csproj`：编译入口 policy 和新测试。
- `Tests/NetworkRegression/Program.cs`：执行新测试组。
- `docs/network_verification.md`：增加 Room-only 单人 AI 补位和无房间直开验证。

**明确不修改**

- `Assets/Scripts/Core/Network/Room.cs` 的比赛职责在本计划中不做抽象迁移；只用回归锁定其现有唯一权威位置。
- `Assets/Scripts/Core/Network/GameServer.cs`、`LocalPlayerClient.cs`、`SimpleAIClient.cs` 不因删除本地权威而重写。
- `Assets/Scripts/Systems/DeckManager.cs` 暂不删除；牌面资源与遗留表现调用另行清理，不再把它传给 `GameServer` 即可满足本计划边界。

**本计划锁定的公共接口**

```csharp
namespace MahjongGame.Systems
{
    public enum NetworkGameSceneEntryDecision
    {
        InitializeNetworkClient,
        ReturnToPersistent
    }

    public static class NetworkGameSceneEntryPolicy
    {
        public static NetworkGameSceneEntryDecision Decide(
            bool hasNetworkManager,
            bool hasRoomService,
            bool hasRoom);
    }
}
```

```csharp
// NetworkManager
public Task ReturnToPersistentFlowAsync();
```

---

### Task 1: Lock the network-only game-scene entry contract

**Files:**

- Create: `Assets/Scripts/Systems/NetworkGameSceneEntryPolicy.cs`
- Create: `Tests/NetworkRegression/NetworkAuthorityBoundaryTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

**Interfaces:**

- Consumes: `RoomReadyPolicy.CanMarkMatchReady(bool aiFill, int humanCount)` and `GameSession.GetTotalRounds()`.
- Produces: `NetworkGameSceneEntryPolicy.Decide(bool, bool, bool)` for `GameManager.Start()` in Task 2.

- [ ] **Step 1: Add only the failing test file to the regression project**

Append this compile item beside the existing test files:

```xml
<Compile Include="NetworkAuthorityBoundaryTests.cs" />
```

Call the test after `RoomSessionTests.Run(runner);`:

```csharp
NetworkAuthorityBoundaryTests.Run(runner);
```

- [ ] **Step 2: Write failing entry, AI-fill, and GameMode regressions**

Create the test class with these exact behavioral checks:

```csharp
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Systems;

internal static class NetworkAuthorityBoundaryTests
{
    public static void Run(RegressionRunner runner)
    {
        TestGameSceneEntry(runner);
        TestSingleHumanAiFill(runner);
        TestGameModeLengths(runner);
    }

    private static void TestGameSceneEntry(RegressionRunner runner)
    {
        runner.Check(NetworkGameSceneEntryPolicy.Decide(true, true, true)
            == NetworkGameSceneEntryDecision.InitializeNetworkClient,
            "Game scene initializes only for an existing network room.");

        foreach (var state in new[]
        {
            (Manager: false, Service: false, Room: false),
            (Manager: true, Service: false, Room: false),
            (Manager: true, Service: true, Room: false)
        })
        {
            runner.Check(NetworkGameSceneEntryPolicy.Decide(state.Manager, state.Service, state.Room)
                == NetworkGameSceneEntryDecision.ReturnToPersistent,
                "Missing network authority must return to Persistent without a local fallback.");
        }
    }

    private static void TestSingleHumanAiFill(RegressionRunner runner)
    {
        runner.Check(RoomReadyPolicy.CanMarkMatchReady(aiFill: true, humanCount: 1),
            "One human can start when AI fill is enabled.");
        runner.Check(!RoomReadyPolicy.CanMarkMatchReady(aiFill: false, humanCount: 1),
            "One human cannot start when AI fill is disabled.");
    }

    private static void TestGameModeLengths(RegressionRunner runner)
    {
        runner.Check(new GameSession(GameMode.Single).GetTotalRounds() == 1,
            "Single remains a one-round mode, not a local-play switch.");
        runner.Check(new GameSession(GameMode.EastOnly).GetTotalRounds() == 4,
            "EastOnly remains four rounds.");
        runner.Check(new GameSession(GameMode.HalfGame).GetTotalRounds() == 8,
            "HalfGame remains eight rounds.");
        runner.Check(new GameSession(GameMode.FullGame).GetTotalRounds() == 16,
            "FullGame remains sixteen rounds.");
    }
}
```

- [ ] **Step 3: Run the regression and confirm RED**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

Expected: compile failure naming missing `NetworkGameSceneEntryPolicy` and `NetworkGameSceneEntryDecision`.

- [ ] **Step 4: Implement and include the minimal pure policy**

Add this compile item beside the existing system policies:

```xml
<Compile Include="..\..\Assets\Scripts\Systems\NetworkGameSceneEntryPolicy.cs" Link="NetworkGameSceneEntryPolicy.cs" />
```

Create the source file:

```csharp
namespace MahjongGame.Systems
{
    public enum NetworkGameSceneEntryDecision
    {
        InitializeNetworkClient,
        ReturnToPersistent
    }

    public static class NetworkGameSceneEntryPolicy
    {
        public static NetworkGameSceneEntryDecision Decide(
            bool hasNetworkManager,
            bool hasRoomService,
            bool hasRoom)
        {
            return hasNetworkManager && hasRoomService && hasRoom
                ? NetworkGameSceneEntryDecision.InitializeNetworkClient
                : NetworkGameSceneEntryDecision.ReturnToPersistent;
        }
    }
}
```

- [ ] **Step 5: Run the focused regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Systems/NetworkGameSceneEntryPolicy.cs Tests/NetworkRegression; git commit -m 'test: lock network-only game scene entry'"
```

Expected: all regression groups pass; no implementation file outside the policy is changed yet.

---

### Task 2: Remove match authority from `GameManager`

**Files:**

- Modify: `Assets/Scripts/Core/GameManager.cs`
- Modify: `Assets/Scripts/Systems/NetworkManager.cs`
- Modify: `Tests/NetworkRegression/NetworkAuthorityBoundaryTests.cs`

**Interfaces:**

- Consumes: `NetworkGameSceneEntryPolicy.Decide(bool, bool, bool)` from Task 1 and existing `ClientRoomService`/`RemoteServerProxy` APIs.
- Produces: `NetworkManager.ReturnToPersistentFlowAsync()` and a network-only `GameManager.StartNextRound()`.

- [ ] **Step 1: Add a failing source-boundary regression**

Extend `NetworkAuthorityBoundaryTests.Run` with `TestGameManagerHasNoAuthority(runner);`, and add:

```csharp
private static void TestGameManagerHasNoAuthority(RegressionRunner runner)
{
    string source = ReadRepoFile("Assets", "Scripts", "Core", "GameManager.cs");
    string[] forbidden =
    {
        "new GameServer(",
        "new SimpleAIClient(",
        "Session.AdvanceRound(",
        "starting_capital",
        "BuildTalentConfigs(",
        "StartGameWithConfig(",
        "StartSession("
    };

    foreach (string fragment in forbidden)
        runner.Check(!source.Contains(fragment, StringComparison.Ordinal),
            $"GameManager must not contain authority fragment: {fragment}");

    string roomSource = ReadRepoFile("Assets", "Scripts", "Core", "Network", "Room.cs");
    runner.Check(Count(roomSource, "Session.AdvanceRound(") == 1,
        "Room owns the single authoritative round advance call.");
}

private static string ReadRepoFile(params string[] segments)
{
    DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SuperMajiang.sln")))
        directory = directory.Parent;
    if (directory == null) throw new InvalidOperationException("Repository root not found.");
    return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray()));
}

private static int Count(string source, string value)
{
    int count = 0;
    int offset = 0;
    while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}
```

Add `using System;`, `using System.IO;`, and `using System.Linq;` to the test file.

- [ ] **Step 2: Run the regression and confirm RED on current local authority**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

Expected: failures report `new GameServer(`, `new SimpleAIClient(`, `Session.AdvanceRound(`, `starting_capital`, and the obsolete local entry methods in `GameManager.cs`.

- [ ] **Step 3: Add the persistent-flow router to `NetworkManager`**

Add this public method next to the other scene-loading methods:

```csharp
public async Task ReturnToPersistentFlowAsync()
{
    if (!SceneManager.GetSceneByName(SceneNames.Persistent).isLoaded)
    {
        SceneManager.LoadScene(SceneNames.Persistent, LoadSceneMode.Single);
        return;
    }

    await EnsureRecoverySceneAsync(SceneNames.Login);
}
```

This reuses the existing recovery scene routine: it activates Login and unloads MainLobby/Game while keeping Persistent. It must not call `RoomService.CreateRoom`, reconnect, or load Game.

- [ ] **Step 4: Replace `GameManager.Start()` with a network-only entry**

Keep `actionTimeout` and `responseTimeout` because current `LocalPlayerClient` uses them as presentation timer fallbacks. Remove `gameMode`, debug-hand fields, AI-cheat fields, `ActiveConfigs`, `_currentServer`, `_hostConfig`, local turn handlers, and `IsNetworkClient`.

Add cached presentation fields:

```csharp
private ClientRoomService _roomService;
private int _localSeatIndex = -1;
```

Replace `Start()` and the invalid-entry helper with:

```csharp
private void Start()
{
    NetworkManager networkManager = NetworkManager.Instance;
    ClientRoomService roomService = networkManager?.RoomService;
    NetworkGameSceneEntryDecision decision = NetworkGameSceneEntryPolicy.Decide(
        networkManager != null,
        roomService != null,
        roomService?.HasRoom == true);

    if (decision != NetworkGameSceneEntryDecision.InitializeNetworkClient)
    {
        Debug.LogError("[GameManager] MissingNetworkRoomForGameScene");
        ReturnToPersistentFlow();
        return;
    }

    _roomService = roomService;
    _localSeatIndex = roomService.SeatIndex;
    Session = new GameSession(roomService.GameMode);
    InitializeNetworkClient();
}

private async void ReturnToPersistentFlow()
{
    if (NetworkManager.Instance != null)
    {
        await NetworkManager.Instance.ReturnToPersistentFlowAsync();
        return;
    }

    SceneManager.LoadScene(SceneNames.Persistent, LoadSceneMode.Single);
}
```

Add `using UnityEngine.SceneManagement;`. Do not read ProfileManager or selected loadouts in `GameManager`; the server already accepted the room loadout.

- [ ] **Step 5: Reduce the remaining methods to client projection only**

Apply these exact ownership changes:

```csharp
public OpponentViewController GetOpponentView(int playerId)
{
    if (_localSeatIndex < 0) return null;
    int relativeSeat = (playerId - _localSeatIndex + 4) % 4;
    if (relativeSeat == 1) return rightOpponent;
    if (relativeSeat == 2) return topOpponent;
    if (relativeSeat == 3) return leftOpponent;
    return null;
}

public void StartNextRound()
{
    if (_roomService?.HasRoom != true)
    {
        Debug.LogWarning("[GameManager] Cannot ready next round without a room.");
        return;
    }
    _roomService.SendReady(ReadyPhase.NextRound);
}
```

`InitializeNetworkClient()` uses `_roomService` and `_localSeatIndex`, creates exactly one `LocalPlayerClient`, and binds it to one `RemoteServerProxy`. `ApplyNetworkRecoverySnapshot` continues updating the client projection. `OnDestroy` only calls `_currentClientProxy.Cleanup()`.

Delete `GetAlienationScore`, `StartGameWithConfig`, `StartSession`, `BuildTalentConfigs`, local `OnRoundFinished`, and `EndSession`. Remove now-unused `ProfileManager`, `PlayerProfile`, `TalentSlotConfig`, `SimpleAIClient` and local server usings.

- [ ] **Step 6: Run regression, inspect authority fragments, and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "rg -n 'new GameServer|new SimpleAIClient|Session\.AdvanceRound|starting_capital|StartGameWithConfig|StartSession|BuildTalentConfigs' Assets/Scripts/Core/GameManager.cs"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Expected: regression passes and `rg` returns no matches. Then commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/GameManager.cs Assets/Scripts/Systems/NetworkManager.cs Tests/NetworkRegression/NetworkAuthorityBoundaryTests.cs; git commit -m 'refactor: make game manager network projection only'"
```

---

### Task 3: Remove HUD and result-panel local fallbacks

**Files:**

- Modify: `Assets/UI/GameHUD/GameHUDController.cs`
- Modify: `Assets/UI/ResultPanelController.cs`
- Modify: `Tests/NetworkRegression/NetworkAuthorityBoundaryTests.cs`

**Interfaces:**

- Consumes: `GameManager.StartNextRound()` and `NetworkManager.ReturnToPersistentFlowAsync()` from Task 2.
- Produces: network-message-only wall count and a result flow with no call to removed `GameManager.EndSession()`.

- [ ] **Step 1: Add failing HUD/result source guards**

Call `TestPresentationHasNoLocalAuthority(runner);` and add:

```csharp
private static void TestPresentationHasNoLocalAuthority(RegressionRunner runner)
{
    string hud = ReadRepoFile("Assets", "UI", "GameHUD", "GameHUDController.cs");
    runner.Check(!hud.Contains("DeckManager.Instance", StringComparison.Ordinal),
        "HUD wall count comes only from server projection.");
    runner.Check(!hud.Contains("IsNetworkRoom", StringComparison.Ordinal),
        "HUD has no local/network authority branch.");

    string result = ReadRepoFile("Assets", "UI", "ResultPanelController.cs");
    runner.Check(!result.Contains("GameManager.Instance.EndSession", StringComparison.Ordinal),
        "Result UI never broadcasts a client-owned session end.");
    runner.Check(!result.Contains("SceneManager.LoadScene(SceneNames.Game", StringComparison.Ordinal),
        "Result fallback never reloads the Game scene.");
}
```

- [ ] **Step 2: Run the regression and confirm RED**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

Expected: current HUD reports `DeckManager.Instance`/`IsNetworkRoom`; result panel reports `EndSession` and Game reload fallback.

- [ ] **Step 3: Make wall count event-driven only**

In `UpdateRoundInfo`, replace the local/network branch with:

```csharp
int remaining = Mathf.Max(_lastRemainingCount, 0);
```

Delete the wall-count polling block at the start of `Update()` and delete `IsNetworkRoom`. Keep `UpdateRemainingCount(int)` and `ApplyRecoverySnapshot(...)`; they are the only authoritative inputs.

- [ ] **Step 4: Make result actions projection-only**

Replace the final branch of `OnRestartClicked`:

```csharp
else
{
    ShowSessionResult();
}
```

Keep the middle branch calling `GameManager.Instance.StartNextRound()`. Replace `ReturnToLobby()` with:

```csharp
private async void ReturnToLobby()
{
    NetworkManager networkManager = NetworkManager.Instance;
    if (networkManager == null)
    {
        SceneManager.LoadScene(SceneNames.Persistent, LoadSceneMode.Single);
        return;
    }

    networkManager.RoomService?.LeaveRoom();
    await networkManager.LoadSceneAndUnloadCurrentAsync(SceneNames.MainLobby, SceneNames.Game);
}
```

This is the only normal result exit. It clears the completed room binding through `LeaveRoom` before returning to the lobby.

- [ ] **Step 5: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "rg -n 'DeckManager\.Instance|IsNetworkRoom|EndSession\(|LoadScene\(SceneNames\.Game' Assets/UI/GameHUD/GameHUDController.cs Assets/UI/ResultPanelController.cs"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Expected: no `rg` matches and all regressions pass. Commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/UI/GameHUD/GameHUDController.cs Assets/UI/ResultPanelController.cs Tests/NetworkRegression/NetworkAuthorityBoundaryTests.cs; git commit -m 'refactor: remove local presentation fallbacks'"
```

---

### Task 4: Clean the game scene and verify the one-human online flow

**Files:**

- Modify: `Assets/Scenes/03_Game.unity`
- Modify: `docs/network_verification.md`
- Modify: `Tests/NetworkRegression/NetworkAuthorityBoundaryTests.cs`

**Interfaces:**

- Consumes: all network-only boundaries from Tasks 1–3.
- Produces: a clean Unity scene and the final manual verification procedure required before starting the talent plans.

- [ ] **Step 1: Add a failing scene serialization guard**

Call `TestGameSceneHasNoLocalModeFields(runner);` and add:

```csharp
private static void TestGameSceneHasNoLocalModeFields(RegressionRunner runner)
{
    string scene = ReadRepoFile("Assets", "Scenes", "03_Game.unity");
    string[] forbidden =
    {
        "  gameMode:",
        "  useDebugHand:",
        "  debugHand:",
        "  forceAIDiscard:",
        "  aiCheatDiscards:",
        "  isNetworkMode:",
        "  isServer:",
        "  serverAddress:"
    };
    foreach (string fragment in forbidden)
        runner.Check(!scene.Contains(fragment, StringComparison.Ordinal),
            $"Game scene must not serialize removed mode field: {fragment.Trim()}");

    runner.Check(scene.Contains("  actionTimeout: 30", StringComparison.Ordinal)
        && scene.Contains("  responseTimeout: 10", StringComparison.Ordinal),
        "Client presentation timer fallbacks remain serialized.");
}
```

- [ ] **Step 2: Run regression and confirm RED on obsolete YAML fields**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

Expected: failures list `gameMode`, debug hand, AI cheat, and old `isNetworkMode/isServer/serverAddress` fields.

- [ ] **Step 3: Remove only the obsolete serialized fields**

In the `GameManager` MonoBehaviour block identified by script GUID `cfc5edaf9e048d5419183a0c3db180ad`, retain:

```yaml
  playerHandController: {fileID: 776909412}
  rightOpponent: {fileID: 1258930456}
  topOpponent: {fileID: 1523404013}
  leftOpponent: {fileID: 93327062}
  actionTimeout: 30
  responseTimeout: 10
```

Delete the serialized `gameMode`, 13-entry `debugHand`, `forceAIDiscard`, `aiCheatDiscards`, `isNetworkMode`, `isServer`, and `serverAddress` records. Do not reserialize unrelated scene objects.

- [ ] **Step 4: Document the exact Room-only verification matrix**

Add a section `Room-only 一人 AI 补位` to `docs/network_verification.md` with these commands and expected outcomes:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

Manual matrix:

1. Start Dedicated Server with default `--aiFill=true`.
2. Start one normal client, log in, create a `Single` room, click ready, and verify seats 1–3 are AI.
3. Finish the round, view final standings, return to lobby, and create another room.
4. Repeat with `HalfGame` through at least two round transitions.
5. Disconnect during a main decision, reconnect by username + room ticket, and verify snapshot recovery and AI handback at the next decision boundary.
6. Stop Dedicated Server, try creating a room, and verify the client stays in the lobby.
7. Directly run `03_Game` in Editor and in a development build; verify `MissingNetworkRoomForGameScene` and return to Persistent.

- [ ] **Step 5: Run the complete automated gate**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "dotnet build Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "rg -n 'new GameServer|new SimpleAIClient|Session\.AdvanceRound|starting_capital|StartGameWithConfig|StartSession|BuildTalentConfigs' Assets/Scripts/Core/GameManager.cs"
pwsh -NoLogo -NoProfile -Command "rg -n 'DeckManager\.Instance|IsNetworkRoom|EndSession\(|LoadScene\(SceneNames\.Game' Assets/UI/GameHUD/GameHUDController.cs Assets/UI/ResultPanelController.cs"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git status --short"
```

Expected: both .NET commands pass; both `rg` commands return no matches; diff check passes; status lists only Task 4 files. Do not claim Unity compilation until the editor imports the changed scripts and scene without Console errors.

- [ ] **Step 6: Perform Unity/manual verification and commit**

Run the eight cases in the design spec, including Single, two HalfGame rounds, reconnect, server-off failure, Editor direct Game, build direct Game, and re-create after final result. Record any failure before proceeding to talents.

After Unity reports no compile errors and the manual matrix passes:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scenes/03_Game.unity docs/network_verification.md Tests/NetworkRegression/NetworkAuthorityBoundaryTests.cs; git commit -m 'docs: verify room-only match authority'"
```

---

## Completion Gate

只有以下条件全部满足，才能开始 `2026-08-04-talent-foundation-and-alienation.md`：

- [ ] `GameMode.Single` 仍为一小局，默认 AI 补位房间允许一真人准备并补齐三 AI。
- [ ] `GameManager` 不包含 `GameServer`/AI 创建、构筑装配、天赋 ID、`Session.AdvanceRound()` 或本地 session 生命周期。
- [ ] `Room.cs` 保留唯一的权威 `Session.AdvanceRound()` 调用。
- [ ] 无房间进入 `03_Game` 时记录 `MissingNetworkRoomForGameScene` 并返回 Persistent；不存在 Editor 特例。
- [ ] HUD 余量只来自服务端事件/快照，结果面板不广播客户端 session end，也不重新加载 Game。
- [ ] `03_Game.unity` 不再序列化本地模式、调试手牌、AI 作弊和过期网络模式字段。
- [ ] 一真人 AI 补位的 Single、多局推进、断线恢复、服务端不可用和总结算返回全部手工通过。
- [ ] NetworkRegression、`dotnet build`、Unity 脚本编译和 `git diff --check` 全部通过。
- [ ] 工作区不存在占位注释、未实现异常、空方法、假成功返回或计划外改动。
