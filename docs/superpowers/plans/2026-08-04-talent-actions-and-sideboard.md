# Talent Actions and Sideboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有主回合决策上增加不关闭基本动作的天赋补充动作，实现 `藏锋`、`截流`、`定心` 三项锚点天赋，并在半庄/全庄第 4 小局后加入一次 45 秒并行中场备牌。

**Architecture:** Dedicated Server 的 `Room` 是唯一比赛权威并持有整场 `TalentMatchRuntime`。普通麻将动作继续由 `NetworkDecisionTracker` 控制，天赋请求先以同一 `decisionId` 做只读资格校验，再由 runtime 用 `talentId` 找到实例并调用多态激活方法；成功后不写入 `SubmittedSeats`、不关闭主回合。负面效果经过统一防御链；中场备牌由同一 `Room` 持有的 `SideboardDecisionTracker` 管理，收到选择后原子替换该席生效集合，断线或超时立即锁回原集合。

**Tech Stack:** Unity/Tuanjie、C#、`TalentMatchRuntime`、WebSocket 协议 v3、UI Toolkit 消息模型（具体视觉在第三份计划）、`Tests/NetworkRegression`。

## Global Constraints

- 开始前必须完成 `docs/superpowers/plans/2026-08-04-room-authority-remove-local-mode.md` 的 Completion Gate。
- 开始前必须完成 `docs/superpowers/plans/2026-08-04-talent-foundation-and-alienation.md` 的 Completion Gate。
- 阶段执行、验证、合并和真人验收边界以 `docs/superpowers/specs/2026-08-12-talent-phase2-3-validation-boundary-design.md` 为准。
- 本计划与第三阶段持续使用同一功能分支 `codex/talent-actions-ui-unified`；本计划完成后不得合并 `master`，必须直接在该分支继续第三阶段。
- 本计划不新增临时 UI、调试面板、快捷键或编辑器直开入口；正式主动技能、中场备牌和异化档位 UI 全部由第三阶段实现。
- 本计划仍须完成 `Room` / `GameServer` / `TalentMatchRuntime` 的正式生产接线，但新增验收只覆盖纯 C# policy、tracker、规则、状态机、协议模型和组件边界，不新增 WebSocket 全链路或真实 `Room -> GameServer -> TalentMatchRuntime` 集成验收。
- 仍运行现有完整 NetworkRegression 与编译以防旧功能回归；Unity、Dedicated Server、真人联机和新增生产链路端到端验证统一推迟到第三阶段。
- `Room` 持有 runtime、sideboard tracker 和跨局状态；客户端与 `GameManager` 只提交请求、消费按席投影，不设置天赋生效集合。
- `talentId` 只负责在玩家已携带的 runtime entry 中定位实例；`GameServer`/`Room` 不得根据 ID 解释、分支或执行效果。
- 天赋补充动作只复用当前决策的身份与时限，不占用普通动作提交位，也不延长原决策 deadline。
- 首批主动天赋仅允许主回合窗口；响应窗口接口保留枚举能力，但本计划不开放响应型天赋。
- 控制不能修改对手暗手牌，不能禁止基础摸/打/吃/碰/杠/胡，也不能让已合法的胡失效。
- `截流` 只影响已公开、当前层数大于 0 的充能天赋；目标和层数必须由服务端重验。
- `定心` 每小局只取消第一次负面天赋效果；即使取消，来源天赋的次数/资源仍消耗。
- `藏锋` 的 +16 番只能在基础胡牌已经满足合法门槛后加入，不能帮助不满 8 番的牌型取得胡牌资格。
- 半庄和全庄仅在完成第 4 小局后进入一次中场；单局、东风局不进入。
- 中场只允许从开场携带的 6 主 + 3 备选中提交新的生效集合；可以在预算内追加备选、停用可切换主天赋或重新启用它们，生效数量不固定为 6。`StartingCapital` 必须始终留在生效集合。
- 已揭示天赋在中场后仍保持“已知”，但是否继续激活不公开；最后一次公开动态数值冻结显示。

---

## File and Interface Map

**新增文件**

- `Assets/Scripts/Talent/TalentActionModels.cs`
- `Assets/Scripts/Talent/TalentNegativeEffect.cs`
- `Assets/Scripts/Talent/TalentFanModifierPolicy.cs`
- `Assets/Scripts/Talent/Impl/SheathedEdgeTalent.cs`
- `Assets/Scripts/Talent/Impl/InterceptionTalent.cs`
- `Assets/Scripts/Talent/Impl/ComposureTalent.cs`
- `Assets/Scripts/Core/Network/SideboardDecisionTracker.cs`
- `Tests/NetworkRegression/TalentActionTests.cs`
- `Tests/NetworkRegression/SideboardTests.cs`

**主要修改文件**

