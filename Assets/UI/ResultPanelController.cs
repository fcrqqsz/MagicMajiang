using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement; // 用于重启场景
using System.Collections.Generic;
using System.Collections; // 添加以支持 Coroutine

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

        public void ShowDraw(List<string> playerStatuses = null)
        {
            // 1. 设置标题
            _titleLabel.text = "流  局";
            _titleLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f)); // 灰色

            // 2. 清空列表
            _listContainer.Clear();
            
            if (playerStatuses != null && playerStatuses.Count > 0)
            {
                foreach (var status in playerStatuses)
                {
                    Label item = new Label(status);
                    item.AddToClassList("fan-item");
                    _listContainer.Add(item);
                }
            }
            else
            {
                Label info = new Label("牌山已空，无人胡牌");
                info.AddToClassList("fan-item");
                _listContainer.Add(info);
            }

            // 3. 隐藏总分
            _totalLabel.text = "";

            // 4. 显示
            _overlay.style.display = DisplayStyle.Flex;
            Invoke(nameof(FadeIn), 0.05f);
        }

        public void ShowLose(int aiId, int totalFan, List<string> fanDetails)
        {
            // 1. 设置标题
            _titleLabel.text = $"玩家 {aiId} 胡牌";
            _titleLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.8f)); // 蓝紫色代表AI赢

            // 2. 清空旧列表
            _listContainer.Clear();

            // 3. 填充番种详情
            foreach (var detail in fanDetails)
            {
                Label item = new Label(detail);
                item.AddToClassList("fan-item");
                _listContainer.Add(item);
            }

            // 4. 隐藏不需要的玩家专属分
            _totalLabel.text = $"被扣除：{totalFan} 番";

            // 5. 显示动画
            _overlay.style.display = DisplayStyle.Flex;
            Invoke(nameof(FadeIn), 0.05f);
        }

        public void ShowWin(int totalFan, List<string> fanDetails, bool isTsumo)
        {
            // 1. 设置标题
            _titleLabel.text = isTsumo ? "自  摸" : "荣  胡";
            _titleLabel.style.color = new StyleColor(new Color(1f, 0.26f, 0.26f)); // 恢复红色

            // 2. 清空旧列表
            _listContainer.Clear();
            _totalLabel.text = "合计：0 番";

            // 3. 显示动画与数据
            _overlay.style.display = DisplayStyle.Flex;

            // 开启滚动计分协程
            StartCoroutine(RollScoreRoutine(fanDetails));
            
            // 延迟一帧加 class 以触发 transition 动画
            Invoke(nameof(FadeIn), 0.05f);
        }

        private struct FanItemData
        {
            public string FullText;
            public int Score;
        }

        private IEnumerator RollScoreRoutine(List<string> fanDetails)
        {
            List<FanItemData> parsedDetails = new List<FanItemData>();

            // 解析每个字符串，提取番数
            foreach (var detail in fanDetails)
            {
                int score = 0;
                int startIdx = detail.LastIndexOf('(');
                int endIdx = detail.LastIndexOf(')');
                if (startIdx >= 0 && endIdx > startIdx)
                {
                    string numStr = detail.Substring(startIdx + 1, endIdx - startIdx - 1);
                    int.TryParse(numStr, out score);
                }
                
                parsedDetails.Add(new FanItemData { FullText = detail, Score = score });
            }

            // 从高往低排序
            parsedDetails.Sort((a, b) => b.Score.CompareTo(a.Score));

            int currentTotal = 0;

            // 逐个显示并增加番数
            foreach (var item in parsedDetails)
            {
                // 在列表中添加该番种
                Label label = new Label(item.FullText);
                label.AddToClassList("fan-item");
                _listContainer.Add(label);

                // 数字滚动效果
                int targetTotal = currentTotal + item.Score;
                float rollDuration = 0.2f;
                float elapsed = 0f;

                while (elapsed < rollDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / rollDuration;
                    int tempTotal = Mathf.RoundToInt(Mathf.Lerp(currentTotal, targetTotal, t));
                    _totalLabel.text = $"合计：{tempTotal} 番";
                    yield return null;
                }

                currentTotal = targetTotal;
                _totalLabel.text = $"合计：{currentTotal} 番";

                // 停顿0.5秒
                yield return new WaitForSeconds(0.5f);
            }
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