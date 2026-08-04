# Talent UI, AI, and Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让玩家能清楚构筑 6 主 + 3 备选、理解异化值档位、在牌桌上看见自身技能和已知对手天赋、完成中场换装，并通过结算拆分与 AI 策略持续验证“做大牌、克制、攒大招”的玩法闭环。

**Architecture:** 所有 UI 都只消费 `ClientRoomState`/`ClientGameState` 的权威投影和纯 C# presentation policy，不直接读取 `Room`、`GameServer` 或其他玩家私有数据。普通麻将按钮与补充天赋按钮共享当前 decision 视图但拥有独立回调。AI 使用同一 `TalentActionOption` 和服务端校验路径，策略只看公开/本席可见信息。结构化 telemetry 记录玩法节奏和天赋贡献，作为后续平衡依据。

**Tech Stack:** Unity UI Toolkit（UXML/USS/C#，禁止 Canvas/UGUI）、TextCore `Assets/Font/MSYH_UITK.asset`、DOTween Pro（动态对象动画必须 `.SetLink(gameObject)`）、纯 C# presentation policies、`Tests/NetworkRegression`、Unity Play Mode 手工视觉验证。

## Global Constraints

- 开始前必须完成前两份计划的 Completion Gate。
- 采用已确认 UI 方向：牌桌上对手天赋靠近席位，本家天赋条靠近手牌，主动按钮并入现有 `ActionPanel`；中场是全屏战术页；牌库编辑器只显示当前 40/80/120 预览档位的一根异化值条。
- 所有界面使用 UI Toolkit；USS 字体只引用 `Assets/Font/MSYH_UITK.asset`，共用 `PanelSettings` 保持绑定 `SuperMajiangTextSettings.asset`。
- 客户端不推导权威分数、轮次、天赋 active 状态或可用技能；只展示服务端消息/快照。
- 对手已知天赋只显示“已揭示”和最后公开动态值，不显示中场后是否仍激活。
- 技能提示只播放实时收到的新事件；应用重连快照时不得重播 toast、音效、动画或选择弹窗。
- 中场本地草稿只存在于 `SideboardPanelController`；服务器未接受前不能写回 `ClientGameState`。
- 保存超预算构筑是允许行为，UI 显示警告但不禁用保存；进入房间仍由服务端拒绝。
- UI 动画不是功能验收替代品；每个任务先用纯 C# policy 回归锁定信息与行为，再接 UXML。

---

## File and Interface Map

**新增文件**

- `Assets/Scripts/Core/AlienationGaugePolicy.cs`
- `Assets/Scripts/Core/TalentHudProjectionPolicy.cs`
- `Assets/Scripts/Core/TalentActionPanelPolicy.cs`
- `Assets/Scripts/Core/SideboardDraftPolicy.cs`
- `Assets/Scripts/Core/TalentResultBreakdown.cs`
- `Assets/Scripts/Core/Agents/AiTalentLoadoutFactory.cs`
- `Assets/Scripts/Core/Agents/SimpleTalentActionPolicy.cs`
- `Assets/Scripts/Talent/TalentTelemetry.cs`
- `Assets/UI/SideboardPanel.uxml`
- `Assets/UI/SideboardPanelStyles.uss`
- `Assets/UI/SideboardPanelController.cs`
- `Assets/UI/TalentChipTemplate.uxml`
- `Assets/UI/TalentChipTemplate.uss`
- `docs/talent_playtest_protocol.md`
- `Tests/NetworkRegression/TalentPresentationTests.cs`
- `Tests/NetworkRegression/AiTalentPolicyTests.cs`

**主要修改文件**

- `Assets/UI/DeckEditorView.uxml`
- `Assets/UI/DeckEditorStyles.uss`
- `Assets/UI/DeckEditorToolkit.cs`
- `Assets/UI/MainLobby.uxml`
- `Assets/UI/MainLobbyStyles.uss`
- `Assets/UI/LobbyController.cs`
- `Assets/UI/GameHUD/GameHUD.uxml`
- `Assets/UI/GameHUD/GameHUDStyles.uss`
- `Assets/UI/GameHUD/GameHUDController.cs`
- `Assets/UI/ActionPanel.uxml`
- `Assets/UI/ActionPanelStyles.uss`
- `Assets/UI/ActionPanelController.cs`
- `Assets/UI/FloatingTilePanelController.cs`
- `Assets/UI/ResultPanel.uxml`
- `Assets/UI/ResultPanelStyles.uss`
- `Assets/UI/ResultPanelController.cs`
- `Assets/Scripts/Core/Agents/IPlayerClient.cs`
- `Assets/Scripts/Core/Agents/SimpleAIClient.cs`
- `Assets/Scripts/Core/Network/RemotePlayerClient.cs`
- `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- `Assets/Scripts/Core/Network/ClientRoomState.cs`
- `Assets/Scripts/Core/Network/ClientGameState.cs`
- `Assets/Scripts/Core/Network/Room.cs`
- `Assets/Scripts/Core/Network/GameServer.cs`
- `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`
- `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- `Tests/NetworkRegression/NetworkRegression.csproj`
- `Tests/NetworkRegression/Program.cs`

