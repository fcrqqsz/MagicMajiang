# 藏锋动态消耗与加番 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让藏锋在至少 1 层时即可使用，立即消耗当前全部层数，并按已消耗层数 × 12 为本局下一次合法胡牌加番。

**Architecture:** 保留比赛级公开计数 `edge` 与现有首次主回合主动窗口，在主动使用时将公开计数复制到新的回合级已消费计数后清零。候选、最终与反事实算番继续通过 `GetPostLegalFanBonus` 只读该回合计数；正式接受胡牌后清理武装状态，不新增退款路径、协议字段或 UI 控制器。

**Tech Stack:** C#、`TalentMatchRuntime`、`TalentRuntimeState`、现有 `NetworkRegression` 纯 C# 测试工程。

## Global Constraints

- 藏锋积攒规则和 3 层上限保持不变。
- 主动窗口仍仅为玩家每小局第一次主回合决策。
- 0 层不可使用；1、2、3 层分别提供 12、24、36 番。
- 使用时立即清空公开层数；不实现退款、返还或未胡惩罚分支。
- 截流只影响尚未使用的公开层数，不影响已经锁定的回合内加番。
- 不新增协议字段、Unity 资产、`.meta` 或生成工程文件。
- 只运行纯 C# 自动验证；Unity/Tuanjie Refresh 与视觉验收留给人工。
- 工作区包含既有未提交改动；只编辑必要文件，并在提交时按文件/片段精确暂存，禁止覆盖或夹带其他修改。

---

### Task 1: 藏锋按消费层数动态加番

**Files:**
- Modify: `Tests/NetworkRegression/TalentActionTests.cs`
- Modify: `Assets/Scripts/Talent/Impl/SheathedEdgeTalent.cs`
- Modify: `Tests/NetworkRegression/ActionValidationTests.cs`
- Modify: `Tests/NetworkRegression/TalentResultAttributionTests.cs`

**Interfaces:**
- Consumes: `TalentRuntimeState.GetCounter/SetCounter`、`TalentMatchRuntime.GetAvailableActions/TryActivate/ResolvePostLegalFan/ConfirmAcceptedWin`。
- Produces: `SheathedEdgeTalent.GetPostLegalFanBonus(TalentWinContext)` 返回 `本次已消费层数 * 12`；现有 `TalentActionOption`、`TalentActionResult` 和网络协议保持不变。

- [ ] **Step 1: 写入 0/1/2/3 层行为回归**

在 `TalentActionTests.Run` 调用的新测试或重写后的藏锋主动测试中，以真实 `TalentMatchRuntime` 建立四种状态。测试辅助方法接受明确层数，逐局调用现有 `EndNonWinningRound` 充能：

```csharp
private static TalentMatchRuntime CreateChargedSheathedEdgeRuntime(
    int layers,
    out GameSession session)
{
    TalentMatchRuntime runtime = CreateSheathedEdgeRuntime(out session);
    for (int index = 0; index < layers; index++)
        EndNonWinningRound(runtime, session, winnerSeatIndex: 1);
    return runtime;
}
```

0 层断言不产生选项且直接提交返回 `TalentActionErrorCodes.InsufficientResource`。1、2、3 层分别打开第一次主回合决策并断言：

```csharp
int expectedBonus = layers * 12;
runner.Check(options.Count == 1, $"{layers} sheathed-edge layers expose the active option");
runner.Check(result.Accepted && result.EffectApplied,
    $"{layers} sheathed-edge layers can be consumed");
runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 0,
    "activation immediately consumes every public layer");

TalentFanResolution first = runtime.ResolvePostLegalFan(
    new TalentWinContext(session, 0), eligibilityFan: 8);
TalentFanResolution second = runtime.ResolvePostLegalFan(
    new TalentWinContext(session, 0), eligibilityFan: 8);
runner.Check(first.PostLegalBonusFan == expectedBonus
             && first.FinalFan == 8 + expectedBonus
             && second.FinalFan == first.FinalFan,
    "candidate and final scoring read the captured consumed layer count");
```

