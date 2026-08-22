using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Talents;

namespace MahjongGame.UI
{
    public enum ActionPanelChoice
    {
        Chi,
        Pon,
        MingGan,
        AnGan,
        JiaGang,
        Hu,
        Skip
    }

    public class ActionPanelController : MonoBehaviour
    {
        public static ActionPanelController Instance;

        [SerializeField] private UIDocument _document;
        private VisualElement _root;
        private VisualElement _btnContainer;
        private VisualElement _talentActionContainer;
        private Label _talentActionStatus;
        private Button _btnChi, _btnPon, _btnMingGan, _btnAnGan, _btnJiaGang, _btnHu, _btnSkip;

        // 当前的回调函数引用
        private Action<ActionPanelChoice> _currentCallback;
        private Action<int> _currentChiCallback;
        private Action<TileData> _currentKongCallback;
        private Action<TalentActionOption> _talentSelectedCallback;
        private TalentActionPanelState _talentState = TalentActionPanelPolicy.Clear();
        private IVisualElementScheduledItem _talentStatusClearSchedule;

        void Awake()
        {
            // 单例保护
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _root = _document.rootVisualElement;
            _btnContainer = _root.Q<VisualElement>("ButtonContainer");
            _talentActionContainer = _root.Q<VisualElement>("TalentActionContainer");
            _talentActionStatus = _root.Q<Label>("TalentActionStatus");

            _btnChi = _root.Q<Button>("BtnChi");
            _btnPon = _root.Q<Button>("BtnPon");
            _btnMingGan = _root.Q<Button>("BtnMingGan");
            _btnAnGan = _root.Q<Button>("BtnAnGan");
            _btnJiaGang = _root.Q<Button>("BtnJiaGang");
            _btnHu =  _root.Q<Button>("BtnHu");
            _btnSkip = _root.Q<Button>("BtnSkip");

            // --- 关键修复：只在这里绑定一次事件 ---
            // 点击时，只调用 _currentCallback，不要在这里 += 具体的逻辑
            _btnChi.clicked += () => SafeInvoke(() => _currentCallback?.Invoke(ActionPanelChoice.Chi));
            _btnPon.clicked += () => SafeInvoke(() => _currentCallback?.Invoke(ActionPanelChoice.Pon));
            _btnMingGan.clicked += () => SafeInvoke(() => _currentCallback?.Invoke(ActionPanelChoice.MingGan));
            _btnAnGan.clicked += () => SafeInvoke(() => _currentCallback?.Invoke(ActionPanelChoice.AnGan));
            _btnJiaGang.clicked += () => SafeInvoke(() => _currentCallback?.Invoke(ActionPanelChoice.JiaGang));
            _btnHu.clicked +=  () => SafeInvoke(() => _currentCallback?.Invoke(ActionPanelChoice.Hu));
            _btnSkip.clicked += () => SafeInvoke(() => _currentCallback?.Invoke(ActionPanelChoice.Skip));

            Hide();
        }

        private void OnDestroy()
        {
            _talentStatusClearSchedule?.Pause();
            _talentStatusClearSchedule = null;
            ClearTalentActions(0);
            if (Instance == this) Instance = null;
        }

        // 防抖动辅助：防止极短时间内双击导致调用两次
        private void SafeInvoke(Action action)
        {
            if (_root.style.display == DisplayStyle.None) return;
            action?.Invoke();
        }

        public void Hide()
        {
            // 清理回调，防止意外触发
            _currentCallback = null;
            _currentChiCallback = null;
            _currentKongCallback = null;
            if (_btnContainer != null)
                _btnContainer.style.display = DisplayStyle.None;
            RefreshRootVisibility();
        }

        public void Show(AllowedActions actions, Action<ActionPanelChoice> callback)
        {
            // 1. 赋值回调
            _currentCallback = callback;

            // 2. 还原界面状态 (如果之前显示了二级菜单，这里要还原)
            RestoreMainButtons();
            _btnContainer.style.display = DisplayStyle.Flex;

            // 3. 设置可见性
            SetVisible(_btnChi, actions.CanChiLeft || actions.CanChiMiddle || actions.CanChiRight);
            SetVisible(_btnPon, actions.CanPon);
            SetVisible(_btnMingGan, actions.CanMingGan);
            SetVisible(_btnAnGan, actions.CanAnGan);
            SetVisible(_btnJiaGang, actions.CanJiaGang);
            SetVisible(_btnHu, actions.CanHu);
            SetVisible(_btnSkip, true);

            _root.style.display = DisplayStyle.Flex;
        }