---

### Task 1: Build the 6+3 deck editor and one-preset alienation gauge

**Files:**

- Create: `Assets/Scripts/Core/AlienationGaugePolicy.cs`
- Modify: `Assets/UI/DeckEditorView.uxml`
- Modify: `Assets/UI/DeckEditorStyles.uss`
- Modify: `Assets/UI/DeckEditorToolkit.cs`
- Modify: `Assets/UI/MainLobby.uxml`
- Modify: `Assets/UI/MainLobbyStyles.uss`
- Modify: `Assets/UI/LobbyController.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

- [ ] **Step 1: Add failing pure-policy tests for gauge states**

```csharp
AlienationGaugeView lowSafe = AlienationGaugePolicy.Build(total: 35, AlienationPreset.Low);
runner.Check(lowSafe.Limit == 40 && lowSafe.Fill01 == 0.875f && !lowSafe.IsOverLimit,
    "low preset gauge uses the selected 40-point limit");

AlienationGaugeView standardSafe = AlienationGaugePolicy.Build(total: 45, AlienationPreset.Standard);
runner.Check(standardSafe.Limit == 80 && !standardSafe.IsOverLimit,
    "the same deck can be safe under standard preset");

AlienationGaugeView over = AlienationGaugePolicy.Build(total: 125, AlienationPreset.High);
runner.Check(over.Fill01 == 1f && over.Overflow == 5 && over.IsOverLimit,
    "over-cap gauge clamps visual fill but preserves exact overflow");
```

再覆盖无效 preset 回退 Standard，以及“总值等于 limit”不算超限。

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少 gauge policy 和 view model。

- [ ] **Step 3: Implement the policy without Unity dependencies**

```csharp
public sealed class AlienationGaugeView
{
    public int Total { get; set; }
    public int Limit { get; set; }
    public float Fill01 { get; set; }
    public int Overflow { get; set; }
    public bool IsOverLimit => Overflow > 0;
}

public static class AlienationGaugePolicy
{
    public static AlienationGaugeView Build(int total, AlienationPreset preset)
    {
        if (!AlienationBudgetPolicy.IsDefined(preset)) preset = AlienationPreset.Standard;
        int limit = AlienationBudgetPolicy.GetLimit(preset);
        return new AlienationGaugeView
        {
            Total = Math.Max(0, total),
            Limit = limit,
            Fill01 = Math.Min(1f, Math.Max(0, total) / (float)limit),
            Overflow = Math.Max(0, total - limit)
        };
    }
}
```

- [ ] **Step 4: Add explicit main and reserve sections to the editor**

`DeckEditorView.uxml` 在牌区上方/侧栏加入以下命名元素：

```xml
<ui:VisualElement name="AlienationPreview" class="alienation-preview">
    <ui:Button name="BtnPresetPrev" text="‹" class="preset-arrow" />
    <ui:Label name="PresetLabel" text="标准 80" class="preset-label" />
    <ui:Button name="BtnPresetNext" text="›" class="preset-arrow" />
    <ui:VisualElement name="AlienationTrack" class="alienation-track">
        <ui:VisualElement name="AlienationFill" class="alienation-fill" />
    </ui:VisualElement>
    <ui:Label name="ScoreText" text="异化值 0 / 80" class="stat-label" />
    <ui:Label name="AlienationWarning" text="当前构筑超出该档位，仍可保存" class="alienation-warning" />
