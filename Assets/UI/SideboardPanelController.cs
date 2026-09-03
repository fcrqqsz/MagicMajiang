using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;
using MahjongGame.Systems;
using UnityEngine;
using UnityEngine.UIElements;

namespace MahjongGame.UI
{
    /// <summary>Owns the local sideboard presentation only; authority stays in ClientRoomService.</summary>
    public sealed class SideboardPanelController : MonoBehaviour
    {
        public static SideboardPanelController Instance { get; private set; }

        [SerializeField] private UIDocument _document;

        private VisualElement _documentRoot;
        private VisualElement _overlay;
        private Label _timerLabel;
        private VisualElement _activeTalents;
        private VisualElement _reserveCards;
        private VisualElement _knownOpponentIntel;
        private VisualElement _budgetTrack;
        private VisualElement _budgetFill;
        private Label _deckCostLabel;
        private Label _talentCostLabel;
        private Label _budgetLabel;
        private VisualElement _seatLockStatus;
        private Label _errorLabel;
        private Button _lockButton;
        private Button _battleMenuButton;
        private readonly Dictionary<Button, Action> _cardCallbacks = new Dictionary<Button, Action>();
        private Action _lockClicked;

        private RemoteServerProxy _proxy;
        private ClientRoomService _roomService;
        private SideboardPanelViewState _state = SideboardPanelViewState.Closed;
        private IVisualElementScheduledItem _deadlineSchedule;
        private bool _disposed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (_document == null) _document = GetComponent<UIDocument>();
            _documentRoot = _document?.rootVisualElement;
            VisualElement root = _documentRoot;
            if (root == null)
            {
                Debug.LogError("[SideboardPanel] Missing UIDocument root.");
                return;
            }
            root.pickingMode = PickingMode.Ignore;

            _overlay = root.Q<VisualElement>("SideboardOverlay");
            _timerLabel = root.Q<Label>("TimerLabel");
            _activeTalents = root.Q<VisualElement>("ActiveTalents");
            _reserveCards = root.Q<VisualElement>("ReserveCards");
            _knownOpponentIntel = root.Q<VisualElement>("KnownOpponentIntel");
            _budgetTrack = root.Q<VisualElement>("BudgetTrack");
            _budgetFill = root.Q<VisualElement>("BudgetFill");
            _deckCostLabel = root.Q<Label>("DeckCostLabel");
            _talentCostLabel = root.Q<Label>("TalentCostLabel");
            _budgetLabel = root.Q<Label>("BudgetLabel");
            _seatLockStatus = root.Q<VisualElement>("SeatLockStatus");
            _errorLabel = root.Q<Label>("ErrorLabel");
            _lockButton = root.Q<Button>("LockButton");
            _battleMenuButton = root.Q<Button>("BattleMenuButton");
            if (_battleMenuButton != null) _battleMenuButton.clicked += OpenBattleMenu;

            _lockClicked = HandleLockClicked;
            if (_lockButton != null) _lockButton.clicked += _lockClicked;
            Render();
        }

        private void OnDestroy()
        {
            if (_battleMenuButton != null) _battleMenuButton.clicked -= OpenBattleMenu;
            DisposePresentation();
            if (Instance == this) Instance = null;
        }

        private static void OpenBattleMenu() => BattleMenuController.Instance?.OpenMenu();

        public void BindServerProxy(RemoteServerProxy proxy) =>
            Bind(proxy, NetworkManager.Instance?.RoomService);

        public void UnbindServerProxy(RemoteServerProxy proxy) => Unbind(proxy);

        public void ApplyRecoverySnapshot(SnapshotSideboardState state) => ApplyRecovery(state);

        public void Bind(RemoteServerProxy proxy, ClientRoomService roomService)
        {
            if (_disposed || proxy == null || roomService == null) return;
            if (ReferenceEquals(_proxy, proxy) && ReferenceEquals(_roomService, roomService)) return;
            Unbind(_proxy);
            _proxy = proxy;
            _roomService = roomService;
            _proxy.SideboardStartedReceived += HandleStarted;
            _proxy.SideboardLockedReceived += HandleLocked;
            _proxy.SideboardProgressReceived += HandleProgress;
            _proxy.SideboardResetRequested += HandleAuthoritativeReset;
        }

