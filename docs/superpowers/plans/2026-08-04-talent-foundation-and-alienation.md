# Talent Foundation and Alienation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立跨小局持久的天赋运行时、40/80/120 异化值房间预算和 v2 构筑数据，并把现有六个天赋迁入统一多态生命周期，不再由 `Room`/`GameServer` 按天赋 ID 执行效果。

**Architecture:** Dedicated Server 的 `Room` 持有一场比赛唯一的 `TalentMatchRuntime`，每个新 `GameServer` 只借用它执行本小局钩子；客户端 `GameManager` 只展示服务端投影，不持有运行时。`TalentRegistry` 继续以稳定 ID 负责注册、存档、网络和实例创建；效果本身由 `TalentRule` 虚方法执行。房间创建时锁定异化值档位，服务端按当前激活的六个主槽重新计算总异化值，客户端只收到档位和自己的精确总值。

**Tech Stack:** Unity 2022.3.61t9 / Tuanjie 1.6.8、C#、纯 C# 天赋管道、WebSocket 协议 v3、UI Toolkit（本计划只增加设置字段，不实现界面）、`Tests/NetworkRegression` 控制台回归。

## Global Constraints

- 开始前必须完成 `docs/superpowers/plans/2026-08-04-room-authority-remove-local-mode.md` 的 Completion Gate。
- 设计真源为 `docs/superpowers/specs/2026-08-04-talent-vertical-slice-design.md`；发生歧义时先回到该文档。
- ID 可以用于注册、存档、网络、日志、UI、测试和实例查找；禁止在 `Room`、`GameServer` 中用 `if/switch(talentId)` 执行效果。
- 本计划只完成基础设施与现有六天赋迁移，不提前实现 `藏锋`、`截流`、`定心`、主动天赋动作或中场备牌切换。
- `SavedDeck` 允许保存超出当前预览档位的构筑；真正的预算拒绝发生在创建/加入房间的服务端校验处。
- 对手只能看到房间异化值档位，不能看到其他真人的精确总异化值、暗手牌、牌库、未揭示天赋或私有窥探结果。
- `StartingCapital` 一场比赛只触发一次；`Peek` 和 `DrawReward` 每个小局重新触发；`HeadStart`、`DragonAscent`、`MidasTouch` 每个小局持续可用。
- 每个任务严格执行红—绿—重构：先新增失败回归，再写最小实现，再跑相关测试，最后提交。
- 所有 PowerShell 命令使用 `pwsh -NoLogo -NoProfile -Command`；联机回归命令为 `dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore`。

---

## File and Interface Map

**新增文件**

- `Assets/Scripts/Core/AlienationPreset.cs`：档位枚举、限额与统一预算计算。
- `Assets/Scripts/Talent/TalentMetadata.cs`：状态周期、公开策略、备牌限制等静态元数据。
- `Assets/Scripts/Talent/TalentRuntimeState.cs`：每席位每个天赋的跨局计数器、标志和公开状态。
- `Assets/Scripts/Talent/TalentMatchRuntime.cs`：一场比赛唯一的天赋生命周期协调器。
- `Assets/Scripts/Talent/TalentRuntimeEvent.cs`：结构化公开/私有天赋事件。
- `Tests/NetworkRegression/TalentFoundationTests.cs`：本计划的纯 C# 回归。

**主要修改文件**

- `Assets/Scripts/Talent/TalentRuleAttribute.cs`
- `Assets/Scripts/Talent/TalentRule.cs`
- `Assets/Scripts/Talent/TalentRegistry.cs`
- `Assets/Scripts/Talent/TalentSlotConfig.cs`
- `Assets/Scripts/Core/DeckConfig.cs`
- `Assets/Scripts/Core/Network/Data/SavedDeck.cs`
- `Assets/Scripts/Core/Network/Data/PlayerProfile.cs`
- `Assets/Scripts/Systems/ProfileManager.cs`
- `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- `Assets/Scripts/Core/Network/PlayerLoadoutCodec.cs`
- `Assets/Scripts/Core/Network/IAccountAuthenticator.cs`
- `Assets/Scripts/Core/Network/Room.cs`
- `Assets/Scripts/Core/Network/RoomManager.cs`
- `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`
- `Assets/Scripts/Core/Network/ClientGameState.cs`
- `Assets/Scripts/Core/Network/ClientRoomService.cs`
- `Assets/Scripts/Core/Network/GameServer.cs`
- `Assets/Scripts/Core/Network/GameSession.cs`
- `Assets/Scripts/Talent/Impl/MidasTouchTalent.cs`
- `Assets/Scripts/Talent/Impl/PeekTalent.cs`
- `Assets/Scripts/Talent/Impl/DragonAscentTalent.cs`
- `Assets/Scripts/Talent/Impl/DrawRewardTalent.cs`
- `Assets/Scripts/Talent/Impl/HeadStartTalent.cs`
- `Assets/Scripts/Talent/Impl/StartingCapitalTalent.cs`
- `Tests/NetworkRegression/NetworkRegression.csproj`
- `Tests/NetworkRegression/Program.cs`

**完成后删除**

- `Assets/Scripts/Talent/TalentManager.cs` 及其 `.meta`：其职责全部并入跨局 `TalentMatchRuntime`。
- `Assets/Scripts/Core/Network/SessionTalentPolicy.cs` 及其 `.meta`：不再保留按 ID 特判的启动资金策略。

**本计划锁定的公共接口**

```csharp
public enum AlienationPreset { Low = 40, Standard = 80, High = 120 }