</ui:VisualElement>
<ui:VisualElement name="MainTalentSlots" class="talent-slot-section" />
<ui:VisualElement name="ReserveTalentSlots" class="talent-slot-section reserve" />
```

若原 `ScoreText` 已存在，移动而不是复制，保证 Q 查询唯一。

- [ ] **Step 5: Render six main slots and three reserve slots from one controller**

`DeckEditorToolkit.GenerateTalentSlots()` 分别遍历 `MainSlotCount` 与 `ReserveSlotCount`。主槽标签为“大 ×1 / 中 ×2 / 小 ×3”，备选为“备选中 ×1 / 备选小 ×2”。选择列表：

- 主槽调用 `CanEquip`；
- 备选调用 `CanEquipReserve`；
- 已在其余 8 槽的 ID 置灰；
- `MainOnly`/`MainOnlyLocked` 不出现在备选列表；
- 移除/替换后同步刷新全部 9 槽和总异化值。

保存时写入 `SavedDeck.Talents` 的两个数组；无论是否超当前 preset，保存按钮都保持可用。

- [ ] **Step 6: Persist and switch only the preview preset**

左右按钮按 Low → Standard → High 循环，写入 `ProfileSettings.SelectedAlienationPreset` 并调用现有 `ProfileManager` 保存接口。刷新时用 `AlienationBudgetPolicy.Calculate(deck, mainIds, registry)`，备选不计入当前开场总值。

警告规则：`IsOverLimit` 时显示红/橙 warning 和 overflow；否则隐藏。USS 使用 class 切换，不在 C# 中硬编码颜色。

- [ ] **Step 7: Add the same preset selector to room creation**

`MainLobby.uxml` 在 GameMode selector 下增加 `AlienationPresetSelector`；`LobbyController.OnMatchmakingClicked` 把选择传给 `ClientRoomService.CreateRoom(gameMode, preset, nickname)`。房间页显示公共“异化档位：标准 80”和私有“本家异化：45 / 80”，移除对手席的精确异化标签。

- [ ] **Step 8: Run tests and visual smoke check, then commit**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "rg -n 'totalAlienation|acceptedTotalAlienation' Assets/UI/LobbyController.cs"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

在 Unity 中打开大厅，依次验证 40/80/120 文案、九槽选择、超限仍可保存、旧存档备选为空。第二条 `rg` 只允许命中本家字段，不允许用 `seat.totalAlienation` 展示他人。

Commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/AlienationGaugePolicy.cs Assets/UI Tests/NetworkRegression; git commit -m 'feat: add talent loadout budget UI'"
```

---

### Task 2: Show own talents, known opponent talents, and a real-time effect feed

**Files:**

- Create: `Assets/Scripts/Core/TalentHudProjectionPolicy.cs`
- Create: `Assets/UI/TalentChipTemplate.uxml`
- Create: `Assets/UI/TalentChipTemplate.uss`
- Modify: `Assets/UI/GameHUD/GameHUD.uxml`
- Modify: `Assets/UI/GameHUD/GameHUDStyles.uss`
- Modify: `Assets/UI/GameHUD/GameHUDController.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`

- [ ] **Step 1: Add failing projection and reconnect-suppression tests**

```csharp
TalentHudView view = TalentHudProjectionPolicy.Build(snapshot, localSeatIndex: 0);
runner.Check(view.OwnTalents.All(item => item.ShowActiveState),
    "owner talent bar shows authoritative active state");
runner.Check(view.OpponentTalents.All(item => !item.ShowActiveState),
    "known opponent talents never reveal post-sideboard active state");
runner.Check(view.OpponentTalents.Single().ValueText == "锋 2",
    "opponent talent retains its last public dynamic value");

IReadOnlyList<TalentFeedItem> recoveryFeed = TalentHudProjectionPolicy.BuildFeed(
    snapshotEvents, isRecoveryApplication: true);
runner.Check(recoveryFeed.Count == 0,
    "recovery snapshot does not replay talent feed animations");
```

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少 HUD policy/view models。

- [ ] **Step 3: Implement privacy-preserving view models**

```csharp
public sealed class TalentHudItem
{
    public int OwnerSeatIndex { get; set; }
    public string TalentId { get; set; }
    public string DisplayName { get; set; }
    public string ValueText { get; set; }
    public bool ShowActiveState { get; set; }
    public bool IsActive { get; set; }
    public bool IsConsumedThisRound { get; set; }
}

public sealed class TalentFeedItem
{
    public long EventId { get; set; }
    public int OwnerSeatIndex { get; set; }
    public string Text { get; set; }
    public bool IsPositive { get; set; }
}
```

