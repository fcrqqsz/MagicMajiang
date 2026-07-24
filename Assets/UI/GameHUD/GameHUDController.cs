using System;
using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Systems;
using DG.Tweening;

namespace MahjongGame.UI
{
    public class GameHUDController : MonoBehaviour
    {
        public static GameHUDController Instance { get; private set; }

        [SerializeField] private UIDocument _document;

        private Label _infoLabel;
        private Label[] _windLabels = new Label[4];   // 0=底部(本地), 1=右, 2=上, 3=左
        private Label[] _scoreLabels = new Label[4];
        private VisualElement[] _glowElements = new VisualElement[4];
        private Label _timerText;
        private VisualElement[] _arcSegments = new VisualElement[4]; // top, right, bottom, left

        private int _activePlayerIndex = -1;
        private float _timerRemaining;
        private float _timerTotal;
        private bool _isTimerRunning;
        private Tweener _pulseTween;
        private string _infoPrefix;        // 缓存 "余" 之前的前缀
        private int _lastRemainingCount = -1; // 缓存上次余量，避免每帧拼字符串

        private static readonly string[] ModeNames = { "单局", "东风", "半庄", "全庄" };
        private static readonly string[] WindChars = { "", "东", "南", "西", "北" };

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            var root = _document.rootVisualElement;

            _infoLabel = root.Q<Label>("InfoLabel");

            // 座位: Bottom(0=自己), Right(1=下家), Top(2=对家), Left(3=上家)
            _windLabels[0] = root.Q<Label>("WindBottom");
            _windLabels[1] = root.Q<Label>("WindRight");
            _windLabels[2] = root.Q<Label>("WindTop");
            _windLabels[3] = root.Q<Label>("WindLeft");

            _scoreLabels[0] = root.Q<Label>("ScoreBottom");
            _scoreLabels[1] = root.Q<Label>("ScoreRight");
            _scoreLabels[2] = root.Q<Label>("ScoreTop");
            _scoreLabels[3] = root.Q<Label>("ScoreLeft");

            _glowElements[0] = root.Q<VisualElement>("GlowBottom");
            _glowElements[1] = root.Q<VisualElement>("GlowRight");
            _glowElements[2] = root.Q<VisualElement>("GlowTop");
            _glowElements[3] = root.Q<VisualElement>("GlowLeft");

            // 进度条段: top, right, bottom, left
            _arcSegments[0] = root.Q<VisualElement>("ArcTop");
            _arcSegments[1] = root.Q<VisualElement>("ArcRight");
            _arcSegments[2] = root.Q<VisualElement>("ArcBottom");
            _arcSegments[3] = root.Q<VisualElement>("ArcLeft");

            _timerText = root.Q<Label>("TimerText");
        }