public sealed class TalentMatchRuntime
{
    public void BeginMatch(GameSession session);
    public void BeginRound(TalentRoundContext context);
    public void ApplyWallBuilding(TalentWallContext context);
    public void ResolvePostShuffle(TalentPostShuffleContext context);
    public TileData ApplyDraw(TalentDrawContext context, TileData drawnTile);
    public TileData ApplyDiscard(TalentDiscardContext context, TileData discardedTile);
    public void ValidateAction(TalentActionContext context);
    public ScoringOptions BuildScoringOptions(TalentScoringContext context);
    public void NotifyTileBecamePublic(TalentPublicTileContext context, TileData tile);
    public void ResolveAcceptedWinVisibility(TalentAcceptedWinContext context);
    public IReadOnlyList<TileData> GetPrivatePeekTiles(int seatIndex);
    public void EndRound(TalentRoundOutcome outcome, GameSession session);
}
```

---

### Task 1: Add talent metadata and the 6+3 carried-loadout schema

**Files:**

- Create: `Assets/Scripts/Talent/TalentMetadata.cs`
- Modify: `Assets/Scripts/Talent/TalentRuleAttribute.cs`
- Modify: `Assets/Scripts/Talent/TalentRegistry.cs`
- Modify: `Assets/Scripts/Talent/TalentSlotConfig.cs`
- Modify: `Assets/Scripts/Core/Network/Data/SavedDeck.cs`
- Modify: `Assets/Scripts/Core/Network/Data/PlayerProfile.cs`
- Modify: `Assets/Scripts/Systems/ProfileManager.cs`
- Test: `Tests/NetworkRegression/TalentFoundationTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

- [x] **Step 1: Add a failing metadata and slot-normalization regression**

在 `TalentFoundationTests.cs` 建立统一入口，并先覆盖旧存档、6 个主槽、3 个备选槽、跨区重复和元数据默认值：

```csharp
internal static class TalentFoundationTests
{
    public static void Run(RegressionRunner runner)
    {
        TalentSlotConfig legacy = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "midas_touch", null, null, null, null, null },
            ReserveTalentIds = null
        };

        legacy.Normalize();

        runner.Check(legacy.SlotTalentIds.Length == TalentSlotConfig.MainSlotCount,
            "legacy main slots normalize to six");
        runner.Check(legacy.ReserveTalentIds.Length == TalentSlotConfig.ReserveSlotCount,
            "legacy save without reserve slots normalizes to three empty entries");
        runner.Check(legacy.GetCarriedIds().SequenceEqual(new[] { "midas_touch" }),
            "carried ids combine normalized main and reserve slots");

        TalentMetadata metadata = TalentRegistry.Instance.GetMetadata("starting_capital");
        runner.Check(metadata.SideboardPolicy == TalentSideboardPolicy.MainOnlyLocked,
            "starting capital is marked as locked main-only metadata");
    }
}
```

把测试文件加入 `NetworkRegression.csproj` 的显式 `<Compile Include>`，并在 `Program.cs` 现有测试之后调用 `TalentFoundationTests.Run(runner)`。

- [x] **Step 2: Run the focused regression and confirm RED**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

Expected: 编译失败，缺少 `ReserveTalentIds`、`Normalize()`、`GetCarriedIds()`、`TalentMetadata` 和 `GetMetadata()`。

- [x] **Step 3: Implement immutable metadata with backward-compatible attribute defaults**

在 `TalentMetadata.cs` 添加：

```csharp
namespace MahjongGame.Talents
{
    public enum TalentStateScope { Round, Match }

    [System.Flags]
    public enum TalentActivationWindow
    {
        None = 0,
        MainTurn = 1,
        Response = 2
    }

    public enum TalentRevealPolicy
    {
        HiddenUntilPublicEffect,
        PublicAtMatchStart,
        OwnerOnly
    }

    public enum TalentSideboardPolicy
    {
        Flexible,
        MainOnly,
        MainOnlyLocked
    }

    public sealed class TalentMetadata
    {
        public TalentStateScope StateScope { get; }
        public TalentActivationWindow ActivationWindow { get; }
        public TalentRevealPolicy RevealPolicy { get; }
        public TalentSideboardPolicy SideboardPolicy { get; }

        public TalentMetadata(
            TalentStateScope stateScope,
            TalentActivationWindow activationWindow,
            TalentRevealPolicy revealPolicy,
            TalentSideboardPolicy sideboardPolicy)
        {
            StateScope = stateScope;
            ActivationWindow = activationWindow;
            RevealPolicy = revealPolicy;
            SideboardPolicy = sideboardPolicy;
        }
    }
}
```

在 `TalentRuleAttribute` 保留现有构造函数，增加可命名属性；默认值必须保持现有六天赋可加载：

