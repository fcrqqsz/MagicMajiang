# Phase 3 Layered Tactical Talent UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有权威联机链路上完成卡组档位、分层牌桌反馈、主动天赋操作、原版中场战术桌、逐天赋结算、AI 和遥测，使 Phase 3 合并回 `master` 后可以统一进行 Unity 与真人联机玩法验收。

**Architecture:** `Room` / `GameServer` / `TalentMatchRuntime` 继续是唯一比赛权威；客户端 UI 只消费 `ClientRoomState`、`ClientGameState` 和纯 C# presentation policy。先升级卡组档位和逐天赋结算的权威数据，再按卡组/大厅、HUD、ActionPanel、中场、结算的顺序接 UI；AI 使用与真人相同的 runtime 校验路径，反馈只消费有序实时事件，恢复快照保持静默。

**Tech Stack:** Unity 2022.3.61t9（团结引擎 1.6.8）、C#、UI Toolkit UXML/USS、DOTween Pro、WebSocket 协议、有序 `SeatMessageStream`、纯 C# `Tests/NetworkRegression`、WAV PCM 占位音。

本计划取代 `docs/superpowers/plans/2026-08-04-talent-ui-ai-feedback.md` 的 Phase 3 执行任务；旧计划只作为历史设计来源，出现冲突时以本计划和已批准的 2026-08-12 设计补充为准。

## Global Constraints

- 本计划实现依据：`docs/superpowers/specs/2026-08-12-talent-phase3-layered-tactical-ui-design.md`。
- UI 只能使用 UI Toolkit；禁止 Canvas/UGUI。
- UI Toolkit 字体统一使用 `Assets/Font/MSYH_UITK.asset`，不得引用 TTC 或 TMP `MSYH_SDF.asset`。
- 客户端不得读取 `Room`、`GameServer` 或 `TalentMatchRuntime`，不得推导权威 active 集合、番数、分数或预算结果。
- 服务端不得依赖 `GameManager.Instance`、UI 控制器、HUD 或场景对象。
- 所有房间内实时消息继续经过每席独立 `SeatMessageStream`；恢复快照不重播 Toast、事件流、音效、Tween 或临时选择器。
- 本家可以看到完整九个携带天赋、生效状态、私有动态值和精确异化值；他家只能看到已揭示天赋和最后公开值，中场后 active 状态仍保密。
- 每套卡组保存自己的 Low 40 / Standard 80 / High 120 档位；超限卡组允许保存，但创建和加入房间必须被权威阻止。
- 中场沿用原版全屏战术桌；九个携带天赋点击切换，生效数量不固定为 6，`MainOnlyLocked` 不可停用。
- 最终番置顶；基础番和每项天赋增减逐条显示；客户端诊断和不一致时不得覆盖服务器 `finalFan`。
- 强反馈首期只由服务器确认“主动天赋玩法效果已应用”触发；请求 Accepted 但被阻挡不触发强反馈。
- 所有 DOTween 动画必须链式 `.SetLink(gameObject)`。
- 每个新建的 `Assets/**` C#、UXML、USS、WAV 或目录资源必须在同一任务提交唯一 Unity `.meta`；提交前扫描 GUID 不重复，不依赖后续 Unity 自动补文件。
- Phase 3 功能提交留在 `codex/talent-actions-ui-unified`；完成检查后先汇报，不得自行 merge、push 或切换 `master`。用户确认后才合并回 `master` 并进行 Unity/真人验收。
- Windows PowerShell 命令使用 `pwsh -NoLogo -NoProfile -Command`。

## File and Interface Map

### Authority and protocol

- `Assets/Scripts/Core/Network/Data/SavedDeck.cs`: 持久化每套卡组的 `AlienationPreset`，旧存档规范化为 Standard。
- `Assets/Scripts/Core/Network/PlayerLoadoutCodec.cs`: 构筑 schema v3，编解码卡组档位并验证期望房间档位。
- `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`: 卡组档位、结构化房间错误、主动效果应用标志和逐天赋结算 wire DTO。
- `Assets/Scripts/Core/ScoringOptions.cs`: 权威逐天赋贡献模型。
- `Assets/Scripts/Talent/TalentMatchRuntime.cs`: 稳定顺序归因、主动效果实际应用事件。
- `Assets/Scripts/Core/Network/GameServer.cs`: 生成最终番及逐天赋贡献，落入 PlayerWin 与 Room 结果源。
- `Assets/Scripts/Core/Network/Room.cs`: 向各席发送隐私安全的实时事件、结果和恢复数据。
- `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`: 恢复同一份权威结算与 sideboard 状态。
- `Assets/Scripts/Core/Network/ClientGameState.cs`: 原子投影逐天赋结算和天赋状态。

### Pure presentation policies

- `Assets/Scripts/Core/AlienationGaugePolicy.cs`: 单表盘数值、超限和文案 view model。
- `Assets/Scripts/Core/RoomLoadoutAdmissionPresentationPolicy.cs`: 创建/加入的本地预检与结构化错误文案。
- `Assets/Scripts/Core/TalentHudProjectionPolicy.cs`: 本家常驻/折叠、对手前两项/`+N`、隐私排序。
- `Assets/Scripts/Core/TalentEventPresentationPolicy.cs`: weak/medium/strong 分类、Toast/feed/audio 决策和未知事件兜底。
- `Assets/Scripts/Core/TalentActionPanelPolicy.cs`: 补充动作选择、单按钮 pending 和基础动作保持。
- `Assets/Scripts/Core/SideboardDraftPolicy.cs`: 本地草稿复制、开关、预算、锁定和提交 view model。
- `Assets/Scripts/Core/TalentResultPresentationPolicy.cs`: 最终番、基础番和逐天赋行的纯展示投影。

### UI and audio

- `Assets/UI/DeckEditorView.uxml`, `Assets/UI/DeckEditorStyles.uss`, `Assets/UI/DeckEditorToolkit.cs`: 6+3 和卡组所属档位。
- `Assets/UI/MainLobby.uxml`, `Assets/UI/MainLobbyStyles.uss`, `Assets/UI/LobbyController.cs`: 创建档位、房间档位和明确阻止信息。
- `Assets/UI/TalentChipTemplate.uxml`, `Assets/UI/TalentChipTemplate.uss`: 可复用 Chip。
- `Assets/UI/GameHUD/GameHUD.uxml`, `Assets/UI/GameHUD/GameHUDStyles.uss`, `Assets/UI/GameHUD/GameHUDController.cs`: B 型分层密度、Toast、feed、audio。
- `Assets/UI/ActionPanel.uxml`, `Assets/UI/ActionPanelStyles.uss`, `Assets/UI/ActionPanelController.cs`: 主动天赋按钮行。
- `Assets/UI/FloatingTilePanelController.cs`: 截流目标选择模式。
- `Assets/UI/SideboardPanel.uxml`, `Assets/UI/SideboardPanelStyles.uss`, `Assets/UI/SideboardPanelController.cs`: 原版全屏战术桌。
- `Assets/UI/ResultPanel.uxml`, `Assets/UI/ResultPanelStyles.uss`, `Assets/UI/ResultPanelController.cs`: 最终番置顶和贡献行。
- `Tools/GenerateTalentPlaceholderAudio.ps1`: 可复现生成占位音。
- `Assets/Audio/SFX/Talent/talent_active_generic.wav`: 0.6–0.8 秒通用主动成功音。