- `Assets/Scripts/Talent/TalentRule.cs`
- `Assets/Scripts/Talent/TalentContext.cs`
- `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- `Assets/Scripts/Talent/TalentRuntimeState.cs`
- `Assets/Scripts/Core/Network/NetworkDecisionTracker.cs`
- `Assets/Scripts/Core/Network/NetworkActionSubmissionPolicy.cs`
- `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- `Assets/Scripts/Core/Network/Room.cs`
- `Assets/Scripts/Core/Network/RoomManager.cs`
- `Assets/Scripts/Core/Network/RoomState.cs`
- `Assets/Scripts/Core/Network/GameServer.cs`
- `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`
- `Assets/Scripts/Core/Network/ClientGameState.cs`
- `Assets/Scripts/Core/Network/ClientRoomState.cs`
- `Assets/Scripts/Core/Network/ClientRoomService.cs`
- `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- `Assets/Scripts/Core/ScoringOptions.cs`
- `Tests/NetworkRegression/NetworkRegression.csproj`
- `Tests/NetworkRegression/Program.cs`

**本计划锁定的动作接口**

```csharp
public sealed class TalentActionRequest
{
    public long DecisionId { get; set; }
    public string TalentId { get; set; }
    public int TargetSeatIndex { get; set; } = -1;
    public string TargetTalentId { get; set; }
}

public abstract class TalentRule
{
    public virtual void GetAvailableActions(
        TalentActionQueryContext context,
        List<TalentActionOption> output) { }

    public virtual TalentActionResult TryActivate(
        TalentActivationContext context,
        TalentActionRequest request) => TalentActionResult.NotSupported();

    public virtual int GetPostLegalFanBonus(TalentWinContext context) => 0;
    public virtual int GetPostLegalFanPenalty(TalentWinContext context) => 0;
}
```

---

### Task 1: Add supplemental talent actions without consuming the base decision

**Files:**

- Create: `Assets/Scripts/Talent/TalentActionModels.cs`
- Modify: `Assets/Scripts/Talent/TalentRule.cs`
- Modify: `Assets/Scripts/Talent/TalentContext.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Modify: `Assets/Scripts/Core/Network/NetworkDecisionTracker.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Test: `Tests/NetworkRegression/TalentActionTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

- [ ] **Step 1: Add failing tracker tests for non-consuming validation**

```csharp
NetworkDecisionTracker tracker = new NetworkDecisionTracker();
NetworkDecisionContext decision = tracker.OpenMainTurn(2, FutureDeadline());

bool accepted = tracker.TryValidateSupplementalAction(
    decision.DecisionId,
    seatIndex: 2,
    requiredPhase: NetworkDecisionPhase.MainTurn,
    out string errorCode);

runner.Check(accepted && errorCode == null,
    "supplemental action validates against the active main decision");
runner.Check(tracker.Active.SubmittedSeats.Length == 0,
    "supplemental validation does not consume the base action slot");

runner.Check(tracker.TrySubmitNetworkAction(
    decision.DecisionId, 2, ClientActionType.Discard, out _),
    "ordinary discard remains legal after a supplemental action");
```

再覆盖错误席位、过期 deadline、旧 decisionId、响应阶段请求主回合天赋，错误码分别复用 `WrongController`、`DecisionExpired`、`StaleDecision`、`WrongPhase`。

- [ ] **Step 2: Run the regression and confirm RED**

Expected: 缺少 `TryValidateSupplementalAction` 和动作模型。

- [ ] **Step 3: Implement a read-only decision admission method**

在 `NetworkDecisionTracker` 添加：

```csharp
public bool TryValidateSupplementalAction(
    long decisionId,
    int seatIndex,
    NetworkDecisionPhase requiredPhase,
    out string errorCode)
{
    errorCode = ValidateActiveDecision(decisionId);
    if (errorCode != null) return false;
    if (_active.Phase != requiredPhase)
    {
        errorCode = NetworkErrorCodes.WrongPhase;
        return false;
    }
    if (requiredPhase == NetworkDecisionPhase.MainTurn
        && seatIndex != _active.ControllerSeatIndex)
    {
        errorCode = NetworkErrorCodes.WrongController;
        return false;
    }
    if (requiredPhase != NetworkDecisionPhase.MainTurn
        && !_active.EligibleSeats.Contains(seatIndex))
    {
        errorCode = NetworkErrorCodes.NotEligible;
        return false;
    }
    return true;
}
```

把“无决策 / ID / deadline”抽为私有 `ValidateActiveDecision`，普通提交和补充校验共享，但只有 `TrySubmitNetworkAction` 调用 `WithSubmittedSeat`。

- [ ] **Step 4: Add JsonUtility-safe wire messages and runtime result types**

`NetworkMessage.cs`：

```csharp
[Serializable]
public sealed class TalentActionMessage
{
    public long decisionId;
    public string talentId;
    public int targetSeatIndex = -1;
    public string targetTalentId;
}

[Serializable]
public sealed class TalentActionResolvedMessage
{
    public long decisionId;
    public int ownerSeatIndex;
    public string talentId;
    public bool accepted;
    public string errorCode;
}
```

`TalentActionModels.cs`：

