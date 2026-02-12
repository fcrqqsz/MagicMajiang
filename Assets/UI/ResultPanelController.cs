using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement; // 用于重启场景
using System.Collections.Generic;

namespace MahjongGame.UI
{
    public class ResultPanelController : MonoBehaviour
    {
        public static ResultPanelController Instance;

        [SerializeField] private UIDocument _document;
        private VisualElement _overlay;
        private Label _titleLabel;
        private ScrollView _listContainer;
        private Label _totalLabel;
        private Button _btnRestart;

        void Awake()
        {
            Instance = this;
            var root = _document.rootVisualElement;

            _overlay = root.Q<VisualElement>("Overlay");
            _titleLabel = root.Q<Label>("TitleLabel");
            _listContainer = root.Q<ScrollView>("FanListContainer");
            _totalLabel = root.Q<Label>("TotalScoreLabel");
            _btnRestart = root.Q<Button>("BtnRestart");

            _btnRestart.clicked += OnRestartClicked;

            // 初始隐藏
            _overlay.style.display = DisplayStyle.None;
        }

        public void ShowDraw()
        {
            // 1. 设置标题
            _titleLabel.text = "流  局";

            // 2. 清空列表
            _listContainer.Clear();
            Label info = new Label("牌山已空，无人胡牌");
            info.AddToClassList("fan-item");
            _listContainer.Add(info);

            // 3. 隐藏总分
            _totalLabel.text = "";

            // 4. 显示
            _overlay.style.display = DisplayStyle.Flex;
            Invoke(nameof(FadeIn), 0.05f);
        }

        public void ShowWin(int totalFan, List<string> fanDetails, bool isTsumo)
        {
            // 1. 设置标题
            _titleLabel.text = isTsumo ? "自  摸" : "荣  胡";

            // 2. 清空旧列表
            _listContainer.Clear();

            // 3. 填充番种详情
            foreach (var detail in fanDetails)
            {
                // 格式通常是 "断幺九(2)"
                // 我们把它拆开显示，或者直接显示
                Label item = new Label(detail);
                item.AddToClassList("fan-item"); // 使用 USS 样式
                _listContainer.Add(item);
            }

            // 4. 设置总分
            _totalLabel.text = $"合计：{totalFan} 番";

            // 5. 显示动画
            _overlay.style.display = DisplayStyle.Flex;
            
            // 延迟一帧加 class 以触发 transition 动画
            Invoke(nameof(FadeIn), 0.05f);
        }

        private void FadeIn()
        {
            _overlay.AddToClassList("overlay--visible");
        }

        private void OnRestartClicked()
        {
            // 简单粗暴：重载当前场景
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}