显示名称通过 `TalentRegistry` 的稳定 ID 元数据查询；效果文案由 `TalentEventPresentationPolicy` 按 `EventType` 映射，不用服务端发送任意富文本。未知 event type 显示安全通用文案“天赋效果生效”，并写 warning 日志。

- [ ] **Step 4: Add seat-anchored and hand-anchored containers**

`GameHUD.uxml` 加：

- `OwnTalentBar`：底部手牌上方，最多 9 个紧凑 chip，inactive 本家条目降透明度；
- `Seat0KnownTalents` ... `Seat3KnownTalents`：靠各席比分/风位；本席容器隐藏以免重复；
- `TalentEffectFeed`：牌桌侧边纵向，最多保留 4 条实时消息；
- `TalentToast`：重要主动效果短提示。

`TalentChipTemplate` 包含 `NameLabel`、`ValueLabel`、`ConsumedMarker`，不显示未经授权的数据。

- [ ] **Step 5: Apply snapshots silently and live events visibly**

`GameHUDController.ApplyRecoverySnapshot` 只刷新 chip，不调用 feed/toast。实时 `TalentRuntimeEventMessage` 经 `ClientRoomService` 排序后调用 `AppendTalentEvent`，用 USS transition 或 DOTween 淡入；若用 DOTween，必须 `.SetLink(gameObject)`。

同一 `EventId` 重复到达不重复播放，controller 保存最后展示 ID；切场景/销毁时清理 callbacks 和 tween。

- [ ] **Step 6: Verify four-seat privacy in local fixtures**

使用同一服务端 source 生成四份快照，分别断言每席看见自己的 active 状态、只看见他人已揭示条目。Unity Play Mode 手工切换本地席位模拟数据，确认 chip 位置不遮挡风位、分数、牌山剩余数和操作按钮。

- [ ] **Step 7: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/TalentHudProjectionPolicy.cs Assets/UI/GameHUD Assets/UI/TalentChipTemplate.* Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: add talent table feedback'"
```

---

### Task 3: Integrate supplemental skills into the existing ActionPanel

**Files:**

- Create: `Assets/Scripts/Core/TalentActionPanelPolicy.cs`
- Modify: `Assets/UI/ActionPanel.uxml`
- Modify: `Assets/UI/ActionPanelStyles.uss`
- Modify: `Assets/UI/ActionPanelController.cs`
- Modify: `Assets/UI/FloatingTilePanelController.cs`
- Modify: `Assets/Scripts/Core/Agents/IPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Agents/LocalPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`

- [ ] **Step 1: Add a failing pure selection-state test**

新增 `TalentActionPanelPolicy` 纯 C# 状态机，覆盖点击技能不会完成基础 action callback：

```csharp
TalentActionPanelState state = TalentActionPanelPolicy.Open(
    decisionId: 72,
    baseActions: AllowedActions.Discard,
    talentActions: new[] { SheathedEdgeOption(), InterceptionOption() });

state = TalentActionPanelPolicy.SelectTalent(state, "interception");
runner.Check(!state.IsBaseDecisionCompleted && state.DecisionId == 72,
    "choosing a talent keeps the ordinary decision open");

state = TalentActionPanelPolicy.ResolveTalent(state, accepted: true);
runner.Check(state.BaseActions == AllowedActions.Discard,
    "accepted talent action still leaves discard available");
```

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少 action panel policy/state。

- [ ] **Step 3: Add a separate talent-action row and callback**

`ActionPanel.uxml` 在基础按钮上方增加 `TalentActionContainer`。Controller 接口固定为：

```csharp
public void ShowTalentActions(
    long decisionId,
    IReadOnlyList<TalentActionOption> options,
    Action<TalentActionOption> onSelected);

public void ClearTalentActions(long decisionId);
```

不要复用 `Action<ActionPanelChoice>`，不要在点击天赋后调用现有 `Hide()`。只有服务端关闭该 decision 或 basic action 已提交时才清理天赋按钮。

- [ ] **Step 4: Handle zero-target and target-selection talents**

- `藏锋`：点击后直接发送 `TalentActionMessage`，按钮进入等待态；accepted 后更新 chip，基础按钮仍在。
- `截流`：点击后打开 `FloatingTilePanelController` 的通用选择模式，显示服务端 `TalentActionOption` 给出的公开充能目标（席位名、天赋名、当前公开层数）；选中后发送。取消只关闭 picker。
- `定心`：被动，不生成按钮。

picker 中未发送的目标选择不写入 `ClientGameState`。网络恢复、decision 关闭或场景销毁时强制关闭 picker。

- [ ] **Step 5: Prevent duplicate input while preserving basic actions**

同一天赋请求在等待 resolved 时只禁用该 talent button；其他补充动作和基础麻将按钮按服务端 options 保持。错误返回恢复按钮并显示简短提示。`StaleDecision`/`DecisionExpired` 直接清空整个 action panel，等待权威快照/新 decision。

- [ ] **Step 6: Run regression and Unity interaction check, then commit**

在 Play Mode 验证：武装藏锋后仍能出牌；取消截流目标不影响出牌；截流被定心挡住后基础按钮仍在；重连时所有 picker 关闭但待出牌恢复。

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/UI/ActionPanel* Assets/UI/FloatingTilePanelController.cs Assets/Scripts/Core/Agents Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: integrate talent action buttons'"
```

