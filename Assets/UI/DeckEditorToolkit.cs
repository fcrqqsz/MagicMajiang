using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using MahjongGame.Core.Network.Data;
using MahjongGame.Systems;

namespace MahjongGame.UI
{
    public class DeckEditorToolkit : MonoBehaviour
    {
        private const int MAX_DECKS = 5;

        [Header("UI Document")]
        [SerializeField] private UIDocument _document;

        [Header("Assets")]
        [SerializeField] private VisualTreeAsset _itemTemplate;
        [SerializeField] private StyleSheet _styleSheet;

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
            GenerateRows();
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

                var btnDelete = new Button(() => OnDeleteDeckClicked(index)) { text = "✕" };
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
            _savedDecks[_selectedDeckIndex].AlienationScore = _currentConfig.AlienationScore;

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
            _totalText.text = $"Total: {total} / 34";
            _scoreText.text = $"Alienation: {_currentConfig.AlienationScore}";
            _totalText.EnableInClassList("text-green", total == 34);
            _totalText.EnableInClassList("text-white", total != 34);
            _btnSave.SetEnabled(total == 34);
        }

        private void RefreshUI()
        {
            _currentConfig.CalculateAlienationScore();
            foreach (var refresh in _allItemRefreshers) refresh();
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
    }
}