### AI, telemetry, tests

- `Assets/Scripts/Core/Agents/AiTalentLoadoutFactory.cs`: 按房间档位生成合法 6+3 AI 构筑。
- `Assets/Scripts/Core/Agents/AiTalentDecisionPolicy.cs`: 公开信息范围内选择主动动作和中场方案。
- `Assets/Scripts/Talent/TalentTelemetry.cs`: 无个人/暗手数据的 JSON line 记录。
- `Tests/NetworkRegression/TalentPresentationTests.cs`: 卡组、HUD、ActionPanel、结果、音频头验证。
- `Tests/NetworkRegression/TalentResultAttributionTests.cs`: 权威逐天赋归因、协议与快照。
- `Tests/NetworkRegression/AiTalentPolicyTests.cs`: AI 构筑、主动动作、中场和 telemetry。

---

### Task 1: Make alienation preset part of each saved and submitted loadout

**Files:**

- Modify: `Assets/Scripts/Core/Network/Data/SavedDeck.cs`
- Modify: `Assets/Scripts/Core/Network/Data/PlayerProfile.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/PlayerLoadoutCodec.cs`
- Modify: `Assets/Scripts/Core/Network/PlayerLoadoutErrorCodes.cs`
- Modify: `Assets/Scripts/Core/Network/IAccountAuthenticator.cs`
- Modify: `Assets/Scripts/Core/Network/RoomManager.cs`
- Modify: `Assets/Scripts/Core/Network/RoomErrorPresentationPolicy.cs`
- Modify: `Assets/Scripts/Core/Network/ClientRoomService.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

**Interfaces:**

- Produces: `SavedDeck.AlienationPreset`, `PlayerLoadoutMessage.alienationPreset`, `TrustedPlayerLoadout.AlienationPreset`, `PlayerLoadoutErrorCodes.AlienationPresetMismatch`。
- Produces: `ClientRoomService.CreateRoom(GameMode gameMode, AlienationPreset roomPreset, string nickname, string address = null)`。
- Produces: structured `RoomErrorMessage` fields `loadoutAlienationPreset`, `roomAlienationPreset`, `actual`, `limit`。
- Consumes: existing `AlienationBudgetPolicy`, `TalentSlotConfig`, `SeatMessageStream` admission flow。

- [ ] **Step 1: Add RED tests for saved-deck migration and wire schema v3**

Add `TalentPresentationTests.RunLoadoutPresetTests` and call it from `Run`:

```csharp
var legacy = new SavedDeck { Config = DeckConfig.CreateStandard(), Talents = new TalentSlotConfig() };
legacy.Normalize();
runner.Check(legacy.AlienationPreset == AlienationPreset.Standard,
    "legacy saved decks default to Standard without changing their contents");

var lowDeck = new SavedDeck
{
    Config = DeckConfig.CreateStandard(),
    Talents = new TalentSlotConfig(),
    AlienationPreset = AlienationPreset.Low
};
PlayerLoadoutMessage wire = PlayerLoadoutCodec.CreateMessage(
    lowDeck.Config, lowDeck.Talents, lowDeck.AlienationPreset);
runner.Check(wire.schemaVersion == 3 && wire.alienationPreset == (int)AlienationPreset.Low,
    "loadout schema v3 carries the saved deck preset");
```

Also assert an undefined persisted enum normalizes to Standard, while Low remains Low.

- [ ] **Step 2: Run the focused suite and verify RED**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-presentation"
```

Expected: compile failure for missing `SavedDeck.AlienationPreset` and the three-argument `CreateMessage`, not an unrelated baseline failure.

- [ ] **Step 3: Implement saved-deck normalization and schema v3**

Use the explicit enum field so missing JSON deserializes to zero and `Normalize` can migrate it:

```csharp
public AlienationPreset AlienationPreset = AlienationPreset.Standard;

public void Normalize()
{
    Talents ??= new TalentSlotConfig();
    Talents.Normalize();
    if (!AlienationBudgetPolicy.IsDefined(AlienationPreset))
        AlienationPreset = AlienationPreset.Standard;
}
```

Remove `ProfileSettings.SelectedAlienationPreset`; during `PlayerProfile.Normalize`, normalize every saved deck and keep all other settings unchanged. Set `TrustedPlayerLoadout.CurrentSchemaVersion = 3`, add the preset to `PlayerLoadoutMessage` and trusted clone/create/decode paths.

- [ ] **Step 4: Add RED server admission tests for mismatch versus over-cap**

Through real `GameEndpoint -> RoomManager`, cover:

```csharp
runner.Check(mismatch.code == PlayerLoadoutErrorCodes.AlienationPresetMismatch
    && mismatch.loadoutAlienationPreset == (int)AlienationPreset.Low
    && mismatch.roomAlienationPreset == (int)AlienationPreset.Standard,
    "create reports the saved-deck and requested-room preset without conflating budget");

runner.Check(over.code == PlayerLoadoutErrorCodes.AlienationLimitExceeded
    && over.actual == 45 && over.limit == 40,
    "matching preset still reports an independent over-cap rejection");
```

Repeat mismatch coverage for JoinRoom against an existing Standard room. Assert no room/seat is created on failure.

- [ ] **Step 5: Implement authoritative preset matching and structured errors**

`PlayerLoadoutCodec.TryDecode(message, expectedPreset, ...)` order is fixed:

1. schema and message preset are defined;
2. message preset equals `expectedPreset`, otherwise `AlienationPresetMismatch`;
3. deck/talent slots are rebuilt;
4. total is calculated and compared with the matched limit.

`RoomManager` sends both preset values for mismatch and `actual/limit` for over-cap. `RoomErrorPresentationPolicy` maps them to Chinese UI text without parsing arbitrary server strings.

- [ ] **Step 6: Add RED client command tests**

Tests require CreateRoom to accept an explicit room preset while the wire loadout carries the selected deck's distinct saved preset. JoinRoom carries the saved preset. Cover default Standard when no saved decks exist and prevent an invalid selected index from sending. Run the focused test and require a missing-signature/field RED.

- [ ] **Step 7: Implement the client command changes**

Change CreateRoom to the interface declared above. `TryBuildSelectedLoadout` always serializes the selected deck's own preset; JoinRoom sends the same saved preset and lets the server compare it with the unknown room preset. Remove `GetSelectedAlienationPreset` and all reliance on `ProfileSettings.SelectedAlienationPreset`.

- [ ] **Step 8: Bump protocol and verify rejection boundaries**

Because `PlayerLoadoutMessage`, `RoomErrorMessage`, and later PlayerWin payloads change in Phase 3, set `NetworkProtocol.Version = 4`. Update protocol regression expectations; protocol v3 must fail at Hello, schema v2 must fail as `UnsupportedLoadoutVersion` when tested directly.

