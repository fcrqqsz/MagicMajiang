# 自己回合暗杠/加杠选择 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (\`- [ ]\`) syntax for tracking.

**Goal:** 让玩家在自摸决策中明确选择暗杠/加杠及其目标牌，同时保持现有服务端权威杠处理与联机协议不变。

**Architecture:** 新增不依赖 Unity 的 \`SelfTurnKongResolver\`，作为自摸杠候选牌的唯一来源。`ActionValidator` 将候选结果映射为语义明确的权限字段；UI 使用强类型选择与杠目标二级菜单；`LocalPlayerClient` 根据选中的类型和目标直接提交既有网络动作。

**Tech Stack:** Unity 2022.3 UI Toolkit、C#、现有 `Tests/NetworkRegression` .NET 10 回归运行器。

## Global Constraints

- UI 必须使用 UI Toolkit，不得引入 Canvas/UGUI。
- 所有 UI Toolkit 文本字体继续使用 `Assets/Font/MSYH_UITK.asset`。
- 不改 `ClientActionType` 数值、网络消息格式或服务端杠后直接补牌的状态流转。
- 本期不实现抢杠胡，也不改变 `SimpleAIClient` 的主动杠策略。
- 使用 `apply_patch` 进行文件内容编辑；不得触碰用户已提交的字体资源。

---

## 文件结构

- Create: `Assets/Scripts/Core/SelfTurnKongOptions.cs` — 纯 C# 暗杠/加杠候选模型与解析器。
- Modify: `Assets/Scripts/Core/ActionValidator.cs` — 用清晰字段暴露明杠、暗杠、加杠权限。
- Modify: `Assets/Scripts/Core/Agents/LocalPlayerClient.cs` — 以类型和目标提交自摸杠。
- Modify: `Assets/Scripts/Core/Agents/SimpleAIClient.cs` — 适配权限字段重命名，不新增策略。
- Modify: `Assets/UI/ActionPanelController.cs` — 强类型主按钮与杠目标二级菜单。
- Modify: `Assets/UI/ActionPanel.uxml` — 三个明确的杠按钮。
- Modify: `Assets/UI/ActionPanelStyles.uss` — 明杠、暗杠、加杠按钮样式。
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj` — 链接新的纯 C# 候选解析器。
- Modify: `Tests/NetworkRegression/SnapshotReconnectTests.cs` — 加入解析器和加杠投影测试。

### Task 1: 为自摸杠候选模型写失败测试

**Files:**

- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/SnapshotReconnectTests.cs`

**Interfaces:**

- Produces: `SelfTurnKongResolver.Resolve(IEnumerable<TileData>, IEnumerable<Meld>) -> SelfTurnKongOptions`。

- [ ] **Step 1: 链接待实现的纯 C# 文件并写入候选牌失败测试**

~~~csharp
var options = SelfTurnKongResolver.Resolve(hand, melds);
runner.Check(options.AnGangTargets.Single().GetName() == "5万"
    && options.JiaGangTargets.Single().GetName() == "9筒",
    "Self-turn kong options must preserve separate concealed and added-kong targets.");
~~~

- [ ] **Step 2: 运行测试，确认因类型尚不存在而失败**

Run: `dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore`

Expected: FAIL，编译错误指出 `SelfTurnKongResolver` 不存在。

- [ ] **Step 3: 写入多目标和空输入断言**

~~~csharp
runner.Check(options.AnGangTargets.Count == 2 && options.JiaGangTargets.Count == 2,
    "Every legal self-turn kong target must require an explicit selection when multiple exist.");
runner.Check(!SelfTurnKongResolver.Resolve(null, null).HasAny,
    "Empty self-turn state must have no kong targets.");
~~~

- [ ] **Step 4: 再运行测试，确认失败原因仍是实现缺失**

Run: `dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore`

Expected: FAIL，失败原因仍是缺失的候选解析器，而不是测试项目配置错误。

### Task 2: 实现纯 C# 自摸杠候选解析器

**Files:**

- Create: `Assets/Scripts/Core/SelfTurnKongOptions.cs`

**Interfaces:**

- Consumes: `TileData.TileSuit`、`TileData.Value`、`Meld.Type`、`Meld.FirstTile`。
- Produces: `SelfTurnKongOptions.AnGangTargets`、`SelfTurnKongOptions.JiaGangTargets`。

- [ ] **Step 1: 实现不可变选项目标容器和解析器**

~~~csharp
public sealed class SelfTurnKongOptions
{
    public IReadOnlyList<TileData> AnGangTargets { get; }
    public IReadOnlyList<TileData> JiaGangTargets { get; }
    public bool HasAny => AnGangTargets.Count > 0 || JiaGangTargets.Count > 0;
}

public static SelfTurnKongOptions Resolve(IEnumerable<TileData> hand, IEnumerable<Meld> melds)
~~~

- [ ] **Step 2: 运行回归测试，确认候选解析器通过**

Run: `dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore`

Expected: PASS，所有网络回归和新候选判定用例通过。

- [ ] **Step 3: 检查重复碰副露不产生重复加杠目标**

~~~csharp
runner.Check(options.JiaGangTargets.Select(tile => tile.GetName()).Distinct().Count() == options.JiaGangTargets.Count,
    "Added-kong targets must not duplicate the same pon tile.");
~~~

- [ ] **Step 4: 提交候选模型和回归测试**

~~~bash
git add Assets/Scripts/Core/SelfTurnKongOptions.cs Tests/NetworkRegression
git commit -m "test: cover self-turn kong options"
~~~

### Task 3: 将权限模型和 UI 选择改为强类型

**Files:**

- Modify: `Assets/Scripts/Core/ActionValidator.cs`
- Modify: `Assets/UI/ActionPanelController.cs`
- Modify: `Assets/UI/ActionPanel.uxml`
- Modify: `Assets/UI/ActionPanelStyles.uss`

**Interfaces:**

- Consumes: `SelfTurnKongOptions`。
- Produces: `AllowedActions.CanMingGan`、`AllowedActions.CanAnGan`、`AllowedActions.CanJiaGang`；`ActionPanelChoice` 枚举。

- [ ] **Step 1: 将 `CanGan` 拆为三个语义字段**

~~~csharp
public bool CanMingGan;
public bool CanAnGan;
public bool CanJiaGang;
~~~

- [ ] **Step 2: 增加强类型按钮选择和目标选择 API**

~~~csharp
public enum ActionPanelChoice { Chi, Pon, MingGan, AnGan, JiaGang, Hu, Skip }
public void ShowKongSelection(ActionPanelChoice choice, IReadOnlyList<TileData> targets,
    Action<TileData> callback)
~~~

- [ ] **Step 3: 将 UXML 按钮命名为 `BtnMingGan`、`BtnAnGan`、`BtnJiaGang`**

~~~xml
<ui:Button name="BtnAnGan" text="暗杠" class="action-btn btn-an-gang" />
<ui:Button name="BtnJiaGang" text="加杠" class="action-btn btn-jia-gang" />
~~~

- [ ] **Step 4: 运行编译检查与回归测试**

Run: `dotnet build Tests\NetworkRegression\NetworkRegression.csproj --no-restore`

Expected: PASS；Unity Editor 打开项目后 Console 无本次 C# 编译错误。

### Task 4: 接入本地客户端并验证联机投影

**Files:**

- Modify: `Assets/Scripts/Core/Agents/LocalPlayerClient.cs`
- Modify: `Assets/Scripts/Core/Agents/SimpleAIClient.cs`
- Modify: `Tests/NetworkRegression/SnapshotReconnectTests.cs`

**Interfaces:**

- Consumes: `ActionPanelChoice`、`SelfTurnKongOptions`。
- Produces: 具有精确 `ClientActionType` 与目标牌的 `ClientAction`。

- [ ] **Step 1: 替换字符串分支和 `[0]` 默认目标**

~~~csharp
case ActionPanelChoice.AnGan:
    SubmitOrSelectKong(ActionPanelChoice.AnGan, kongOptions.AnGangTargets, ClientActionType.AnGan);
    break;
case ActionPanelChoice.JiaGang:
    SubmitOrSelectKong(ActionPanelChoice.JiaGang, kongOptions.JiaGangTargets, ClientActionType.JiaGang);
    break;
~~~

- [ ] **Step 2: 保持 AI 不主动杠，并把弃牌响应映射到 `CanMingGan`**

~~~csharp
if (actions.CanMingGan && UnityEngine.Random.value > 0.5f)
    _server.SubmitAction(new ClientAction(PlayerId, ClientActionType.MingGan, discardedTile));
~~~

- [ ] **Step 3: 执行加杠投影回归与完整网络回归**

Run: `dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore`

Expected: PASS；加杠把既有 `Pon` 升级为 `Kan_Added`，本家暗手数量只减少一张。

- [ ] **Step 4: 手动验证四种 UI 场景**

1. 仅暗杠：只显示“暗杠”。
2. 仅加杠：只显示“加杠”。
3. 两者都有：两按钮同时显示并各自提交正确类型。
4. 任一类型有两张候选：先出现二级牌选择，未点牌前不发网络动作。

### Task 5: 全量验证与文档收尾

**Files:**

- Modify: `docs/network_verification.md`（仅在验证步骤需要补充时）。

**Interfaces:**

- Verifies: 现有协议与服务端动作类型不变。

- [ ] **Step 1: 运行完整回归和编译验证**

~~~bash
dotnet build Tests\NetworkRegression\NetworkRegression.csproj --no-restore
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore
git diff --check
~~~

- [ ] **Step 2: 审核范围边界**

确认没有修改 `ClientActionType` 的数值、网络消息 DTO、`GameServer` 杠后直接补牌逻辑，且没有实现抢杠胡。