        public void ShowTalentActions(
            long decisionId,
            IReadOnlyList<TalentActionOption> options,
            Action<TalentActionOption> onSelected)
        {
            _talentSelectedCallback = onSelected;
            ClearTalentStatus();
            _talentState = TalentActionPanelPolicy.Open(
                decisionId,
                ReadBaseActionAvailability(),
                options);
            RenderTalentActions();
            RefreshRootVisibility();
        }

        public void ClearTalentActions(long decisionId)
        {
            if (decisionId > 0 && _talentState.DecisionId != decisionId) return;
            _talentState = TalentActionPanelPolicy.Clear();
            _talentSelectedCallback = null;
            ClearTalentStatus();
            _talentActionContainer?.Clear();
            if (_talentActionContainer != null)
                _talentActionContainer.style.display = DisplayStyle.None;
            RefreshRootVisibility();
        }

        public void BeginTalentActionSubmit(TalentActionOption selected)
        {
            if (selected == null) return;
            _talentState = TalentActionPanelPolicy.BeginSubmit(_talentState, selected.TalentId);
            RenderTalentActions();
        }

        public void RestoreRejectedTalentAction(
            long decisionId,
            string talentId,
            string errorCode)
        {
            _talentState = TalentActionPanelPolicy.Resolve(
                _talentState, decisionId, talentId, accepted: false, errorCode);
            ShowTalentStatus(TalentActionPanelPolicy.GetRejectionCopy(errorCode));
            RenderTalentActions();
            RefreshRootVisibility();
        }