- [ ] **Step 9: Run full regression and commit**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/Network Assets/Scripts/Core/Network/Data Tests/NetworkRegression; git commit -m 'feat: bind alienation presets to saved loadouts'"
```

Expected: all regressions pass; only Task 1 files are staged.

---

### Task 2: Build the 6+3 deck editor and explicit room admission UI

**Files:**

- Create: `Assets/Scripts/Core/AlienationGaugePolicy.cs`
- Create: `Assets/Scripts/Core/RoomLoadoutAdmissionPresentationPolicy.cs`
- Modify: `Assets/UI/DeckEditorView.uxml`
- Modify: `Assets/UI/DeckEditorStyles.uss`
- Modify: `Assets/UI/DeckEditorToolkit.cs`
- Modify: `Assets/UI/MainLobby.uxml`
- Modify: `Assets/UI/MainLobbyStyles.uss`
- Modify: `Assets/UI/LobbyController.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`

**Interfaces:**

- Consumes: Task 1 `SavedDeck.AlienationPreset`, new CreateRoom signature and structured error fields。
- Produces: `AlienationGaugePolicy.Build(int deckCost, int talentCost, AlienationPreset preset)` and `RoomLoadoutAdmissionPresentationPolicy.Validate(AlienationPreset loadoutPreset, AlienationPreset roomPreset, int total)` view models used only by presentation code。

- [ ] **Step 1: Add RED pure-policy tests for gauge and admission copy**

```csharp
AlienationGaugeView over = AlienationGaugePolicy.Build(
    deckCost: 28, talentCost: 17, AlienationPreset.Low);
runner.Check(over.Total == 45 && over.Limit == 40 && over.Fill01 == 1f
    && over.Overflow == 5 && over.CanSave,
    "over-cap decks remain saveable while exposing the exact overflow");

RoomLoadoutAdmissionView mismatch = RoomLoadoutAdmissionPresentationPolicy.Validate(
    AlienationPreset.Low, AlienationPreset.Standard, total: 35);
runner.Check(!mismatch.CanEnter && mismatch.Code == PlayerLoadoutErrorCodes.AlienationPresetMismatch
    && mismatch.Message.Contains("低异化 40") && mismatch.Message.Contains("标准 80"),
    "room admission shows both mismatched presets");
```

Also test exact-limit safe, over-cap matching preset, invalid preset fallback only for display, and that policy does not mutate `SavedDeck`.

- [ ] **Step 2: Run focused tests and confirm RED**

Expected: missing policy types.

- [ ] **Step 3: Implement pure view models without Unity dependencies**

`AlienationGaugeView` contains `Total`, `Limit`, `Fill01`, `Overflow`, `IsOverLimit`, `CanSave=true`, `DeckCost`, and `TalentCost`. `Build` clamps both cost inputs to non-negative values, computes `Total = DeckCost + TalentCost`, and falls back to Standard only when the enum is undefined. `RoomLoadoutAdmissionView` contains `CanEnter`, stable `Code`, and final Chinese `Message`; it never sends commands or changes profiles.

- [ ] **Step 4: Add the editor UXML structure and generate all nine slots**

Add unique query names:

```xml
<ui:VisualElement name="AlienationPresetSelector" class="alienation-selector">
    <ui:Button name="BtnPresetPrev" text="‹" class="preset-arrow" />
    <ui:Label name="PresetLabel" text="标准 80" class="preset-label" />
    <ui:Button name="BtnPresetNext" text="›" class="preset-arrow" />
</ui:VisualElement>
<ui:VisualElement name="AlienationTrack" class="alienation-track">
    <ui:VisualElement name="AlienationFill" class="alienation-fill" />
</ui:VisualElement>
<ui:Label name="AlienationBreakdownLabel" />
<ui:Label name="AlienationWarning" class="alienation-warning" />
<ui:VisualElement name="MainTalentSlots" />
<ui:VisualElement name="ReserveTalentSlots" />
```

Move the existing `ScoreText` responsibility instead of leaving duplicate IDs. Main labels are 大 ×1 / 中 ×2 / 小 ×3; reserve labels are 备选中 ×1 / 备选小 ×2. Main selection uses `CanEquip`; reserve uses `CanEquipReserve`; duplicate IDs and disallowed metadata are disabled.

- [ ] **Step 5: Bind preset switching to the current saved deck**

Cycle Low -> Standard -> High -> Low into a controller draft field `_currentAlienationPreset`, recalculate the gauge, and do not mutate the selected `SavedDeck` yet. `OnSaveClicked` writes config, talents, preset and total together, then saves the profile. Switching decks discards the unsaved preset draft and loads the newly selected deck's saved preset. Saving remains enabled when total tiles are 34 even if over-cap; warning remains visible and card list shows `超限 X`.

- [ ] **Step 6: Add create-room selector and explicit blockers**

Place a room preset selector under GameMode. It changes only the pending room configuration, not the selected `SavedDeck`. Before CreateRoom, run the policy: mismatch or over-cap shows the blocking panel and does not call service. JoinRoom cannot know the room preset from an ID alone, so it sends the saved deck and maps the authoritative structured server rejection on receipt.

Room view shows public `异化档位：标准 80` and private `本家异化：45 / 80`; remove exact opponent totals.

- [ ] **Step 7: Add source-shape and policy regressions**

Assert UXML query IDs are unique, there are separate main/reserve containers, Save is not disabled by `IsOverLimit`, and `LobbyController` never assigns a room preset back to `SavedDeck.AlienationPreset`.

- [ ] **Step 8: Run regression, diff check, and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/AlienationGaugePolicy.cs Assets/Scripts/Core/RoomLoadoutAdmissionPresentationPolicy.cs Assets/UI Tests/NetworkRegression; git commit -m 'feat: add per-deck alienation UI'"
```

---

### Task 3: Attribute the accepted win to stable per-talent contribution rows

**Files:**

- Modify: `Assets/Scripts/Core/MahjongLogic.cs`
- Modify: `Assets/Scripts/Core/ScoringOptions.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Modify: `Assets/Scripts/Core/Agents/IPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Agents/LocalPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Agents/SimpleAIClient.cs`
- Modify: `Assets/Scripts/Core/Network/RemotePlayerClient.cs`
- Modify: `Assets/Scripts/Core/Network/StableSeatController.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Modify: `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`
- Modify: `Assets/Scripts/Core/Network/ClientGameState.cs`
- Test: `Tests/NetworkRegression/TalentResultAttributionTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

**Interfaces:**

- Produces: `TalentFanContribution { TalentId, FanDelta, Category, Sequence }`, `TalentFanResolution.BaseFan`, `Contributions`, and `TalentMatchRuntime.ResolveAcceptedWinFan(TalentAcceptedWinAttributionContext context)`。
- Produces: `TalentFanBreakdownMessage` on `PlayerWinMessage.talentFanBreakdown` and `SnapshotRoundResult.talentFanBreakdown`。
- Consumes: existing stable runtime entry order and detached/counterfactual evaluation protections。

- [ ] **Step 1: Add RED tests for ungated base evaluation**

Extract a pure evaluation result from `MahjongLogic`:

```csharp
FanEvaluation raw = MahjongLogic.EvaluateBestFan(
    hand, melds, winTile, isSelfDraw: true,
    roundWind, seatWind, options: null, isRobKongWin: false);
runner.Check(raw.HasWinningShape && raw.Fan == 6,
    "base evaluation returns the actual fan below the eight-fan eligibility gate");
runner.Check(!MahjongLogic.CheckWinWithFan(
        hand, melds, winTile, true, out _, out _,
        roundWind, seatWind, options: null, isRobKongWin: false),
    "the public legality method still enforces the eight-fan gate");