---

### Task 4: Build the full-screen halftime sideboard

**Files:**

- Create: `Assets/Scripts/Core/SideboardDraftPolicy.cs`
- Create: `Assets/UI/SideboardPanel.uxml`
- Create: `Assets/UI/SideboardPanelStyles.uss`
- Create: `Assets/UI/SideboardPanelController.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Modify: `Assets/Scripts/Core/Network/ClientRoomState.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`

- [ ] **Step 1: Add failing sideboard draft-policy tests**

创建纯 C# `SideboardDraftPolicy`，测试 UI 本地草稿不直接改变权威 active slots：

```csharp
SideboardDraft draft = SideboardDraftPolicy.Create(startedMessage);
SideboardDraft changed = SideboardDraftPolicy.SetActive(
    draft, carriedTalentId: "interception", isActive: true);

runner.Check(!startedMessage.currentActiveTalentIds.Contains("interception")
        && changed.ActiveTalentIds.Contains("interception"),
    "editing sideboard creates a local copy");
runner.Check(changed.TotalAlienation <= changed.AlienationLimit && changed.CanSubmit,
    "valid draft enables lock-in");
SideboardDraft lockedAttempt = SideboardDraftPolicy.SetActive(
    changed, "starting_capital", isActive: false);
runner.Check(lockedAttempt.ActiveTalentIds.Contains("starting_capital")
        && lockedAttempt.ErrorCode == SideboardDraftErrorCodes.LockedTalent,
    "locked starting capital cannot be deactivated through the draft UI");
```

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少 draft policy。

- [ ] **Step 3: Create the full-screen tactical layout**

`SideboardPanel.uxml` 命名结构：

```xml
<ui:VisualElement name="SideboardOverlay" class="sideboard-overlay">
    <ui:Label name="TitleLabel" text="中场整备" class="sideboard-title" />
    <ui:Label name="TimerLabel" text="45" class="sideboard-timer" />
    <ui:VisualElement name="ActiveTalents" class="sideboard-active-talents" />
    <ui:VisualElement name="ReserveCards" class="sideboard-reserve-cards" />
    <ui:VisualElement name="BudgetTrack" class="alienation-track">
        <ui:VisualElement name="BudgetFill" class="alienation-fill" />
    </ui:VisualElement>
    <ui:Label name="BudgetLabel" text="异化值 0 / 80" />
    <ui:VisualElement name="SeatLockStatus" class="seat-lock-status" />
    <ui:Label name="ErrorLabel" class="sideboard-error" />
    <ui:Button name="LockButton" text="锁定方案" class="sideboard-lock-button" />
</ui:VisualElement>
```

九张携带卡都显示“生效/停用”状态；点击可切换天赋直接加入或移出生效集合，不使用 UI Toolkit drag-and-drop。`MainOnlyLocked` 卡显示锁图标且不能停用。当前生效数量不固定，预算条实时决定能否提交。

- [ ] **Step 4: Bind only to the private started message**

`SideboardPanelController.Open(SideboardStartedMessage)` 复制本家数据建立 draft，使用 `AlienationGaugePolicy` 刷新预算。点击锁定发送一次 `SideboardSubmitMessage`；客户端 policy 已知的非法草稿禁用提交按钮。若服务端仍返回 invalid，它会同时锁回原方案，controller 立即丢弃草稿并进入只读等待；收到任何 locked 结果后所有编辑禁用。

- [ ] **Step 5: Handle timeout, disconnect, and recovery deterministically**

倒计时只用于显示，真正 deadline 以服务端为准。收到 `SideboardLockedMessage(reason=timeout/disconnected)` 后丢弃草稿。应用重连快照且 `ownLocked=true` 时，只显示只读等待页；绝不重建可编辑草稿。

退出房间/场景时注销 `ClientRoomService` 事件，避免下一场误开旧面板。

- [ ] **Step 6: Perform four-client visual verification and commit**

按 `docs/network_verification.md` 启动 Dedicated Server 与至少两个真人客户端，其余 AI：第 4 局后确认同步进入中场；一人换装、一人超时、一人断线，确认均能结束；重连者不能重选；其他席只看到锁定状态。

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/UI/SideboardPanel* Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: add halftime sideboard UI'"
```