        void OnDestroy()
        {
            _pulseTween?.Kill();
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 更新左上角局信息 + 四家风位和分数
        /// </summary>
        public void UpdateRoundInfo(GameSession session)
        {
            if (session == null) return;

            string modeName = GetModeName(session.Mode);
            int remaining = IsNetworkRoom
                ? Mathf.Max(_lastRemainingCount, 0)
                : DeckManager.Instance != null ? DeckManager.Instance.RemainingCount : 0;

            if (session.Mode == GameMode.Single)
                _infoPrefix = $"{modeName}-";
            else
                _infoPrefix = $"{modeName}-{session.GetRoundLabel()}-";

            _lastRemainingCount = remaining;
            _infoLabel.text = $"{_infoPrefix}余{remaining}张";

            // 更新四家风位和分数
            for (int playerIdx = 0; playerIdx < 4; playerIdx++)
            {
                int slot = PlayerIndexToUISlot(playerIdx);
                WindDirection seatWind = session.GetSeatWind(playerIdx);
                _windLabels[slot].text = WindChars[(int)seatWind];
                _scoreLabels[slot].text = session.Scores[playerIdx].ToString();
            }
        }

        /// <summary>Applies the dedicated server's authoritative wall count for an online round.</summary>
        public void UpdateRemainingCount(int remainingCount)
        {
            _lastRemainingCount = Mathf.Max(remainingCount, 0);
            if (_infoLabel != null && _infoPrefix != null)
                _infoLabel.text = $"{_infoPrefix}余{_lastRemainingCount}张";
        }

        /// <summary>
        /// 启动倒计时
        /// </summary>
        public void StartTimer(float totalSeconds, int activePlayerIndex)
        {
            _timerTotal = totalSeconds;
            _timerRemaining = totalSeconds;
            _isTimerRunning = true;
            _timerText.text = Mathf.CeilToInt(totalSeconds).ToString();

            SetActivePlayer(activePlayerIndex);
            UpdateArcColor(1f);
            UpdateArcScale(1f);
        }

        /// <summary>
        /// 停止倒计时
        /// </summary>
        public void StopTimer()
        {
            _isTimerRunning = false;
        }

        /// <summary>Cancels transient HUD state before applying an authoritative recovery projection.</summary>
        public void ResetForRecovery()
        {
            StopTimer();
            _pulseTween?.Kill();
            _pulseTween = null;
            _timerText.text = string.Empty;
            SetActivePlayer(-1);
        }

        public void ApplyRecoverySnapshot(RoomGameSnapshot snapshot, GameSession session)
        {
            if (snapshot == null || session == null) return;
            ResetForRecovery();
            UpdateRoundInfo(session);
            UpdateRemainingCount(snapshot.remainingWallCount);

            var decision = snapshot.activeDecision;
            if (decision == null || decision.deadlineUnixMilliseconds <= 0) return;
            long remainingMilliseconds = decision.deadlineUnixMilliseconds - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (remainingMilliseconds <= 0) return;

            int activeSeat = decision.actingSeatIndex >= 0 ? decision.actingSeatIndex : decision.discardingSeatIndex;
            StartTimer(Mathf.Max(0.1f, remainingMilliseconds / 1000f), activeSeat);
        }

        void Update()
        {
            // 实时更新牌山余量（仅在数量变化时更新，避免每帧 GC）
            if (!IsNetworkRoom && DeckManager.Instance != null && _infoLabel != null && _infoPrefix != null)
            {
                int remaining = DeckManager.Instance.RemainingCount;
                if (remaining != _lastRemainingCount)
                {
                    _lastRemainingCount = remaining;
                    _infoLabel.text = $"{_infoPrefix}余{remaining}张";
                }
            }

            if (!_isTimerRunning) return;

            _timerRemaining -= Time.deltaTime;
            if (_timerRemaining < 0f) _timerRemaining = 0f;

            // 更新数字
            _timerText.text = Mathf.CeilToInt(_timerRemaining).ToString();

            // 更新进度比例
            float ratio = _timerTotal > 0 ? _timerRemaining / _timerTotal : 0f;
            UpdateArcScale(ratio);
            UpdateArcColor(ratio);
        }

        /// <summary>
        /// 切换活跃玩家高亮
        /// </summary>
        public void SetActivePlayer(int playerIndex)
        {
            // 停止之前的脉冲动画
            _pulseTween?.Kill();

            _activePlayerIndex = playerIndex;
            int activeSlot = playerIndex >= 0 ? PlayerIndexToUISlot(playerIndex) : -1;

            for (int slot = 0; slot < 4; slot++)
            {
                if (slot == activeSlot)
                {
                    _windLabels[slot].RemoveFromClassList("wind-label--inactive");
                    _windLabels[slot].AddToClassList("wind-label--active");
                    _scoreLabels[slot].AddToClassList("score-label--active");
                    _glowElements[slot].AddToClassList("glow--visible");

                    // 脉冲缩放动画
                    var label = _windLabels[slot];
                    label.style.scale = new Scale(Vector2.one);
                    _pulseTween = DOVirtual.Float(1f, 1.15f, 0.6f, v =>
                    {
                        label.style.scale = new Scale(new Vector2(v, v));
                    })
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine)
                    .SetLink(gameObject);
                }
                else
                {
                    _windLabels[slot].RemoveFromClassList("wind-label--active");
                    _windLabels[slot].AddToClassList("wind-label--inactive");
                    _scoreLabels[slot].RemoveFromClassList("score-label--active");
                    _glowElements[slot].RemoveFromClassList("glow--visible");
                    _windLabels[slot].style.scale = new Scale(Vector2.one);
                }
            }
        }

        /// <summary>
        /// 更新四家分数显示
        /// </summary>
        public void UpdateScores(int[] scores)
        {
            if (scores == null) return;
            for (int playerIdx = 0; playerIdx < 4 && playerIdx < scores.Length; playerIdx++)
            {
                int slot = PlayerIndexToUISlot(playerIdx);
                _scoreLabels[slot].text = scores[playerIdx].ToString();
            }
        }

        private void UpdateArcScale(float ratio)
        {
            // 水平进度条用 scaleX，垂直用 scaleY
            _arcSegments[0].style.scale = new Scale(new Vector2(ratio, 1f)); // top (水平)
            _arcSegments[1].style.scale = new Scale(new Vector2(1f, ratio)); // right (垂直)
            _arcSegments[2].style.scale = new Scale(new Vector2(ratio, 1f)); // bottom (水平)
            _arcSegments[3].style.scale = new Scale(new Vector2(1f, ratio)); // left (垂直)
        }

        private void UpdateArcColor(float ratio)
        {
            Color color;
            if (ratio > 0.5f)
                color = Color.Lerp(new Color(1f, 0.92f, 0f), new Color(0.3f, 0.81f, 0.31f), (ratio - 0.5f) * 2f); // 黄→绿
            else if (ratio > 0.25f)
                color = Color.Lerp(new Color(0.96f, 0.26f, 0.21f), new Color(1f, 0.92f, 0f), (ratio - 0.25f) * 4f); // 红→黄
            else
                color = new Color(0.96f, 0.26f, 0.21f); // 红

            for (int i = 0; i < 4; i++)
            {
                _arcSegments[i].style.backgroundColor = color;
            }
        }

        /// <summary>
        /// 玩家索引到 UI 槽位: 0→底部, 1→右, 2→上, 3→左
        /// </summary>
        private int PlayerIndexToUISlot(int playerIndex)
        {
            // 玩家0(本地)=底部(0), 1=右(1), 2=上(2), 3=左(3)
            int localSeat = NetworkManager.Instance?.RoomService?.SeatIndex ?? 0;
            return (playerIndex - localSeat + 4) % 4;
        }

        private bool IsNetworkRoom => NetworkManager.Instance?.RoomService?.HasRoom == true;

        private string GetModeName(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Single: return ModeNames[0];
                case GameMode.EastOnly: return ModeNames[1];
                case GameMode.HalfGame: return ModeNames[2];
                case GameMode.FullGame: return ModeNames[3];
                default: return "未知";
            }
        }
    }
}