```csharp
public sealed class TalentActionOption
{
    public string TalentId { get; set; }
    public int TargetSeatIndex { get; set; } = -1;
    public string TargetTalentId { get; set; }
}

public sealed class TalentActionResult
{
    public bool Accepted { get; private set; }
    public string ErrorCode { get; private set; }

    public static TalentActionResult Success() => new TalentActionResult { Accepted = true };
    public static TalentActionResult Reject(string code) =>
        new TalentActionResult { Accepted = false, ErrorCode = code };
    public static TalentActionResult NotSupported() => Reject(TalentActionErrorCodes.NotAvailable);
}
```

定义常量 `NotAvailable`、`InvalidTarget`、`InsufficientResource`、`AlreadyUsedThisTurn`，网络层仍传稳定字符串。

- [ ] **Step 5: Route by identity, execute by polymorphism**

`TalentMatchRuntime.TryActivate` 的查找和调用边界必须清晰：

```csharp
public TalentActionResult TryActivate(
    int ownerSeatIndex,
    TalentActionRequest request,
    TalentActivationContext context)
{
    RuntimeEntry entry = FindActiveEntry(ownerSeatIndex, request.TalentId);
    if (entry == null) return TalentActionResult.Reject(TalentActionErrorCodes.NotCarriedOrInactive);

    TalentMetadata metadata = entry.Metadata;
    if (!metadata.ActivationWindow.HasFlag(context.RequiredWindow))
        return TalentActionResult.Reject(TalentActionErrorCodes.NotAvailable);

    return entry.Rule.TryActivate(context.WithState(entry.State), request);
}
```

这是允许的 ID 实例查找；禁止在该方法中写 `request.TalentId == "sheathed_edge"` 等效果分支。

- [ ] **Step 6: Wire Room and GameServer without closing the decision**

`Room.SubmitTalentAction` 校验房间/席位/真人控制边界后调用 `GameServer.SubmitNetworkTalentAction`。`GameServer` 顺序固定为：

1. `TryValidateSupplementalAction`；
2. runtime 重新计算该席当前可用动作；
3. 多态 `TryActivate`；
4. 广播过滤后的 runtime 事件和私有 resolved 消息；
5. 不调用 `CloseDecision`，不完成 `_pendingActionTcs`。

重复请求由天赋自身资源/每回合标志拒绝；不能把成功请求写入基本动作的 `SubmittedSeats`。

- [ ] **Step 7: Run regression and commit**

Run:

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
```

Commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Talent Assets/Scripts/Core/Network Tests/NetworkRegression; git commit -m 'feat: add supplemental talent actions'"
```

---

### Task 2: Implement the control channel and `定心`

**Files:**

- Create: `Assets/Scripts/Talent/TalentNegativeEffect.cs`
- Create: `Assets/Scripts/Talent/Impl/ComposureTalent.cs`
- Modify: `Assets/Scripts/Talent/TalentRule.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Test: `Tests/NetworkRegression/TalentActionTests.cs`

- [ ] **Step 1: Add failing tests for first-effect cancellation and round reset**

```csharp
TalentNegativeEffect first = BuildLayerReduction(sourceSeat: 1, targetSeat: 0);
TalentNegativeEffectResult blocked = runtime.ApplyNegativeEffect(first);
TalentNegativeEffectResult second = runtime.ApplyNegativeEffect(first);

runner.Check(blocked.WasBlocked && !blocked.WasApplied,
    "composure blocks the first negative talent effect each round");
runner.Check(!second.WasBlocked && second.WasApplied,
    "the second negative effect in the same round is not blocked");

runtime.EndRound(DrawOutcome(), session);
runtime.BeginRound(NextRoundContext(session));
runner.Check(runtime.ApplyNegativeEffect(first).WasBlocked,
    "composure refreshes at the next round boundary");
```

另断言触发前 `定心` 未公开，第一次拦截后公开且本局显示 consumed。

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少负面效果模型、防御钩子和 `ComposureTalent`。

- [ ] **Step 3: Add a narrow negative-effect contract**

```csharp
public sealed class TalentNegativeEffect
{
    public int SourceSeatIndex { get; set; }
    public string SourceTalentId { get; set; }
    public int TargetSeatIndex { get; set; }
    public string TargetTalentId { get; set; }
    public string EffectType { get; set; }
    public Action Apply { get; set; }
}

public sealed class TalentNegativeEffectResult
{
    public bool WasBlocked { get; set; }
    public bool WasApplied { get; set; }
    public string BlockingTalentId { get; set; }
}
```

`Apply` 只能闭包服务端 runtime 内已经解析的目标状态，不能捕获客户端对象或任意房间状态。`TalentRule` 增加：

```csharp
public virtual bool TryBlockNegativeEffect(
    TalentNegativeEffectContext context,
    TalentNegativeEffect effect) => false;
```

runtime 按 priority 遍历目标席的激活防御天赋；第一个返回 true 后发阻挡事件且不调用 `effect.Apply`，否则只调用一次。

- [ ] **Step 4: Implement `定心` as a round-scoped defense rule**

```csharp
[TalentRule("composure", "定心", "每小局首次受到的负面天赋效果无效。",
    TalentTier.Small, 6, TalentPhase.ActionValidation,
    StateScope = TalentStateScope.Round,
    RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect)]