---

### Task 5: Explain talent contribution in round results

**Files:**

- Create: `Assets/Scripts/Core/TalentResultBreakdown.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`
- Modify: `Assets/Scripts/Core/Network/ClientGameState.cs`
- Modify: `Assets/UI/ResultPanel.uxml`
- Modify: `Assets/UI/ResultPanelStyles.uss`
- Modify: `Assets/UI/ResultPanelController.cs`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`

- [ ] **Step 1: Add failing normalization tests**

```csharp
TalentResultBreakdown result = TalentResultBreakdown.Create(
    baseFan: 8,
    eligibilityTalentFan: 2,
    postLegalTalentFan: 16,
    negativeTalentFan: -4);

runner.Check(result.FinalFan == 22,
    "result sums base, positive talent, and negative talent fan");
runner.Check(result.PositiveTalentFan == 18 && result.NegativeTalentFan == -4,
    "result preserves readable contribution buckets");
```

负面累计值强制 clamp 到 -8，单项由服务端效果层 clamp -4；纯展示类仍对异常输入做规范化并记录 warning。

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少 result breakdown 和网络字段。

- [ ] **Step 3: Send an authoritative breakdown in result messages and snapshots**

扩展胡牌结果：

```csharp
[Serializable]
public sealed class TalentFanBreakdownMessage
{
    public int baseFan;
    public int eligibilityTalentFan;
    public int postLegalTalentFan;
    public int negativeTalentFan;
    public int finalFan;
}
```

字段由 GameServer 的 `TalentFanResolution` 生成，客户端只校验总和用于诊断，不自行覆盖 `finalFan`。重连停在结算页时 snapshot 携带同一结构。

- [ ] **Step 4: Add a compact four-row explanation above fan details**

`ResultPanel.uxml` 增加 `TalentFanBreakdown`，行文固定：

- 基础番：`baseFan`
- 天赋增益：`+positiveTalentFan`（0 时弱化显示）
- 天赋压制：`negativeTalentFan`（0 时弱化显示）
- 最终番：`finalFan`

原有 81 番种详细列表保持不变。藏锋条目在天赋增益下显示“藏锋 +16”，快人一步显示“快人一步 +2”；不要把二者混入同一个不透明的总数。

- [ ] **Step 5: Verify win, loss, draw, and reconnect layouts**

胜者/放铳者结算显示相同权威 breakdown；流局没有 fan 时隐藏整个区域；整场总榜不重复显示上一小局 breakdown。用极长番种列表确认新增区域不会挤掉手牌缩略图和继续按钮。

- [ ] **Step 6: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core Assets/UI/ResultPanel* Tests/NetworkRegression; git commit -m 'feat: explain talent fan contribution'"
```

---

### Task 6: Give AI valid archetypes and deterministic talent decisions

**Files:**

- Create: `Assets/Scripts/Core/Agents/AiTalentLoadoutFactory.cs`
- Create: `Assets/Scripts/Core/Agents/SimpleTalentActionPolicy.cs`
- Modify: `Assets/Scripts/Core/Agents/IPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Agents/SimpleAIClient.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Test: `Tests/NetworkRegression/AiTalentPolicyTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

- [ ] **Step 1: Add failing loadout validity and decision-order tests**

```csharp
foreach (AlienationPreset preset in Enum.GetValues(typeof(AlienationPreset)))
{
    for (int seat = 0; seat < 4; seat++)
    {
        TrustedPlayerLoadout loadout = AiTalentLoadoutFactory.Create(preset, seat);
        runner.Check(loadout.TotalAlienation <= AlienationBudgetPolicy.GetLimit(preset),
            $"AI seat {seat} loadout fits {preset}");
    }
}

TalentActionOption chosen = SimpleTalentActionPolicy.Choose(new[]
{
    InterceptionOption(targetSeat: 3, charge: 1),
    InterceptionOption(targetSeat: 2, charge: 3),
    SheathedEdgeOption()
});
runner.Check(chosen.TalentId == "sheathed_edge",
    "AI arms its ready finisher before optional control");
```