```

`CheckWinWithFan` must delegate to this evaluation and retain existing behavior/details.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: missing `FanEvaluation`/`EvaluateBestFan`.

- [ ] **Step 3: Implement one ungated evaluation path**

`FanEvaluation` contains `HasWinningShape`, `Fan`, `FanDetails`. Move decomposition selection, relaxed pure straight, and bonus fan calculation into `EvaluateBestFan`; `CheckWinWithFan` returns false when `!HasWinningShape || Fan < 8` and only then exposes accepted totals/details. Do not duplicate fan-rule loops.

- [ ] **Step 4: Add RED marginal-attribution tests**

Use real runtime entries and accepted hands:

```csharp
TalentFanResolution r = runtime.ResolveAcceptedWinFan(new TalentAcceptedWinAttributionContext(
    session, winnerSeatIndex,
    evaluateOptions: options => MahjongLogic.EvaluateBestFan(
        hand, melds, winTile, isSelfDraw,
        roundWind, seatWind, options, isRobKongWin)));
runner.Check(r.FinalFan == 24 && r.BaseFan == 6,
    "final and no-talent base fan remain distinct");
runner.Check(r.Contributions.Select(x => (x.TalentId, x.FanDelta)).SequenceEqual(new[]
{
    ("head_start", 2),
    ("dragon_ascent", 0), // omit zero from wire/result rows
    ("sheathed_edge", 16)
}.Where(x => x.Item2 != 0)),
    "stable marginal attribution explains actual accepted fan without ID branches");
runner.Check(r.BaseFan + r.Contributions.Sum(x => x.FanDelta) == r.FinalFan,
    "contribution rows reconcile exactly to authoritative final fan");
```

Add a relaxed-pure-straight case where the contribution is derived from counterfactual evaluation, a negative clamp case, and two entries whose order matters. Candidate evaluations must not mutate state or emit events.

- [ ] **Step 5: Implement stable marginal attribution**

Add `TalentAcceptedWinAttributionContext` with `GameSession Session`, `int WinnerSeatIndex`, and `Func<ScoringOptions, FanEvaluation> EvaluateOptions`. `TalentMatchRuntime.ResolveAcceptedWinFan(context)` starts with no active talent entries to obtain `BaseFan`; it adds winner entries in runtime `Sequence` order, evaluating cumulative scoring options and post-legal modifiers after each addition. The row delta is `nextFinal - previousFinal`; omit zero rows. Apply the effective negative clamp as its source talent row after positive entries. Store category as `Eligibility`, `PostLegal`, or `Negative` and preserve runtime sequence.

This method must be polymorphic and entry-based: no `if (talentId == ...)` or effect-ID branches. Counterfactuals use detached state and null event sinks. `FinalFan` remains the already accepted authoritative value; if attribution cannot reconcile, preserve `FinalFan`, log a diagnostic, and include an internal unattributed diagnostic only in server logs—not a fake UI talent row.

- [ ] **Step 6: Carry the breakdown through live win and recovery**

Define wire DTOs:

```csharp
[Serializable]
public sealed class TalentFanContributionMessage
{
    public string talentId;
    public int fanDelta;
    public int category;
    public int sequence;
}

[Serializable]
public sealed class TalentFanBreakdownMessage
{
    public int baseFan;
    public int finalFan;
    public TalentFanContributionMessage[] contributions;
}
```

Pass the same immutable breakdown through `IPlayerClient.OnPlayerWin`, `RemotePlayerClient`, `PlayerWinMessage`, Room result source, `RoomGameSnapshot`, `ClientGameState`, `RemoteServerProxy`, and local result presentation. Deep-copy arrays at every snapshot/projection boundary.

- [ ] **Step 7: Add four-seat privacy and duplicate/gap tests**

All seats may see the completed round's same public breakdown, but no hidden carried list/private state. Applying duplicate PlayerWin does not duplicate rows; a sequence gap does not partially apply the result; reconnect produces the exact same values without runtime reevaluation.

- [ ] **Step 8: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "rg -n 'talentId\s*==|switch\s*\(.*talentId' Assets/Scripts/Core Assets/Scripts/Talent"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core Assets/Scripts/Talent Tests/NetworkRegression; git commit -m 'feat: attribute talent fan contributions'"
```

Expected: tests pass; source guard has no effect execution branches by talent ID.

---

### Task 4: Standardize authoritative active-effect feedback semantics

**Files:**

- Modify: `Assets/Scripts/Talent/TalentActionModels.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Modify: `Assets/Scripts/Talent/Impl/SheathedEdgeTalent.cs`
- Modify: `Assets/Scripts/Talent/Impl/InterceptionTalent.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Test: `Tests/NetworkRegression/TalentActionTests.cs`

**Interfaces:**

- Produces: `TalentActionResult.EffectApplied` and public event type `active_talent_applied`。
- Consumes: Phase 2 exactly-once action route and `TalentNegativeEffectResult.WasApplied/WasBlocked`。

- [ ] **Step 1: Add RED success-versus-blocked tests**

Cover:

```csharp
runner.Check(sheathed.Accepted && sheathed.EffectApplied,
    "arming sheathed edge is an applied active effect");
runner.Check(interceptionBlocked.Accepted && !interceptionBlocked.EffectApplied,
    "a blocked interception still spends its use but is not a strong success");
runner.Check(interceptionApplied.Accepted && interceptionApplied.EffectApplied,
    "an unblocked charge reduction is a strong success");
runner.Check(events.Count(x => x.EventType == "active_talent_applied") == 1,
    "an applied request emits one standardized feedback event");
```

Also assert rejected, duplicate and stale requests emit zero standardized events.

- [ ] **Step 2: Run focused tests and confirm RED**

Expected: missing `EffectApplied`.

- [ ] **Step 3: Implement polymorphic result semantics**

Add `TalentActionResult.Success(bool effectApplied)`; default Success is not implicit. `SheathedEdgeTalent` returns true after arming. `InterceptionTalent` captures `TalentNegativeEffectResult` and returns true only for `WasApplied`; consumed uses/token remain unchanged when blocked, preserving Phase 2 behavior.

After runtime accepts a result with `EffectApplied`, it emits one public `active_talent_applied` event owned by the source talent. Do not emit on the Room/client side and do not infer from talent ID.

- [ ] **Step 4: Carry the private resolved flag and preserve ordering**

Add `effectApplied` to `TalentActionResolvedMessage`. Room event order remains runtime public/private projection first, then exactly one resolved envelope. Client presentation may use resolved to clear pending state, but strong public feedback is keyed by the standardized runtime event ID.

- [ ] **Step 5: Run full regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Talent Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: mark applied active talent effects'"
```

---

### Task 5: Build privacy-preserving layered HUD projections and feedback policies

**Files:**

- Create: `Assets/Scripts/Core/TalentHudProjectionPolicy.cs`
- Create: `Assets/Scripts/Core/TalentEventPresentationPolicy.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`

**Interfaces:**

- Consumes: Task 4 `active_talent_applied`; Phase 2 own/known talent snapshots and event IDs。
- Produces: `TalentHudView`, `TalentHudItem`, `TalentSeatSummary`, `TalentFeedbackView` consumed by Task 6 UI。

- [ ] **Step 1: Add RED layered-density and privacy tests**

```csharp
TalentHudView view = TalentHudProjectionPolicy.Build(snapshot, localSeatIndex: 0);
runner.Check(view.OwnVisible.All(x => x.IsActive) && view.OwnCollapsedCount == 3,
    "only active own talents remain in the persistent hand-anchored row");
runner.Check(view.Seats[1].Visible.Count == 2 && view.Seats[1].CollapsedCount == 2,
    "opponents show two authorized known talents and a +N summary");
