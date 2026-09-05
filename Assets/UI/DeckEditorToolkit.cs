using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using MahjongGame.Core.Network.Data;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Systems;
using MahjongGame.Talents;

namespace MahjongGame.UI
{
    public class DeckEditorToolkit : MonoBehaviour
    {
        private const int MAX_DECKS = 5;

        [Header("UI Document")]
        [SerializeField] private UIDocument _document;

        [Header("Assets")]
        [SerializeField] private VisualTreeAsset _itemTemplate;
        [SerializeField] private VisualTreeAsset _talentSlotTemplate;
        [SerializeField] private StyleSheet _styleSheet;
        [SerializeField] private StyleSheet _talentSlotStyleSheet;

        public event Action<DeckConfig> OnDeckSaved;
        public event Action OnExitRequested;

        // UI 元素引用
        private VisualElement _root;
        private VisualElement _mainGrid;
        private Label _totalText;
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
        private Button _btnSave;
        private Button _btnExit;
        private Button _btnClearAll;
        private Button _btnResetAll;
        private VisualElement _unsavedChangesOverlay;
        private Label _unsavedChangesMessage;
        private Button _btnUnsavedSave;
        private Button _btnUnsavedDiscard;
        private Button _btnUnsavedCancel;

        // Sidebar 引用
        private VisualElement _deckListContainer;
        private Button _btnNewDeck;
        private VisualElement _deckSidebar;
        private VisualElement _deckListScroll;
        private Label _deckListTitle;

        // Talent section
        private VisualElement _talentSlotsContainer;
        private VisualElement _mainTalentSlots;
        private VisualElement _reserveTalentSlots;
        private TalentSlotConfig _currentTalents;

        // 数据
        private DeckConfig _currentConfig;
        private List<SavedDeck> _savedDecks;
        private int _selectedDeckIndex;
        private AlienationPreset _currentAlienationPreset = AlienationPreset.Standard;
        private bool _isDraftDirty;
        private bool _isRoomAiMode;
        private AiLoadoutDraft _roomAiWorkingDraft;
        private Action<AiLoadoutDraft> _roomAiApply;
        public bool IsRoomAiEditorOpen => _isRoomAiMode && _root?.style.display.value == DisplayStyle.Flex;

        private Action _selectLowPreset;
        private Action _selectStandardPreset;
        private Action _selectHighPreset;
        private Action _pendingDraftNavigation;

        private List<Action> _allItemRefreshers = new List<Action>();

        void OnEnable()
        {
            if (_document == null) _document = GetComponent<UIDocument>();

            _root = _document.rootVisualElement;

            if (_styleSheet != null && !_root.styleSheets.Contains(_styleSheet))
                _root.styleSheets.Add(_styleSheet);
            if (_talentSlotStyleSheet != null && !_root.styleSheets.Contains(_talentSlotStyleSheet))
                _root.styleSheets.Add(_talentSlotStyleSheet);

            _mainGrid = _root.Q<VisualElement>("MainGrid");
            _totalText = _root.Q<Label>("TotalText");
            _budgetTitle = _root.Q<Label>("BudgetTitle");
            _budgetDeckName = _root.Q<Label>("BudgetDeckName");
            _alienationDialHost = _root.Q<VisualElement>("AlienationDialHost");
            _alienationDialValue = _root.Q<Label>("AlienationDialValue");
            _btnPresetLow = _root.Q<Button>("BtnPresetLow");
            _btnPresetStandard = _root.Q<Button>("BtnPresetStandard");
            _btnPresetHigh = _root.Q<Button>("BtnPresetHigh");
            _budgetDeckCost = _root.Q<Label>("BudgetDeckCost");
            _budgetTalentCost = _root.Q<Label>("BudgetTalentCost");
            _budgetReserveCost = _root.Q<Label>("BudgetReserveCost");
            _budgetTotal = _root.Q<Label>("BudgetTotal");
            _budgetStatus = _root.Q<Label>("BudgetStatus");
            _btnSave = _root.Q<Button>("BtnSave");
            _btnExit = _root.Q<Button>("BtnExit");
            _btnClearAll = _root.Q<Button>("BtnClearAll");
            _btnResetAll = _root.Q<Button>("BtnResetAll");
            _unsavedChangesOverlay = _root.Q<VisualElement>("UnsavedChangesOverlay");
            _unsavedChangesMessage = _root.Q<Label>("UnsavedChangesMessage");
            _btnUnsavedSave = _root.Q<Button>("BtnUnsavedSave");
            _btnUnsavedDiscard = _root.Q<Button>("BtnUnsavedDiscard");
            _btnUnsavedCancel = _root.Q<Button>("BtnUnsavedCancel");

            // Sidebar
            _deckListContainer = _root.Q<VisualElement>("DeckListContainer");
            _btnNewDeck = _root.Q<Button>("BtnNewDeck");
            _deckSidebar = _root.Q<VisualElement>("DeckSidebar");
            _deckListScroll = _root.Q<VisualElement>("DeckListScroll");
            _deckListTitle = _root.Q<Label>("DeckListTitle");
            _talentSlotsContainer = _root.Q<VisualElement>("TalentSlotsSection");
            _mainTalentSlots = _root.Q<VisualElement>("MainTalentSlots");
            _reserveTalentSlots = _root.Q<VisualElement>("ReserveTalentSlots");
            _talentDetailLabel = _root.Q<Label>("TalentDetailLabel");

            _alienationDial = new AlienationDialElement();
            _alienationDialHost.Insert(0, _alienationDial);
            _selectLowPreset = () => SelectAlienationPreset(AlienationPreset.Low);
            _selectStandardPreset = () => SelectAlienationPreset(AlienationPreset.Standard);
            _selectHighPreset = () => SelectAlienationPreset(AlienationPreset.High);

            // 绑定事件
            _btnSave.clicked += OnSaveClicked;
            _btnExit.clicked += OnExitClicked;
            _btnClearAll.clicked += OnClearAllClicked;
            _btnResetAll.clicked += OnResetAllClicked;
            _btnNewDeck.clicked += OnNewDeckClicked;
            _btnPresetLow.clicked += _selectLowPreset;
            _btnPresetStandard.clicked += _selectStandardPreset;
            _btnPresetHigh.clicked += _selectHighPreset;
            _btnUnsavedSave.clicked += OnUnsavedSaveClicked;
            _btnUnsavedDiscard.clicked += OnUnsavedDiscardClicked;
            _btnUnsavedCancel.clicked += OnUnsavedCancelClicked;

            // 初始化默认 config 供 GenerateRows 中的 updateLocalUI 使用
            _currentConfig = DeckConfig.CreateStandard();
            _currentTalents = new TalentSlotConfig();
            GenerateRows();
            GenerateTalentSlots();
            InitializeDeckList();

            // 初始状态隐藏，由 LobbyController 切换 display 显示
            _root.style.display = DisplayStyle.None;
        }