再断言只有截流时选最高 charge，平手按目标席位再按 talentId 排序，保证测试可复现。

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少 AI loadout factory 和 action policy。

- [ ] **Step 3: Define four content archetypes through stable IDs**

`AiTalentLoadoutFactory` 可以使用稳定 ID 组装内容预设，这是注册/配置用途而非效果执行。四席轮换：

1. 藏锋终结：`sheathed_edge` + 小天赋；
2. 大牌构筑：`dragon_ascent` + `head_start`；
3. 克制构筑：`interception` + `composure`；
4. 资源构筑：`midas_touch` + `peek` + `draw_reward`。

每个 preset 先尝试完整构筑，再按成本从最低优先级移除，最后调用 `PlayerLoadoutCodec` 的服务端同款验证。标准牌库异化值为 0；任何 factory 结果校验失败都抛异常并阻止开房，不能静默送入非法 AI。

- [ ] **Step 4: Add an optional talent-decision client interface**

不要把补充动作塞进基础 `IPlayerClient.OnTurn` 返回值。新增：

```csharp
public interface ITalentActionClient
{
    IReadOnlyList<TalentActionRequest> ChooseTalentActions(TalentDecisionView view);
}
```

`SimpleAIClient` 实现该接口；`RemotePlayerClient` 不实现，真人继续通过网络消息异步提交。`TalentDecisionView` 只含 AI 本席可用 options、当前 decisionId 和服务端已经允许公开的目标数据。

- [ ] **Step 5: Execute AI supplements before requesting its base action**

GameServer 打开 AI 主回合后：获取 runtime options → 调用 `ChooseTalentActions` → 每个请求走与真人相同的 `TryValidateSupplementalAction` 和 runtime `TryActivate` → 再请求普通出牌。最多执行 options 数量次，遇到拒绝停止，防止策略死循环。藏锋优先；截流选最高公开 charge；没有合法动作返回空列表。

- [ ] **Step 6: Give AI a deterministic sideboard choice**

AI 在中场从 carried 9 张中按 archetype priority 形成生效子集并通过 `SideboardLoadoutPolicy`：优先加入针对已公开大型天赋的备选；超限时按费用从低到高停用可切换天赋，同费用按 talentId 排序，直到合法。若候选无效，显式锁回 original；记录 reason。不能把 AI 特权写入 runtime 校验。

- [ ] **Step 7: Run 100 deterministic simulated sessions and commit**

在回归程序集增加固定种子循环，至少运行 100 个简化决策序列，断言无非法目标、负次数、charge < 0、异化超限或未关闭 sideboard。若完整牌局模拟过慢，策略层使用构造的 authoritative views，不伪造 GameServer 成功。

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/Agents Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: add deterministic talent AI'"
```

---

### Task 7: Add playtest telemetry and perform the release verification pass

**Files:**

- Create: `Assets/Scripts/Talent/TalentTelemetry.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Create: `docs/talent_playtest_protocol.md`
- Test: `Tests/NetworkRegression/TalentPresentationTests.cs`
- Test: `Tests/NetworkRegression/AiTalentPolicyTests.cs`

- [ ] **Step 1: Add failing telemetry serialization tests**

```csharp
TalentTelemetryRecord record = new TalentTelemetryRecord
{
    matchId = "test-match",
    alienationPreset = (int)AlienationPreset.Standard,
    roundNumber = 5,
    eventType = "talent_activated",
    ownerSeatIndex = 0,
    talentId = "sheathed_edge",
    value = 3
};

string json = TalentTelemetry.Serialize(record);
runner.Check(json.Contains("\"talentId\":\"sheathed_edge\""),
    "telemetry is structured and machine-readable");
runner.Check(!json.Contains("hand") && !json.Contains("peekTiles"),
    "telemetry excludes concealed and private peek data");
```

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少 telemetry model/serializer。

- [ ] **Step 3: Record the minimum balance dataset**

使用 `JsonUtility` 可序列化的字段模型，避免在 Dedicated Server 引入另一套 JSON 依赖：

