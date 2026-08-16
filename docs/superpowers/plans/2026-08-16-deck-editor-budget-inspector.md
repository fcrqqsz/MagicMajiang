# Deck Editor Fixed Budget Inspector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将当前卡组编辑器顶部的简化异化条改为右侧栏固定 B 型预算表盘，并为牌张、天赋和档位草稿增加一致的未保存离开保护。

**Architecture:** 保留现有 `DeckEditorToolkit`、牌张网格、6+3 天赋槽和 `SavedDeck` 持久化模型。新增一个纯 C# 展示策略负责预算色阶、保存资格和离开提示；右侧栏上方使用独立的 `AlienationDialElement` 绘制圆形表盘，下方牌库列表单独滚动；UI 控制器只协调草稿、渲染和确认框，不复制预算算法。

**Tech Stack:** C#、Unity 2022.3.61t9 / Tuanjie 1.6.8、UI Toolkit UXML/USS、纯 C# `NetworkRegression`。

## Global Constraints

- UI 只使用 UI Toolkit；禁止 Canvas/UGUI。
- 继续使用 `AlienationGaugePolicy.Build(deckCost, talentCost, preset)` 作为预算数值来源，不在 UI 中复制异化算法。
- Low/Standard/High 上限固定为 40/80/120；备牌成本不计入当前开局预算。
- 异化值超限仍可保存；只有牌数不等于 34 时禁止保存。
- 未保存期间，牌库条目保持上次保存的数据；只有预算检查器显示“未保存”。
- 不新增网络消息、场景对象或独立 `UIDocument`。
- 不手写或修改 Unity `.meta`，不修改 Unity 生成的 `Assembly-CSharp.csproj`；Unity Refresh 和视觉点击验收由人工执行。
- 所有新增 UI 回调必须在 `OnDisable`/销毁边界解绑。

---

### Task 1: Pure Budget and Draft Presentation Policy

**Files:**
- Create: `Assets/Scripts/Core/DeckEditorDraftPresentationPolicy.cs`（不手写 `.meta`）
- Modify: `Tests/NetworkRegression/NetworkRegression.csproj`
- Modify: `Tests/NetworkRegression/TalentPresentationTests.cs`

**Interfaces:**
- Consumes: `AlienationGaugeView` from `AlienationGaugePolicy.Build(...)`.
- Produces: `DeckEditorDraftPresentationPolicy.Build(...)`, `DeckEditorDraftPresentationPolicy.BuildLeavePrompt(...)`, `DeckEditorDraftView`, `DeckEditorLeavePromptView`, and `DeckEditorBudgetTone` for Tasks 2-3.

- [ ] **Step 1: Write failing policy tests**

Add `RunDeckEditorDraftPresentationPolicyTests(runner)` to `TalentPresentationTests.Run`. Cover normal, near-limit, over-limit, dirty, invalid tile count, and leave prompt behavior:

```csharp
private static void RunDeckEditorDraftPresentationPolicyTests(RegressionRunner runner)
{
    AlienationGaugeView normalGauge = AlienationGaugePolicy.Build(20, 10, AlienationPreset.Low);
    DeckEditorDraftView normal = DeckEditorDraftPresentationPolicy.Build(
        normalGauge, tileCount: 34, isDirty: false);
    runner.Check(normal.Title == "当前构筑预算"
                 && normal.Tone == DeckEditorBudgetTone.Normal
                 && normal.CanSave
                 && normal.StatusText == "当前方案可进入该档位房间",
        "deck editor normal draft is saveable and uses the normal budget tone");

    AlienationGaugeView nearGauge = AlienationGaugePolicy.Build(55, 10, AlienationPreset.Standard);
    DeckEditorDraftView near = DeckEditorDraftPresentationPolicy.Build(
        nearGauge, tileCount: 34, isDirty: true);
    runner.Check(near.Title == "当前构筑预算 · 未保存"
                 && near.Tone == DeckEditorBudgetTone.NearLimit
                 && near.CanSave,
        "deck editor marks a dirty draft and turns amber at eighty percent");

    AlienationGaugeView overGauge = AlienationGaugePolicy.Build(28, 17, AlienationPreset.Low);
    DeckEditorDraftView over = DeckEditorDraftPresentationPolicy.Build(
        overGauge, tileCount: 34, isDirty: true);
    runner.Check(over.Tone == DeckEditorBudgetTone.OverLimit
                 && over.CanSave
                 && over.StatusText == "超限 5，仍可保存，不能进入该档位房间",
        "over-limit drafts remain saveable and expose the exact overflow");

    DeckEditorDraftView invalidTiles = DeckEditorDraftPresentationPolicy.Build(
        normalGauge, tileCount: 33, isDirty: true);
    DeckEditorLeavePromptView invalidPrompt =
        DeckEditorDraftPresentationPolicy.BuildLeavePrompt(isDirty: true, tileCount: 33);
    runner.Check(!invalidTiles.CanSave
                 && invalidTiles.StatusText == "当前牌数 33 / 34，无法保存或进入房间"
                 && invalidPrompt.IsRequired
                 && !invalidPrompt.CanSave
                 && invalidPrompt.Message.Contains("不是 34 张", StringComparison.Ordinal),
        "non-34-tile drafts cannot offer Save while leaving");

    DeckEditorLeavePromptView cleanPrompt =
        DeckEditorDraftPresentationPolicy.BuildLeavePrompt(isDirty: false, tileCount: 34);
    DeckEditorLeavePromptView dirtyPrompt =
        DeckEditorDraftPresentationPolicy.BuildLeavePrompt(isDirty: true, tileCount: 34);
    runner.Check(!cleanPrompt.IsRequired
                 && dirtyPrompt.IsRequired
                 && dirtyPrompt.CanSave,
        "only dirty valid drafts require a leave prompt with Save enabled");
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- talent-presentation
```

Expected: compile failure for missing `DeckEditorDraftPresentationPolicy`, `DeckEditorDraftView`, `DeckEditorLeavePromptView`, and `DeckEditorBudgetTone`.

- [ ] **Step 3: Add the new policy to the explicit regression project**

Add this non-Unity-generated test-project include next to `AlienationGaugePolicy.cs`:

```xml
<Compile Include="..\..\Assets\Scripts\Core\DeckEditorDraftPresentationPolicy.cs"
         Link="DeckEditorDraftPresentationPolicy.cs" />
```

- [ ] **Step 4: Implement the minimal pure C# policy**

Create `DeckEditorDraftPresentationPolicy.cs` in namespace `MahjongGame.Core`:

```csharp
using System;

namespace MahjongGame.Core
{
    public enum DeckEditorBudgetTone
    {
        Normal,
        NearLimit,
        OverLimit
    }

    public sealed class DeckEditorDraftView
    {
        public string Title { get; }
        public DeckEditorBudgetTone Tone { get; }
        public bool CanSave { get; }
        public string StatusText { get; }

        public DeckEditorDraftView(
            string title,
            DeckEditorBudgetTone tone,
            bool canSave,
            string statusText)
        {
            Title = title;
            Tone = tone;
            CanSave = canSave;
            StatusText = statusText;
        }
    }

    public sealed class DeckEditorLeavePromptView
    {
        public bool IsRequired { get; }
        public bool CanSave { get; }
        public string Message { get; }

        public DeckEditorLeavePromptView(bool isRequired, bool canSave, string message)
        {
            IsRequired = isRequired;
            CanSave = canSave;
            Message = message ?? string.Empty;
        }
    }

    public static class DeckEditorDraftPresentationPolicy
    {
        public static DeckEditorDraftView Build(
            AlienationGaugeView gauge,
            int tileCount,
            bool isDirty)
        {
            if (gauge == null) throw new ArgumentNullException(nameof(gauge));

            DeckEditorBudgetTone tone = gauge.IsOverLimit
                ? DeckEditorBudgetTone.OverLimit
                : gauge.Total * 5 >= gauge.Limit * 4
                    ? DeckEditorBudgetTone.NearLimit
                    : DeckEditorBudgetTone.Normal;

            bool canSave = tileCount == 34;
            string status = !canSave
                ? $"当前牌数 {tileCount} / 34，无法保存或进入房间"
                : gauge.IsOverLimit
                    ? $"超限 {gauge.Overflow}，仍可保存，不能进入该档位房间"
                    : "当前方案可进入该档位房间";

            return new DeckEditorDraftView(
                isDirty ? "当前构筑预算 · 未保存" : "当前构筑预算",
                tone,
                canSave,
                status);
        }

        public static DeckEditorLeavePromptView BuildLeavePrompt(bool isDirty, int tileCount)
        {
            if (!isDirty) return new DeckEditorLeavePromptView(false, false, string.Empty);
            if (tileCount != 34)
            {
                return new DeckEditorLeavePromptView(
                    true,
                    false,
                    $"当前牌数为 {tileCount} 张，不是 34 张，无法保存。是否放弃修改？");
            }

            return new DeckEditorLeavePromptView(
                true,
                true,
                "当前构筑有未保存修改。请选择保存、放弃或取消。");
        }
    }
}
```

- [ ] **Step 5: Run focused and full regressions**

Run:

```powershell
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- talent-presentation
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore
```

Expected: both print `Network regression tests passed.`

- [ ] **Step 6: Commit Task 1**

```powershell
git add -- Assets/Scripts/Core/DeckEditorDraftPresentationPolicy.cs Tests/NetworkRegression/NetworkRegression.csproj Tests/NetworkRegression/TalentPresentationTests.cs
git diff --cached --check
git commit -m "feat: add deck editor draft presentation policy"
```

Do not add a hand-written `.meta`; leave Unity asset import to the manual Refresh gate.

---

### Task 2: Fixed Sidebar Budget Dial

**Files:**
- Create: `Assets/UI/AlienationDialElement.cs`（不手写 `.meta`）
- Modify: `Assets/UI/DeckEditorView.uxml`
- Modify: `Assets/UI/DeckEditorStyles.uss`
- Modify: `Assets/UI/DeckEditorToolkit.cs`
- Modify: `Tests/NetworkRegression/TalentPresentationTests.cs`

**Interfaces:**
- Consumes: `DeckEditorDraftPresentationPolicy.Build(...)` and `DeckEditorBudgetTone` from Task 1.
- Produces: fixed UXML query names and `AlienationDialElement.SetValue(float, DeckEditorBudgetTone)` for the editor controller.

- [ ] **Step 1: Replace the old artifact expectations with failing fixed-sidebar assertions**

In `RunTalentEditorAndLobbySourceTests`, replace the old arrow/track assertion with checks for the fixed inspector and verify it is outside `DeckListScroll`:

```csharp
XElement budgetInspector = editorUxml.Descendants()
    .Single(element => element.Attribute("name")?.Value == "BudgetInspector");
XElement deckListScroll = editorUxml.Descendants()
    .Single(element => element.Attribute("name")?.Value == "DeckListScroll");
string[] requiredBudgetNames =
{
    "BudgetTitle", "BudgetDeckName", "AlienationDialHost", "AlienationDialValue",
    "BtnPresetLow", "BtnPresetStandard", "BtnPresetHigh", "BudgetDeckCost",
    "BudgetTalentCost", "BudgetReserveCost", "BudgetTotal", "BudgetStatus"
};
runner.Check(requiredBudgetNames.All(queryNames.Contains)
             && !queryNames.Contains("BtnPresetPrev")
             && !queryNames.Contains("BtnPresetNext")
             && !queryNames.Contains("AlienationTrack"),
    "deck editor exposes the fixed B dial and direct preset controls");
runner.Check(!deckListScroll.DescendantsAndSelf().Contains(budgetInspector),
    "budget inspector stays outside the independently scrolling deck list");

string styles = File.ReadAllText(GetRepoPath("Assets", "UI", "DeckEditorStyles.uss"));
string dialSource = File.ReadAllText(GetRepoPath("Assets", "UI", "AlienationDialElement.cs"));
runner.Check(styles.Contains(".deck-sidebar", StringComparison.Ordinal)
             && styles.Contains("width: 320px", StringComparison.Ordinal)
             && dialSource.Contains("generateVisualContent", StringComparison.Ordinal)
             && dialSource.Contains("SetValue", StringComparison.Ordinal),
    "fixed sidebar width and radial dial renderer exist");
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- talent-presentation
```

Expected: failures for missing fixed inspector names, old arrow/track names still present, 280px sidebar width, and missing `AlienationDialElement.cs`.

- [ ] **Step 3: Restructure the UXML without changing the central editor flow**

Keep `EditorArea` and move all budget content into `DeckSidebar` before the list title. Use these exact names:

```xml
<ui:VisualElement name="DeckSidebar" class="deck-sidebar">
    <ui:VisualElement name="BudgetInspector" class="budget-inspector">
        <ui:Label name="BudgetTitle" text="当前构筑预算" class="budget-title" />
        <ui:Label name="BudgetDeckName" class="budget-deck-name" />
        <ui:VisualElement name="AlienationDialHost" class="alienation-dial-host">
            <ui:Label name="AlienationDialValue" text="0 / 80" class="alienation-dial-value" />
        </ui:VisualElement>
        <ui:VisualElement class="budget-preset-row">
            <ui:Button name="BtnPresetLow" text="低 40" class="budget-preset-button" />
            <ui:Button name="BtnPresetStandard" text="标准 80" class="budget-preset-button" />
            <ui:Button name="BtnPresetHigh" text="高 120" class="budget-preset-button" />
        </ui:VisualElement>
        <ui:VisualElement class="budget-breakdown">
            <ui:Label name="BudgetDeckCost" class="budget-breakdown-row" />
            <ui:Label name="BudgetTalentCost" class="budget-breakdown-row" />
            <ui:Label name="BudgetReserveCost" text="备牌成本    不计入" class="budget-breakdown-row" />
            <ui:Label name="BudgetTotal" class="budget-breakdown-total" />
        </ui:VisualElement>
        <ui:Label name="BudgetStatus" class="budget-status" />
    </ui:VisualElement>
    <ui:Label text="牌库列表" class="sidebar-title" />
    <ui:ScrollView name="DeckListScroll" class="deck-list-scroll">
        <ui:VisualElement name="DeckListContainer" class="deck-list-container" />
    </ui:ScrollView>
    <ui:Button name="BtnNewDeck" text="＋ 新建牌库" class="btn-new-deck" />
</ui:VisualElement>
```

Remove `AlienationPresetSelector`, `BtnPresetPrev`, `BtnPresetNext`, `PresetLabel`, `AlienationTrack`, `AlienationFill`, `AlienationBreakdownLabel`, and `AlienationWarning` from the header. Keep `TotalText`, Clear, and Reset.

- [ ] **Step 4: Add sidebar and dial styles**

Change `.deck-sidebar` to `width: 320px`. Add fixed inspector styles; only `.deck-list-scroll` receives `flex-grow: 1` and scrolling:

```css
.budget-inspector {
    flex-shrink: 0;
    background-color: #222831;
    border-radius: 12px;
    padding: 14px;
    margin-bottom: 14px;
}

.alienation-dial-host {
    width: 136px;
    height: 136px;
    align-self: center;
    justify-content: center;
    align-items: center;
    position: relative;
}

.alienation-dial-value {
    position: absolute;
    left: 20px;
    right: 20px;
    top: 52px;
    font-size: 22px;
    -unity-font-style: bold;
    -unity-text-align: middle-center;
}

.budget-preset-row { flex-direction: row; margin-top: 10px; }
.budget-preset-button { flex-grow: 1; height: 34px; margin: 0 2px; }
.budget-preset-button.selected { background-color: #00ADB5; color: #222831; }
.budget-status.near-limit { color: #e9c267; }
.budget-status.over-limit { color: #ff6b6b; }
```

Remove the obsolete `.alienation-gauge-panel`, arrow, track, fill, breakdown, and warning rules.

- [ ] **Step 5: Implement the radial renderer**

Create `AlienationDialElement` in `MahjongGame.UI`. It is instantiated by the controller, not declared as a custom UXML type:

```csharp
using MahjongGame.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace MahjongGame.UI
{
    public sealed class AlienationDialElement : VisualElement
    {
        private float _fill01;
        private DeckEditorBudgetTone _tone;

        public AlienationDialElement()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += Draw;
        }

        public void SetValue(float fill01, DeckEditorBudgetTone tone)
        {
            _fill01 = Mathf.Clamp01(fill01);
            _tone = tone;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            Vector2 center = contentRect.center;
            float radius = Mathf.Max(0f, Mathf.Min(contentRect.width, contentRect.height) * 0.5f - 8f);
            Painter2D painter = context.painter2D;
            painter.lineWidth = 10f;
            painter.strokeColor = new Color32(45, 60, 79, 255);
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(-90f), Angle.Degrees(270f));
            painter.Stroke();

            painter.strokeColor = _tone == DeckEditorBudgetTone.OverLimit
                ? new Color32(255, 107, 107, 255)
                : _tone == DeckEditorBudgetTone.NearLimit
                    ? new Color32(233, 194, 103, 255)
                    : new Color32(0, 173, 181, 255);
            painter.BeginPath();
            painter.Arc(
                center,
                radius,
                Angle.Degrees(-90f),
                Angle.Degrees(-90f + 360f * _fill01));
            painter.Stroke();
        }
    }
}
```

If Tuanjie reports a different overload for `Painter2D.Arc`, adapt only the `Angle` construction to the installed 2022.3 API; preserve the same start angle, clockwise fill, and capped `_fill01` behavior. Do not replace the dial with a generated texture or UGUI.

- [ ] **Step 6: Rebind `DeckEditorToolkit` to the fixed inspector**

Replace old fields with:

```csharp
private Label _budgetTitle;
private Label _budgetDeckName;
private VisualElement _alienationDialHost;
private AlienationDialElement _alienationDial;
private Label _alienationDialValue;
private Button _btnPresetLow;
private Button _btnPresetStandard;
private Button _btnPresetHigh;
private Label _budgetDeckCost;
private Label _budgetTalentCost;
private Label _budgetReserveCost;
private Label _budgetTotal;
private Label _budgetStatus;
private bool _isDraftDirty;
```

In `OnEnable`, query the named elements, insert the dial as the first child of `AlienationDialHost`, and bind the three buttons through stored delegates:

```csharp
private Action _selectLowPreset;
private Action _selectStandardPreset;
private Action _selectHighPreset;

// OnEnable
_alienationDial = new AlienationDialElement();
_alienationDialHost.Insert(0, _alienationDial);
_selectLowPreset = () => SelectAlienationPreset(AlienationPreset.Low);
_selectStandardPreset = () => SelectAlienationPreset(AlienationPreset.Standard);
_selectHighPreset = () => SelectAlienationPreset(AlienationPreset.High);
_btnPresetLow.clicked += _selectLowPreset;
_btnPresetStandard.clicked += _selectStandardPreset;
_btnPresetHigh.clicked += _selectHighPreset;

// OnDisable
_btnPresetLow.clicked -= _selectLowPreset;
_btnPresetStandard.clicked -= _selectStandardPreset;
_btnPresetHigh.clicked -= _selectHighPreset;
```

