using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using MahjongGame.Core.Network.Data;
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
        private Label _scoreText;
        private Button _btnSave;
        private Button _btnExit;
        private Button _btnClearAll;
        private Button _btnResetAll;

        // Sidebar 引用
        private VisualElement _deckListContainer;
        private Button _btnNewDeck;

        // Talent section
        private VisualElement _talentSlotsContainer;
        private TalentSlotConfig _currentTalents;

        // 数据
        private DeckConfig _currentConfig;
        private List<SavedDeck> _savedDecks;
        private int _selectedDeckIndex;

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
            _scoreText = _root.Q<Label>("ScoreText");
            _btnSave = _root.Q<Button>("BtnSave");
            _btnExit = _root.Q<Button>("BtnExit");
            _btnClearAll = _root.Q<Button>("BtnClearAll");
            _btnResetAll = _root.Q<Button>("BtnResetAll");

            // Sidebar
            _deckListContainer = _root.Q<VisualElement>("DeckListContainer");
            _btnNewDeck = _root.Q<Button>("BtnNewDeck");

            // 绑定事件
            _btnSave.clicked += OnSaveClicked;
            _btnExit.clicked += OnExitClicked;
            _btnClearAll.clicked += OnClearAllClicked;
            _btnResetAll.clicked += OnResetAllClicked;
            _btnNewDeck.clicked += OnNewDeckClicked;

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
        }

        private void OnClearAllClicked() => BatchUpdateDeck(0);
        private void OnResetAllClicked() => BatchUpdateDeck(1);

        /// <summary>
        /// 切换到 Workshop 时由 LobbyController 调用，刷新卡组列表和当前选中项
        /// </summary>
        public void RefreshDeckList()
        {
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
                var scoreLabel = new Label($"异化值: {deck.AlienationScore}");
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
                            _selectedDeckIndex = index;
                            SelectDeck(index);
                            UpdateSelectedHighlight();
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

            int num = _savedDecks.Count + 1;
            var newDeck = new SavedDeck
            {
                DeckId = Guid.NewGuid().ToString(),
                DeckName = $"卡组 {num}",
                AlienationScore = 0,
                Config = DeckConfig.CreateStandard()
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

            _savedDecks.RemoveAt(index);
            ProfileManager.Instance?.SaveProfile();

            if (_selectedDeckIndex >= _savedDecks.Count)
                _selectedDeckIndex = _savedDecks.Count - 1;
            else if (_selectedDeckIndex > index)
                _selectedDeckIndex--;

            SelectDeck(_selectedDeckIndex);
            RebuildDeckList();
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
            if (_selectedDeckIndex < 0 || _selectedDeckIndex >= _savedDecks.Count) return;

            // Write current config back to saved deck
            string json = JsonUtility.ToJson(_currentConfig);
            _savedDecks[_selectedDeckIndex].Config = JsonUtility.FromJson<DeckConfig>(json);

            // Save talent config
            string talentJson = JsonUtility.ToJson(_currentTalents);
            _savedDecks[_selectedDeckIndex].Talents = JsonUtility.FromJson<TalentSlotConfig>(talentJson);

            _savedDecks[_selectedDeckIndex].AlienationScore = DeckConfig.CalculateTotalAlienation(_currentConfig, _currentTalents);

            // 记录选中的卡组索引
            if (ProfileManager.Instance?.CurrentProfile != null)
                ProfileManager.Instance.CurrentProfile.SelectedDeckIndex = _selectedDeckIndex;

            ProfileManager.Instance?.SaveProfile();
            RebuildDeckList();

            OnDeckSaved?.Invoke(_currentConfig);
        }

        private void OnExitClicked()
        {
            OnExitRequested?.Invoke();
        }

        public void LoadConfig(DeckConfig config)
        {
            if (config != null)
                _currentConfig = config;
            else
                _currentConfig = DeckConfig.CreateStandard();
            RefreshUI();
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
                BatchUpdateSuit(Suit.Wind, 0, false);
                BatchUpdateSuit(Suit.Dragon, 0, true);
            }) { text = "清空" };
            btnClear.AddToClassList("control-btn");

            Button btnReset = new Button(() => {
                BatchUpdateSuit(Suit.Wind, 1, false);
                BatchUpdateSuit(Suit.Dragon, 1, true);
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
                RefreshStats();
            };

            btnMinus.clicked += () =>
            {
                int current = _currentConfig.GetCardCount(suit, value);
                if (current > 0)
                {
                    _currentConfig.SetCardCount(suit, value, current - 1);
                    _currentConfig.CalculateAlienationScore();
                    updateLocalUI();
                    RefreshStats();
                }
            };

            _allItemRefreshers.Add(updateLocalUI);
            updateLocalUI();
            return instance;
        }

        private void BatchUpdateSuit(Suit suit, int count, bool refreshAll = true)
        {
            int maxVal = (suit == Suit.Wind) ? 4 : (suit == Suit.Dragon ? 3 : 9);
            for (int v = 1; v <= maxVal; v++) _currentConfig.SetCardCount(suit, v, count);

            if (refreshAll)
            {
                _currentConfig.CalculateAlienationScore();
                foreach (var refresh in _allItemRefreshers) refresh();
                RefreshStats();
            }
        }

        private void BatchUpdateDeck(int count)
        {
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            {
                int maxVal = (suit == Suit.Wind) ? 4 : (suit == Suit.Dragon ? 3 : 9);
                for (int v = 1; v <= maxVal; v++) _currentConfig.SetCardCount(suit, v, count);
            }
            _currentConfig.CalculateAlienationScore();
            foreach (var refresh in _allItemRefreshers) refresh();
            RefreshStats();
        }

        private void RefreshStats()
        {
            int total = _currentConfig.GenerateTiles(0).Count;
            int totalAlienation = DeckConfig.CalculateTotalAlienation(_currentConfig, _currentTalents);
            _totalText.text = $"Total: {total} / 34";
            _scoreText.text = $"Alienation: {totalAlienation}";
            _totalText.EnableInClassList("text-green", total == 34);
            _totalText.EnableInClassList("text-white", total != 34);
            _btnSave.SetEnabled(total == 34);
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
        // 分隔线插入位置：index 1 (大|中) 和 index 3 (中|小)
        private static readonly HashSet<int> SeparatorBeforeSlot = new HashSet<int> { 1, 3 };

        private Label _talentDetailLabel; // 天赋详情区域

        private void GenerateTalentSlots()
        {
            // 防止重复创建
            if (_talentSlotsContainer != null)
            {
                _talentSlotsContainer.RemoveFromHierarchy();
                _talentSlotsContainer = null;
            }

            // 在 MainGrid 下方创建天赋区域
            _talentSlotsContainer = new VisualElement();
            _talentSlotsContainer.name = "TalentSlotsSection";
            _talentSlotsContainer.AddToClassList("talent-section");

            var header = new Label("天赋槽");
            header.AddToClassList("talent-section-header");
            _talentSlotsContainer.Add(header);

            var slotsRow = new VisualElement();
            slotsRow.style.flexDirection = FlexDirection.Row;
            slotsRow.style.flexWrap = Wrap.NoWrap;
            slotsRow.style.justifyContent = Justify.Center;
            slotsRow.style.alignItems = Align.Stretch;

            for (int i = 0; i < 6; i++)
            {
                int slotIndex = i;

                // 在品阶分组之间插入竖线分隔
                if (SeparatorBeforeSlot.Contains(slotIndex))
                {
                    var separator = new VisualElement();
                    separator.style.width = 2;
                    separator.style.backgroundColor = new Color(0.4f, 0.4f, 0.5f, 0.8f);
                    separator.style.marginLeft = 4;
                    separator.style.marginRight = 4;
                    separator.style.alignSelf = Align.Stretch;
                    slotsRow.Add(separator);
                }

                VisualElement slot;

                if (_talentSlotTemplate != null)
                {
                    var instance = _talentSlotTemplate.Instantiate();
                    slot = instance.Q<VisualElement>("Root") ?? instance;
                    var tierLabel = instance.Q<Label>("TierLabel");
                    if (tierLabel != null) tierLabel.text = SlotTierLabels[slotIndex];
                }
                else
                {
                    slot = new VisualElement();
                    slot.AddToClassList("talent-slot");
                    var tierLabel = new Label(SlotTierLabels[slotIndex]);
                    tierLabel.AddToClassList("talent-slot-tier");
                    slot.Add(tierLabel);
                    var nameLabel = new Label("空");
                    nameLabel.name = "NameLabel";
                    nameLabel.AddToClassList("talent-slot-name");
                    slot.Add(nameLabel);
                    var btnClear = new Button() { text = "×" };
                    btnClear.name = "BtnClear";
                    btnClear.AddToClassList("talent-slot-clear");
                    slot.Add(btnClear);
                }

                var tierClass = slotIndex == 0 ? "tier-large" : (slotIndex <= 2 ? "tier-medium" : "tier-small");
                slot.AddToClassList(tierClass);

                // Clear button (must register before slot click to stop propagation)
                var clearBtn = slot.Q<Button>("BtnClear");
                if (clearBtn != null)
                {
                    clearBtn.RegisterCallback<ClickEvent>(evt =>
                    {
                        evt.StopPropagation();
                        _currentTalents.SlotTalentIds[slotIndex] = null;
                        RefreshTalentSlots();
                        RefreshStats();
                    });
                }

                // Click to open talent picker
                slot.RegisterCallback<ClickEvent>(evt =>
                {
                    ShowTalentPicker(slotIndex);
                });

                slotsRow.Add(slot);
            }

            _talentSlotsContainer.Add(slotsRow);

            // 天赋详情区域
            _talentDetailLabel = new Label();
            _talentDetailLabel.name = "TalentDetailLabel";
            _talentDetailLabel.style.marginTop = 8;
            _talentDetailLabel.style.paddingTop = 6;
            _talentDetailLabel.style.paddingBottom = 6;
            _talentDetailLabel.style.paddingLeft = 10;
            _talentDetailLabel.style.paddingRight = 10;
            _talentDetailLabel.style.fontSize = 12;
            _talentDetailLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            _talentDetailLabel.style.whiteSpace = WhiteSpace.Normal;
            _talentDetailLabel.style.display = DisplayStyle.None;
            _talentSlotsContainer.Add(_talentDetailLabel);

            _mainGrid.parent?.Add(_talentSlotsContainer);
        }

        private void RefreshTalentSlots()
        {
            if (_talentSlotsContainer == null) return;

            var slots = _talentSlotsContainer.Query(className: "talent-slot").ToList();
            bool hasAnyEquipped = false;

            for (int i = 0; i < Mathf.Min(slots.Count, 6); i++)
            {
                var slot = slots[i];
                var nameLabel = slot.Q<Label>("NameLabel");
                string talentId = _currentTalents?.SlotTalentIds[i];
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
                    for (int i = 0; i < 6; i++)
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
                    _talentDetailLabel.text = string.Join("\n", lines);
                    _talentDetailLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _talentDetailLabel.style.display = DisplayStyle.None;
                }
            }
        }

        private void ShowTalentPicker(int slotIndex)
        {
            var allIds = TalentRegistry.Instance.GetAllIds();
            TalentTier slotTier = TalentSlotConfig.GetSlotTier(slotIndex);

            // 过滤：品阶匹配 + 排除其他槽位已装备的（当前槽位的不排除，允许替换）
            string currentSlotTalent = _currentTalents.SlotTalentIds[slotIndex];
            var equippedInOtherSlots = new HashSet<string>();
            for (int i = 0; i < 6; i++)
            {
                if (i == slotIndex) continue;
                var tid = _currentTalents.SlotTalentIds[i];
                if (!string.IsNullOrEmpty(tid)) equippedInOtherSlots.Add(tid);
            }

            var available = allIds.Where(id =>
            {
                var tier = TalentRegistry.Instance.GetTier(id);
                return tier <= slotTier && !equippedInOtherSlots.Contains(id);
            }).ToList();

            if (available.Count == 0)
            {
                Debug.Log("[DeckEditor] 没有可用的天赋");
                return;
            }

            // 创建弹出选择列表
            var overlay = new VisualElement();
            overlay.name = "TalentPickerOverlay";
            overlay.AddToClassList("talent-picker-overlay");

            var panel = new VisualElement();
            panel.AddToClassList("talent-picker-panel");

            var title = new Label($"选择天赋 — {SlotTierLabels[slotIndex]}槽位");
            title.AddToClassList("talent-picker-title");
            panel.Add(title);

            var scrollView = new ScrollView();
            scrollView.AddToClassList("talent-picker-scroll");

            // "清空此槽位" 选项（如果当前已装备）
            if (!string.IsNullOrEmpty(currentSlotTalent))
            {
                var clearItem = new Button(() =>
                {
                    _currentTalents.SlotTalentIds[slotIndex] = null;
                    RefreshTalentSlots();
                    RefreshStats();
                    _root.Remove(overlay);
                });
                clearItem.text = "清空此槽位";
                clearItem.AddToClassList("talent-picker-clear");
                scrollView.Add(clearItem);
            }

            foreach (var id in available)
            {
                string displayName = TalentRegistry.Instance.GetDisplayName(id);
                string desc = TalentRegistry.Instance.GetDescription(id);
                int cost = TalentRegistry.Instance.GetCost(id);
                var tier = TalentRegistry.Instance.GetTier(id);
                string tierName = tier == TalentTier.Large ? "大" : (tier == TalentTier.Medium ? "中" : "小");
                string tierClass = tier == TalentTier.Large ? "tier-large" : (tier == TalentTier.Medium ? "tier-medium" : "tier-small");
                bool isCurrent = id == currentSlotTalent;

                var item = new VisualElement();
                item.AddToClassList("talent-picker-item");
                if (isCurrent) item.AddToClassList("current-equipped");

                var nameLabel = new Label(displayName);
                nameLabel.AddToClassList("talent-picker-item-name");
                item.Add(nameLabel);

                var descLabel = new Label(desc);
                descLabel.AddToClassList("talent-picker-item-desc");
                item.Add(descLabel);

                var metaRow = new VisualElement();
                metaRow.AddToClassList("talent-picker-item-meta");

                var tierLabel = new Label($"[{tierName}]");
                tierLabel.AddToClassList("talent-picker-item-tier");
                tierLabel.AddToClassList(tierClass);
                metaRow.Add(tierLabel);

                var costLabel = new Label($"异化值 +{cost}");
                costLabel.AddToClassList("talent-picker-item-cost");
                metaRow.Add(costLabel);

                item.Add(metaRow);

                item.RegisterCallback<ClickEvent>(evt =>
                {
                    _currentTalents.SlotTalentIds[slotIndex] = id;
                    RefreshTalentSlots();
                    RefreshStats();
                    _root.Remove(overlay);
                });

                scrollView.Add(item);
            }

            panel.Add(scrollView);

            var btnCancel = new Button(() => _root.Remove(overlay)) { text = "取消" };
            btnCancel.AddToClassList("talent-picker-cancel");
            panel.Add(btnCancel);

            overlay.Add(panel);

            // Click overlay background to close
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == overlay)
                    _root.Remove(overlay);
            });

            _root.Add(overlay);
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