```csharp
[Serializable]
public sealed class TalentTelemetryRecord
{
    public string matchId;
    public int alienationPreset;
    public int gameMode;
    public int roundNumber;
    public string eventType;
    public int ownerSeatIndex;
    public string talentId;
    public int value;
    public int baseFan;
    public int positiveTalentFan;
    public int negativeTalentFan;
    public int finalFan;
    public int[] drawsPerSeat;
    public int winnerSeatIndex = -1;
    public bool effectBlocked;
    public bool effectApplied;
    public string sideboardReason;
}

public static string Serialize(TalentTelemetryRecord record) =>
    JsonUtility.ToJson(record);
```

每条 JSON line 只记录：match/session 匿名 ID、档位、模式、局号、事件类型、席位、talentId、公开数值、round draws per seat、基础番、天赋正/负番、最终番、获胜席、控制 blocked/applied、sideboard accepted/original/timeout。禁止记录 username、暗手、完整牌库顺序、Peek 内容或连接凭据。

事件点：match start、round start/end、talent reveal/activate/block/apply、sideboard lock、win resolution。runtime 生成玩法事件，Room/GameServer 只补上下文并交给注入的 `ITalentTelemetrySink`；测试使用 memory sink，Dedicated Server 默认写标准日志。

- [ ] **Step 4: Write a concrete playtest protocol**

`docs/talent_playtest_protocol.md` 包含：

- 三档各至少 20 场半庄，四种 AI archetype 轮换席位；
- 人类重点场景：大牌构筑、公开克制、三层藏锋终结、中场换装反制；
- 观察指标：每席平均摸牌轮数、10 摸前结束率、各天赋携带/揭示/激活/胜率、藏锋武装后兑现率、截流命中/被挡率、定心触发率、中场换装率、基础番与天赋番占比；
- 警戒线：平均每席摸牌显著高于 10、单天赋胜率偏离总体超过 10 个百分点、天赋正番中位数超过基础番、控制导致基础计划完全不可执行；
- 每轮只改一个成本/数值变量，保留版本号和对照样本。

- [ ] **Step 5: Run automated verification**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "dotnet build Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "rg -n 'TODO|TBD|FIXME|NotImplementedException' Assets/Scripts/Talent Assets/Scripts/Core/Network Assets/Scripts/Core/Agents Assets/UI docs/talent_playtest_protocol.md"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Expected: 测试与构建成功；placeholder 扫描只允许引用历史文档中的文字，不允许本功能代码命中；diff 无空白错误。

- [ ] **Step 6: Run Unity and Dedicated Server verification**

1. Unity 打开项目，无 C#、UXML、USS import 错误。
2. 本地 Single：现有六天赋、三新天赋各走至少一次关键效果。
3. Dedicated Server HalfGame：四席不同构筑，第 4 局中场，含断线/重连、超时、AI 托管。
4. 主回合打开截流 picker 后断线：恢复待出牌，不恢复 picker。
5. Peek 私有牌、exact alienation、未揭示天赋不串席。
6. 结算正确拆分基础番、天赋增益、天赋压制、最终番。
7. 全部动态 UI 在 16:9 与项目支持的最窄分辨率下不遮挡核心牌桌。

把执行日期、构建版本、通过/失败证据记录到 `docs/network_verification.md` 的本次验证记录区；失败项必须修复并重跑相关范围后才能宣称完成。

- [ ] **Step 7: Commit the verification assets**

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Talent Assets/Scripts/Core Assets/UI Tests/NetworkRegression docs; git commit -m 'feat: add talent playtest telemetry and verification'"
```

---

## Plan 3 Completion Gate

- [ ] 牌库编辑器完整显示 6 主 + 3 备选，并用单根 gauge 预览 40/80/120 当前档位。
- [ ] 超预算构筑可保存但有明确警告，服务器仍拒绝超限进房。
- [ ] 本家天赋条显示 active，其他席只显示已知天赋和最后公开值。
- [ ] 技能按钮与普通动作并存，天赋选择/失败/成功不意外关闭待出牌。
- [ ] 中场全屏页能处理有效提交、非法提交立即锁回原方案、超时、断线、重连只读等待。
- [ ] 结算清楚拆分基础番、天赋增益、天赋压制和最终番。
- [ ] AI 构筑在三档均合法，决策只用允许信息并走真人同款校验。
- [ ] telemetry 不包含隐私数据，playtest protocol 可直接执行。
- [ ] .NET 回归、构建、Unity 编译、Dedicated Server 半庄验证和布局检查全部有证据通过。
- [ ] 工作区无占位实现、无 UGUI/Canvas 新依赖、无未预期改动。
