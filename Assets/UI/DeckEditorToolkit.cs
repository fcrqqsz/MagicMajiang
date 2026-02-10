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

        // UI 元素引用
        private VisualElement _root;
        private VisualElement _gridContainer;
        private Label _totalText;
        private Label _scoreText;
        private Button _btnStart;

        // 数据
        private DeckConfig _currentConfig;

        void OnEnable()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            
            // 获取根节点
            _root = _document.rootVisualElement;
            
            // 加载样式表 (保险起见)
            if (_styleSheet != null && !_root.styleSheets.Contains(_styleSheet))
                _root.styleSheets.Add(_styleSheet);

            // 查找组件 (对应 UXML 中的 name)
            _gridContainer = _root.Q<VisualElement>("GridContainer");
            _totalText = _root.Q<Label>("TotalText");
            _scoreText = _root.Q<Label>("ScoreText");
            _btnStart = _root.Q<Button>("BtnStart");

            // 绑定开始按钮
            _btnStart.clicked += OnStartGameClicked;

            // 初始化数据
            InitializeEditor();
        }

        private void InitializeEditor()
        {
            // _currentConfig = new DeckConfig();
            _currentConfig = DeckConfig.CreateStandard();
            GenerateGrid();
            RefreshStats();
        }

        private void GenerateGrid()
        {
            _gridContainer.Clear(); // 清空

            foreach (Suit suit in System.Enum.GetValues(typeof(Suit)))
            {
                int maxVal = (suit == Suit.Wind) ? 4 : (suit == Suit.Dragon ? 3 : 9);
                for (int val = 1; val <= maxVal; val++)
                {
                    CreateTileItem(suit, val);
                }
            }
        }

        private void CreateTileItem(Suit suit, int value)
        {
            // 1. 从模板克隆 UI 树
            TemplateContainer instance = _itemTemplate.Instantiate();
            
            // 2. 获取内部组件
            var faceLabel = instance.Q<Label>("FaceLabel");
            var countLabel = instance.Q<Label>("CountLabel");
            var btnPlus = instance.Q<Button>("BtnPlus");
            var btnMinus = instance.Q<Button>("BtnMinus");

            // 3. 初始化显示
            faceLabel.text = GetTileName(suit, value);
            
            // 4. 绑定事件 (使用闭包捕获 suit 和 value)
            // 注意：这里定义局部刷新函数，避免重绘整个 Grid
            Action updateLocalUI = () => 
            {
                int count = _currentConfig.GetCardCount(suit, value);
                countLabel.text = count.ToString();
                
                // 样式切换：有数字变绿，没数字变灰
                if (count > 0) countLabel.AddToClassList("active");
                else countLabel.RemoveFromClassList("active");

                RefreshStats(); // 刷新顶部的总分
            };

            btnPlus.clicked += () => 
            {
                int current = _currentConfig.GetCardCount(suit, value);
                // 可以在这里加单卡上限判断
                _currentConfig.SetCardCount(suit, value, current + 1);
                _currentConfig.CalculateAlienationScore(); // 重新计算分值
                updateLocalUI();
            };

            btnMinus.clicked += () => 
            {
                int current = _currentConfig.GetCardCount(suit, value);
                if (current > 0)
                {
                    _currentConfig.SetCardCount(suit, value, current - 1);
                    _currentConfig.CalculateAlienationScore();
                    updateLocalUI();
                }
            };

            // 初始刷新一次
            updateLocalUI();

            // 5. 加入 Grid
            _gridContainer.Add(instance);
        }

        private void RefreshStats()
        {
            // 获取总数 (假设你在 DeckConfig 加了 TotalCount 属性，或者用 Values.Sum())
            // int total = _currentConfig.TotalCount; 
            int total = _currentConfig.GenerateTiles(0).Count; // 临时替代方案

            _totalText.text = $"Total: {total} / 34";
            _scoreText.text = $"Alienation: {_currentConfig.AlienationScore}";

            // 样式反馈
            _totalText.EnableInClassList("text-green", total == 34);
            _totalText.EnableInClassList("text-white", total != 34); // 简单处理，实际可用更复杂的逻辑
            
            // 按钮状态
            _btnStart.SetEnabled(total == 34);
        }

        // 在 DeckEditorToolkit.cs 中

        private void OnStartGameClicked()
        {
            // 1. 隐藏编辑器界面
            // 如果是 GameObject 挂载方式:
            gameObject.SetActive(false);
            
            // 如果是纯 UI Document 方式 (也就是挂在 UIDocument 组件上):
            // _document.rootVisualElement.style.display = DisplayStyle.None;

            // 2. 传递数据给 GameManager
            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartGameWithConfig(_currentConfig);
            }
            else
            {
                Debug.LogError("场景中找不到 GameManager!");
            }
        }

        private string GetTileName(Suit s, int v)
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