runner.Check(view.Seats[1].Visible.All(x => !x.ShowActiveState),
    "opponent chips never reveal post-sideboard active state");
```

Hidden entries must not affect count or ordering. Stable order: pinned/key public entries first, then most recent public event ID descending, then talent ID ordinal.

- [ ] **Step 2: Add RED feedback-level tests**

```csharp
TalentFeedbackView strong = TalentEventPresentationPolicy.Build(ActiveAppliedEvent(), false);
runner.Check(strong.Level == TalentFeedbackLevel.Strong
    && strong.ShowToast && strong.AppendFeed && strong.PulseChip && strong.PlayAudio,
    "only standardized applied active effects produce the four-part strong feedback");

runner.Check(TalentEventPresentationPolicy.Build(BlockedEvent(), false).Level == TalentFeedbackLevel.Medium,
    "control blocking is medium feedback");
runner.Check(TalentEventPresentationPolicy.Build(PrivateRefresh(), false).Level == TalentFeedbackLevel.Weak,
    "ordinary projection refresh only updates chips");
runner.Check(TalentEventPresentationPolicy.Build(ActiveAppliedEvent(), true).IsSilent,
    "recovery suppresses all historical feedback");
```

- [ ] **Step 3: Run focused suite and confirm RED**

Expected: missing policy/view types.

- [ ] **Step 4: Implement policies with registry metadata only**

Policies map stable `talentId` to display name using `TalentRegistry` metadata and map known event types to safe Chinese copy. Unknown event type returns weak generic `天赋状态已更新` and sets `ShouldLogWarning`; never render server-provided rich text.

Feedback mapping is exact:

- strong: `active_talent_applied` only;
- medium: `talent_revealed`, `blocked_negative_effect`, `public_charge_reduced`, public counter/uses change;
- weak: private state refresh and other state-only updates.

- [ ] **Step 5: Add duplicate/recovery behavior to policy fixtures**

Use `TalentFeedbackHistory.TryAccept(eventId)` to reject non-positive IDs, duplicates and lower IDs within the same match. `ResetForNewMatch` clears it; recovery projection does not seed or replay feed rows.

- [ ] **Step 6: Run full regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/TalentHudProjectionPolicy.cs Assets/Scripts/Core/TalentEventPresentationPolicy.cs Tests/NetworkRegression; git commit -m 'feat: define layered talent feedback policies'"
```

---

### Task 6: Render the layered HUD and generate the generic active-talent sound

**Files:**

- Create: `Assets/UI/TalentChipTemplate.uxml`
- Create: `Assets/UI/TalentChipTemplate.uss`
- Modify: `Assets/UI/GameHUD/GameHUD.uxml`
- Modify: `Assets/UI/GameHUD/GameHUDStyles.uss`
- Modify: `Assets/UI/GameHUD/GameHUDController.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Modify: `Assets/Scenes/03_Game.unity`
- Create: `Tools/GenerateTalentPlaceholderAudio.ps1`
- Create: `Assets/Audio/SFX/Talent/talent_active_generic.wav`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`

**Interfaces:**

- Consumes: Task 5 HUD/feedback view models and Task 4 ordered standardized event。
- Produces: visible B-density HUD, silent recovery path and generic `AudioClip` fallback。

- [ ] **Step 1: Add RED source/UI structure checks**

Assert GameHUD contains `OwnTalentBar`, `OwnTalentCollapsedButton`, four `SeatNKnownTalents`, four `SeatNKnownTalentMore`, `TalentEffectFeed`, and `TalentToast`. Chip template contains `NameLabel`, `ValueLabel`, `ConsumedMarker`; no opponent active marker binding exists.

- [ ] **Step 2: Add the UXML/USS layout**

Place own bar directly above the hand-safe region, opponent containers beside existing seat score/wind containers, feed at the side, and Toast in the central safe area. Persistent rows use at most two opponent chips plus `+N`; expanded drawers overlay only their seat edge and close on table click/new decision.

Use USS classes for active/inactive/known/consumed/positive/negative; do not hardcode colors in controller. Verify `GameHUDStyles.uss` imports/binds `MSYH_UITK.asset` through the existing panel settings path.

- [ ] **Step 3: Bind snapshots silently and live events visibly**

`ApplyRecoverySnapshot` rebuilds chips and action availability only. `RemoteServerProxy.TalentRuntimeEventReceived` drives `TalentFeedbackHistory`; accepted strong feedback updates chip, appends one feed row, shows Toast, and plays sound. Medium updates chip/feed without Toast/audio. Weak only rebuilds chip values.

Feed keeps four items. OnDestroy unsubscribes all events, clears scheduled actions and kills linked tweens. Every DOTween call uses `.SetLink(gameObject)`.

- [ ] **Step 4: Add the deterministic WAV generator**

`Tools/GenerateTalentPlaceholderAudio.ps1` writes PCM WAV with:

- sample rate 48000;
- 16 bits;
- 2 channels;
- duration 0.70 seconds;
- a short filtered/noisy click envelope during the first 80 ms;
- a sine/chime glide from roughly 620 Hz to 980 Hz with exponential decay;
- final 30 ms fade-out;
- normalized peak <= -1 dBFS.

The script accepts `-OutputPath` and writes via `.NET BinaryWriter`; its click noise uses a fixed xorshift seed so repeated generation is byte-identical. It uses no network or external audio package.

- [ ] **Step 5: Generate and verify the audio asset**

Run:

```powershell
pwsh -NoLogo -NoProfile -File Tools/GenerateTalentPlaceholderAudio.ps1 -OutputPath Assets/Audio/SFX/Talent/talent_active_generic.wav
```

Add a regression WAV-header reader asserting `RIFF/WAVE`, PCM 1, 48000 Hz, 2 channels, 16 bits, and duration 0.60–0.80 seconds. Regenerating must produce byte-identical output.

- [ ] **Step 6: Bind the serialized generic clip and prevent misplays**

Add serialized `AudioClip _genericActiveTalentClip` and `AudioSource _talentAudioSource` fields to `GameHUDController`. In `Assets/Scenes/03_Game.unity`, bind `talent_active_generic.wav` to the clip field and a non-spatial UI `AudioSource` to the source field; do not move the file under Resources or call `Resources.Load`. Play only when `TalentFeedbackView.PlayAudio` is true. Missing references log one warning and do not break HUD. Button click, accepted-but-blocked result, reject, duplicate event and recovery all have tests proving zero Play requests.

- [ ] **Step 7: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/UI/GameHUD Assets/UI/TalentChipTemplate.* Assets/Audio/SFX/Talent Assets/Scenes/03_Game.unity Tools/GenerateTalentPlaceholderAudio.ps1 Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: add layered talent HUD feedback'"
```

---

### Task 7: Integrate supplemental talent actions into the existing ActionPanel

**Files:**

- Create: `Assets/Scripts/Core/TalentActionPanelPolicy.cs`
- Modify: `Assets/UI/ActionPanel.uxml`
- Modify: `Assets/UI/ActionPanelStyles.uss`
- Modify: `Assets/UI/ActionPanelController.cs`
- Modify: `Assets/UI/FloatingTilePanelController.cs`
- Modify: `Assets/Scripts/Core/Agents/LocalPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`

**Interfaces:**

- Consumes: Phase 2 `TalentActionsChanged`, `TalentActionResolvedReceived`, `SubmitTalentAction` and Task 4 `effectApplied`。
- Produces: separate `ShowTalentActions`/`ClearTalentActions` controller API; never completes base action callback。

- [ ] **Step 1: Add RED pure state-machine tests**

```csharp
TalentActionPanelState state = TalentActionPanelPolicy.Open(
    72, new BaseActionAvailability { CanDiscard = true },
    new[] { SheathedEdgeOption(), InterceptionOption() });