```csharp
public TalentStateScope StateScope { get; set; } = TalentStateScope.Round;
public TalentActivationWindow ActivationWindow { get; set; } = TalentActivationWindow.None;
public TalentRevealPolicy RevealPolicy { get; set; } = TalentRevealPolicy.HiddenUntilPublicEffect;
public TalentSideboardPolicy SideboardPolicy { get; set; } = TalentSideboardPolicy.Flexible;
```

`TalentRegistry` 在扫描属性时构造并缓存 `TalentMetadata`，暴露：

```csharp
public TalentMetadata GetMetadata(string talentId)
{
    if (!_entries.TryGetValue(talentId, out RegistryEntry entry))
        throw new KeyNotFoundException($"Unknown talent id: {talentId}");
    return entry.Metadata;
}
```

给 `StartingCapitalTalent` 的属性增加 `StateScope = TalentStateScope.Match`、`RevealPolicy = TalentRevealPolicy.PublicAtMatchStart`、`SideboardPolicy = TalentSideboardPolicy.MainOnlyLocked`。给 `PeekTalent` 设置 `RevealPolicy = TalentRevealPolicy.OwnerOnly`。

- [x] **Step 4: Extend `TalentSlotConfig` without breaking old saves**

保留 `SlotTalentIds` 作为六个主槽的序列化字段，新增三备选槽和显式枚举方法：

```csharp
public const int MainSlotCount = 6;
public const int ReserveSlotCount = 3;

public string[] SlotTalentIds = new string[MainSlotCount];
public string[] ReserveTalentIds = new string[ReserveSlotCount];

public void Normalize()
{
    SlotTalentIds = NormalizeArray(SlotTalentIds, MainSlotCount);
    ReserveTalentIds = NormalizeArray(ReserveTalentIds, ReserveSlotCount);
}

public IEnumerable<string> GetMainIds() => GetNonEmpty(SlotTalentIds);
public IEnumerable<string> GetReserveIds() => GetNonEmpty(ReserveTalentIds);
public IEnumerable<string> GetCarriedIds() => GetMainIds().Concat(GetReserveIds());

// 兼容旧调用：在所有调用点迁移完成前，它仍只表示开场激活的六个主槽。
public IEnumerable<string> GetAllEquippedIds() => GetMainIds();
```

`NormalizeArray` 必须复制到固定长度而不是直接信任存档数组；`SavedDeck.Normalize()` 执行 `Talents ??= new TalentSlotConfig()` 和 `Talents.Normalize()`。`PlayerProfile.Normalize()` 补齐空 `Settings`/`SavedDecks` 并遍历 deck；`ProfileManager.LoadProfile()` 在 `JsonUtility.FromJson` 后立即调用，若反序列化结果为 null 则创建新档。`CanEquip` 继续校验主槽层级，另增 `CanEquipReserve(int index, TalentTier tier)`，只允许中/小两种固定槽位。

- [x] **Step 5: Add duplicate validation across all nine carried slots**

在 `TalentSlotConfig` 添加：

```csharp
public bool HasDuplicateCarriedIds()
{
    HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (string id in GetCarriedIds())
    {
        if (!seen.Add(id)) return true;
    }
    return false;
}
```

扩充测试，验证同一 ID 同时出现在主槽与备选槽时返回 `true`，空字符串不计入重复。

- [x] **Step 6: Run regression, inspect diff, and commit**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Expected: 全部回归通过，`git diff --check` 无输出。

Commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Talent Assets/Scripts/Core/Network/Data Assets/Scripts/Systems/ProfileManager.cs Tests/NetworkRegression; git commit -m 'feat: add carried talent metadata schema'"
```

---

### Task 2: Enforce alienation presets in loadout decoding

**Files:**

- Create: `Assets/Scripts/Core/AlienationPreset.cs`
- Modify: `Assets/Scripts/Core/DeckConfig.cs`
- Modify: `Assets/Scripts/Core/Network/Data/PlayerProfile.cs`
- Modify: `Assets/Scripts/Systems/ProfileManager.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/PlayerLoadoutCodec.cs`
- Create: `Assets/Scripts/Core/Network/PlayerLoadoutErrorCodes.cs`
- Test: `Tests/NetworkRegression/TalentFoundationTests.cs`

- [x] **Step 1: Add failing tests for preset limits, active-only cost, and schema v2**

新增以下断言，测试数据使用一个 34 张合法牌库、主槽成本 15、备选槽成本 8：

```csharp
PlayerLoadoutMessage message = new PlayerLoadoutMessage
{
    schemaVersion = TrustedPlayerLoadout.CurrentSchemaVersion,
    deckEntries = BuildValidDeckEntries(deckAlienation: 30),
    mainTalentSlotIds = new[] { "midas_touch", null, null, null, null, null },
    reserveTalentSlotIds = new[] { null, "network_test_small", null }
};

bool lowAccepted = PlayerLoadoutCodec.TryDecode(
    message, AlienationPreset.Low, out _, out string lowError);
runner.Check(!lowAccepted && lowError == PlayerLoadoutErrorCodes.AlienationLimitExceeded,
    "low preset rejects 30 deck + 15 active talent");