        private void RenderTalentActions()
        {
            if (_talentActionContainer == null) return;
            _talentActionContainer.Clear();
            if (!string.IsNullOrWhiteSpace(_talentState.ChoiceSelection))
            {
                RenderTalentChoice();
                _talentActionContainer.style.display = DisplayStyle.Flex;
                return;
            }

            foreach (IGrouping<string, TalentActionPanelOption> group in
                     _talentState.Options.GroupBy(option => option.TalentId, StringComparer.Ordinal))
            {
                TalentActionPanelOption presentation = group.First();
                bool isPending = group.Any(option => option.IsPending);
                TalentActionOption selected = TalentActionPanelPolicy.CloneOption(presentation.Option);
                var button = new Button
                {
                    text = isPending
                        ? $"{TalentRegistry.Instance.GetDisplayName(selected.TalentId)}…"
                        : TalentRegistry.Instance.GetDisplayName(selected.TalentId)
                };
                button.AddToClassList("talent-action-btn");
                button.SetEnabled(!isPending);
                button.clicked += () =>
                {
                    if (!_talentState.IsOpen || isPending) return;
                    if (selected.Choice != null
                        && string.IsNullOrWhiteSpace(selected.SelectedChoiceId))
                    {
                        _talentState = TalentActionPanelPolicy.BeginChoiceSelection(
                            _talentState,
                            selected.TalentId);
                        RenderTalentActions();
                        return;
                    }
                    _talentSelectedCallback?.Invoke(TalentActionPanelPolicy.CloneOption(selected));
                };
                _talentActionContainer.Add(button);
            }
            _talentActionContainer.style.display = _talentState.IsOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void RenderTalentChoice()
        {
            TalentActionPanelOption presentation = _talentState.Options.FirstOrDefault(option =>
                string.Equals(
                    option.TalentId,
                    _talentState.ChoiceSelection,
                    StringComparison.Ordinal)
                && option.Option.Choice != null
                && !option.IsPending);
            TalentChoiceSet choice = presentation?.Option?.Choice;
            if (choice == null)
            {
                _talentState = TalentActionPanelPolicy.CancelChoiceSelection(_talentState);
                return;
            }

            var prompt = new Label(choice.PromptKey);
            prompt.AddToClassList("talent-choice-prompt");
            _talentActionContainer.Add(prompt);
            foreach (TalentChoiceOption choiceOption in choice.Options)
            {
                string choiceId = choiceOption.ChoiceId;
                var button = new Button { text = choiceOption.DisplayKey };
                button.AddToClassList("talent-action-btn");
                button.AddToClassList("talent-choice-btn");
                button.clicked += () =>
                {
                    TalentActionOption selected = TalentActionPanelPolicy.SelectChoice(
                        presentation.Option,
                        choiceId);
                    if (selected != null)
                        _talentSelectedCallback?.Invoke(selected);
                };
                _talentActionContainer.Add(button);
            }

            var cancel = new Button { text = "取消" };
            cancel.AddToClassList("talent-action-btn");
            cancel.AddToClassList("talent-choice-cancel-btn");
            cancel.clicked += () =>
            {
                _talentState = TalentActionPanelPolicy.CancelChoiceSelection(_talentState);
                RenderTalentActions();
            };
            _talentActionContainer.Add(cancel);
        }

        private BaseActionAvailability ReadBaseActionAvailability() => new BaseActionAvailability
        {
            CanHu = IsVisible(_btnHu),
            CanPon = IsVisible(_btnPon),
            CanChi = IsVisible(_btnChi),
            CanKong = IsVisible(_btnMingGan) || IsVisible(_btnAnGan) || IsVisible(_btnJiaGang),
            CanSkip = IsVisible(_btnSkip),
            CanDiscard = true
        };

        private static bool IsVisible(VisualElement element) =>
            element != null && element.style.display != DisplayStyle.None;

        private void RefreshRootVisibility()
        {
            if (_root == null) return;
            bool hasBaseCallback = _currentCallback != null
                || _currentChiCallback != null
                || _currentKongCallback != null;
            _root.style.display = hasBaseCallback || _talentState.IsOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void ShowTalentStatus(string copy)
        {
            if (_talentActionStatus == null) return;
            _talentStatusClearSchedule?.Pause();
            _talentActionStatus.text = copy ?? string.Empty;
            _talentActionStatus.style.display = string.IsNullOrEmpty(copy)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            _talentStatusClearSchedule = _talentActionStatus.schedule.Execute(ClearTalentStatus)
                .StartingIn(2200);
        }

        private void ClearTalentStatus()
        {
            _talentStatusClearSchedule?.Pause();
            _talentStatusClearSchedule = null;
            if (_talentActionStatus == null) return;
            _talentActionStatus.text = string.Empty;
            _talentActionStatus.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 显示“吃”的二级菜单
        /// </summary>
        public void ShowChiSelection(List<string> options, Action<int> callback)
        {
            _currentChiCallback = callback;
            
            // 1. 隐藏主按钮
            HideMainButtons();

            // 2. 关键修复：先清理旧的临时按钮，防止重复堆叠！
            ClearTempButtons();

            // 3. 生成新按钮
            for (int i = 0; i < options.Count; i++)
            {
                int index = i; 
                Button btn = new Button();
                btn.text = $"吃\n{options[i]}";
                btn.AddToClassList("action-btn"); // 复用 USS 样式
                btn.style.width = 160;
                btn.style.height = 100;
                btn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.6f, 0.2f));
                
                // 标记为临时按钮
                btn.userData = "temp_chi";

                // 绑定点击
                btn.clicked += () => 
                {
                    // 同样做防抖保护
                    if (_root.style.display == DisplayStyle.None) return;
                    
                    _currentChiCallback?.Invoke(index);
                    
                    // 选完后恢复并关闭
                    RestoreMainButtons(); 
                    Hide(); 
                };

                _btnContainer.Add(btn);
            }
        }

        public void ShowKongSelection(ActionPanelChoice choice, IReadOnlyList<TileData> targets, Action<TileData> callback)
        {
            if (targets == null || targets.Count == 0) return;

            _currentKongCallback = callback;
            HideMainButtons();
            ClearTempButtons();

            string actionName = choice == ActionPanelChoice.AnGan ? "暗杠" : "加杠";
            foreach (var target in targets)
            {
                if (target == null) continue;
                var selectedTarget = target;
                Button btn = new Button
                {
                    text = $"{actionName}\n{selectedTarget.GetName()}"
                };
                btn.AddToClassList("action-btn");
                btn.AddToClassList("kong-target-btn");
                btn.userData = "temp_kong";
                btn.clicked += () =>
                {
                    if (_root.style.display == DisplayStyle.None) return;
                    var selectedCallback = _currentKongCallback;
                    RestoreMainButtons();
                    Hide();
                    selectedCallback?.Invoke(selectedTarget);
                };
                _btnContainer.Add(btn);
            }
        }

        private void SetVisible(VisualElement elem, bool visible)
        {
            elem.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HideMainButtons()
        {
            SetVisible(_btnChi, false);
            SetVisible(_btnPon, false);
            SetVisible(_btnMingGan, false);
            SetVisible(_btnAnGan, false);
            SetVisible(_btnJiaGang, false);
            SetVisible(_btnHu, false);
            SetVisible(_btnSkip, false);
        }

        // 彻底清理临时按钮
        private void ClearTempButtons()
        {
            // 倒序遍历删除
            for (int i = _btnContainer.childCount - 1; i >= 0; i--)
            {
                var child = _btnContainer.ElementAt(i);
                string tempType = child.userData as string;
                if (tempType == "temp_chi" || tempType == "temp_kong")
                {
                    child.RemoveFromHierarchy();
                }
            }
        }

        private void RestoreMainButtons()
        {
            ClearTempButtons();
            // 注意：SetVisible 会在下次 Show 时根据 Action 重新设置，这里只需清理临时按钮
        }
    }
}
