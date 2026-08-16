using System;
using System.Collections.Generic;
using System.Linq;
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
        [SerializeField] private VisualTreeAsset _talentChipTemplate;
        [SerializeField] private AudioClip _genericActiveTalentClip;
        [SerializeField] private AudioSource _talentAudioSource;

        private Label _infoLabel;
        private Label[] _windLabels = new Label[4];   // 0=底部(本地), 1=右, 2=上, 3=左
        private Label[] _scoreLabels = new Label[4];
        private VisualElement[] _glowElements = new VisualElement[4];
        private Label _timerText;
        private VisualElement[] _arcSegments = new VisualElement[4]; // top, right, bottom, left
        private VisualElement _root;
        private VisualElement _ownTalentBar;
        private Button _ownTalentCollapsedButton;
        private VisualElement _ownTalentDrawer;
        private readonly VisualElement[] _seatTalentRows = new VisualElement[4];
        private readonly Button[] _seatTalentMoreButtons = new Button[4];
        private readonly Action[] _seatTalentMoreClicked = new Action[4];
        private readonly VisualElement[] _seatTalentDrawers = new VisualElement[4];
        private Button _talentDrawerDismissLayer;
        private Action _ownTalentCollapsedClicked;
        private Action _talentDrawerDismissClicked;
        private VisualElement _talentEffectFeed;
        private Label _talentToast;
        private VisualElement _expandedTalentDrawer;
        private IVisualElementScheduledItem _toastHideSchedule;
        private RemoteServerProxy _serverProxy;
        private RoomGameSnapshot _talentSnapshot;
        private readonly List<TalentRuntimeEventMessage> _acceptedPublicTalentEvents = new List<TalentRuntimeEventMessage>();
        private readonly TalentFeedbackHistory _talentFeedbackHistory = new TalentFeedbackHistory();
        private readonly TalentTransientPresentationState _talentTransientState = new TalentTransientPresentationState();
        private Tweener _talentChipTween;
        private Tweener _talentToastTween;
        private VisualElement _talentPulsedChip;
        private bool _missingAudioWarningLogged;
        private bool _missingTemplateWarningLogged;

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

            _root = _document.rootVisualElement;

            _infoLabel = _root.Q<Label>("InfoLabel");

            // 座位: Bottom(0=自己), Right(1=下家), Top(2=对家), Left(3=上家)
            _windLabels[0] = _root.Q<Label>("WindBottom");
            _windLabels[1] = _root.Q<Label>("WindRight");
            _windLabels[2] = _root.Q<Label>("WindTop");
            _windLabels[3] = _root.Q<Label>("WindLeft");

            _scoreLabels[0] = _root.Q<Label>("ScoreBottom");
            _scoreLabels[1] = _root.Q<Label>("ScoreRight");
            _scoreLabels[2] = _root.Q<Label>("ScoreTop");
            _scoreLabels[3] = _root.Q<Label>("ScoreLeft");

            _glowElements[0] = _root.Q<VisualElement>("GlowBottom");
            _glowElements[1] = _root.Q<VisualElement>("GlowRight");
            _glowElements[2] = _root.Q<VisualElement>("GlowTop");
            _glowElements[3] = _root.Q<VisualElement>("GlowLeft");

            // 进度条段: top, right, bottom, left
            _arcSegments[0] = _root.Q<VisualElement>("ArcTop");
            _arcSegments[1] = _root.Q<VisualElement>("ArcRight");
            _arcSegments[2] = _root.Q<VisualElement>("ArcBottom");
            _arcSegments[3] = _root.Q<VisualElement>("ArcLeft");

            _timerText = _root.Q<Label>("TimerText");
            BindTalentElements();
        }

        void OnDestroy()
        {
            UnbindTalentElementCallbacks();
            UnbindServerProxy(_serverProxy);
            _toastHideSchedule?.Pause();
            _toastHideSchedule = null;
            _pulseTween?.Kill();
            ResetTalentChipPulse();
            _talentToastTween?.Kill();
            _talentToastTween = null;
            if (Instance == this) Instance = null;
        }

        private void BindTalentElements()
        {
            _ownTalentBar = _root.Q<VisualElement>("OwnTalentBar");
            _ownTalentCollapsedButton = _root.Q<Button>("OwnTalentCollapsedButton");
            _ownTalentDrawer = _root.Q<VisualElement>("OwnTalentDrawer");
            _talentDrawerDismissLayer = _root.Q<Button>("TalentDrawerDismissLayer");
            _talentEffectFeed = _root.Q<VisualElement>("TalentEffectFeed");
            _talentToast = _root.Q<Label>("TalentToast");

            for (int slot = 0; slot < 4; slot++)
            {
                _seatTalentRows[slot] = _root.Q<VisualElement>($"Seat{slot}KnownTalents");
                _seatTalentMoreButtons[slot] = _root.Q<Button>($"Seat{slot}KnownTalentMore");
                _seatTalentDrawers[slot] = _root.Q<VisualElement>($"Seat{slot}KnownTalentDrawer");
                int capturedSlot = slot;
                _seatTalentMoreClicked[slot] = () => ToggleTalentDrawer(_seatTalentDrawers[capturedSlot]);
                _seatTalentMoreButtons[slot].clicked += _seatTalentMoreClicked[slot];
            }

            _ownTalentCollapsedClicked = () => ToggleTalentDrawer(_ownTalentDrawer);
            _ownTalentCollapsedButton.clicked += _ownTalentCollapsedClicked;
            _talentDrawerDismissClicked = CloseTalentDrawers;
            _talentDrawerDismissLayer.clicked += _talentDrawerDismissClicked;
            CloseTalentDrawers();
        }

        private void UnbindTalentElementCallbacks()
        {
            if (_ownTalentCollapsedButton != null && _ownTalentCollapsedClicked != null)
                _ownTalentCollapsedButton.clicked -= _ownTalentCollapsedClicked;
            _ownTalentCollapsedClicked = null;

            for (int slot = 0; slot < _seatTalentMoreButtons.Length; slot++)
            {
                if (_seatTalentMoreButtons[slot] != null && _seatTalentMoreClicked[slot] != null)
                    _seatTalentMoreButtons[slot].clicked -= _seatTalentMoreClicked[slot];
                _seatTalentMoreClicked[slot] = null;
            }

            if (_talentDrawerDismissLayer != null && _talentDrawerDismissClicked != null)
                _talentDrawerDismissLayer.clicked -= _talentDrawerDismissClicked;
            _talentDrawerDismissClicked = null;
        }

        public void BindServerProxy(RemoteServerProxy proxy)
        {
            if (proxy == null || ReferenceEquals(_serverProxy, proxy)) return;
            UnbindServerProxy(_serverProxy);
            _serverProxy = proxy;
            _talentFeedbackHistory.ResetForNewMatch();
            _acceptedPublicTalentEvents.Clear();
            _serverProxy.TalentRuntimeEventReceived += HandleTalentRuntimeEvent;
            _serverProxy.TalentActionsChanged += HandleTalentActionsChanged;
            RebuildTalentHudFromClientState();
        }

        public void UnbindServerProxy(RemoteServerProxy proxy)
        {
            if (proxy == null || !ReferenceEquals(_serverProxy, proxy)) return;
            _serverProxy.TalentRuntimeEventReceived -= HandleTalentRuntimeEvent;
            _serverProxy.TalentActionsChanged -= HandleTalentActionsChanged;
            _serverProxy = null;
        }

        private void HandleTalentActionsChanged(long decisionId, IReadOnlyList<MahjongGame.Talents.TalentActionOption> actions)
        {
            CloseTalentDrawers();
            RebuildTalentHudFromClientState();
        }

        private void HandleTalentRuntimeEvent(TalentRuntimeEventMessage runtimeEvent)
        {
            if (!_talentFeedbackHistory.TryBuild(runtimeEvent, false, out TalentFeedbackView feedback)) return;
            _talentTransientState.RecordLiveFeedback(feedback);

            if (runtimeEvent.visibility == (int)MahjongGame.Talents.TalentEventVisibility.Public)
            {
                _acceptedPublicTalentEvents.Add(runtimeEvent);
            }

            RebuildTalentHudFromClientState();
            if (feedback.ShouldLogWarning)
                Debug.LogWarning("[GameHUD] UnknownTalentRuntimeEvent");
            if (feedback.PulseChip)
                PulseTalentChip(runtimeEvent.ownerSeatIndex, runtimeEvent.talentId);
            if (feedback.AppendFeed)
                AppendTalentFeed(feedback, runtimeEvent);
            if (feedback.ShowToast)
                ShowTalentToast(feedback.Copy);
            if (feedback.PlayAudio)
                PlayTalentAudio();
        }

        private void RebuildTalentHudFromClientState()
        {
            RoomGameSnapshot snapshot = NetworkManager.Instance?.RoomService?.GameState?.Snapshot;
            if (snapshot != null)
            {
                _talentSnapshot = snapshot;
                RenderTalentHud(snapshot);
            }
        }

        private void RenderTalentHud(RoomGameSnapshot snapshot)
        {
            if (snapshot == null || _ownTalentBar == null) return;
            int localSeatIndex = snapshot.requestingSeatIndex >= 0
                ? snapshot.requestingSeatIndex
                : NetworkManager.Instance?.RoomService?.SeatIndex ?? 0;
            TalentHudView view = TalentHudProjectionPolicy.Build(
                snapshot,
                localSeatIndex,
                _acceptedPublicTalentEvents);

            RenderTalentItems(_ownTalentBar, view.OwnVisible, isOwn: true);
            RenderTalentItems(_ownTalentDrawer, view.OwnCollapsed, isOwn: true);
            ConfigureMoreButton(_ownTalentCollapsedButton, view.OwnCollapsedCount);

            for (int slot = 0; slot < 4; slot++)
            {
                _seatTalentRows[slot]?.Clear();
                _seatTalentDrawers[slot]?.Clear();
                ConfigureMoreButton(_seatTalentMoreButtons[slot], 0);
            }

            foreach (KeyValuePair<int, TalentSeatSummary> pair in view.Seats)
            {
                int slot = PlayerIndexToUISlot(pair.Key);
                if (slot == 0) continue;
                RenderTalentItems(_seatTalentRows[slot], pair.Value.Visible, isOwn: false);
                RenderTalentItems(_seatTalentDrawers[slot], pair.Value.Expanded, isOwn: false);
                ConfigureMoreButton(_seatTalentMoreButtons[slot], pair.Value.CollapsedCount);
            }
        }

        private void RenderTalentItems(
            VisualElement container,
            IEnumerable<TalentHudItem> items,
            bool isOwn)
        {
            if (container == null) return;
            container.Clear();
            foreach (TalentHudItem item in items ?? Enumerable.Empty<TalentHudItem>())
            {
                VisualElement chip = CreateTalentChip(item, isOwn);
                container.Add(chip);
            }
        }

        private VisualElement CreateTalentChip(TalentHudItem item, bool isOwn)
        {
            VisualElement instanceRoot;
            VisualElement chip;
            if (_talentChipTemplate != null)
            {
                TemplateContainer template = _talentChipTemplate.CloneTree();
                instanceRoot = template;
                chip = template.Q<VisualElement>("TalentChip") ?? template;
            }
            else
            {
                if (!_missingTemplateWarningLogged)
                {
                    Debug.LogWarning("[GameHUD] Missing talent chip template; using safe fallback.");
                    _missingTemplateWarningLogged = true;
                }
                chip = new VisualElement { name = "TalentChip" };
                instanceRoot = chip;
                chip.AddToClassList("talent-chip");
                chip.Add(new Label { name = "NameLabel" });
                chip.Add(new Label { name = "ValueLabel" });
                chip.Add(new Label { name = "ConsumedMarker" });
            }

            chip.userData = TalentChipKey(item.TalentId, isOwn);
            Label nameLabel = chip.Q<Label>("NameLabel");
            Label valueLabel = chip.Q<Label>("ValueLabel");
            Label consumedMarker = chip.Q<Label>("ConsumedMarker");
            if (nameLabel != null) nameLabel.text = item.DisplayName;
            if (valueLabel != null) valueLabel.text = item.ShowValue ? item.Value.ToString() : string.Empty;
            if (consumedMarker != null) consumedMarker.style.display = DisplayStyle.None;

            SetClass(chip, "talent-chip--active", item.ShowActiveState && isOwn && item.IsActive);
            SetClass(chip, "talent-chip--inactive", item.ShowActiveState && isOwn && !item.IsActive);
            SetClass(chip, "talent-chip--known", !isOwn);
            if (item.ShouldLogWarning)
                Debug.LogWarning("[GameHUD] Unknown active own talent rendered with fallback copy.");
            return instanceRoot;
        }

        private static void ConfigureMoreButton(Button button, int collapsedCount)
        {
            if (button == null) return;
            button.text = collapsedCount > 0 ? $"+{collapsedCount}" : string.Empty;
            button.style.display = collapsedCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetClass(VisualElement element, string className, bool enabled)
        {
            if (enabled) element.AddToClassList(className);
            else element.RemoveFromClassList(className);
        }

        private void ToggleTalentDrawer(VisualElement drawer)
        {
            if (drawer == null) return;
            if (ReferenceEquals(_expandedTalentDrawer, drawer))
            {
                CloseTalentDrawers();
                return;
            }
            CloseTalentDrawers();
            _expandedTalentDrawer = drawer;
            _talentTransientState.OpenDrawer();
            drawer.AddToClassList("talent-drawer--visible");
            _talentDrawerDismissLayer.AddToClassList("talent-drawer-dismiss--visible");
        }

        public void CloseTalentDrawers()
        {
            _expandedTalentDrawer?.RemoveFromClassList("talent-drawer--visible");
            _expandedTalentDrawer = null;
            _talentTransientState.CloseDrawers();
            _talentDrawerDismissLayer?.RemoveFromClassList("talent-drawer-dismiss--visible");
        }

        private void AppendTalentFeed(TalentFeedbackView feedback, TalentRuntimeEventMessage runtimeEvent)
        {
            if (_talentEffectFeed == null) return;
            var row = new Label(feedback.Copy);
            row.AddToClassList("talent-feed-row");
            row.AddToClassList(IsNegativeEvent(runtimeEvent?.eventType)
                ? "talent-feed-row--negative"
                : "talent-feed-row--positive");
            _talentEffectFeed.Add(row);
            while (_talentEffectFeed.childCount > 4)
                _talentEffectFeed.RemoveAt(0);
        }

        private void ShowTalentToast(string copy)
        {
            if (_talentToast == null) return;
            _toastHideSchedule?.Pause();
            _talentToastTween?.Kill();
            _talentToast.text = copy ?? string.Empty;
            _talentToast.AddToClassList("talent-toast--visible");
            _talentToast.style.opacity = 0f;
            _talentToastTween = DOVirtual.Float(0f, 1f, 0.18f,
                    value => _talentToast.style.opacity = value)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
            _toastHideSchedule = _talentToast.schedule.Execute(HideTalentToast).StartingIn(1800);
        }

        private void HideTalentToast()
        {
            _talentToast?.RemoveFromClassList("talent-toast--visible");
            _toastHideSchedule = null;
            _talentTransientState.HideToast();
        }

        private void PulseTalentChip(int ownerSeatIndex, string talentId)
        {
            bool isOwn = ownerSeatIndex == (_talentSnapshot?.requestingSeatIndex
                ?? NetworkManager.Instance?.RoomService?.SeatIndex ?? 0);
            VisualElement chip = FindTalentChip(ownerSeatIndex, talentId, isOwn);
            if (chip == null) return;
            ResetTalentChipPulse();
            _talentPulsedChip = chip;
            chip.style.scale = new Scale(Vector2.one);
            _talentChipTween = DOVirtual.Float(1f, 1.12f, 0.16f,
                    value => chip.style.scale = new Scale(new Vector2(value, value)))
                .SetLoops(2, LoopType.Yoyo)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        private void ResetTalentChipPulse()
        {
            _talentChipTween?.Kill();
            _talentChipTween = null;
            if (_talentPulsedChip != null)
                _talentPulsedChip.style.scale = new Scale(Vector2.one);
            _talentPulsedChip = null;
        }

        private VisualElement FindTalentChip(int ownerSeatIndex, string talentId, bool isOwn)
        {
            IEnumerable<VisualElement> containers;
            if (isOwn)
                containers = new[] { _ownTalentBar, _ownTalentDrawer };
            else
            {
                int slot = PlayerIndexToUISlot(ownerSeatIndex);
                containers = new[] { _seatTalentRows[slot], _seatTalentDrawers[slot] };
            }

            string key = TalentChipKey(talentId, isOwn);
            return containers.Where(container => container != null)
                .SelectMany(container => container.Query<VisualElement>(className: "talent-chip").ToList())
                .FirstOrDefault(element => string.Equals(element.userData as string, key, StringComparison.Ordinal));
        }

        private void PlayTalentAudio()
        {
            if (_talentAudioSource == null || _genericActiveTalentClip == null)
            {
                if (!_missingAudioWarningLogged)
                {
                    Debug.LogWarning("[GameHUD] Missing generic active-talent AudioSource or AudioClip.");
                    _missingAudioWarningLogged = true;
                }
                return;
            }
            _talentAudioSource.PlayOneShot(_genericActiveTalentClip);
        }

        private static bool IsNegativeEvent(string eventType) =>
            string.Equals(eventType, "public_charge_reduced", StringComparison.Ordinal);

        private static string TalentChipKey(string talentId, bool isOwn) =>
            (isOwn ? "own:" : "known:") + (talentId ?? string.Empty);

        /// <summary>
        /// 更新左上角局信息 + 四家风位和分数
        /// </summary>
        public void UpdateRoundInfo(GameSession session)
        {
            if (session == null) return;

            string modeName = GetModeName(session.Mode);
            int remaining = Mathf.Max(_lastRemainingCount, 0);

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
            ResetTalentFeedbackForRecovery();
        }

        private void ResetTalentFeedbackForRecovery()
        {
            _toastHideSchedule?.Pause();
            _toastHideSchedule = null;
            ResetTalentChipPulse();
            _talentToastTween?.Kill();
            _talentToastTween = null;
            _talentToast?.RemoveFromClassList("talent-toast--visible");
            if (_talentToast != null)
            {
                _talentToast.text = string.Empty;
                _talentToast.style.opacity = 0f;
            }
            _talentEffectFeed?.Clear();
            CloseTalentDrawers();
            _talentTransientState.ResetForRecovery();
        }

        public void ApplyRecoverySnapshot(RoomGameSnapshot snapshot, GameSession session)
        {
            if (snapshot == null || session == null) return;
            ResetForRecovery();
            _acceptedPublicTalentEvents.Clear();
            _talentSnapshot = snapshot;
            RenderTalentHud(snapshot);
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