bool standardAccepted = PlayerLoadoutCodec.TryDecode(
    message, AlienationPreset.Standard, out TrustedPlayerLoadout standard, out _);
runner.Check(standardAccepted && standard.TotalAlienation == 45,
    "inactive reserve cost is excluded from room-entry alienation");
runner.Check(TrustedPlayerLoadout.CurrentSchemaVersion == 2,
    "carried-loadout wire schema is v2");
```

这里先在测试程序集定义临时带成本的测试天赋，或复用现有稳定 ID；不要依赖尚未实现的三项新天赋。

- [x] **Step 2: Run regression and confirm RED**

Expected: 缺少 `AlienationPreset`、v2 消息字段和接收档位的解码重载。

- [x] **Step 3: Implement one authoritative alienation policy**

`AlienationPreset.cs`：

```csharp
public enum AlienationPreset
{
    Low = 40,
    Standard = 80,
    High = 120
}

public static class AlienationBudgetPolicy
{
    public static bool IsDefined(AlienationPreset preset) =>
        preset == AlienationPreset.Low ||
        preset == AlienationPreset.Standard ||
        preset == AlienationPreset.High;

    public static int GetLimit(AlienationPreset preset)
    {
        if (!IsDefined(preset))
            throw new ArgumentOutOfRangeException(nameof(preset));
        return (int)preset;
    }