        public void Unbind(RemoteServerProxy proxy)
        {
            if (proxy == null || !ReferenceEquals(_proxy, proxy)) return;
            _proxy.SideboardStartedReceived -= HandleStarted;
            _proxy.SideboardLockedReceived -= HandleLocked;
            _proxy.SideboardProgressReceived -= HandleProgress;
            _proxy.SideboardResetRequested -= HandleAuthoritativeReset;
            _proxy = null;
            _roomService = null;
            Close();
        }

        public void ApplyRecovery(SnapshotSideboardState state)
        {
            if (_disposed) return;
            _state = SideboardPanelStatePolicy.Recover(state);
            Render();
        }

        private void HandleStarted(int receivedSeatIndex, SideboardStartedMessage started)
        {
            int localSeatIndex = _roomService?.SeatIndex ?? -1;
            _state = SideboardPanelStatePolicy.OpenStarted(
                _state, started, receivedSeatIndex, localSeatIndex);
            Render();
        }

        private void HandleLocked(int receivedSeatIndex, SideboardLockedMessage locked)
        {
            if (receivedSeatIndex != (_roomService?.SeatIndex ?? -1)) return;
            _state = SideboardPanelStatePolicy.ApplyLocked(_state, locked);
            Render();
        }

        private void HandleProgress(SideboardProgressMessage progress)
        {
            _state = SideboardPanelStatePolicy.ApplyProgress(_state, progress);
            Render();
        }

        private void HandleAuthoritativeReset()
        {
            _state = SideboardPanelStatePolicy.Reset(_state);
            Render();
        }

        private void HandleCardClicked(string talentId)
        {
            if (BattleMenuInputGate.Instance.IsBlocked(Time.frameCount)) return;
            if (!_state.IsEditable || _state.PrivateDraft == null) return;
            bool isActive = _state.PrivateDraft.ActiveTalentIds.Contains(talentId, StringComparer.Ordinal);
            SideboardDraft changed = SideboardDraftPolicy.SetActive(
                _state.PrivateDraft,
                talentId,
                !isActive,
                TalentRegistry.Instance);
            _state = SideboardPanelStatePolicy.UpdateDraft(_state, changed);
            Render();
        }

        private void HandleLockClicked()
        {
            if (BattleMenuInputGate.Instance.IsBlocked(Time.frameCount)) return;
            if (!SideboardPanelStatePolicy.TryBeginSubmit(
                    _state, out SideboardPanelViewState pending, out string[] activeTalentIds)) return;

            _state = pending;
            Render();
            // A false result remains pending. Only a new authoritative decision may restore editing.
            _proxy?.SubmitSideboard(activeTalentIds);
        }

        private void Render()
        {
            SetDocumentVisibility(_state.IsVisible);
            if (_overlay == null) return;
            SetClass(_overlay, "sideboard-overlay--visible", _state.IsVisible);
            if (!_state.IsVisible)
            {
                StopDeadlineDisplay();
                ClearCardCallbacks();
                _activeTalents?.Clear();
                _reserveCards?.Clear();
                return;
            }

            StartDeadlineDisplay();
            RenderCards();
            RenderKnownOpponentIntel();
            RenderSeatLocks();
            RenderBudgetAndAction();
        }