state = TalentActionPanelPolicy.BeginSubmit(state, "interception");
runner.Check(state.BaseActions.CanDiscard
    && state.Options.Single(x => x.TalentId == "interception").IsPending,
    "submitting one talent leaves base actions and other talent options available");
state = TalentActionPanelPolicy.Resolve(state, "interception", accepted: false, stale: false);
runner.Check(!state.Options.Single(x => x.TalentId == "interception").IsPending,
    "ordinary rejection restores only the submitted talent button");
```

Cover stale clearing all, accepted keeping base actions, recovery reset, and target-picker cancel not sending.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: missing `TalentActionPanelPolicy`, `TalentActionPanelState`, and `BaseActionAvailability`.

- [ ] **Step 3: Implement the pure action-panel policy**

Define presentation-only `BaseActionAvailability` fields `CanDiscard`, `CanHu`, `CanPon`, `CanChi`, `CanKong`, and `CanSkip` instead of reusing `AllowedActions`, because discard/skip are controller actions rather than validator flags. Keep decision ID as `long`; options are copied, never mutated in `ClientGameState`. Run the focused tests and require GREEN before editing UXML.

- [ ] **Step 4: Add a separate talent row and controller callbacks**

```csharp
public void ShowTalentActions(
    long decisionId,
    IReadOnlyList<TalentActionOption> options,
    Action<TalentActionOption> onSelected);

public void ClearTalentActions(long decisionId);
```

Place `TalentActionContainer` above `ButtonContainer`. Do not reuse `Action<ActionPanelChoice>` and do not call existing `Hide()` after a talent click.

- [ ] **Step 5: Bind zero-target and target-selection actions**

`藏锋` submits immediately. `截流` opens `FloatingTilePanelController` selection mode showing authorized seat name, target talent display name, and public charge from `TalentActionOption`; selecting sends the original option, cancel only closes. `定心` never appears because the server sends no option.

- [ ] **Step 6: Handle ordered results and decision boundaries**

An ordinary reject shows short mapped text and restores one button. `StaleDecision`/`DecisionExpired`, base action submission, Discarded, AddedKongDeclared, win, draw, session end, recovery and scene destroy clear buttons and picker at the same boundaries already proven in Phase 2 client projection tests.

- [ ] **Step 7: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/TalentActionPanelPolicy.cs Assets/UI/ActionPanel* Assets/UI/FloatingTilePanelController.cs Assets/Scripts/Core/Agents/LocalPlayerClient.cs Assets/Scripts/Core/Network/RemoteServerProxy.cs Tests/NetworkRegression; git commit -m 'feat: integrate talent action controls'"
```

---

### Task 8: Build the original full-screen halftime tactical sideboard

**Files:**

- Create: `Assets/Scripts/Core/SideboardDraftPolicy.cs`
- Create: `Assets/UI/SideboardPanel.uxml`
- Create: `Assets/UI/SideboardPanelStyles.uss`
- Create: `Assets/UI/SideboardPanelController.cs`
- Modify: `Assets/UI/GameHUD/GameHUD.uxml`
- Modify: `Assets/UI/GameHUD/GameHUDController.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Test: `Tests/NetworkRegression/SideboardTests.cs`

**Interfaces:**

- Consumes: Phase 2 `SnapshotSideboardState`, `SideboardStarted/Locked/Progress` and `SubmitSideboard`。
- Consumes: Task 2 gauge policy and Task 5 known-opponent HUD projection。
- Produces: immutable `SideboardDraft`, full-screen editable/readonly controller states。

- [ ] **Step 1: Add RED draft-policy tests**

```csharp
SideboardDraft original = SideboardDraftPolicy.Create(started);
SideboardDraft changed = SideboardDraftPolicy.SetActive(original, "interception", true, loadout, registry);
runner.Check(!ReferenceEquals(original, changed)
    && !original.ActiveTalentIds.Contains("interception")
    && changed.ActiveTalentIds.Contains("interception"),
    "sideboard edits an immutable local draft only");

SideboardDraft lockedAttempt = SideboardDraftPolicy.SetActive(changed, "starting_capital", false, loadout, registry);
runner.Check(lockedAttempt.ErrorCode == SideboardDraftErrorCodes.LockedTalent,
    "locked main talent cannot be disabled locally");
```

Cover add/replace/disable, active count not fixed to six, duplicate/unknown IDs, budget over-cap, source message deep copy, and readonly recovery.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: missing `SideboardDraftPolicy`, `SideboardDraft`, and `SideboardDraftErrorCodes`.

- [ ] **Step 3: Implement the pure draft policy**

The policy accepts only the nine carried IDs, canonicalizes them in original carried slot order, calculates total with Task 2 gauge policy, and exposes `CanLock`. It never calls network or changes client authority. Run the focused tests and require GREEN before creating the panel.

- [ ] **Step 4: Create the approved original tactical layout**

UXML names:

```xml
<ui:VisualElement name="SideboardOverlay" class="sideboard-overlay">
    <ui:Label name="TitleLabel" text="中场整备" />
    <ui:Label name="TimerLabel" text="45" />
    <ui:VisualElement name="ActiveTalents" />
    <ui:VisualElement name="ReserveCards" />
    <ui:VisualElement name="KnownOpponentIntel" />
    <ui:VisualElement name="BudgetTrack"><ui:VisualElement name="BudgetFill" /></ui:VisualElement>
    <ui:Label name="BudgetLabel" />
    <ui:VisualElement name="SeatLockStatus" />
    <ui:Label name="ErrorLabel" />
    <ui:Button name="LockButton" text="锁定方案" />
</ui:VisualElement>
```

Active and reserve remain visually separate. Every card displays tier, active/stopped state and cost; locked talent shows a lock. Opponent intel uses only known public entries and never active state.

- [ ] **Step 5: Bind editable, locked, timeout and recovery states**

On live Started, deep-copy into a draft. Card click updates draft/gauge. Lock sends once and disables immediately. Any Locked discards draft and becomes readonly; Progress updates four-seat confirmation only. Countdown uses server deadline for display, never locally decides timeout. Recovery with `ownLocked=true` opens readonly wait; it never rebuilds an editable draft.

- [ ] **Step 6: Add controller lifecycle and privacy tests**

Leaving room/scene unsubscribes events. Wrong-seat private started data cannot populate local cards. Other seats expose only locked booleans. Duplicate/gap messages obey `ClientRoomService` gate. Invalid submission cannot re-enable editing after server locks original.

- [ ] **Step 7: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- sideboard"
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/SideboardDraftPolicy.cs Assets/UI/SideboardPanel* Assets/UI/GameHUD Assets/Scripts/Core/Network/RemoteServerProxy.cs Tests/NetworkRegression; git commit -m 'feat: add halftime tactical sideboard UI'"
```

---

### Task 9: Put final fan first and render itemized talent contributions

**Files:**

- Create: `Assets/Scripts/Core/TalentResultPresentationPolicy.cs`
- Modify: `Assets/UI/ResultPanel.uxml`
- Modify: `Assets/UI/ResultPanelStyles.uss`
- Modify: `Assets/UI/ResultPanelController.cs`
- Modify: `Assets/Scripts/Core/GameManager.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`