public sealed class ComposureTalent : TalentRule
{
    private const string ConsumedKey = "consumed";

    public override bool TryBlockNegativeEffect(
        TalentNegativeEffectContext context,
        TalentNegativeEffect effect)
    {
        if (context.State.GetFlag(ConsumedKey, TalentStateScope.Round)) return false;
        context.State.SetFlag(ConsumedKey, true, TalentStateScope.Round);
        context.Reveal("blocked_negative_effect", 1);
        return true;
    }
}
```

runtime 的 `BeginRound` 清除 round flag，因此不需要规则手动复位。

- [ ] **Step 5: Enforce control safety invariants at the effect boundary**

`TalentNegativeEffect` 不提供手牌、基本动作许可或胡牌合法性的写入口。负面效果类型先只允许 `ReducePublicChargeLayer`；未知类型拒绝并记录服务端日志。未来新增持续 debuff 时，统一在此处落实“单效果最多 -4 番、同时最多 -8 番、最多两种 active negative statuses、同类型不叠加强度”，本计划不为 `截流` 虚构持续状态。

- [ ] **Step 6: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Talent Tests/NetworkRegression; git commit -m 'feat: add composure control defense'"
```

---

### Task 3: Implement `藏锋` and post-legal fan bonuses

**Files:**

- Create: `Assets/Scripts/Talent/Impl/SheathedEdgeTalent.cs`
- Create: `Assets/Scripts/Talent/TalentFanModifierPolicy.cs`
- Modify: `Assets/Scripts/Talent/TalentRule.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Modify: `Assets/Scripts/Core/Network/GameServer.cs`
- Modify: `Assets/Scripts/Core/ScoringOptions.cs`
- Test: `Tests/NetworkRegression/TalentActionTests.cs`
- Test: `Tests/NetworkRegression/ActionValidationTests.cs`

- [ ] **Step 1: Add failing charge, timing, and scoring regressions**

覆盖精确语义：

```csharp
runtime.EndRound(Outcome(winnerSeat: 1), session); // owner 0 did not win
runtime.EndRound(Outcome(winnerSeat: null), session);
runtime.EndRound(Outcome(winnerSeat: 2), session);
runtime.EndRound(Outcome(winnerSeat: 3), session);
runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 3,
    "sheathed edge gains one layer on non-winning rounds and caps at three");

runtime.OpenMainDecision(ownerSeat: 0, decisionId: 91);
runner.Check(runtime.GetAvailableActions(0, MainTurnContext(91)).Count == 1,
    "three layers can arm only on the first main decision of the round");
runner.Check(runtime.TryActivate(0, SheathedEdgeRequest(91), ActivationContext(91)).Accepted,
    "arming spends all three layers");
runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 0,
    "arming immediately exposes the spent charge");
```

算番必须有两个案例：基础 6 番 + 藏锋 16 仍不可胡；基础 8 番 + 藏锋 16 最终为 24 番。另用测试规则返回 -10 和 -5 两个负向修正，断言单项分别压到 -4、总和压到 -8，基础合法胡牌不会因最终番降低而被撤销。

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少锚点规则、首个主回合标志和 post-legal fan 通道。

- [ ] **Step 3: Track the first main decision per seat and round**

`TalentMatchRuntime.OpenMainDecision(int seatIndex, long decisionId)` 在每局 round state 中记录首次 decisionId；同一席之后的主回合不能再返回 `IsFirstMainDecisionOfRound = true`。GameServer 每次打开主回合 `NetworkDecisionContext` 后立即调用，重连时不重开新的 decision，也不重置该标志。

- [ ] **Step 4: Implement a public charge target interface**

```csharp
public interface IPublicChargeTalent
{
    int GetCurrentCharge(TalentRuntimeState state);
    bool TryReduceCharge(TalentRuntimeState state, int amount);
}
```

runtime 把接口和该 entry 的 `TalentRuntimeState` 包成只读 `PublicChargeTarget`；不要让规则用 MonoBehaviour 字段保存跨局值。`GetPublicChargeTargets(requestingSeat)` 只返回 `IsActive && IsRevealed && GetCurrentCharge(state) > 0` 的对手 entry。

- [ ] **Step 5: Implement `藏锋`**

```csharp
[TalentRule("sheathed_edge", "藏锋", "未获胜积攒锋，消耗3层令本局下次合法胡牌+16番。",
    TalentTier.Large, 28, TalentPhase.Scoring,
    StateScope = TalentStateScope.Match,
    ActivationWindow = TalentActivationWindow.MainTurn,
    RevealPolicy = TalentRevealPolicy.PublicAtMatchStart,
    SideboardPolicy = TalentSideboardPolicy.MainOnly)]
public sealed class SheathedEdgeTalent : TalentRule, IPublicChargeTalent
{
    private const string ChargeKey = "edge";
    private const string ArmedKey = "armed";