    public static int Calculate(
        DeckConfig deck,
        IEnumerable<string> activeTalentIds,
        TalentRegistry registry)
    {
        deck.CalculateAlienationScore();
        int total = deck.AlienationScore;
        foreach (string id in activeTalentIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            total += registry.GetCost(id);
        return total;
    }
}
```

把 `DeckConfig.CalculateTotalAlienation` 改为委托该策略，禁止在编辑器、房间和运行时各自复制成本循环。

- [x] **Step 4: Upgrade the loadout wire schema to v2**

`PlayerLoadoutMessage` 使用明确字段：

```csharp
public int schemaVersion;
public DeckTileCountMessage[] deckEntries;
public string[] mainTalentSlotIds;
public string[] reserveTalentSlotIds;
```

`TrustedPlayerLoadout.CurrentSchemaVersion = 2`。解码顺序固定为：字段/数量 → 牌库合法性 → 9 槽层级 → 跨 9 槽重复 → ID 存在 → 元数据限制 → 只按主槽计算异化值 → 与房间档位比较。成功结果必须保留规范化后的 `TalentSlotConfig` 和 `TotalAlienation`：

```csharp
public static class PlayerLoadoutErrorCodes
{
    public const string MissingLoadout = "MissingLoadout";
    public const string InvalidDeck = "InvalidDeck";
    public const string InvalidTalent = "InvalidTalent";
    public const string InvalidAlienationPreset = "InvalidAlienationPreset";
    public const string AlienationLimitExceeded = "AlienationLimitExceeded";
    public const string UnsupportedLoadoutVersion = "UnsupportedLoadoutVersion";
}
```

```csharp
public static bool TryDecode(
    PlayerLoadoutMessage message,
    AlienationPreset preset,
    out TrustedPlayerLoadout loadout,
    out string errorCode)
{
    loadout = null;
    errorCode = null;
    if (!AlienationBudgetPolicy.IsDefined(preset))
    {
        errorCode = PlayerLoadoutErrorCodes.InvalidAlienationPreset;
        return false;
    }

    if (message == null)
    {
        errorCode = PlayerLoadoutErrorCodes.MissingLoadout;
        return false;
    }
    if (message.schemaVersion != TrustedPlayerLoadout.CurrentSchemaVersion)
    {
        errorCode = PlayerLoadoutErrorCodes.UnsupportedLoadoutVersion;
        return false;
    }
    if (!TryBuildDeck(message.deckEntries, out DeckConfig deck))
    {
        errorCode = PlayerLoadoutErrorCodes.InvalidDeck;
        return false;
    }
    if (!TryBuildTalents(
            message.mainTalentSlotIds,
            message.reserveTalentSlotIds,
            out TalentSlotConfig talents))
    {
        errorCode = PlayerLoadoutErrorCodes.InvalidTalent;
        return false;
    }
    int total = AlienationBudgetPolicy.Calculate(deck, talents.GetMainIds(), TalentRegistry.Instance);
    if (total > AlienationBudgetPolicy.GetLimit(preset))
    {
        errorCode = PlayerLoadoutErrorCodes.AlienationLimitExceeded;
        return false;
    }

    loadout = new TrustedPlayerLoadout(message.schemaVersion, deck, talents, total);
    return true;
}
```

错误消息带 `actual` 和 `limit` 只发给提交者；不要把精确总值写进公共房间广播。

- [x] **Step 5: Persist only the editor preview preference**

在 `Assets/Scripts/Core/Network/Data/PlayerProfile.cs` 的 `ProfileSettings` 新增：

```csharp
public AlienationPreset SelectedAlienationPreset = AlienationPreset.Standard;
```

`ProfileSettings.Normalize()` 在遇到未定义枚举值时回退 `Standard`，并由 Task 1 已接入的 `PlayerProfile.Normalize()` 调用。此字段只决定大厅/编辑器默认预览和创建房间默认选项，不能替代服务端传入的房间档位。

- [x] **Step 6: Make legacy v1 handling explicit**

协议升级后不静默猜测 v1 网络包。`TryDecode` 对 `schemaVersion != 2` 返回 `PlayerLoadoutErrorCodes.UnsupportedLoadoutVersion`。旧的本地 `SavedDeck` 仍由 Task 1 的归一化逻辑兼容；网络客户端必须用 v2 重新编码。

- [x] **Step 7: Run regression and commit**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core Tests/NetworkRegression; git commit -m 'feat: enforce alienation room presets'"
```

---

### Task 3: Carry the preset through protocol v3 without leaking exact totals

**Files:**

- Modify: `Assets/Scripts/Core/Network/IAccountAuthenticator.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/RoomManager.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Modify: `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`
- Modify: `Assets/Scripts/Core/Network/ClientRoomService.cs`
- Modify: `Assets/Scripts/Core/Network/ClientGameState.cs`
- Test: `Tests/NetworkRegression/IdentityConnectionTests.cs`
- Test: `Tests/NetworkRegression/RoomSessionTests.cs`
- Test: `Tests/NetworkRegression/SnapshotReconnectTests.cs`

- [x] **Step 1: Add failing protocol and privacy regressions**

覆盖以下行为：

```csharp
runner.Check(NetworkProtocol.Version == 3, "talent loadout rollout uses protocol v3");

CreateRoomMessage create = new CreateRoomMessage
{
    gameMode = (int)GameMode.HalfGame,
    alienationPreset = (int)AlienationPreset.Standard,
    loadout = BuildValidLoadoutMessage()
};

Room room = fixture.CreateRoom(create);
runner.Check(room.AlienationPreset == AlienationPreset.Standard,
    "room locks the selected alienation preset");

RoomSeatMessage opponentView = fixture.GetSeatMessageFor(observerSeat: 1, subjectSeat: 0);
runner.Check(!opponentView.HasExactAlienationField(),
    "public seat projection does not contain exact alienation");

RoomJoinedMessage ownJoin = fixture.GetJoinedMessage(seat: 0);
runner.Check(ownJoin.ownTotalAlienation == fixture.Seat0Total,
    "owner receives its own exact total privately");
```

`HasExactAlienationField()` 可在测试中用 JSON 序列化并断言不存在 `totalAlienation`；产品代码不需要该辅助方法。

- [x] **Step 2: Run regression and confirm RED**

Expected: 协议仍为 v2、创建房间没有档位、公共席位消息仍包含精确总异化值。

- [x] **Step 3: Upgrade the handshake and room-create contract**

把 `NetworkProtocol.Version` 改为 `3`。`CreateRoomMessage` 增加非空 `AlienationPreset`；`JoinRoomMessage` 不重复提交档位，它只能接受房间既有档位。`RoomManager` 创建时校验枚举值，然后传入：

```csharp
Room room = new Room(
    roomId,
    (GameMode)message.gameMode,
    (AlienationPreset)message.alienationPreset,
    hostConnectionId,
    aiFill,
    messageCacheSize);
```

`Room` 暴露只读 `AlienationPreset`。创建者和后续加入者的构筑都调用 `PlayerLoadoutCodec.TryDecode(message.loadout, room.AlienationPreset, out loadout, out errorCode)`。

- [x] **Step 4: Split public preset from private exact totals**

消息字段固定为：

```csharp
public sealed class RoomJoinedMessage
{
    public int alienationPreset;
    public int ownTotalAlienation;
    // existing room and seat fields
}

public sealed class RoomSeatMessage
{
    public int seatIndex;
    public string displayName;
    public bool isReady;
    public bool isAi;
    // no totalAlienation field
}
```

`RoomGameSnapshot` 顶层携带公共 `AlienationPreset`，另在仅发给本家的私有段携带 `OwnTotalAlienation`。`ClientGameState` 原子应用这两个字段；不得从其他席位天赋或牌库反推精确值。

- [x] **Step 5: Reject invalid and over-budget joins before mutating room state**

`RoomManager`/`Room` 必须先解码成功，再分配席位或变更 ready 状态。为低档位超限加入新增回归：返回 `AlienationLimitExceeded`，房间人数、连接绑定、席位消息序列均不变。

- [x] **Step 6: Update client encoding and reconnect snapshot assertions**

`ClientRoomService` 始终发送 schema v2 的主/备选数组；创建房间默认使用 `ProfileSettings.SelectedAlienationPreset`。重连快照测试验证：档位恢复、本人精确总值恢复、他人精确值仍不可见。

- [x] **Step 7: Run regression and commit**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: carry alienation preset in protocol v3'"
```

---

### Task 4: Build a Room-owned, cross-round talent runtime

**Files:**

- Create: `Assets/Scripts/Talent/TalentRuntimeState.cs`
- Create: `Assets/Scripts/Talent/TalentRuntimeEvent.cs`
- Create: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Modify: `Assets/Scripts/Talent/TalentRule.cs`
- Modify: `Assets/Scripts/Talent/TalentContext.cs`
- Test: `Tests/NetworkRegression/TalentFoundationTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`

- [x] **Step 1: Add a failing lifecycle regression with an instrumented test talent**

测试天赋必须记录 match/round 次数，并跨两个小局保留 match counter：

```csharp
[TalentRule("network_test_lifecycle", "Lifecycle", "test", TalentTier.Small, 0,
    TalentPhase.OnDraw, StateScope = TalentStateScope.Match)]
private sealed class LifecycleTestTalent : TalentRule
{
    public override int GetMatchStartScoreDelta(TalentMatchContext context) => 7;

    public override void OnRoundStarted(TalentRoundContext context)
    {
        context.State.IncrementCounter("round_started", TalentStateScope.Round);
        context.State.IncrementCounter("match_rounds", TalentStateScope.Match);
    }

    public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome)
    {
        context.State.SetFlag(
            "last_round_won",
            outcome.WinnerSeatIndex == context.OwnerSeatIndex,
            TalentStateScope.Match);
    }
}
```

构造 runtime，依次 `BeginMatch`、`BeginRound`、`EndRound` 两次，断言开场加分只发生一次、match counter 为 2、round-scope 状态在第二局前清空。为避免把玩家 ID 和席位混为一谈，`TalentRoundOutcome` 同时存 `WinnerSeatIndex`，规则通过上下文的 `OwnerSeatIndex` 判断。

- [x] **Step 2: Run regression and confirm RED**

Expected: 缺少 runtime、state、event 和新生命周期钩子。

- [x] **Step 3: Implement explicit state and event value objects**

`TalentRuntimeState` 只提供类型明确的状态操作：

```csharp
public sealed class TalentRuntimeState
{
    private readonly Dictionary<string, int> _matchCounters = new();
    private readonly Dictionary<string, int> _roundCounters = new();
    private readonly HashSet<string> _matchFlags = new();
    private readonly HashSet<string> _roundFlags = new();

    public bool IsActive { get; internal set; }
    public bool IsRevealed { get; internal set; }

    public int GetCounter(string key, TalentStateScope scope);
    public void SetCounter(string key, int value, TalentStateScope scope);
    public int IncrementCounter(string key, TalentStateScope scope, int amount = 1);
    public bool GetFlag(string key, TalentStateScope scope);
    public void SetFlag(string key, bool value, TalentStateScope scope);
    internal void ResetRoundState();
}
```

不暴露底层字典，不把状态序列化格式耦合给天赋实现。事件模型：

```csharp
public enum TalentEventVisibility { OwnerOnly, Public }

public sealed class TalentRuntimeEvent
{
    public long EventId { get; set; }
    public int OwnerSeatIndex { get; set; }
    public string TalentId { get; set; }
    public string EventType { get; set; }
    public TalentEventVisibility Visibility { get; set; }
    public int Value { get; set; }
}
```

事件中的 `TalentId` 用于显示和网络身份，不用于在 `Room`/`GameServer` 选择效果代码。

- [x] **Step 4: Add typed lifecycle contexts and rule hooks**

在 `TalentRule` 增加：

```csharp
public int OwnerSeatIndex { get; internal set; }
public virtual void InitializeMatchState(TalentMatchContext context) { }
public virtual int GetMatchStartScoreDelta(TalentMatchContext context) => 0;
public virtual void OnRoundStarted(TalentRoundContext context) { }
public virtual void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome) { }
public virtual int GetRoundStartPeekCount(TalentRoundContext context) => 0;
public virtual void ConfigureScoring(TalentScoringContext context, ScoringOptions options) { }
```

所有 context 至少包含 `OwnerSeatIndex`、只读 `GameSession`/场况、该实例的 `TalentRuntimeState` 和 `Emit(TalentRuntimeEvent)`；不允许规则直接访问 `GameManager.Instance`、HUD 或房间 socket。

同时把现有容易与协议字符串 `playerId` 混淆的 `TalentRule.OwnerPlayerId` 重命名为 `OwnerSeatIndex`，把 `TalentRegistry.CreateInstance(string id, int ownerPlayerId)` 参数同步改为 `ownerSeatIndex`。这是席位索引，不是账号身份；网络层稳定 `playerId` 不进入天赋规则。

`TalentRoundOutcome` 固定字段：

```csharp
public sealed class TalentRoundOutcome
{
    public int? WinnerSeatIndex { get; set; }
    public int? DiscarderSeatIndex { get; set; }
    public bool IsDraw => !WinnerSeatIndex.HasValue;
    public int FinalFan { get; set; }
}
```

- [x] **Step 5: Implement `TalentMatchRuntime` as the sole lifecycle coordinator**

构造函数输入每席位的 `TalentSlotConfig` 和 `TalentRegistry`，实例化全部 9 个 carried entries，只有开场主槽设为 active；这样中场启用备选时不会临时创造第二套身份。同一实例跨小局复用：

```csharp
public TalentMatchRuntime(
    IReadOnlyDictionary<int, TalentSlotConfig> loadouts,
    TalentRegistry registry)
{
    foreach ((int seat, TalentSlotConfig config) in loadouts)
    {
        HashSet<string> activeIds = new HashSet<string>(config.GetMainIds(), StringComparer.Ordinal);
        foreach (string id in config.GetCarriedIds())
        {
            TalentRule rule = registry.CreateInstance(id, seat);
            _entries.Add(new RuntimeEntry(
                seat,
                rule,
                new TalentRuntimeState { IsActive = activeIds.Contains(id) }));
        }
    }
}
```

`BeginMatch` 只能成功一次；重复调用抛 `InvalidOperationException`，以便尽早暴露 Room 生命周期错误。它先对全部 carried entries 调用 `InitializeMatchState`，再只对 active entries 应用 `GetMatchStartScoreDelta`，保证备选天赋可以初始化次数但不会提前产生效果。active 且 `RevealPolicy == PublicAtMatchStart` 的 entry 在这里永久设为 `IsRevealed=true` 并产生公开事件。`BeginRound` 先清理所有 round-scope 数据，再只调用 active 规则。`ApplyWallBuilding` 发生在洗牌前；GameServer 洗牌后调用 `ResolvePostShuffle`，runtime 此时才根据 `GetRoundStartPeekCount` 读取牌山顶部并保存各席私有结果。`ApplyDraw`/`ApplyDiscard` 维持现有优先级稳定排序和管道链式返回。`BuildScoringOptions` 创建新实例后依次调用规则，不能复用上次胡牌的可变对象。`DrainEventsForSeat` 根据公开/私有可见性过滤并保证事件 ID 单调递增。

- [x] **Step 6: Verify two-round persistence and event privacy**

扩充测试：公共事件四席可见；OwnerOnly 只有本家可见；读取一席事件不能让其他席丢失。实现采用每席游标或按席缓存，不能使用全局 destructive dequeue。

- [x] **Step 7: Run regression and commit**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Talent Tests/NetworkRegression; git commit -m 'feat: add cross-round talent match runtime'"
```

---

### Task 5: Migrate the existing six talents into the sole Room-owned runtime

**Files:**

- Modify: `Assets/Scripts/Talent/Impl/MidasTouchTalent.cs`
- Modify: `Assets/Scripts/Talent/Impl/PeekTalent.cs`
- Modify: `Assets/Scripts/Talent/Impl/DragonAscentTalent.cs`
- Modify: `Assets/Scripts/Talent/Impl/DrawRewardTalent.cs`
- Modify: `Assets/Scripts/Talent/Impl/HeadStartTalent.cs`
- Modify: `Assets/Scripts/Talent/Impl/StartingCapitalTalent.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Delete: `Assets/Scripts/Talent/TalentManager.cs`
- Delete: `Assets/Scripts/Core/Network/SessionTalentPolicy.cs`
- Test: `Tests/NetworkRegression/TalentFoundationTests.cs`
- Test: `Tests/NetworkRegression/RoomSessionTests.cs`
- Test: `Tests/NetworkRegression/SnapshotReconnectTests.cs`

- [x] **Step 1: Add failing behavior regressions for all six talents across two rounds**

建立一场两局测试，至少断言：

```csharp
runner.Check(scoreAfterMatchStart == initialScore + 30,
    "starting capital applies exactly once per match");
runner.Check(peekNotificationsByRound.SequenceEqual(new[] { 4, 4 }),
    "peek refreshes privately at each round start");
runner.Check(drawRewardDeltas.SequenceEqual(new[] { 5, 5 }),
    "draw reward applies at each drawn round");
runner.Check(firstScoringOptions.BonusFan == 2 && secondScoringOptions.BonusFan == 2,
    "head start configures scoring every round");
runner.Check(firstScoringOptions.RelaxedPureStraight,
    "dragon ascent configures relaxed pure straight polymorphically");
runner.Check(transformedTile.Suit == Suit.Dragon && transformedTile.Value == 2,
    "midas touch remains in the draw pipeline");
```

再断言公开条件：点金后的牌仍在暗手时不揭示、进入牌河/副露后揭示；`HeadStart` 在接受胡牌时揭示；`DragonAscent` 只有反事实重算证明它改变合法性或番数时才揭示；`Peek` 永不进入他席公开投影。

再加源码守卫：读取 `Room.cs`、`GameServer.cs`，断言不包含六个稳定 ID 的字符串字面量。测试只防回归，不替代代码审查。

- [x] **Step 2: Run regression and confirm RED**

Expected: runtime 尚未由 `Room` 持有，现有硬编码策略仍使跨局次数或源码守卫失败。

- [x] **Step 3: Move each effect into its rule override**

实现映射固定如下：

```csharp
// StartingCapitalTalent
public override int GetMatchStartScoreDelta(TalentMatchContext context) => 30;

// PeekTalent
public override int GetRoundStartPeekCount(TalentRoundContext context) => 4;

// DrawRewardTalent
public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome)
{
    if (outcome.IsDraw)
        context.ApplyScoreDelta(5, "draw_reward");
}

// HeadStartTalent
public override void ConfigureScoring(TalentScoringContext context, ScoringOptions options)
{
    options.BonusFan += 2;
}

// DragonAscentTalent
public override void ConfigureScoring(TalentScoringContext context, ScoringOptions options)
{
    options.RelaxedPureStraight = true;
}
```

`MidasTouchTalent` 保持 `OnDraw` 牌变换，但公开事件只能在变换后的牌进入公共区域时由后续系统发出；摸到手中时不得广播天赋和牌面。

为此，GameServer 在牌真正进入牌河或公开副露后统一调用 `NotifyTileBecamePublic`。runtime 读取 `IsModified/SpecialEffectID` 定位来源 entry 并按其 reveal policy 发事件；这里只用 ID 找到身份，不执行效果分支。

- [x] **Step 4: Make Room own the runtime for the whole session**

`Room` 在四席构筑锁定、比赛启动之前创建一次：

```csharp
_talentRuntime = new TalentMatchRuntime(BuildTalentLoadoutsBySeat(), TalentRegistry.Instance);
_talentRuntime.BeginMatch(_gameSession);
BroadcastTalentEventsAtSafeBoundary();
```

每次 `StartRound()` 新建 `GameServer` 时传入同一 `_talentRuntime`。`OnRoundFinished` 的顺序固定为：

1. runtime `EndRound(outcome, Session)`；
2. 向各席发送过滤后的事件和分数变化；
3. `Session.AdvanceRound()`；
4. 决定进入下一小局或整场结束。

这样 `DrawReward` 使用的是刚结束小局的结果，而 `StartingCapital` 不会因重建 `GameServer` 再触发。

- [x] **Step 5: Replace `GameServer` hardcoding with runtime calls**

删除 `GameServer.StartGame` 中对 `HeadStart`、`DragonAscent`、`Peek` 的 ID 查询，改为：

```csharp
_talentRuntime.BeginRound(roundContext);
_talentRuntime.ApplyWallBuilding(wallContext);
_wallService.Shuffle();
_talentRuntime.ResolvePostShuffle(postShuffleContext);
SendPrivatePeekResults(_talentRuntime);

TileData finalTile = _talentRuntime.ApplyDraw(drawContext, rawTile);
ScoringOptions options = _talentRuntime.BuildScoringOptions(scoringContext);
```

胡牌被服务端正式接受后，调用 `ResolveAcceptedWinVisibility`。`TalentAcceptedWinContext` 提供最终结果以及“排除某一 rule 后重建 options 并重算”的只读委托；runtime 只对 active、尚未揭示的 scoring rules 做反事实重算。排除后合法性或最终番数发生变化才揭示该规则，因此 `DragonAscent` 不会因为普通清龙误揭示，`HeadStart` 的 +2 会在实际胡牌时揭示。候选胡牌检查、听牌提示和恢复快照不得触发 reveal。

流局结算不再检查 `DrawReward` ID；统一由 `Room` 调用 `EndRound`。`GameServer` 仍负责权威发牌、动作校验和算番，不拥有 runtime 生命周期。客户端只消费 `TalentRuntimeEvent`、分数和快照，不调用任何 runtime 生命周期方法。

- [x] **Step 6: Remove obsolete managers and update explicit project includes**

确认所有调用点迁移后删除 `TalentManager.cs`、`SessionTalentPolicy.cs` 及 `.meta`，从 `NetworkRegression.csproj` 移除旧 `<Compile Include>`，加入新 runtime 文件。运行：

```powershell
pwsh -NoLogo -NoProfile -Command "rg -n 'TalentManager|SessionTalentPolicy|head_start|dragon_ascent|peek|draw_reward|starting_capital|midas_touch' Assets/Scripts/Core/Network/Room.cs Assets/Scripts/Core/Network/GameServer.cs Assets/Scripts/Core/GameManager.cs"
```

Expected: 不出现旧类型；稳定 ID 不出现在 `Room.cs` 或 `GameServer.cs`。`GameManager` 也不应包含效果型 ID 分支。

- [x] **Step 7: Run full verification and commit**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "dotnet build Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

如果 Unity 编辑器可用，再执行一次无界面脚本编译或打开项目确认无 Console 编译错误；不要把仅有 .NET 回归通过描述成 Unity 已验证。

Commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts Tests/NetworkRegression; git commit -m 'refactor: migrate talents to match runtime'"
```

---

## Plan 1 Completion Gate

只有以下条件全部满足，才能开始第二份计划：

- [x] 协议版本为 v3，构筑网络 schema 为 v2。
- [x] 40/80/120 三档预算由服务端统一校验，备选槽未激活时不计成本。
- [x] 公共房间消息不包含其他玩家精确总异化值。
- [x] `Room` 每场只创建一次 `TalentMatchRuntime`，`GameManager` 不创建、不持有也不调用 runtime。
- [x] 六个现有天赋在跨两小局回归中保持设计次数。
- [x] `Room.cs`、`GameServer.cs` 无六天赋 ID 效果分支。
- [x] 网络回归、构建和 `git diff --check` 全部通过。
- [x] `git status --short` 只显示预期文件，且不存在占位注释、未实现异常、空方法或假成功返回。