        void OnDisable()
        {
            _btnSave.clicked -= OnSaveClicked;
            _btnExit.clicked -= OnExitClicked;
            _btnClearAll.clicked -= OnClearAllClicked;
            _btnResetAll.clicked -= OnResetAllClicked;
            _btnNewDeck.clicked -= OnNewDeckClicked;
            _btnPresetLow.clicked -= _selectLowPreset;
            _btnPresetStandard.clicked -= _selectStandardPreset;
            _btnPresetHigh.clicked -= _selectHighPreset;
            _btnUnsavedSave.clicked -= OnUnsavedSaveClicked;
            _btnUnsavedDiscard.clicked -= OnUnsavedDiscardClicked;
            _btnUnsavedCancel.clicked -= OnUnsavedCancelClicked;
            CloseUnsavedPrompt();
            _alienationDial?.RemoveFromHierarchy();
            _alienationDial = null;
            CleanupRoomAiEditor(false);
        }

        private void OnClearAllClicked() => BatchUpdateDeck(0);
        private void OnResetAllClicked() => BatchUpdateDeck(1);

        /// <summary>
        /// 切换到 Workshop 时由 LobbyController 调用，刷新卡组列表和当前选中项
        /// </summary>
        public void RefreshDeckList()
        {
            if (_isRoomAiMode) return;
            _savedDecks = ProfileManager.Instance?.CurrentProfile?.SavedDecks;
            if (_savedDecks == null || _savedDecks.Count == 0)
            {
                InitializeDeckList();
                return;
            }

            // 同步 SelectedDeckIndex
            int idx = ProfileManager.Instance?.CurrentProfile?.SelectedDeckIndex ?? 0;
            if (idx < 0 || idx >= _savedDecks.Count) idx = 0;

            _selectedDeckIndex = idx;
            SelectDeck(_selectedDeckIndex);
            RebuildDeckList();
        }

        private void InitializeDeckList()
        {
            _savedDecks = ProfileManager.Instance?.CurrentProfile?.SavedDecks;
            if (_savedDecks == null)
            {
                _savedDecks = new List<SavedDeck>();
                if (ProfileManager.Instance?.CurrentProfile != null)
                    ProfileManager.Instance.CurrentProfile.SavedDecks = _savedDecks;
            }

            if (_savedDecks.Count == 0)
            {
                _savedDecks.Add(new SavedDeck
                {
                    DeckId = Guid.NewGuid().ToString(),
                    DeckName = "标准牌库",
                    AlienationScore = 0,
                    Config = DeckConfig.CreateStandard()
                });
                ProfileManager.Instance?.SaveProfile();
            }

            _selectedDeckIndex = 0;
            SelectDeck(0);
            RebuildDeckList();
        }

        private void RebuildDeckList()
        {
            _deckListContainer.Clear();

            for (int i = 0; i < _savedDecks.Count; i++)
            {
                int index = i;
                var deck = _savedDecks[i];

                var item = new VisualElement();
                item.AddToClassList("deck-list-item");
                if (index == _selectedDeckIndex) item.AddToClassList("selected");

                // Header: name + delete button
                var header = new VisualElement();
                header.AddToClassList("deck-item-header");

                var nameLabel = new Label(deck.DeckName);
                nameLabel.AddToClassList("deck-name-label");

                var btnDelete = new Button(() => OnDeleteDeckClicked(index)) { text = "×" };
                btnDelete.AddToClassList("btn-delete-deck");
                if (_savedDecks.Count <= 1) btnDelete.SetEnabled(false);

                header.Add(nameLabel);
                header.Add(btnDelete);

                // Score
                int limit = AlienationBudgetPolicy.GetLimit(AlienationBudgetPolicy.IsDefined(deck.AlienationPreset)
                    ? deck.AlienationPreset
                    : AlienationPreset.Standard);
                int currentAlienation = deck.CalculateCurrentAlienation();
                int overflow = Math.Max(0, currentAlienation - limit);
                var scoreLabel = new Label(overflow > 0
                    ? $"异化值: {currentAlienation} / {limit}　超限 {overflow}"
                    : $"异化值: {currentAlienation} / {limit}");
                scoreLabel.AddToClassList("deck-score-label");

                item.Add(header);
                item.Add(scoreLabel);

                // Click to select
                item.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.clickCount == 2)
                    {
                        StartRename(item, nameLabel, index);
                    }
                    else if (evt.clickCount == 1)
                    {
                        if (_selectedDeckIndex != index)
                        {
                            RequestDraftNavigation(() =>
                            {
                                SelectDeck(index);
                                UpdateSelectedHighlight();
                            });
                        }
                    }
                });