    public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome)
    {
        if (outcome.WinnerSeatIndex == context.OwnerSeatIndex) return;
        int current = context.State.GetCounter(ChargeKey, TalentStateScope.Match);
        context.SetPublicCounter(ChargeKey, Math.Min(3, current + 1), TalentStateScope.Match);
    }

    public override TalentActionResult TryActivate(
        TalentActivationContext context,
        TalentActionRequest request)
    {
        if (!context.IsFirstMainDecisionOfRound) return TalentActionResult.NotSupported();
        if (context.State.GetCounter(ChargeKey, TalentStateScope.Match) < 3)
            return TalentActionResult.Reject(TalentActionErrorCodes.InsufficientResource);
        context.SetPublicCounter(ChargeKey, 0, TalentStateScope.Match);
        context.State.SetFlag(ArmedKey, true, TalentStateScope.Round);
        context.EmitPublic("armed", 1);
        return TalentActionResult.Success();
    }

    public override int GetPostLegalFanBonus(TalentWinContext context) =>
        context.State.GetFlag(ArmedKey, TalentStateScope.Round) ? 16 : 0;

    public int GetCurrentCharge(TalentRuntimeState state) =>
        state.GetCounter(ChargeKey, TalentStateScope.Match);

    public bool TryReduceCharge(TalentRuntimeState state, int amount)
    {
        int current = GetCurrentCharge(state);
        if (amount <= 0 || current <= 0) return false;
        state.SetCounter(ChargeKey, Math.Max(0, current - amount), TalentStateScope.Match);
        return true;
    }
}
```

`GetAvailableActions` 只在首主回合、3 层、未 armed 时添加一个无目标 option。`GetCurrentCharge(state)` 读取 match counter；`TryReduceCharge(state, amount)` 用 `Math.Max(0, current - amount)` 更新同一 counter。禁止在 rule 实例字段保存权威层数。

- [ ] **Step 6: Split eligibility fan from post-legal fan**

保留 `ScoringOptions.BonusFan` 表示会参与 8 番门槛的加成（`HeadStart`）。新增结果结构而不是把藏锋写回 `BonusFan`：

```csharp
public sealed class TalentFanResolution
{
    public int EligibilityFan { get; set; }
    public int PostLegalBonusFan { get; set; }
    public int NegativeFan { get; set; }
    public int FinalFan { get; set; }
}
```

增加唯一的负番限制策略：

```csharp
public static class TalentFanModifierPolicy
{
    public const int MinPerEffect = -4;
    public const int MinTotal = -8;

    public static int ClampPenalty(int requested) => Math.Max(MinPerEffect, Math.Min(0, requested));

    public static int SumPenalties(IEnumerable<int> requested) =>
        Math.Max(MinTotal, requested.Sum(ClampPenalty));
}
```

`GameServer` 先调用 `MahjongLogic.CheckWinWithFan(..., options)`；返回 false 立即拒绝，不查询 post-legal bonus/penalty。返回 true 后调用 runtime 汇总 `GetPostLegalFanBonus` 和 `GetPostLegalFanPenalty`，最终番数为 `Math.Max(0, serverFan + postLegalBonus + negativeFan)`，并把拆分数据保存在本局结果中。候选校验和最终落账调用同一只读计算；负番发生在合法性确认之后，所以不能撤销已经合法的胡。只有确认胡牌并结束小局时发公开 armed-consumed 事件。

- [ ] **Step 7: Ensure an unused arm expires at round end**

round state 自然清理 `armed`。添加未胡结束的测试，确认下一局没有 +16，已花的 3 层不返还。

- [ ] **Step 8: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Talent Assets/Scripts/Core Tests/NetworkRegression; git commit -m 'feat: add sheathed edge finisher'"
```

---

### Task 4: Implement `截流` against public charge talents

**Files:**

- Create: `Assets/Scripts/Talent/Impl/InterceptionTalent.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Test: `Tests/NetworkRegression/TalentActionTests.cs`

- [ ] **Step 1: Add failing usage, targeting, and defense regressions**

必须覆盖：整场 3 次、同一主回合最多 1 次、目标需对手/公开/active/层数大于 0、成功减 1、`定心` 阻挡时仍耗次数、首次使用后公开且显示剩余次数。

```csharp
TalentActionResult blocked = runtime.TryActivate(
    ownerSeatIndex: 1,
    InterceptionRequest(decisionId: 44, targetSeat: 0, targetTalent: "sheathed_edge"),
    MainActivationContext(44));

runner.Check(blocked.Accepted, "a defended interception is still an accepted use");
runner.Check(runtime.GetPrivateCounter(1, "interception", "uses_remaining") == 2,
    "composure does not refund interception usage");
runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 2,
    "blocked interception leaves target charge unchanged");