`SelectAlienationPreset` sets `_currentAlienationPreset`, marks the draft dirty only when the value changes, and calls `RefreshStats()`.

Replace the old gauge rendering in `RefreshStats` with:

```csharp
AlienationGaugeView gauge = AlienationGaugePolicy.Build(deckCost, talentCost, _currentAlienationPreset);
DeckEditorDraftView draftView = DeckEditorDraftPresentationPolicy.Build(
    gauge, total, _isDraftDirty);
_budgetTitle.text = draftView.Title;
_budgetDeckName.text = _savedDecks[_selectedDeckIndex].DeckName;
_alienationDialValue.text = $"{gauge.Total} / {gauge.Limit}";
_alienationDial.SetValue(gauge.Fill01, draftView.Tone);
_budgetDeckCost.text = $"牌山成本    {gauge.DeckCost}";
_budgetTalentCost.text = $"主天赋成本  {gauge.TalentCost}";
_budgetReserveCost.text = "备牌成本    不计入";
_budgetTotal.text = $"当前总计    {gauge.Total}";
_budgetStatus.text = draftView.StatusText;
_budgetStatus.EnableInClassList("near-limit", draftView.Tone == DeckEditorBudgetTone.NearLimit);
_budgetStatus.EnableInClassList("over-limit", draftView.Tone == DeckEditorBudgetTone.OverLimit);
_btnSave.SetEnabled(draftView.CanSave);
```

Toggle the `selected` class on the three preset buttons. `SelectDeck` must set `_isDraftDirty = false` after loading the saved deep copy. Do not rebuild list-row values during ordinary draft edits.

- [ ] **Step 7: Run focused/full regressions and static checks**

Run:

```powershell
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- talent-presentation
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore
git diff --check
```

Expected: both regressions pass; UXML names are unique; no obsolete arrows/track remain; diff check has no errors.

- [ ] **Step 8: Commit Task 2**

```powershell
git add -- Assets/UI/AlienationDialElement.cs Assets/UI/DeckEditorView.uxml Assets/UI/DeckEditorStyles.uss Assets/UI/DeckEditorToolkit.cs Tests/NetworkRegression/TalentPresentationTests.cs
git diff --cached --check
git commit -m "feat: add fixed deck budget inspector"
```

Do not add `.meta` files or run Unity/Tuanjie.

---

### Task 3: Unsaved Draft Navigation Guard

**Files:**
- Modify: `Assets/UI/DeckEditorView.uxml`
- Modify: `Assets/UI/DeckEditorStyles.uss`
- Modify: `Assets/UI/DeckEditorToolkit.cs`
- Modify: `Tests/NetworkRegression/TalentPresentationTests.cs`

**Interfaces:**
- Consumes: `DeckEditorDraftPresentationPolicy.BuildLeavePrompt(bool, int)` and the Task 2 `_isDraftDirty` field.
- Produces: one shared `RequestDraftNavigation(Action)` boundary used by card selection, New, deletion of the current deck, and Exit.

- [ ] **Step 1: Write failing confirmation and source-boundary tests**

Extend the editor UXML/source assertions:

```csharp
string[] unsavedNames =
{
    "UnsavedChangesOverlay", "UnsavedChangesMessage", "BtnUnsavedSave",
    "BtnUnsavedDiscard", "BtnUnsavedCancel"
};
runner.Check(unsavedNames.All(queryNames.Contains),
    "deck editor provides one shared unsaved-draft confirmation overlay");

runner.Check(editorSource.Contains("RequestDraftNavigation", StringComparison.Ordinal)
             && editorSource.Contains("MarkDraftDirty", StringComparison.Ordinal)
             && editorSource.Contains("BuildLeavePrompt", StringComparison.Ordinal)
             && editorSource.Contains("_pendingDraftNavigation", StringComparison.Ordinal),
    "deck editor routes dirty navigation through one confirmation boundary");
```