**Interfaces:**

- Consumes: Task 3 `TalentFanBreakdownMessage` live and snapshot projections。
- Produces: `TalentResultView` with authoritative final, base row and stable non-zero contribution rows。

- [ ] **Step 1: Add RED result-presentation tests**

```csharp
TalentResultView result = TalentResultPresentationPolicy.Build(new TalentFanBreakdownMessage
{
    baseFan = 8,
    finalFan = 22,
    contributions = new[]
    {
        Contribution("sheathed_edge", 16, 2),
        Contribution("head_start", 2, 1),
        Contribution("interception", -4, 3)
    }
}, TalentRegistry.Instance);
runner.Check(result.FinalFanText == "最终番 22" && result.Rows[0].Text == "基础番 8",
    "final fan is the hero result and base fan is the first explanation row");
runner.Check(result.Rows.Skip(1).Select(x => x.Text).SequenceEqual(
    new[] { "快人一步 +2", "藏锋 +16", "截流 -4" }),
    "talent rows use stable authority order and signed deltas");
```

Cover zero omission, unknown talent safe label, draw hides breakdown, mismatch logs diagnostic but preserves finalFan, and reconnect equality.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: missing `TalentResultPresentationPolicy` and `TalentResultView`.

- [ ] **Step 3: Implement the pure result policy**

Do not sum into a replacement final. Use registry display names; negative rows get `IsNegative=true` and explicit minus text. Unknown IDs display `未知天赋` plus warning, without protocol text injection. Run the focused tests and require GREEN before modifying ResultPanel.

- [ ] **Step 4: Restructure ResultPanel hierarchy**

Add `FinalFanHero`, `BaseFanRow`, `TalentContributionList`, and keep existing `FanListContainer`, winning hand and continue button below. Draw and session-final states hide hero/breakdown. Win and lose use the same public breakdown.

Replace the old animation that derives total by parsing `fanDetails`. If a roll animation remains, it animates only display rows and always finishes at the authoritative `finalFan`.

- [ ] **Step 5: Bind live and recovery results once**

`GameManager` and `ResultPanelController.ApplyRecoveryResult` call the same render entry point. Reconnect performs no Toast/audio. Extremely long MCR fan details scroll independently so hero, winning hand and continue button remain fixed.

- [ ] **Step 6: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/TalentResultPresentationPolicy.cs Assets/Scripts/Core/GameManager.cs Assets/UI/ResultPanel* Tests/NetworkRegression; git commit -m 'feat: explain itemized talent fan results'"
```

---

### Task 10: Give AI legal archetypes, active decisions, and one deterministic sideboard choice

**Files:**

- Create: `Assets/Scripts/Core/Agents/AiTalentLoadoutFactory.cs`
- Create: `Assets/Scripts/Core/Agents/AiTalentDecisionPolicy.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Test: `Tests/NetworkRegression/AiTalentPolicyTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

**Interfaces:**

- Consumes: Task 1 schema/preset, Phase 2 `TalentActionOption` and `SideboardLoadoutPolicy`。
- Produces: valid deterministic AI loadouts/actions; AI has no privileged runtime mutation path。

- [ ] **Step 1: Add RED deterministic loadout tests**

For all three presets and four seats, factory output must pass `PlayerLoadoutCodec.TryDecode(message, preset, ...)`, carry exactly 6 main + 3 reserve slots, contain no duplicates, and preserve `StartingCapital` when chosen. Same `(preset, seat, seed)` returns identical IDs.

- [ ] **Step 2: Implement three small archetype priorities**

Use registry/config IDs only for loadout configuration, never effect execution:

- burst: `sheathed_edge`, `head_start` priority;
- control: `interception`, `composure` priority;
- information/value: `peek`, `starting_capital`, existing economic talents.

Start from standard deck, add talents by priority while validating total; if a tier/preset cannot fit, leave the slot empty rather than constructing an invalid loadout.

- [ ] **Step 3: Add RED active-action policy tests**

Given authoritative options only, policy prefers a legal armed finisher, then an interception against the highest public charge, tie-breaking by target seat then talent ID. Empty options yields null. It cannot inspect hidden talents, hands, Peek data from another seat or runtime entries.

- [ ] **Step 4: Route AI through the same GameServer submit path**

At AI main decision boundaries, request `GetAvailableTalentActionsSnapshot(aiSeat)` and, if policy chooses, call `SubmitNetworkTalentAction` with the current decision ID before the normal discard decision. Do not add `talentId` branches to GameServer.

- [ ] **Step 5: Add deterministic sideboard selection**

From carried nine and public known opponent view, form an active subset: retain locked talents, prefer counters to revealed large/charged talents, then archetype priority; if over budget, remove flexible lowest-priority/highest-cost entries until `SideboardLoadoutPolicy` accepts. Failure explicitly locks original. AI still locks immediately; it does not consume the human 45-second UI timer.

- [ ] **Step 6: Run 100 seeded policy sequences**

Assert no invalid targets, negative uses/charge, budget excess, duplicate IDs, stale decision submission, or unfinished sideboard. These are policy/runtime tests, not a fake claim of full GameServer E2E.

- [ ] **Step 7: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "rg -n 'talentId\s*==|switch\s*\(.*talentId' Assets/Scripts/Core/Network/GameServer.cs Assets/Scripts/Core/Network/Room.cs"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/Agents Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: add deterministic AI talent strategy'"
```

---

### Task 11: Add privacy-safe playtest telemetry

**Files:**

- Create: `Assets/Scripts/Talent/TalentTelemetry.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Test: `Tests/NetworkRegression/AiTalentPolicyTests.cs`

**Interfaces:**

- Produces: `ITalentTelemetrySink`, `TalentTelemetryRecord`, `JsonLineTalentTelemetrySink`。
- Consumes: public runtime events, Task 3 breakdown, sideboard result reason and non-identifying session context。

- [ ] **Step 1: Add RED serialization/privacy tests**

```csharp
string json = TalentTelemetry.Serialize(record);
runner.Check(json.Contains("\"eventType\":\"active_talent_applied\"")
    && !json.Contains("username") && !json.Contains("concealedTiles")
    && !json.Contains("peek"),
    "telemetry records gameplay facts without identity or hidden state");
```

Reflect over record fields and reject names for username, displayName, playerId credential, hand, deck order, Peek tiles, room ticket or connection ID.

- [ ] **Step 2: Implement the narrow record and sinks**

Fields are limited to anonymous match/session ID, preset, mode, completed round, event type, seat index, talent ID, public value, draws per seat, base fan, each contribution aggregate, final fan, winner seat, control blocked/applied and sideboard accepted/original/timeout. Serialize one compact JSON object per line.

Provide `NullTalentTelemetrySink` default, memory sink for tests, and Dedicated Server log sink. No external analytics dependency.

- [ ] **Step 3: Emit from existing authoritative boundaries**

Runtime supplies talent event facts; Room/GameServer add match context at match start, round start/end, active apply/block, sideboard lock and accepted win. Telemetry failures are caught/logged and cannot interrupt room lifecycle or gameplay.

- [ ] **Step 4: Add exactly-once and exception tests**

Duplicate/recovery messages do not create server telemetry because only authority emits. One active applied event, one blocked result, one sideboard lock and one win each produce one record. A throwing sink cannot leave Room in `InRound` or prevent round completion.

- [ ] **Step 5: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "rg -n 'username|displayName|concealed|deckOrder|peekTiles|connectionId|streamId' Assets/Scripts/Talent/TalentTelemetry.cs"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Talent/TalentTelemetry.cs Assets/Scripts/Talent/TalentMatchRuntime.cs Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: add talent playtest telemetry'"
```