```

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少 `InterceptionTalent` 和 charge target resolution。

- [ ] **Step 3: Resolve the target entirely on the server**

runtime 用 `(targetSeatIndex, targetTalentId)` 定位 entry 后重验：不是来源席、active、revealed、实现 `IPublicChargeTalent`、当前层数 > 0。客户端传来的层数、公开状态或 uses 一律忽略。

- [ ] **Step 4: Implement `截流` with consume-before-defense ordering**

```csharp
[TalentRule("interception", "截流", "整场3次，令一项公开充能天赋减少1层。",
    TalentTier.Small, 8, TalentPhase.ActionValidation,
    StateScope = TalentStateScope.Match,
    ActivationWindow = TalentActivationWindow.MainTurn,
    RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect)]
public sealed class InterceptionTalent : TalentRule
{
    private const string UsesKey = "uses_remaining";
    private const string UsedDecisionKey = "used_decision";

    public override void InitializeMatchState(TalentMatchContext context)
    {
        context.State.SetCounter(UsesKey, 3, TalentStateScope.Match);
    }

    public override TalentActionResult TryActivate(
        TalentActivationContext context,
        TalentActionRequest request)
    {
        int remaining = context.State.GetCounter(UsesKey, TalentStateScope.Match);
        if (remaining <= 0)
            return TalentActionResult.Reject(TalentActionErrorCodes.InsufficientResource);
        if (context.State.GetToken(UsedDecisionKey, TalentStateScope.Round) == context.DecisionId)
            return TalentActionResult.Reject(TalentActionErrorCodes.AlreadyUsedThisTurn);

        PublicChargeTarget target = context.ResolvePublicChargeTarget(request);
        if (target == null) return TalentActionResult.Reject(TalentActionErrorCodes.InvalidTarget);

        context.State.SetCounter(UsesKey, remaining - 1, TalentStateScope.Match);
        context.State.SetToken(UsedDecisionKey, context.DecisionId, TalentStateScope.Round);
        context.RevealWithPublicCounter("uses_remaining", remaining - 1);
        context.ApplyNegativeEffect(TalentNegativeEffect.ReduceCharge(target, 1));
        return TalentActionResult.Success();
    }
}
```

在 `TalentRuntimeState` 增加 `long GetToken(string key, TalentStateScope scope)` 与 `SetToken(string key, long value, TalentStateScope scope)`，并在 `ResetRoundState()` 清理 round tokens，避免把长期递增的 `decisionId` 强转为 `int`。

- [ ] **Step 5: Keep public knowledge sticky**

首次使用令 `IsRevealed = true`；之后公开事件携带剩余次数 2/1/0。中场停用不会发送“已停用”，对手界面继续显示最后公开值。重新激活也不自动广播，只有下一次公开效果更新数值。

- [ ] **Step 6: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Talent Tests/NetworkRegression; git commit -m 'feat: add interception control talent'"
```

---

### Task 5: Add one atomic halftime sideboard after round four

**Files:**

- Create: `Assets/Scripts/Core/Network/SideboardDecisionTracker.cs`
- Modify: `Assets/Scripts/Core/Network/Messages/NetworkMessage.cs`
- Modify: `Assets/Scripts/Core/Network/RoomState.cs`
- Modify: `Assets/Scripts/Core/Network/Room.cs`
- Modify: `Assets/Scripts/Core/Network/RoomManager.cs`
- Modify: `Assets/Scripts/Talent/TalentMatchRuntime.cs`
- Test: `Tests/NetworkRegression/SideboardTests.cs`
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/Program.cs`

- [ ] **Step 1: Add failing phase-entry tests for every game mode**

```csharp
runner.Check(!ShouldOpenSideboard(GameMode.Single, completedRounds: 1),
    "single game has no sideboard");
runner.Check(!ShouldOpenSideboard(GameMode.EastOnly, completedRounds: 4),
    "east-only has no sideboard");
runner.Check(ShouldOpenSideboard(GameMode.HalfGame, completedRounds: 4),
    "half game opens sideboard after round four");
runner.Check(ShouldOpenSideboard(GameMode.FullGame, completedRounds: 4),
    "full game opens sideboard once after round four");
runner.Check(!ShouldOpenSideboard(GameMode.FullGame, completedRounds: 8),
    "full game does not reopen sideboard later");
```

Room 回归断言第 4 局结束后先进入 `WaitingForSideboard`，全部锁定后才进入 `WaitingForNextRound`。

- [ ] **Step 2: Run regression and confirm RED**

Expected: 缺少 sideboard state、tracker 和消息。

- [ ] **Step 3: Implement a dedicated one-shot tracker**

```csharp
public sealed class SideboardDecisionTracker
{
    public long DecisionId { get; }
    public long DeadlineUnixMilliseconds { get; }
    public bool IsLocked(int seatIndex);
    public bool TrySubmit(int seatIndex, string[] activeTalentIds, out string errorCode);
    public void LockOriginal(int seatIndex, string reason);
    public bool AllLocked { get; }
}
```

构造时复制四席“进入中场前”的生效 ID 集合。`TrySubmit` 不直接改 runtime，只记录按原 9 槽顺序规范化的候选集合；Room 在验证成功后调用 runtime 原子替换并锁定。重复提交返回 `SideboardAlreadyLocked`。

- [ ] **Step 4: Add sideboard wire messages**

```csharp
[Serializable]
public sealed class SideboardStartedMessage
{
    public long decisionId;
    public long deadlineUnixMilliseconds;
    public string[] carriedMainTalentIds;
    public string[] carriedReserveTalentIds;
    public string[] currentActiveTalentIds;
    public int alienationLimit;
    public int currentTotalAlienation;
}