Also assert there is no code that rebuilds the list from `RefreshStats`:

```csharp
runner.Check(!editorSource.Contains("RefreshStats();\n            RebuildDeckList();", StringComparison.Ordinal),
    "unsaved draft refresh never mutates saved deck list presentation");
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore -- talent-presentation
```

Expected: failure for missing confirmation query names and navigation guard methods.

- [ ] **Step 3: Add the shared confirmation overlay**

Add it as the last child of `main-container`, initially hidden:

```xml
<ui:VisualElement name="UnsavedChangesOverlay" class="unsaved-overlay">
    <ui:VisualElement class="unsaved-dialog">
        <ui:Label text="未保存的构筑" class="unsaved-title" />
        <ui:Label name="UnsavedChangesMessage" class="unsaved-message" />
        <ui:VisualElement class="unsaved-actions">
            <ui:Button name="BtnUnsavedSave" text="保存" class="unsaved-save" />
            <ui:Button name="BtnUnsavedDiscard" text="放弃" class="unsaved-discard" />
            <ui:Button name="BtnUnsavedCancel" text="取消" class="unsaved-cancel" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:VisualElement>
```

The overlay must use absolute full-screen bounds, a dim background, centered dialog, and `display: none` by default. It remains pickable while visible so lower editor controls cannot receive clicks.

- [ ] **Step 4: Implement one navigation guard**

Add controller fields for the overlay, label, buttons, and pending continuation. Query/bind them in `OnEnable`, unbind in `OnDisable`:

```csharp
private VisualElement _unsavedChangesOverlay;
private Label _unsavedChangesMessage;
private Button _btnUnsavedSave;
private Button _btnUnsavedDiscard;
private Button _btnUnsavedCancel;
private Action _pendingDraftNavigation;

private void RequestDraftNavigation(Action continuation)
{
    int tileCount = _currentConfig.GenerateTiles(0).Count;
    DeckEditorLeavePromptView prompt =
        DeckEditorDraftPresentationPolicy.BuildLeavePrompt(_isDraftDirty, tileCount);
    if (!prompt.IsRequired)
    {
        continuation();
        return;
    }

    _pendingDraftNavigation = continuation;
    _unsavedChangesMessage.text = prompt.Message;
    _btnUnsavedSave.style.display = prompt.CanSave ? DisplayStyle.Flex : DisplayStyle.None;
    _unsavedChangesOverlay.style.display = DisplayStyle.Flex;
}

private void CloseUnsavedPrompt()
{
    _pendingDraftNavigation = null;
    _unsavedChangesOverlay.style.display = DisplayStyle.None;
}
```

Handlers:

```csharp
private void OnUnsavedSaveClicked()
{
    Action continuation = _pendingDraftNavigation;
    if (!TrySaveCurrentDeck()) return;
    CloseUnsavedPrompt();
    continuation?.Invoke();
}

private void OnUnsavedDiscardClicked()
{
    Action continuation = _pendingDraftNavigation;
    CloseUnsavedPrompt();
    continuation?.Invoke();
}

private void OnUnsavedCancelClicked() => CloseUnsavedPrompt();
```

Refactor `OnSaveClicked` to call `TrySaveCurrentDeck()`. The method returns false unless the draft has 34 tiles; on success it writes config/talents/preset, saves the profile, sets `_isDraftDirty = false`, rebuilds list rows, refreshes the inspector, and raises `OnDeckSaved` exactly once.

- [ ] **Step 5: Mark every draft mutation and guard every destructive navigation**

Add:

```csharp
private void MarkDraftDirty()
{
    if (_isDraftDirty) return;
    _isDraftDirty = true;
    RefreshStats();
}
```

Call it after successful user changes to:

- tile plus/minus;
- Clear All and Reset All;
- main talent selection/clear;
- reserve talent selection/clear;
- a changed alienation preset.

Do not call it during `SelectDeck`, initial normalization, list rebuilding, or save.

Route these actions through `RequestDraftNavigation`:

```csharp
RequestDraftNavigation(() => SelectDeck(index));
RequestDraftNavigation(CreateAndSelectNewDeck);
RequestDraftNavigation(() => DeleteCurrentDeck(index));
RequestDraftNavigation(() => OnExitRequested?.Invoke());
```

Deleting a non-selected deck must not reload the current draft. Remove that saved entry, adjust `_selectedDeckIndex` if its numeric position shifts, rebuild the list, and preserve `_currentConfig`, `_currentTalents`, `_currentAlienationPreset`, and `_isDraftDirty`. Deleting the selected deck uses the guard and then loads the replacement deck.

- [ ] **Step 6: Add source assertions for mutation and navigation coverage**

Keep tests concise but verify the named mutation/navigation methods contain the common calls. Extract each method body with the existing test helper or a bounded substring helper and assert:

```csharp
runner.Check(MethodBody(editorSource, "SelectAlienationPreset").Contains("MarkDraftDirty()")
             && MethodBody(editorSource, "BatchUpdateDeck").Contains("MarkDraftDirty()")
             && MethodBody(editorSource, "OnExitClicked").Contains("RequestDraftNavigation")
             && MethodBody(editorSource, "OnNewDeckClicked").Contains("RequestDraftNavigation")
             && MethodBody(editorSource, "OnDeleteDeckClicked").Contains("RequestDraftNavigation"),
    "all draft mutation and destructive navigation entry points use the shared boundaries");
```

Add this brace-depth helper to `TalentPresentationTests`; it does not depend on exact whitespace or line endings:

```csharp
private static string MethodBody(string source, string methodName)
{
    int signature = source.IndexOf(methodName + "(", StringComparison.Ordinal);
    if (signature < 0) return string.Empty;
    int openBrace = source.IndexOf('{', signature);
    if (openBrace < 0) return string.Empty;

    int depth = 0;
    for (int index = openBrace; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        if (source[index] != '}') continue;
        depth--;
        if (depth == 0) return source.Substring(openBrace, index - openBrace + 1);
    }

    return string.Empty;
}
```

- [ ] **Step 7: Run final automated verification**

Run in order:

```powershell
dotnet build Tests\NetworkRegression\NetworkRegression.csproj --no-restore
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-build -- talent-presentation
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-build
git diff --check
```

Expected:

- build: 0 warnings, 0 errors;
- focused and full runs: `Network regression tests passed.`;
- diff check: no errors;
- `git status --short` contains no generated `.meta` or Unity `.csproj` changes from the agent.

- [ ] **Step 8: Commit Task 3**

```powershell
git add -- Assets/UI/DeckEditorView.uxml Assets/UI/DeckEditorStyles.uss Assets/UI/DeckEditorToolkit.cs Tests/NetworkRegression/TalentPresentationTests.cs
git diff --cached --check
git commit -m "feat: guard unsaved deck editor drafts"
```

- [ ] **Step 9: Hand off the manual Unity gate**

Report exactly:

```text
纯 C# focused/full regression 已通过；Unity 集成与视觉验收待人工执行。
请人工 Refresh 生成新增 C# 资产的 .meta，并验证：
1. 表盘在牌张/天赋滚动时固定；牌库列表独立滚动。
2. 16:9 与最窄支持窗口中，表盘、三档按钮和成本行不重叠。
3. 正常、80%临界、超限、非34张和未保存状态文字/颜色正确。
4. 切换、新建、删除当前卡组和退出的保存/放弃/取消均可点击。
5. 未保存时列表数值不变；保存后才更新。
```

Do not run Unity/Tuanjie unless the user separately authorizes it.
