using System;
using System.Collections.Generic;
using System.Linq; // 确保引用 Linq
using UnityEngine;
using UnityEngine.UIElements; // UI Toolkit 命名空间
using MahjongGame.Core;

namespace MahjongGame.UI
{
    public class DeckEditorToolkit : MonoBehaviour
    {
        [Header("UI Document")]
        // 挂载 UIDocument 组件的物体
        [SerializeField] private UIDocument _document;
        
        [Header("Assets")]
        // 拖入 TileItemTemplate.uxml
        [SerializeField] private VisualTreeAsset _itemTemplate; 
        // 拖入 DeckEditorStyles.uss (可选，如果UXML里没引用的化)
        [SerializeField] private StyleSheet _styleSheet;

        // 当牌库编辑完成并点击保存时触发
        public event Action<DeckConfig> OnDeckSaved;

        // UI 元素引用
        private VisualElement _root;
        private VisualElement _mainGrid;
        private Label _totalText;
        private Label _scoreText;
        private Button _btnStart;
        private Button _btnClearAll;
        private Button _btnResetAll;

        // 数据
        private DeckConfig _currentConfig;
        
        // 存储所有的刷新函数，方便批量更新
        private List<Action> _allItemRefreshers = new List<Action>();

        void OnEnable()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            
            // 获取根节点
            _root = _document.rootVisualElement;
            
            // 加载样式表 (保险起见)
            if (_styleSheet != null && !_root.styleSheets.Contains(_styleSheet))
                _root.styleSheets.Add(_styleSheet);

            // 查找组件 (对应 UXML 中的 name)
            _mainGrid = _root.Q<VisualElement>("MainGrid");
            _totalText = _root.Q<Label>("TotalText");
            _scoreText = _root.Q<Label>("ScoreText");
            _btnStart = _root.Q<Button>("BtnStart");
            _btnClearAll = _root.Q<Button>("BtnClearAll");
            _btnResetAll = _root.Q<Button>("BtnResetAll");

            // 绑定事件
            _btnStart.clicked += OnStartGameClicked;
            _btnClearAll.clicked += () => BatchUpdateDeck(0);
            _btnResetAll.clicked += () => BatchUpdateDeck(1);

            // 初始化数据
            InitializeEditor();
        }

        private void InitializeEditor()
        {
            _currentConfig = DeckConfig.CreateStandard();
            GenerateRows();
            RefreshStats();
        }

        private void GenerateRows()
        {
            _mainGrid.Clear();
            _allItemRefreshers.Clear();

            // 定义 4 个分组：万、筒、索、字 (风+箭)
            CreateSuitRow("万", Suit.Man);
            CreateSuitRow("筒", Suit.Pin);
            CreateSuitRow("索", Suit.Sou);
            
            // 字牌比较特殊，包含 Wind 和 Dragon
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

            // 右侧按钮
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
            
            // 添加风牌
            for (int v = 1; v <= 4; v++) grid.Add(CreateTileItem(Suit.Wind, v));
            // 添加箭牌
            for (int v = 1; v <= 3; v++) grid.Add(CreateTileItem(Suit.Dragon, v));
            
            row.Add(grid);

            // 右侧按钮
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

            // 加载并设置牌面图片
            string imagePath = GetTileImagePath(suit, value);
            Sprite tileSprite = Resources.Load<Sprite>(imagePath);
            if (tileSprite != null)
            {
                faceImage.style.backgroundImage = new StyleBackground(tileSprite);
            }
            else
            {
                // 如果找不到图片，为了调试方便，可以显示文字
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
            _btnStart.SetEnabled(total == 34);
        }

        private void OnStartGameClicked()
        {
            OnDeckSaved?.Invoke(_currentConfig);
        }

        public void LoadConfig(DeckConfig config)
        {
            if (config != null)
            {
                _currentConfig = config;
            }
            else
            {
                _currentConfig = DeckConfig.CreateStandard();
            }
            RefreshUI();
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
                case Suit.Wind: suffix = "z"; break; // 1-4 对应 东南西北
                case Suit.Dragon:
                    suffix = "z";
                    // 资源命名: 7=中, 6=发, 5=白
                    // 枚举值:   1=中, 2=发, 3=白
                    switch (value)
                    {
                        case 1: valueStr = "7"; break; // 中
                        case 2: valueStr = "6"; break; // 发
                        case 3: valueStr = "5"; break; // 白
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