[Serializable]
public sealed class SideboardSubmitMessage
{
    public long decisionId;
    public string[] activeTalentIds;
}

[Serializable]
public sealed class SideboardLockedMessage
{
    public long decisionId;
    public bool acceptedSelection;
    public string reason;
    public int ownTotalAlienation;
}
```

`SideboardStartedMessage` 全部是本家私有内容；其他席只收到“某席已锁定”的布尔等待状态，不收到选择、精确总值或激活状态。`currentActiveTalentIds` 是集合语义，服务端按九个携带槽的固定顺序发送，客户端不得借数组位置解释为新的槽位。

- [ ] **Step 5: Validate an active six-slot selection atomically**

新增 `SideboardLoadoutPolicy.TryValidate`，顺序固定：

1. 数组不可为 `null` 且长度不超过 9；长度为 0 只在没有锁定天赋时才可接受；
2. 非空 ID 都属于开场携带的 9 个 ID；
3. 无重复；
4. 所有 `MainOnlyLocked` 天赋仍包含在生效集合；
5. 当前生效集合的牌库 + 天赋总异化值不超过房间档位；
6. 成功结果按原 9 槽顺序规范化，客户端提供的排列不影响权威结果。

品阶、主/备槽和 `MainOnly` 携带限制已经在进房 codec 对 9 个携带槽校验；中场只开关这些既有实例，不重新分配携带槽。

成功时返回规范化数组和新精确总值并锁定该席；任何校验失败都不改变 runtime，但 Room 必须立即 `LockOriginal(seatIndex, "invalid")`，随后广播该席已锁定。玩家不能重交，避免通过反复试探服务端隐藏规则或拖延中场。

- [ ] **Step 6: Replace active entries without replaying match-start effects**

Plan 1 的 runtime 必须已实例化全部 9 个 carried entries，主槽 `IsActive=true`、备选为 false。增加：

```csharp
public void ReplaceActiveSet(int seatIndex, IReadOnlyCollection<string> activeTalentIds)
{
    HashSet<string> selected = new HashSet<string>(activeTalentIds, StringComparer.Ordinal);
    foreach (RuntimeEntry entry in EntriesForSeat(seatIndex))
        entry.State.IsActive = selected.Contains(entry.Rule.Id);
}
```

替换不能调用 `BeginMatch` 或补发 `StartingCapital`；inactive entry 的 match state 保留。`IsRevealed` 永不回退。因为中场发生在小局之间，替换前先完成上一局 `EndRound`，替换后下一局再 `BeginRound`。

- [ ] **Step 7: Integrate timeout, disconnect, and AI behavior**

Room 进入中场时启动 45 秒计时。AI 席立即 `LockOriginal("ai_default")`；此阶段断线的真人立即 `LockOriginal("disconnected")`；deadline 到期未锁定真人 `LockOriginal("timeout")`。全部锁定后取消 timer，广播中场结束，转入 `WaitingForNextRound` 并沿用现有 next-round ready 流程。

如果玩家断线后重连，已锁定状态不能恢复选择权；只能收到 `SideboardLockedMessage` 和当前等待进度。

- [ ] **Step 8: Run regression and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/Network Assets/Scripts/Talent Tests/NetworkRegression; git commit -m 'feat: add halftime talent sideboard'"
```

---

### Task 6: Snapshot only authoritative talent and sideboard state

**Files:**

- Modify: `Assets/Scripts/Core/Network/RoomGameSnapshot.cs`
- Modify: `Assets/Scripts/Core/Network/ClientGameState.cs`
- Modify: `Assets/Scripts/Core/Network/ClientRoomState.cs`
- Modify: `Assets/Scripts/Core/Network/ClientRoomService.cs`
- Modify: `Assets/Scripts/Core/Network/RemoteServerProxy.cs`
- Test: `Tests/NetworkRegression/SnapshotReconnectTests.cs`
- Test: `Tests/NetworkRegression/SideboardTests.cs`

- [ ] **Step 1: Add failing reconnect tests for main-turn and sideboard boundaries**

覆盖两类恢复：

- 主回合蓄力选择框尚未提交时断线：快照只恢复当前 main-turn `decisionId` 和待出牌状态；不含“正在选藏锋/截流目标”。重连客户端从 runtime 公布的可用动作重新构建按钮。
- 中场本地尚未提交时断线：服务端立即锁回原方案；重连只看到自己已锁定和其他席等待状态，不能重新打开编辑器。