保留并加强现有断言：正式 `ConfirmAcceptedWin` 前没有 `armed_consumed`，重复确认只产生一个消费事件；错过第一次主回合后仍不能使用；跨回合后旧武装不再加番。跨回合断言只检查自然清理和正常获得下一层，不描述或实现退款机制。

- [ ] **Step 2: 运行 focused 测试并确认 RED**

Run:

```powershell
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-actions
```

Expected: FAIL。现实现要求 3 层才提供选项，且固定只加 16 番，因此至少 1 层/2 层可用和 12/24/36 番断言必须失败。失败应来自藏锋行为断言，而不是编译、夹具生命周期或无关测试。

- [ ] **Step 3: 实现即时消费与回合内层数捕获**

在 `SheathedEdgeTalent` 中新增回合级计数键，并更新描述：

```csharp
private const string ArmedChargeKey = "armed_charge";
```

`GetAvailableActions` 将门槛改为至少 1 层。`TryActivate` 先读取当前层数，0 层返回 `InsufficientResource`，其余按以下顺序写入：

```csharp
int consumedLayers = context.State.GetCounter(ChargeKey, TalentStateScope.Match);
if (consumedLayers <= 0)
    return TalentActionResult.Reject(TalentActionErrorCodes.InsufficientResource);

context.State.SetCounter(ArmedChargeKey, consumedLayers, TalentStateScope.Round);
context.SetPublicCounter(ChargeKey, 0, TalentStateScope.Match);
context.State.SetFlag(ArmedKey, true, TalentStateScope.Round);
context.EmitPublic("armed", 1);
return TalentActionResult.Success(effectApplied: true);
```

`GetPostLegalFanBonus` 只在 `armed` 为 true 时返回：

```csharp
context.State.GetCounter(ArmedChargeKey, TalentStateScope.Round) * 12
```

`OnAcceptedWin` 在现有幂等检查后同时清除 `armed` 和 `armed_charge`，再发送一次 `armed_consumed`。不添加回合结束退款或专用失效逻辑；既有 `ResetRoundState` 负责清理回合级状态。

- [ ] **Step 4: 更新真实算番与归因期望**

`ActionValidationTests` 的三层藏锋真实 runtime 期望改为 `PostLegalBonusFan == 36`、`FinalFan == 44`，并保持“不能把低于 8 番的牌型抬过准入线”的断言。

`TalentResultAttributionTests.StableMarginalAttributionExplainsAcceptedFan` 的权威接受番改为 44，贡献行改为：

```csharp
("head_start", 2, TalentFanContributionCategory.Eligibility, 0),
("sheathed_edge", 36, TalentFanContributionCategory.PostLegal, 2)
```

只更新由真实藏锋 runtime 生成的期望；用于验证 DTO 深拷贝或通用 UI 排序的人工示例 `+16` 不属于规则行为，不做机械替换。

- [ ] **Step 5: 运行 focused GREEN**

Run:

```powershell
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-actions
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-attribution
```

Expected: 两个命令均输出 `Network regression tests passed.` 并以 0 退出。

- [ ] **Step 6: 运行完整纯 C# 验证**

Run:

```powershell
dotnet build Tests/NetworkRegression/NetworkRegression.csproj --no-restore
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-build --no-restore
git diff --check
```

Expected: build 0 errors；完整回归输出 `Network regression tests passed.`；`git diff --check` 无错误。不得运行 Unity/Tuanjie 或构建 `Assembly-CSharp.csproj`。

- [ ] **Step 7: 精确暂存并提交**

先检查四个目标文件是否与既有未提交修改重叠；重叠时使用 `git add -p`，只暂存本计划的藏锋片段。确认 cached diff 不包含其他 UI、场景或调试修改后提交：

```powershell
git diff --cached --check
git commit -m "feat: scale sheathed edge with consumed charge"
```

提交后 `git status --short` 中允许保留用户原有修改，但本任务四个文件不应残留本任务遗漏片段。