The privacy `rg` must only match explicit forbidden-field tests/comments, not serialized record members.

---

### Task 12: Run the Phase 3 code checkpoint, review the branch, and stop before master

**Files:**

- Modify only if a valid review finding requires a RED/GREEN fix.
- Create ignored local report: `.superpowers/sdd/2026-08-12-talent-phase3/phase3-code-checkpoint-report.md`
- Reference: `docs/network_verification.md`

**Interfaces:**

- Consumes: Tasks 1–11 and Phase 2 HEAD history。
- Produces: clean candidate branch and evidence-backed checkpoint; no merge/push/master verification yet。

- [ ] **Step 1: Run fresh automated regression and test build**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "dotnet build Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

Expected: exit 0, 0 warnings, 0 errors for the test project.

- [ ] **Step 2: Refresh Unity before judging Assembly-CSharp**

Open the branch in Unity once and wait for import/compile. Do not treat a stale generated `Assembly-CSharp.csproj` as authoritative. After refresh:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet build Assembly-CSharp.csproj --no-restore"
```

Expected: 0 compile errors. Record any pre-existing package warnings separately. If the generated project still omits new source files, list exact omissions, temporarily add only those entries for diagnosis, build, and restore the generated file before continuing; never commit the generated csproj.

- [ ] **Step 3: Run source and diff guards**

```powershell
pwsh -NoLogo -NoProfile -Command "rg -n 'talentId\s*==|switch\s*\(.*talentId' Assets/Scripts/Core/Network Assets/Scripts/Talent"
pwsh -NoLogo -NoProfile -Command "rg -n 'Canvas|UnityEngine\.UI\.' Assets/UI Assets/Scripts/Core"
pwsh -NoLogo -NoProfile -Command "rg -n 'MSYH\.TTC|MSYH_SDF' Assets/UI"
pwsh -NoLogo -NoProfile -Command "git diff --check 8658923..HEAD"
pwsh -NoLogo -NoProfile -Command "git status --short"
```

Expected: no gameplay effect branches by talent ID, no new UGUI/TMP font references, clean range diff, and no generated/temp files.

- [ ] **Step 4: Request independent whole-branch review**

Review from `8658923` through HEAD for Critical/Important issues, specifically:

- card preset migration, create/join mismatch and over-cap distinction;
- itemized fan reconciliation and detached counterfactual side effects;
- strong feedback not firing on blocked/reject/recovery;
- ordered messages, duplicate/gap handling and action pending boundaries;
- HUD/sideboard privacy and opponent `+N` counts;
- Room round completion and telemetry exception isolation;
- AI using the same validation path.

Every accepted finding receives a failing regression first, then the minimal fix, fresh focused/full verification, and its own commit.

- [ ] **Step 5: Perform Unity client smoke checks on the branch**

This is a code checkpoint, not final真人联机验收. In Unity verify:

- all UXML/USS assets import and `MSYH_UITK.asset` renders Chinese text;
- one local client can open deck editor, cycle deck preset, save an over-cap deck and see warning;
- HUD, ActionPanel, sideboard and result panels instantiate without null queries at 16:9 and the project's narrowest supported resolution;
- generic WAV imports as PCM and plays from the HUD test path once.

Record observations; do not claim Dedicated Server or multiplayer behavior yet.

- [ ] **Step 6: Write the checkpoint report and stop**

Report exact commits, commands, outputs, Unity version/import result, known warnings, deferred Dedicated Server/real-player verification, and branch/status. Then stop and ask the user to review Phase 3 before any merge.

---

### Task 13: After explicit approval, merge to master and run unified production verification

**Files:**

- Follow: `docs/network_verification.md`
- Update after success: `plan.md`, `milestone.md`, `summary.md` as appropriate.

**Interfaces:**

- Consumes: user approval of Task 12 checkpoint。
- Produces: verified `master` candidate; this task is forbidden before approval。

- [ ] **Step 1: Confirm clean candidate and explicit user approval**

Verify branch `codex/talent-actions-ui-unified`, clean worktree, Task 12 report complete. Do not infer approval from test success.

- [ ] **Step 2: Merge the approved branch into master without rewriting history**

Use the repository's normal non-destructive merge workflow. Do not push unless separately requested. Confirm the merge commit/range and clean status.

- [ ] **Step 3: Refresh Unity and build client plus Dedicated Server from master**

Use Unity 2022.3.61t9 / Tuanjie 1.6.8. Dedicated Server's only startup scene remains `00_ServerBootstrap`; client first scene remains `00_Persistent`. Run automated regression again on master before manual sessions.

- [ ] **Step 4: Run the unified真人 matrix**

Cover:

1. lobby/deck: three presets, per-deck persistence, over-cap save, explicit create/join blockers;
2. one human + three AI Single: each existing/anchor talent reaches at least one key visible effect;
3. active feedback: applied triggers Toast/chip/feed/audio once; reject, blocked and reconnect do not;
4. at least two humans + AI HalfGame: round-four sideboard valid change, invalid lock-original, AI choice, timeout, disconnect and readonly reconnect;
5. result: final fan hero, base fan, itemized positive/negative talent rows, long MCR details and winning-hand strip;
6. privacy: Peek, hidden talents, precise opponent alienation, sideboard active set and drafts never cross seats;
7. recovery: main-turn action options restore; temporary target/sideboard drafts do not;
8. layouts: 16:9 and narrowest supported resolution do not cover hand, river, score, wind, wall count or base actions.

- [ ] **Step 5: Inspect Dedicated Server logs and telemetry**

Confirm no unhandled exception, duplicate round completion, invalid negative charge/use, sideboard left open, private state leak, or telemetry identity/hand content. Compare active apply/block and result rows with client display.

- [ ] **Step 6: Update project records and report completion**

Only after all gates pass, mark Phase 3 complete in project docs, record exact master commit/builds/matrix, and report remaining balance observations separately from correctness defects.

## Phase 3 Completion Gate

- [ ] 每套卡组保存并恢复自身异化档位；旧存档稳定迁移到 Standard。
- [ ] 超限卡组可保存且持续警告；创建/加入时档位不匹配或超限被明确、权威阻止。
- [ ] 6 主 + 3 备选编辑器、B 型牌桌 HUD、现有 ActionPanel 补充动作和原版中场战术桌均可用。
- [ ] 本家 active/私有值和他家 revealed-only 投影满足四席隐私；重连不重播临时反馈。
- [ ] 只有实际应用的主动天赋触发 Toast、Chip 强调、feed 与通用 WAV；blocked/reject/recovery 不误播。
- [ ] 最终番置顶，基础番与每项非零天赋贡献逐条展示，并严格使用服务器 finalFan。
- [ ] AI 构筑、主动动作和中场选择均通过与真人相同的服务端校验。
- [ ] Telemetry 不含 username、连接凭据、暗手、完整牌库顺序、Peek 内容或未公开天赋。
- [ ] Task 12 在功能分支完成并暂停汇报；只有用户批准后才执行 Task 13 合并和真人验收。