        private void SetDocumentVisibility(bool visible)
        {
            if (_documentRoot == null) return;
            _documentRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RenderCards()
        {
            ClearCardCallbacks();
            _activeTalents?.Clear();
            _reserveCards?.Clear();
            SideboardDraft draft = _state.PrivateDraft;
            if (draft == null) return;

            string[] carried = draft.CarriedMainTalentIds
                .Concat(draft.CarriedReserveTalentIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(TalentRegistry.Instance.HasTalent)
                .ToArray();
            foreach (string talentId in carried.Where(id => draft.ActiveTalentIds.Contains(id, StringComparer.Ordinal)))
                _activeTalents?.Add(CreateTalentCard(talentId, draft));
            foreach (string talentId in carried.Where(id => !draft.ActiveTalentIds.Contains(id, StringComparer.Ordinal)))
                _reserveCards?.Add(CreateTalentCard(talentId, draft));
        }

        private Button CreateTalentCard(string talentId, SideboardDraft draft)
        {
            TalentRegistry registry = TalentRegistry.Instance;
            bool isActive = draft.ActiveTalentIds.Contains(talentId, StringComparer.Ordinal);
            bool isLocked = registry.GetMetadata(talentId).SideboardPolicy == TalentSideboardPolicy.MainOnlyLocked;
            var card = new Button { text = string.Empty, userData = talentId };
            card.AddToClassList("sideboard-card");
            SetClass(card, "sideboard-card--active", isActive);
            SetClass(card, "sideboard-card--stopped", !isActive);
            SetClass(card, "sideboard-card--locked", isLocked);
            SetClass(card, "sideboard-card--readonly", !_state.IsEditable);
            card.SetEnabled(_state.IsEditable && !(isLocked && isActive));

            var name = new Label(registry.GetDisplayName(talentId));
            name.AddToClassList("sideboard-card__name");
            var meta = new Label($"{TierName(registry.GetTier(talentId))} · 异化 {registry.GetCost(talentId)}");
            meta.AddToClassList("sideboard-card__meta");
            var state = new Label(isLocked
                ? "🔒 锁定 · 必须保持启用"
                : isActive ? "已启用" : "已停用");
            state.AddToClassList("sideboard-card__state");
            state.AddToClassList(isActive ? "sideboard-card__state--active" : "sideboard-card__state--stopped");
            card.Add(name);
            card.Add(meta);
            card.Add(state);

            string capturedTalentId = talentId;
            Action callback = () => HandleCardClicked(capturedTalentId);
            card.clicked += callback;
            _cardCallbacks[card] = callback;
            return card;
        }

        private void RenderKnownOpponentIntel()
        {
            if (_knownOpponentIntel == null) return;
            _knownOpponentIntel.Clear();
            RoomGameSnapshot snapshot = _roomService?.GameState?.Snapshot;
            int localSeatIndex = _roomService?.SeatIndex ?? snapshot?.requestingSeatIndex ?? -1;
            TalentHudView view = TalentHudProjectionPolicy.Build(snapshot, localSeatIndex);
            foreach (KeyValuePair<int, TalentSeatSummary> pair in view.Seats.OrderBy(pair => pair.Key))
            {
                var row = new VisualElement();
                row.AddToClassList("sideboard-intel-seat");
                var seatName = new Label(GetSeatName(pair.Key));
                seatName.AddToClassList("sideboard-intel-seat__name");
                row.Add(seatName);
                string knownNames = string.Join(" · ", pair.Value.Expanded.Select(item => item.DisplayName));
                var known = new Label(string.IsNullOrEmpty(knownNames) ? "暂无公开情报" : knownNames);
                known.AddToClassList("sideboard-intel-seat__known");
                row.Add(known);
                _knownOpponentIntel.Add(row);
            }

            if (_knownOpponentIntel.childCount == 0)
            {
                var empty = new Label("暂无公开情报");
                empty.AddToClassList("sideboard-intel-seat__known");
                _knownOpponentIntel.Add(empty);
            }
        }

        private void RenderSeatLocks()
        {
            if (_seatLockStatus == null) return;
            _seatLockStatus.Clear();
            for (int seatIndex = 0; seatIndex < 4; seatIndex++)
            {
                bool locked = seatIndex < _state.SeatLocked.Count && _state.SeatLocked[seatIndex];
                var label = new Label($"{GetSeatName(seatIndex)}  {(locked ? "已确认" : "调整中")}");
                label.AddToClassList("sideboard-seat-lock");
                SetClass(label, "sideboard-seat-lock--confirmed", locked);
                _seatLockStatus.Add(label);
            }
        }

        private void RenderBudgetAndAction()
        {
            SideboardDraft draft = _state.PrivateDraft;
            bool hasDraft = draft != null;
            if (_deckCostLabel != null)
                _deckCostLabel.text = hasDraft ? $"牌山 {draft.DeckAlienation}" : "牌山 —";
            if (_talentCostLabel != null)
                _talentCostLabel.text = hasDraft ? $"当前生效天赋 {draft.ActiveTalentAlienation}" : "当前生效天赋 —";
            if (_budgetLabel != null)
                _budgetLabel.text = hasDraft
                    ? $"总计 {draft.TotalAlienation} / {PresetName(draft.AlienationLimit)}上限 {draft.AlienationLimit}"
                    : "方案已锁定";
            if (_budgetFill != null)
            {
                float fill = hasDraft && draft.AlienationLimit > 0
                    ? Mathf.Clamp01(draft.TotalAlienation / (float)draft.AlienationLimit)
                    : 0f;
                _budgetFill.style.width = new Length(fill * 100f, LengthUnit.Percent);
            }
            SetClass(_budgetTrack, "sideboard-budget--over", draft?.IsOverLimit == true);

            if (_errorLabel != null) _errorLabel.text = GetStatusCopy();
            if (_lockButton != null)
            {
                _lockButton.SetEnabled(_state.IsEditable
                                       && !_state.IsSubmissionPending
                                       && draft?.CanLock == true);
                _lockButton.text = _state.IsSubmissionPending
                    ? "等待服务器确认"
                    : _state.IsReadOnly ? "方案已锁定" : "锁定方案";
            }
        }

        private string GetStatusCopy()
        {
            if (_state.IsSubmissionPending) return "方案已提交，等待服务器确认。";
            if (_state.IsReadOnly)
            {
                if (_state.PrivateDraft?.ErrorCode != null)
                    return GetDraftErrorCopy(_state.PrivateDraft.ErrorCode);
                if (_state.IsComplete) return "四席已确认，等待服务器进入下一局。";
                return _state.LockReason == "recovery_pending_private_state"
                    ? "正在等待服务器恢复本席整备数据。"
                    : "本席方案已锁定，等待其他席位。";
            }
            return GetDraftErrorCopy(_state.PrivateDraft?.ErrorCode);
        }

        private static string GetDraftErrorCopy(string errorCode) => errorCode switch
            {
                SideboardDraftErrorCodes.AlienationLimitExceeded => "已超过异化上限；仍可调整，但无法锁定。",
                SideboardDraftErrorCodes.LockedTalent => "带锁主天赋不能停用。",
                SideboardDraftErrorCodes.UnknownTalent => "无法识别该天赋。",
                SideboardDraftErrorCodes.NotCarried => "只能调整本局携带的九个槽位。",
                SideboardDraftErrorCodes.DuplicateTalent => "同一天赋不能重复启用。",
                SideboardDraftErrorCodes.InvalidSelection => "服务器整备数据不合法，已切换为只读。",
                _ => string.Empty
            };

        private void StartDeadlineDisplay()
        {
            UpdateDeadlineDisplay();
            if (_deadlineSchedule != null || _overlay == null) return;
            _deadlineSchedule = _overlay.schedule.Execute(UpdateDeadlineDisplay).Every(250);
        }

        private void UpdateDeadlineDisplay()
        {
            if (_timerLabel == null) return;
            long remainingMilliseconds = _state.DeadlineUnixMilliseconds
                                         - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _timerLabel.text = remainingMilliseconds > 0
                ? Math.Ceiling(remainingMilliseconds / 1000d).ToString("0")
                : "等待服务器";
        }

        private void StopDeadlineDisplay()
        {
            _deadlineSchedule?.Pause();
            _deadlineSchedule = null;
        }

        private string GetSeatName(int seatIndex)
        {
            string name = (_roomService?.Seats ?? Array.Empty<RoomSeatMessage>())
                .FirstOrDefault(seat => seat != null && seat.seatIndex == seatIndex)?.displayName;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return seatIndex == (_roomService?.SeatIndex ?? -1) ? "本席" : $"席位 {seatIndex + 1}";
        }

        private static string TierName(TalentTier tier) => tier switch
        {
            TalentTier.Large => "大型",
            TalentTier.Medium => "中型",
            _ => "小型"
        };

        private static string PresetName(int limit) => (AlienationPreset)limit switch
        {
            AlienationPreset.Low => "低档",
            AlienationPreset.High => "高档",
            _ => "标准档"
        };

        private static void SetClass(VisualElement element, string className, bool enabled)
        {
            if (element == null) return;
            if (enabled) element.AddToClassList(className);
            else element.RemoveFromClassList(className);
        }

        private void ClearCardCallbacks()
        {
            foreach (KeyValuePair<Button, Action> pair in _cardCallbacks)
                pair.Key.clicked -= pair.Value;
            _cardCallbacks.Clear();
        }

        private void Close()
        {
            _state = SideboardPanelViewState.Closed;
            Render();
        }

        private void DisposePresentation()
        {
            if (_disposed) return;
            Unbind(_proxy);
            StopDeadlineDisplay();
            ClearCardCallbacks();
            if (_lockButton != null) _lockButton.clicked -= _lockClicked;
            _disposed = true;
        }
    }
}