```csharp
runner.Check(snapshot.decision != null && snapshot.decision.phase == (int)NetworkDecisionPhase.MainTurn,
    "main-turn decision remains authoritative after reconnect");
runner.Check(snapshot.transientTalentSelection == null,
    "client-local talent picker is never snapshotted");
runner.Check(snapshot.sideboard.ownLocked,
    "disconnect during sideboard locks the original active set");
```

产品快照无需真的添加 `transientTalentSelection` 字段；该断言可用 JSON 检查字段不存在。

- [ ] **Step 2: Run regression and confirm RED**

Expected: 快照还没有已知天赋投影和 sideboard 状态。

- [ ] **Step 3: Add per-seat filtered talent projections**

快照新增：

```csharp
[Serializable]
public sealed class SnapshotKnownTalent
{
    public int ownerSeatIndex;
    public string talentId;
    public bool isKnown;
    public string lastPublicEventType;
    public int lastPublicValue;
}

[Serializable]
public sealed class SnapshotOwnTalent
{
    public string talentId;
    public bool isActive;
    public int privateValue;
}
```

请求席收到自己的全部 carried talent、active 状态和规则允许公开的私有计数；他席只收到 `IsRevealed` 的 known entries，且不含 active 状态。`Peek` 顶部牌仍走现有本家私有字段。

- [ ] **Step 4: Add sideboard snapshot state without local drafts**

```csharp
[Serializable]
public sealed class SnapshotSideboardState
{
    public bool isActive;
    public long decisionId;
    public long deadlineUnixMilliseconds;
    public bool ownLocked;
    public bool[] seatLocked;
}
```

只有 `isActive && !ownLocked && seat online` 时，首次实时 `SideboardStartedMessage` 才包含可编辑构筑；由于断线会先锁定，本项目不会通过重连快照恢复未提交草稿。

- [ ] **Step 5: Recompute available talent actions from clean authoritative state**

`RemoteServerProxy` 应用恢复快照后，若 basic main decision 仍有效，向表现层下发当前可用 `TalentActionOption[]`；所有选择弹窗默认关闭。不要重播 runtime event 动画、技能 toast 或已接受动作的 resolved dialog。

- [ ] **Step 6: Verify sequence ordering and private delivery**

天赋 resolved、公开 event、私有 runtime projection 必须经过各席 `SeatMessageStream`。增加乱序/重复 envelope 回归，确认 `ClientRoomService` 去重后只应用一次，且私有计数不会串席。

- [ ] **Step 7: Run the final Plan 2 code-checkpoint verification and commit**

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "dotnet build Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
pwsh -NoLogo -NoProfile -Command "rg -n 'if\s*\([^\n]*TalentId|switch\s*\([^\n]*TalentId' Assets/Scripts/Core/Network/Room.cs Assets/Scripts/Core/Network/GameServer.cs"
pwsh -NoLogo -NoProfile -Command "git diff --check"
pwsh -NoLogo -NoProfile -Command "git branch --show-current"
pwsh -NoLogo -NoProfile -Command "git status --short"
```

Expected: 现有回归与构建通过；`rg` 无效果分支命中；diff 检查无输出；当前分支为 `codex/talent-actions-ui-unified`；状态只包含本任务预期文件。此步骤不宣称新增能力已通过联机集成或人工验收。

Commit:

```powershell
pwsh -NoLogo -NoProfile -Command "git add Assets/Scripts/Core/Network Assets/Scripts/Talent Tests/NetworkRegression; git commit -m 'feat: snapshot authoritative talent state'"
```

---

## Plan 2 Code Checkpoint

- [ ] `Room` 是 runtime、sideboard tracker 和跨局天赋状态的唯一 owner，客户端不直接修改生效集合。
- [ ] 补充天赋动作成功后，当前基本主回合仍可正常出牌，deadline 不变。
- [ ] 断线恢复不会恢复任何未提交的蓄力/截流选择框。
- [ ] `藏锋` 严格按未获胜小局充能、首主回合武装、基础合法后 +16。
- [ ] `截流` 整场 3 次、每主回合最多 1 次、只命中公开正层充能目标。
- [ ] `定心` 每小局只挡第一次，挡住截流仍消耗截流次数。
- [ ] 半庄/全庄只在第 4 小局后进入一次 45 秒中场，其他模式不进入。
- [ ] 中场断线/超时锁回原激活方案，重连不能重新选择。
- [ ] 对手快照不泄露 active 状态、精确异化值或未揭示天赋。
- [ ] 新增行为由纯 C# policy、tracker、规则、状态机、协议模型和组件边界测试覆盖；本阶段没有新增真实生产链路集成验收。
- [ ] 现有 NetworkRegression、构建、源码分支守卫和 `git diff --check` 通过。
- [ ] 不存在占位注释、未实现异常、空方法或假成功返回。
- [ ] 检查点报告明确列出 WebSocket 全链路、真实 `Room -> GameServer -> TalentMatchRuntime`、Unity、Dedicated Server 和真人验证均推迟到第三阶段，未将其描述为已通过。
- [ ] 当前仍位于 `codex/talent-actions-ui-unified`；本计划没有合并 `master`、没有删除功能分支，并直接转入第三阶段。