                _deckListContainer.Add(item);
            }

            _btnNewDeck.SetEnabled(_savedDecks.Count < MAX_DECKS);
        }

        private void SelectDeck(int index)
        {
            if (index < 0 || index >= _savedDecks.Count) return;
            _selectedDeckIndex = index;

            // Deep copy via JsonUtility
            var source = _savedDecks[index].Config;
            if (source != null)
            {
                string json = JsonUtility.ToJson(source);
                _currentConfig = JsonUtility.FromJson<DeckConfig>(json);
            }
            else
            {
                _currentConfig = DeckConfig.CreateStandard();
            }

            // Load talent config (deep copy)
            var talentSource = _savedDecks[index].Talents;
            if (talentSource != null)
            {
                string talentJson = JsonUtility.ToJson(talentSource);
                _currentTalents = JsonUtility.FromJson<TalentSlotConfig>(talentJson);
            }
            else
            {
                _currentTalents = new TalentSlotConfig();
            }
            _currentTalents.Normalize();
            _currentAlienationPreset = AlienationBudgetPolicy.IsDefined(_savedDecks[index].AlienationPreset)
                ? _savedDecks[index].AlienationPreset
                : AlienationPreset.Standard;
            _isDraftDirty = false;

            RefreshUI();
        }

        private void UpdateSelectedHighlight()
        {
            var items = _deckListContainer.Children().ToList();
            for (int i = 0; i < items.Count; i++)
            {
                items[i].EnableInClassList("selected", i == _selectedDeckIndex);
            }
        }

        private void OnNewDeckClicked()
        {
            if (_savedDecks.Count >= MAX_DECKS) return;
            RequestDraftNavigation(CreateAndSelectNewDeck);
        }

        private void CreateAndSelectNewDeck()
        {
            int num = _savedDecks.Count + 1;
            var newDeck = new SavedDeck
            {
                DeckId = Guid.NewGuid().ToString(),
                DeckName = $"卡组 {num}",
                AlienationScore = 0,
                Config = DeckConfig.CreateStandard(),
                Talents = new TalentSlotConfig(),
                AlienationPreset = AlienationPreset.Standard
            };
            _savedDecks.Add(newDeck);
            ProfileManager.Instance?.SaveProfile();

            _selectedDeckIndex = _savedDecks.Count - 1;
            SelectDeck(_selectedDeckIndex);
            RebuildDeckList();
        }

        private void OnDeleteDeckClicked(int index)
        {
            if (_savedDecks.Count <= 1) return;
            if (index < 0 || index >= _savedDecks.Count) return;

            if (index == _selectedDeckIndex)
            {
                RequestDraftNavigation(() => DeleteCurrentDeck(index));
                return;
            }

            DeleteUnselectedDeck(index);
        }

        private void DeleteCurrentDeck(int index)
        {
            _savedDecks.RemoveAt(index);
            _selectedDeckIndex = Math.Min(index, _savedDecks.Count - 1);
            SaveSelectedDeckIndex();
            ProfileManager.Instance?.SaveProfile();
            SelectDeck(_selectedDeckIndex);
            RebuildDeckList();
        }

        private void DeleteUnselectedDeck(int index)
        {
            _savedDecks.RemoveAt(index);
            if (_selectedDeckIndex > index)
                _selectedDeckIndex--;
            SaveSelectedDeckIndex();
            ProfileManager.Instance?.SaveProfile();
            RebuildDeckList();
            RefreshStats();
        }

        private void StartRename(VisualElement item, Label nameLabel, int index)
        {
            nameLabel.style.display = DisplayStyle.None;

            var textField = new TextField();
            textField.AddToClassList("deck-name-field");
            textField.value = _savedDecks[index].DeckName;

            var header = item.Q(className: "deck-item-header");
            header.Insert(0, textField);

            textField.schedule.Execute(() => textField.Focus());

            bool committed = false;
            Action commit = () =>
            {
                if (committed) return;
                committed = true;

                string newName = textField.value?.Trim();
                if (!string.IsNullOrEmpty(newName))
                {
                    _savedDecks[index].DeckName = newName;
                    if (index == _selectedDeckIndex && _budgetDeckName != null)
                        _budgetDeckName.text = newName;
                    ProfileManager.Instance?.SaveProfile();
                }

                header.Remove(textField);
                nameLabel.text = _savedDecks[index].DeckName;
                nameLabel.style.display = DisplayStyle.Flex;
            };

            textField.RegisterCallback<FocusOutEvent>(evt => commit());
            textField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    commit();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    // Cancel: restore original name
                    textField.value = _savedDecks[index].DeckName;
                    commit();
                    evt.StopPropagation();
                }
            });
        }

        private void OnSaveClicked()
        {
            if (!TrySaveCurrentDeck()) return;
            if (_isRoomAiMode) CleanupRoomAiEditor(true);
        }

        private bool TrySaveCurrentDeck()
        {
            if (_isRoomAiMode) return TryApplyRoomAiDraft();
            if (_selectedDeckIndex < 0 || _selectedDeckIndex >= _savedDecks.Count) return false;
            if (_currentConfig.GenerateTiles(0).Count != 34) return false;

            // Write current config back to saved deck
            string json = JsonUtility.ToJson(_currentConfig);
            _savedDecks[_selectedDeckIndex].Config = JsonUtility.FromJson<DeckConfig>(json);

            // Save talent config
            string talentJson = JsonUtility.ToJson(_currentTalents);
            _savedDecks[_selectedDeckIndex].Talents = JsonUtility.FromJson<TalentSlotConfig>(talentJson);
            _savedDecks[_selectedDeckIndex].AlienationPreset = _currentAlienationPreset;
            _savedDecks[_selectedDeckIndex].AlienationScore =
                _savedDecks[_selectedDeckIndex].CalculateCurrentAlienation();

            // 记录选中的卡组索引
            SaveSelectedDeckIndex();

            _isDraftDirty = false;
            ProfileManager.Instance?.SaveProfile();
            RebuildDeckList();
            RefreshStats();

            OnDeckSaved?.Invoke(_currentConfig);
            return true;
        }

        private void OnExitClicked()
        {
            RequestDraftNavigation(() =>
            {
                if (_isRoomAiMode) CleanupRoomAiEditor(false);
                else OnExitRequested?.Invoke();
            });
        }

        private void RequestDraftNavigation(Action continuation)
        {
            int tileCount = _currentConfig.GenerateTiles(0).Count;
            DeckEditorLeavePromptView prompt =
                DeckEditorDraftPresentationPolicy.BuildLeavePrompt(_isDraftDirty, tileCount);
            if (!prompt.IsRequired)
            {
                continuation?.Invoke();
                return;
            }

            _pendingDraftNavigation = continuation;
            _unsavedChangesMessage.text = prompt.Message;
            _btnUnsavedSave.style.display = prompt.CanSave ? DisplayStyle.Flex : DisplayStyle.None;
            _unsavedChangesOverlay.style.display = DisplayStyle.Flex;
        }

        private void OnUnsavedSaveClicked()
        {
            Action continuation = _pendingDraftNavigation;
            if (!TrySaveCurrentDeck()) return;
            CloseUnsavedPrompt();
            if (_isRoomAiMode) CleanupRoomAiEditor(true);
            else continuation?.Invoke();
        }

        private void OnUnsavedDiscardClicked()
        {
            Action continuation = _pendingDraftNavigation;
            CloseUnsavedPrompt();
            continuation?.Invoke();
        }

        private void OnUnsavedCancelClicked() => CloseUnsavedPrompt();

        private void CloseUnsavedPrompt()
        {
            _pendingDraftNavigation = null;
            if (_unsavedChangesOverlay != null)
                _unsavedChangesOverlay.style.display = DisplayStyle.None;
        }

        private void SaveSelectedDeckIndex()
        {
            if (ProfileManager.Instance?.CurrentProfile != null)
                ProfileManager.Instance.CurrentProfile.SelectedDeckIndex = _selectedDeckIndex;
        }

        public void LoadConfig(DeckConfig config)
        {
            if (config != null)
                _currentConfig = config;
            else
                _currentConfig = DeckConfig.CreateStandard();
            _isDraftDirty = false;
            RefreshUI();
        }

        /// <summary>
        /// Reuses the player deck editor as a room-scoped AI draft editor. Profile persistence remains disabled.
        /// </summary>
        public void OpenRoomAiDraft(AiLoadoutDraft draft, string displayName, Action<AiLoadoutDraft> onApply)
        {
            if (draft == null || _root == null) return;
            PlayerLoadoutMessage message = draft.ToMessage();
            if (!PlayerLoadoutCodec.TryDecode(message, out TrustedPlayerLoadout trusted, out string error))
            {
                Debug.LogWarning($"[DeckEditor] Unable to open AI draft: {error}");
                return;
            }

            _isRoomAiMode = true;
            _roomAiWorkingDraft = draft.Clone();
            _roomAiApply = onApply;
            _currentConfig = trusted.DeckConfig;
            _currentTalents = trusted.TalentConfig;
            _currentTalents.Normalize();
            _currentAlienationPreset = draft.RoomPreset;
            _isDraftDirty = false;
            CloseUnsavedPrompt();
            _root.Q<VisualElement>("TalentPickerOverlay")?.RemoveFromHierarchy();
            if (_document != null) _document.sortingOrder = 60;
            if (_deckListScroll != null) _deckListScroll.style.display = DisplayStyle.None;
            if (_btnNewDeck != null) _btnNewDeck.style.display = DisplayStyle.None;
            if (_deckListTitle != null)
            {
                _deckListTitle.style.display = DisplayStyle.Flex;
                _deckListTitle.text = "房间 AI 草稿";
            }
            _btnPresetLow.SetEnabled(false);
            _btnPresetStandard.SetEnabled(false);
            _btnPresetHigh.SetEnabled(false);
            _btnSave.text = "应用到房间草稿";
            _btnExit.text = "返回房间";
            _budgetTitle.text = "AI 高级构筑";
            _budgetDeckName.text = string.IsNullOrWhiteSpace(displayName) ? "永久 AI" : displayName;
            _root.style.display = DisplayStyle.Flex;
            RefreshUI();
        }

        public void ShowProfileEditor()
        {
            if (_root == null) return;
            if (_isRoomAiMode) CleanupRoomAiEditor(false);
            if (_document != null) _document.sortingOrder = 0;
            RestoreProfileEditorChrome();
            _root.style.display = DisplayStyle.Flex;
            RefreshDeckList();
        }

        public void HideEditor()
        {
            if (_isRoomAiMode) CleanupRoomAiEditor(false);
            else if (_root != null) _root.style.display = DisplayStyle.None;
        }

        public void CloseRoomAiEditorForAuthorityChange()
        {
            if (_isRoomAiMode) CleanupRoomAiEditor(false);
        }

        private bool TryApplyRoomAiDraft()
        {
            if (_roomAiWorkingDraft == null || _currentConfig.GenerateTiles(0).Count != 34) return false;
            PlayerLoadoutMessage message = PlayerLoadoutCodec.CreateMessage(
                _currentConfig, _currentTalents, _currentAlienationPreset);
            if (!PlayerLoadoutCodec.TryDecode(
                    message, _roomAiWorkingDraft.RoomPreset, out _, out string error))
            {
                Debug.LogWarning($"[DeckEditor] AI draft rejected locally: {error}");
                return false;
            }

            if (_isDraftDirty)
                _roomAiWorkingDraft.ReplaceLoadout(AiLoadoutTemplate.Custom, message);
            _isDraftDirty = false;
            RefreshStats();
            return true;
        }

        private void CleanupRoomAiEditor(bool apply)
        {
            if (!_isRoomAiMode) return;
            Action<AiLoadoutDraft> callback = _roomAiApply;
            AiLoadoutDraft result = apply ? _roomAiWorkingDraft?.Clone() : null;
            _pendingDraftNavigation = null;
            CloseUnsavedPrompt();
            _root?.Q<VisualElement>("TalentPickerOverlay")?.RemoveFromHierarchy();
            if (_root != null) _root.style.display = DisplayStyle.None;
            if (_document != null) _document.sortingOrder = 0;
            _roomAiApply = null;
            _roomAiWorkingDraft = null;
            _isRoomAiMode = false;
            _isDraftDirty = false;
            RestoreProfileEditorChrome();
            if (apply && result != null) callback?.Invoke(result);
        }

        private void RestoreProfileEditorChrome()
        {
            if (_deckListScroll != null) _deckListScroll.style.display = DisplayStyle.Flex;
            if (_btnNewDeck != null) _btnNewDeck.style.display = DisplayStyle.Flex;
            if (_deckListTitle != null)
            {
                _deckListTitle.style.display = DisplayStyle.Flex;
                _deckListTitle.text = "牌库列表";
            }
            _btnPresetLow?.SetEnabled(true);
            _btnPresetStandard?.SetEnabled(true);
            _btnPresetHigh?.SetEnabled(true);
            if (_btnSave != null) _btnSave.text = "保存";
            if (_btnExit != null) _btnExit.text = "退出";
        }

        private void GenerateRows()
        {
            _mainGrid.Clear();
            _allItemRefreshers.Clear();

            CreateSuitRow("万", Suit.Man);
            CreateSuitRow("筒", Suit.Pin);
            CreateSuitRow("索", Suit.Sou);
            CreateWordRow();
        }

        private void CreateSuitRow(string label, Suit suit)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("suit-row");

            VisualElement grid = new VisualElement();
            grid.AddToClassList("grid-container");
            grid.style.flexGrow = 1;

            int maxVal = (suit == Suit.Wind) ? 4 : (suit == Suit.Dragon ? 3 : 9);
            for (int v = 1; v <= maxVal; v++)
            {
                grid.Add(CreateTileItem(suit, v));
            }
            row.Add(grid);

            VisualElement controls = new VisualElement();
            controls.AddToClassList("suit-controls");

            Button btnClear = new Button(() => BatchUpdateSuit(suit, 0)) { text = "清空" };
            btnClear.AddToClassList("control-btn");
            Button btnReset = new Button(() => BatchUpdateSuit(suit, 1)) { text = "重置" };
            btnReset.AddToClassList("control-btn");

            controls.Add(btnClear);
            controls.Add(btnReset);
            row.Add(controls);

            _mainGrid.Add(row);
        }

        private void CreateWordRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("suit-row");

            VisualElement grid = new VisualElement();
            grid.AddToClassList("grid-container");
            grid.style.flexGrow = 1;

            for (int v = 1; v <= 4; v++) grid.Add(CreateTileItem(Suit.Wind, v));
            for (int v = 1; v <= 3; v++) grid.Add(CreateTileItem(Suit.Dragon, v));

            row.Add(grid);

            VisualElement controls = new VisualElement();
            controls.AddToClassList("suit-controls");

            Button btnClear = new Button(() => {
                bool changed = BatchUpdateSuit(Suit.Wind, 0, false);
                changed |= BatchUpdateSuit(Suit.Dragon, 0, false);
                CompleteBatchUpdate(changed);
            }) { text = "清空" };
            btnClear.AddToClassList("control-btn");

            Button btnReset = new Button(() => {
                bool changed = BatchUpdateSuit(Suit.Wind, 1, false);
                changed |= BatchUpdateSuit(Suit.Dragon, 1, false);
                CompleteBatchUpdate(changed);
            }) { text = "重置" };
            btnReset.AddToClassList("control-btn");

            controls.Add(btnClear);
            controls.Add(btnReset);
            row.Add(controls);

            _mainGrid.Add(row);
        }

        private VisualElement CreateTileItem(Suit suit, int value)
        {
            TemplateContainer instance = _itemTemplate.Instantiate();
            var faceImage = instance.Q<VisualElement>("FaceImage");
            var countLabel = instance.Q<Label>("CountLabel");
            var btnPlus = instance.Q<Button>("BtnPlus");
            var btnMinus = instance.Q<Button>("BtnMinus");

            string imagePath = GetTileImagePath(suit, value);
            Sprite tileSprite = Resources.Load<Sprite>(imagePath);
            if (tileSprite != null)
            {
                faceImage.style.backgroundImage = new StyleBackground(tileSprite);
            }
            else
            {
                var fallbackLabel = new Label(GetTileNameForFallback(suit, value));
                fallbackLabel.style.fontSize = 20;
                fallbackLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                faceImage.Add(fallbackLabel);
                Debug.LogWarning($"[DeckEditor] Tile image not found, using fallback text. Path: {imagePath}");
            }

            Action updateLocalUI = () =>
            {
                int count = _currentConfig.GetCardCount(suit, value);
                countLabel.text = count.ToString();
                if (count > 0) countLabel.AddToClassList("active");
                else countLabel.RemoveFromClassList("active");
            };

            btnPlus.clicked += () =>
            {
                _currentConfig.SetCardCount(suit, value, _currentConfig.GetCardCount(suit, value) + 1);
                _currentConfig.CalculateAlienationScore();
                updateLocalUI();
                MarkDraftDirty();
            };

            btnMinus.clicked += () =>
            {
                int current = _currentConfig.GetCardCount(suit, value);
                if (current > 0)
                {
                    _currentConfig.SetCardCount(suit, value, current - 1);
                    _currentConfig.CalculateAlienationScore();
                    updateLocalUI();
                    MarkDraftDirty();
                }
            };

            _allItemRefreshers.Add(updateLocalUI);
            updateLocalUI();
            return instance;
        }

        private bool BatchUpdateSuit(Suit suit, int count, bool refreshAll = true)
        {
            bool changed = false;
            int maxVal = (suit == Suit.Wind) ? 4 : (suit == Suit.Dragon ? 3 : 9);
            for (int v = 1; v <= maxVal; v++)
            {
                if (_currentConfig.GetCardCount(suit, v) == count) continue;
                _currentConfig.SetCardCount(suit, v, count);
                changed = true;
            }

            if (refreshAll)
                CompleteBatchUpdate(changed);
            return changed;
        }

        private void BatchUpdateDeck(int count)
        {
            bool changed = false;
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            {
                int maxVal = (suit == Suit.Wind) ? 4 : (suit == Suit.Dragon ? 3 : 9);
                for (int v = 1; v <= maxVal; v++)
                {
                    if (_currentConfig.GetCardCount(suit, v) == count) continue;
                    _currentConfig.SetCardCount(suit, v, count);
                    changed = true;
                }
            }
            CompleteBatchUpdate(changed);
        }

        private void CompleteBatchUpdate(bool changed)
        {
            if (!changed) return;
            _currentConfig.CalculateAlienationScore();
            foreach (var refresh in _allItemRefreshers) refresh();
            MarkDraftDirty();
        }

        private void RefreshStats()
        {
            int total = _currentConfig.GenerateTiles(0).Count;
            _currentConfig.CalculateAlienationScore();
            int deckCost = _currentConfig.AlienationScore;
            int talentCost = _currentTalents.GetMainIds().Sum(TalentRegistry.Instance.GetCost);
            AlienationGaugeView gauge = AlienationGaugePolicy.Build(deckCost, talentCost, _currentAlienationPreset);
            DeckEditorDraftView draftView = DeckEditorDraftPresentationPolicy.Build(
                gauge, total, _isDraftDirty);
            _totalText.text = $"Total: {total} / 34";
            _budgetTitle.text = _isRoomAiMode ? "AI 高级构筑" : draftView.Title;
            _budgetDeckName.text = _savedDecks != null
                && _selectedDeckIndex >= 0
                && _selectedDeckIndex < _savedDecks.Count
                ? (_isRoomAiMode ? _budgetDeckName.text : _savedDecks[_selectedDeckIndex].DeckName)
                : (_isRoomAiMode ? _budgetDeckName.text : string.Empty);
            _alienationDialValue.text = $"{gauge.Total} / {gauge.Limit}";
            _alienationDial.SetValue(gauge.Fill01, draftView.Tone);
            _budgetDeckCost.text = $"牌山成本    {gauge.DeckCost}";
            _budgetTalentCost.text = $"主天赋成本  {gauge.TalentCost}";
            _budgetReserveCost.text = "备牌成本    不计入";
            _budgetTotal.text = $"当前总计    {gauge.Total}";
            _budgetStatus.text = draftView.StatusText;
            _budgetStatus.EnableInClassList("near-limit", draftView.Tone == DeckEditorBudgetTone.NearLimit);
            _budgetStatus.EnableInClassList("over-limit", draftView.Tone == DeckEditorBudgetTone.OverLimit);
            _btnPresetLow.EnableInClassList("selected", _currentAlienationPreset == AlienationPreset.Low);
            _btnPresetStandard.EnableInClassList("selected", _currentAlienationPreset == AlienationPreset.Standard);
            _btnPresetHigh.EnableInClassList("selected", _currentAlienationPreset == AlienationPreset.High);
            _totalText.EnableInClassList("text-green", total == 34);
            _totalText.EnableInClassList("text-white", total != 34);
            _btnSave.SetEnabled(draftView.CanSave);
        }

        private void SelectAlienationPreset(AlienationPreset preset)
        {
            if (_isRoomAiMode) return;
            if (_currentAlienationPreset == preset) return;
            _currentAlienationPreset = preset;
            MarkDraftDirty();
        }

        private void MarkDraftDirty()
        {
            _isDraftDirty = true;
            RefreshStats();
        }

        private string GetTileImagePath(Suit suit, int value)
        {
            string prefix = "Art/FlatTile/f";
            string suffix = "";
            string valueStr = value.ToString();

            switch (suit)
            {
                case Suit.Man: suffix = "m"; break;
                case Suit.Pin: suffix = "p"; break;
                case Suit.Sou: suffix = "s"; break;
                case Suit.Wind: suffix = "z"; break;
                case Suit.Dragon:
                    suffix = "z";
                    switch (value)
                    {
                        case 1: valueStr = "7"; break;
                        case 2: valueStr = "6"; break;
                        case 3: valueStr = "5"; break;
                    }
                    break;
            }
            return $"{prefix}{valueStr}{suffix}";
        }

        private string GetTileNameForFallback(Suit s, int v)
        {
            switch(s) {
                case Suit.Man: return $"{v}\n万";
                case Suit.Pin: return $"{v}\n筒";
                case Suit.Sou: return $"{v}\n索";
                case Suit.Wind: return v switch {1=>"东", 2=>"南", 3=>"西", 4=>"北", _=>""};
                case Suit.Dragon: return v switch {1=>"中", 2=>"发", 3=>"白", _=>""};
                default: return "?";
            }
        }

        // ======================== 天赋槽 UI ========================

        private static readonly string[] SlotTierLabels = { "大", "中", "中", "小", "小", "小" };
        private static readonly string[] ReserveSlotTierLabels = { "备选中", "备选小", "备选小" };
        // 分隔线插入位置：index 1 (大|中) 和 index 3 (中|小)
        private static readonly HashSet<int> SeparatorBeforeSlot = new HashSet<int> { 1, 3 };

        private Label _talentDetailLabel; // 天赋详情区域

        private void GenerateTalentSlots()
        {
            _mainTalentSlots.Clear();
            _reserveTalentSlots.Clear();
            for (int i = 0; i < TalentSlotConfig.MainSlotCount; i++)
                AddTalentSlot(_mainTalentSlots, i, false, SlotTierLabels[i]);
            for (int i = 0; i < TalentSlotConfig.ReserveSlotCount; i++)
                AddTalentSlot(_reserveTalentSlots, i, true, ReserveSlotTierLabels[i]);
        }

        private void AddTalentSlot(VisualElement container, int slotIndex, bool isReserve, string label)
        {
            if (!isReserve && SeparatorBeforeSlot.Contains(slotIndex))
            {
                var separator = new VisualElement();
                separator.AddToClassList("talent-slot-separator");
                container.Add(separator);
            }

            VisualElement slot;
            if (_talentSlotTemplate != null)
            {
                var instance = _talentSlotTemplate.Instantiate();
                slot = instance.Q<VisualElement>("Root") ?? instance;
                Label tierLabel = instance.Q<Label>("TierLabel");
                if (tierLabel != null) tierLabel.text = label;
            }
            else
            {
                slot = new VisualElement();
                slot.AddToClassList("talent-slot");
                var tierLabel = new Label(label); tierLabel.AddToClassList("talent-slot-tier"); slot.Add(tierLabel);
                var nameLabel = new Label("空") { name = "NameLabel" }; nameLabel.AddToClassList("talent-slot-name"); slot.Add(nameLabel);
                var clearButton = new Button { name = "BtnClear", text = "×" }; clearButton.AddToClassList("talent-slot-clear"); slot.Add(clearButton);
            }

            TalentTier slotTier = isReserve
                ? (slotIndex == 0 ? TalentTier.Medium : TalentTier.Small)
                : TalentSlotConfig.GetSlotTier(slotIndex);
            slot.AddToClassList(slotTier == TalentTier.Large ? "tier-large" : slotTier == TalentTier.Medium ? "tier-medium" : "tier-small");
            slot.userData = new TalentSlotBinding(slotIndex, isReserve);

            Button clear = slot.Q<Button>("BtnClear");
            if (clear != null)
            {
                clear.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    string[] slots = GetTalentSlots(isReserve);
                    if (string.IsNullOrEmpty(slots[slotIndex])) return;
                    slots[slotIndex] = null;
                    RefreshTalentSlots();
                    MarkDraftDirty();
                });
            }
            slot.RegisterCallback<ClickEvent>(_ => ShowTalentPicker(slotIndex, isReserve));
            container.Add(slot);
        }

        private void RefreshTalentSlots()
        {
            if (_talentSlotsContainer == null) return;

            var slots = _talentSlotsContainer.Query(className: "talent-slot").ToList();
            bool hasAnyEquipped = false;

            foreach (VisualElement slot in slots)
            {
                if (slot.userData is not TalentSlotBinding binding) continue;
                var nameLabel = slot.Q<Label>("NameLabel");
                string talentId = GetTalentSlots(binding.IsReserve)[binding.Index];
                bool occupied = !string.IsNullOrEmpty(talentId);

                slot.EnableInClassList("occupied", occupied);

                if (nameLabel != null)
                {
                    nameLabel.text = occupied ? TalentRegistry.Instance.GetDisplayName(talentId) : "空";
                }

                if (occupied) hasAnyEquipped = true;
            }

            // 更新详情区域：列出所有已装备天赋的信息
            if (_talentDetailLabel != null)
            {
                if (hasAnyEquipped)
                {
                    var lines = new List<string>();
                    for (int i = 0; i < TalentSlotConfig.MainSlotCount; i++)
                    {
                        string tid = _currentTalents?.SlotTalentIds[i];
                        if (string.IsNullOrEmpty(tid)) continue;

                        string name = TalentRegistry.Instance.GetDisplayName(tid);
                        string desc = TalentRegistry.Instance.GetDescription(tid);
                        int cost = TalentRegistry.Instance.GetCost(tid);
                        var tier = TalentRegistry.Instance.GetTier(tid);
                        string tierName = tier == TalentTier.Large ? "大" : (tier == TalentTier.Medium ? "中" : "小");
                        lines.Add($"[{SlotTierLabels[i]}槽] {name} ({tierName}) — {desc} — 异化值+{cost}");
                    }
                    for (int i = 0; i < TalentSlotConfig.ReserveSlotCount; i++)
                    {
                        string tid = _currentTalents.ReserveTalentIds[i];
                        if (string.IsNullOrEmpty(tid)) continue;
                        lines.Add($"[{ReserveSlotTierLabels[i]}槽] {TalentRegistry.Instance.GetDisplayName(tid)} — {TalentRegistry.Instance.GetDescription(tid)} — 备选不计当前异化值");
                    }
                    _talentDetailLabel.text = string.Join("\n", lines);
                    _talentDetailLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _talentDetailLabel.style.display = DisplayStyle.None;
                }
            }
        }

        private void ShowTalentPicker(int slotIndex, bool isReserve)
        {
            string[] targetSlots = GetTalentSlots(isReserve);
            string currentSlotTalent = targetSlots[slotIndex];
            TalentTier slotTier = isReserve
                ? (slotIndex == 0 ? TalentTier.Medium : TalentTier.Small)
                : TalentSlotConfig.GetSlotTier(slotIndex);
            string slotLabel = isReserve ? ReserveSlotTierLabels[slotIndex] : SlotTierLabels[slotIndex];
            string slotTierName = slotTier == TalentTier.Large ? "大" : (slotTier == TalentTier.Medium ? "中" : "小");

            // 1. 过滤：排除超出此槽位品阶上限的天赋，以及备选槽不支持的天赋
            var allIds = TalentRegistry.Instance.GetAllIds();
            var candidateList = new List<TalentPickerOption>();

            foreach (var id in allIds)
            {
                var tier = TalentRegistry.Instance.GetTier(id);
                TalentMetadata metadata = TalentRegistry.Instance.GetMetadata(id);

                bool allowedBySlot = isReserve
                    ? _currentTalents.CanEquipReserve(slotIndex, tier)
                    : _currentTalents.CanEquip(slotIndex, tier);
                bool allowedByMetadata = !isReserve || metadata.SideboardPolicy == TalentSideboardPolicy.Flexible;

                if (!allowedBySlot || !allowedByMetadata) continue;

                candidateList.Add(new TalentPickerOption
                {
                    Id = id,
                    DisplayName = TalentRegistry.Instance.GetDisplayName(id),
                    Description = TalentRegistry.Instance.GetDescription(id),
                    Cost = TalentRegistry.Instance.GetCost(id),
                    Tier = tier,
                    TierName = tier == TalentTier.Large ? "大" : (tier == TalentTier.Medium ? "中" : "小"),
                    TierClass = tier == TalentTier.Large ? "tier-large" : (tier == TalentTier.Medium ? "tier-medium" : "tier-small")
                });
            }

            // 2. 多级排序：品阶降序 (大 > 中 > 小) -> 异化值升序 (低费在前) -> 名称字母升序
            candidateList.Sort((a, b) =>
            {
                int tierCmp = ((int)b.Tier).CompareTo((int)a.Tier);
                if (tierCmp != 0) return tierCmp;
                int costCmp = a.Cost.CompareTo(b.Cost);
                if (costCmp != 0) return costCmp;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
            });

            // 3. 构建弹窗 UI 容器
            var overlay = new VisualElement();
            overlay.name = "TalentPickerOverlay";
            overlay.AddToClassList("talent-picker-overlay");

            var panel = new VisualElement();
            panel.AddToClassList("talent-picker-panel");

            // 头部栏
            var header = new VisualElement();
            header.AddToClassList("talent-picker-header");

            var title = new Label($"选择天赋 — {slotLabel}槽位");
            title.AddToClassList("talent-picker-title");
            header.Add(title);

            var slotBadge = new Label($"容量上限: {slotTierName}品阶");
            slotBadge.AddToClassList("talent-picker-slot-badge");
            header.Add(slotBadge);
            panel.Add(header);

            // 搜索与品阶筛选栏
            var filterBar = new VisualElement();
            filterBar.AddToClassList("talent-picker-filter-bar");

            var searchWrap = new VisualElement();
            searchWrap.AddToClassList("talent-picker-search-wrap");

            var searchIcon = new Label("[搜索]");
            searchIcon.AddToClassList("talent-picker-search-icon");
            searchWrap.Add(searchIcon);

            var searchField = new TextField();
            searchField.AddToClassList("talent-picker-search");
            searchField.value = "";
            searchWrap.Add(searchField);
            filterBar.Add(searchWrap);

            var tabsContainer = new VisualElement();
            tabsContainer.AddToClassList("talent-picker-tabs");

            var tabAll = new Button { text = "全部" };
            tabAll.AddToClassList("talent-tab-btn");
            tabAll.AddToClassList("active");
            tabsContainer.Add(tabAll);

            var tabLarge = new Button { text = "大" };
            tabLarge.AddToClassList("talent-tab-btn");
            if (slotTier < TalentTier.Large)
            {
                tabLarge.AddToClassList("disabled-tab");
                tabLarge.SetEnabled(false);
            }
            tabsContainer.Add(tabLarge);

            var tabMedium = new Button { text = "中" };
            tabMedium.AddToClassList("talent-tab-btn");
            if (slotTier < TalentTier.Medium)
            {
                tabMedium.AddToClassList("disabled-tab");
                tabMedium.SetEnabled(false);
            }
            tabsContainer.Add(tabMedium);

            var tabSmall = new Button { text = "小" };
            tabSmall.AddToClassList("talent-tab-btn");
            tabsContainer.Add(tabSmall);

            filterBar.Add(tabsContainer);
            panel.Add(filterBar);

            // 状态与计数提示行
            var statusLine = new VisualElement();
            statusLine.AddToClassList("talent-picker-status-line");

            var countLabel = new Label();
            countLabel.AddToClassList("talent-picker-count-label");
            statusLine.Add(countLabel);

            var hintLabel = new Label("点击卡片即可直接装配并关闭");
            hintLabel.AddToClassList("talent-picker-hint-label");
            statusLine.Add(hintLabel);

            panel.Add(statusLine);

            // 滚动网格区域
            var scrollView = new ScrollView();
            scrollView.AddToClassList("talent-picker-scroll");

            var grid = new VisualElement();
            grid.AddToClassList("talent-picker-grid");
            scrollView.Add(grid);
            panel.Add(scrollView);

            // 底部操作栏
            var footer = new VisualElement();
            footer.AddToClassList("talent-picker-footer");

            var clearBtn = new Button(() =>
            {
                targetSlots[slotIndex] = null;
                RefreshTalentSlots();
                MarkDraftDirty();
                _root.Remove(overlay);
            })
            { text = "清空此槽位" };
            clearBtn.AddToClassList("talent-picker-clear-btn");
            if (string.IsNullOrEmpty(currentSlotTalent))
            {
                clearBtn.style.display = DisplayStyle.None;
            }
            footer.Add(clearBtn);

            var cancelBtn = new Button(() => _root.Remove(overlay)) { text = "取消" };
            cancelBtn.AddToClassList("talent-picker-cancel-btn");
            footer.Add(cancelBtn);

            panel.Add(footer);
            overlay.Add(panel);

            // 筛选状态与刷新函数
            TalentTier? activeTierFilter = null;
            string currentQuery = "";

            void UpdateTabsActiveState()
            {
                tabAll.EnableInClassList("active", activeTierFilter == null);
                tabLarge.EnableInClassList("active", activeTierFilter == TalentTier.Large);
                tabMedium.EnableInClassList("active", activeTierFilter == TalentTier.Medium);
                tabSmall.EnableInClassList("active", activeTierFilter == TalentTier.Small);
            }

            void RefreshGrid()
            {
                grid.Clear();

                var filtered = candidateList.Where(item =>
                {
                    if (activeTierFilter.HasValue && item.Tier != activeTierFilter.Value)
                        return false;

                    if (!string.IsNullOrWhiteSpace(currentQuery))
                    {
                        bool nameMatch = item.DisplayName.IndexOf(currentQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                        bool descMatch = item.Description.IndexOf(currentQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!nameMatch && !descMatch) return false;
                    }

                    return true;
                }).ToList();

                countLabel.text = $"当前匹配 {filtered.Count} 个天赋 (按品阶降序与异化值升序排列)";

                if (filtered.Count == 0)
                {
                    var emptyBox = new VisualElement();
                    emptyBox.AddToClassList("talent-picker-empty");
                    var emptyLabel = new Label("[未找到匹配的天赋]");
                    emptyLabel.AddToClassList("talent-picker-empty-text");
                    emptyBox.Add(emptyLabel);
                    grid.Add(emptyBox);
                    return;
                }

                foreach (var opt in filtered)
                {
                    bool isCurrent = (opt.Id == currentSlotTalent);
                    bool isDuplicate = TalentPickerDuplicatePolicy.IsDuplicateOutsideSlot(
                        _currentTalents, opt.Id, slotIndex, isReserve);

                    var card = new VisualElement();
                    card.AddToClassList("talent-picker-card");
                    if (isCurrent) card.AddToClassList("is-current");
                    if (isDuplicate) card.AddToClassList("is-disabled");

                    // 卡片头部
                    var cardHeader = new VisualElement();
                    cardHeader.AddToClassList("talent-card-header");

                    var titleWrap = new VisualElement();
                    titleWrap.AddToClassList("talent-card-title-wrap");

                    var nameLabel = new Label(opt.DisplayName);
                    nameLabel.AddToClassList("talent-card-name");
                    titleWrap.Add(nameLabel);

                    var tierLabel = new Label($"[{opt.TierName}]");
                    tierLabel.AddToClassList("talent-card-tier");
                    tierLabel.AddToClassList(opt.TierClass);
                    titleWrap.Add(tierLabel);

                    cardHeader.Add(titleWrap);

                    var costLabel = new Label($"+{opt.Cost} 异化值");
                    costLabel.AddToClassList("talent-card-cost");
                    cardHeader.Add(costLabel);

                    card.Add(cardHeader);

                    // 卡片描述
                    var descLabel = new Label(opt.Description);
                    descLabel.AddToClassList("talent-card-desc");
                    card.Add(descLabel);

                    // 卡片底部状态角标
                    var cardFooter = new VisualElement();
                    cardFooter.AddToClassList("talent-card-footer");

                    if (isCurrent)
                    {
                        var badge = new Label("[当前槽位已装备]");
                        badge.AddToClassList("talent-card-status-badge");
                        badge.AddToClassList("badge-current");
                        cardFooter.Add(badge);
                    }
                    else if (isDuplicate)
                    {
                        var badge = new Label("[已在其他槽位装备]");
                        badge.AddToClassList("talent-card-status-badge");
                        badge.AddToClassList("badge-disabled");
                        cardFooter.Add(badge);
                    }

                    card.Add(cardFooter);

                    // 点击事件：不可用则忽略，若是当前则直接关闭，否则装备并关闭
                    string capturedId = opt.Id;
                    card.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (isDuplicate) return;
                        if (targetSlots[slotIndex] != capturedId)
                        {
                            targetSlots[slotIndex] = capturedId;
                            RefreshTalentSlots();
                            MarkDraftDirty();
                        }
                        _root.Remove(overlay);
                    });

                    grid.Add(card);
                }
            }

            // 绑定 Tab 点击事件
            tabAll.clicked += () =>
            {
                activeTierFilter = null;
                UpdateTabsActiveState();
                RefreshGrid();
            };

            tabLarge.clicked += () =>
            {
                if (slotTier < TalentTier.Large) return;
                activeTierFilter = TalentTier.Large;
                UpdateTabsActiveState();
                RefreshGrid();
            };

            tabMedium.clicked += () =>
            {
                if (slotTier < TalentTier.Medium) return;
                activeTierFilter = TalentTier.Medium;
                UpdateTabsActiveState();
                RefreshGrid();
            };

            tabSmall.clicked += () =>
            {
                activeTierFilter = TalentTier.Small;
                UpdateTabsActiveState();
                RefreshGrid();
            };

            // 绑定搜索输入事件
            searchField.RegisterValueChangedCallback(evt =>
            {
                currentQuery = evt.newValue?.Trim() ?? "";
                RefreshGrid();
            });

            // 点击背景关闭
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == overlay)
                    _root.Remove(overlay);
            });

            RefreshGrid();
            _root.Add(overlay);
        }

        private sealed class TalentPickerOption
        {
            public string Id;
            public string DisplayName;
            public string Description;
            public int Cost;
            public TalentTier Tier;
            public string TierName;
            public string TierClass;
        }

        private string[] GetTalentSlots(bool isReserve) => isReserve
            ? _currentTalents.ReserveTalentIds
            : _currentTalents.SlotTalentIds;

        private sealed class TalentSlotBinding
        {
            public int Index { get; }
            public bool IsReserve { get; }

            public TalentSlotBinding(int index, bool isReserve)
            {
                Index = index;
                IsReserve = isReserve;
            }
        }

        private void RefreshUI()
        {
            _currentConfig.CalculateAlienationScore();
            foreach (var refresh in _allItemRefreshers) refresh();
            RefreshTalentSlots();
            RefreshStats();
        }
    }
}

