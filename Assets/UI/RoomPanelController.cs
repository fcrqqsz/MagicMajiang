using System;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Data;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Systems;
using MahjongGame.Talents;
using UnityEngine;
using UnityEngine.UIElements;

namespace MahjongGame.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class RoomPanelController : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private LobbyController lobbyController;
        [SerializeField] private DeckEditorToolkit deckEditorToolkit;

        private readonly SeatCardView[] _seatCards = new SeatCardView[4];
        private readonly EventCallback<ClickEvent>[] _seatClickCallbacks = new EventCallback<ClickEvent>[4];
        private readonly Action[] _seatActionCallbacks = new Action[4];
        private readonly AiQuickConfigController _quickConfig = new AiQuickConfigController();

        private ClientRoomService _service;
        private VisualElement _viewRoom;
        private Label _roomId;
        private Label _roomMode;
        private Label _roomPreset;
        private Label _roomHost;
        private Label _roomState;
        private Label _roomConnection;
        private Button _copyButton;
        private Label _detailName;
        private Label _detailMeta;
        private Label _detailAccess;
        private VisualElement _quickRoot;
        private VisualElement _readOnlyRoot;
        private Label _readOnlyText;
        private Button _difficultyBeginner;
        private Button _difficultyStandard;
        private Button _templateAggressive;
        private Button _templateStable;
        private Button _templateSynergy;
        private Button _templateCopySelf;
        private Label _deckSummary;
        private Label _talentSummary;
        private Label _budgetSummary;
        private VisualElement _overwriteConfirm;
        private Button _overwriteAccept;
        private Button _overwriteCancel;
        private Button _applyButton;
        private Button _advancedButton;
        private Button _removeButton;
        private Label _noticeTitle;
        private Label _noticeCopy;
        private Button _readyButton;
        private Button _leaveButton;
        private int _selectedSeatIndex = -1;
        private string _notice;
        private string _authoritySignature;
        private bool _callbacksBound;
        private bool _quickEditable;

        private void OnEnable()
        {
            document ??= GetComponent<UIDocument>();
            lobbyController ??= GetComponent<LobbyController>();
            lobbyController ??= FindObjectOfType<LobbyController>(true);
            if (deckEditorToolkit == null) deckEditorToolkit = FindObjectOfType<DeckEditorToolkit>(true);
            QueryVisualTree();
            BindCallbacks();
            TryBindService();
            Refresh();
        }

        private void Update()
        {
            if (_service == null) TryBindService();
        }

        private void OnDisable()
        {
            UnbindService();
            UnbindCallbacks();
            deckEditorToolkit?.CloseRoomAiEditorForAuthorityChange();
            _quickConfig.Clear();
        }

        private void OnDestroy()
        {
            deckEditorToolkit?.CloseRoomAiEditorForAuthorityChange();
        }

        private void QueryVisualTree()
        {
            VisualElement root = document?.rootVisualElement;
            if (root == null) return;
            _viewRoom = root.Q<VisualElement>("ViewRoom");
            _roomId = root.Q<Label>("RoomIdLabel");
            _roomMode = root.Q<Label>("RoomModeLabel");
            _roomPreset = root.Q<Label>("RoomPresetPublicLabel");
            _roomHost = root.Q<Label>("RoomHostLabel");
            _roomState = root.Q<Label>("RoomStateLabel");
            _roomConnection = root.Q<Label>("RoomConnectionLabel");
            _copyButton = root.Q<Button>("RoomCopyButton");
            _detailName = root.Q<Label>("RoomDetailName");
            _detailMeta = root.Q<Label>("RoomDetailMeta");
            _detailAccess = root.Q<Label>("RoomDetailAccess");
            _quickRoot = root.Q<VisualElement>("AiQuickConfig");
            _readOnlyRoot = root.Q<VisualElement>("RoomSeatReadOnly");
            _readOnlyText = root.Q<Label>("RoomSeatReadOnlyText");
            _difficultyBeginner = root.Q<Button>("AiDifficultyBeginner");
            _difficultyStandard = root.Q<Button>("AiDifficultyStandard");
            _templateAggressive = root.Q<Button>("AiTemplateAggressive");
            _templateStable = root.Q<Button>("AiTemplateStable");
            _templateSynergy = root.Q<Button>("AiTemplateSynergy");
            _templateCopySelf = root.Q<Button>("AiTemplateCopySelf");
            _deckSummary = root.Q<Label>("AiDeckSummary");
            _talentSummary = root.Q<Label>("AiTalentSummary");
            _budgetSummary = root.Q<Label>("AiBudgetSummary");
            _overwriteConfirm = root.Q<VisualElement>("AiOverwriteConfirm");
            _overwriteAccept = root.Q<Button>("AiOverwriteAccept");
            _overwriteCancel = root.Q<Button>("AiOverwriteCancel");
            _applyButton = root.Q<Button>("AiApplyButton");
            _advancedButton = root.Q<Button>("AiAdvancedButton");
            _removeButton = root.Q<Button>("AiRemoveButton");
            _noticeTitle = root.Q<Label>("RoomNoticeTitle");
            _noticeCopy = root.Q<Label>("RoomNoticeCopy");
            _readyButton = root.Q<Button>("RoomReadyButton");
            _leaveButton = root.Q<Button>("LeaveRoomButton");
            for (int i = 0; i < _seatCards.Length; i++)
            {
                _seatCards[i] = new SeatCardView(
                    root.Q<VisualElement>($"SeatCard{i}"),
                    root.Q<Label>($"Seat{i}Number"),
                    root.Q<Label>($"Seat{i}Name"),
                    root.Q<Label>($"Seat{i}Badges"),
                    root.Q<Label>($"Seat{i}Status"),
                    root.Q<Label>($"Seat{i}Summary"),
                    root.Q<Button>($"Seat{i}Action"));
            }
        }

        private void BindCallbacks()
        {
            if (_callbacksBound) return;
            for (int i = 0; i < _seatCards.Length; i++)
            {
                int seatIndex = i;
                _seatClickCallbacks[i] = _ => SelectSeat(seatIndex);
                _seatActionCallbacks[i] = () => SelectSeat(seatIndex);
                _seatCards[i]?.Root?.RegisterCallback(_seatClickCallbacks[i]);
                if (_seatCards[i]?.Action != null) _seatCards[i].Action.clicked += _seatActionCallbacks[i];
            }
            if (_copyButton != null) _copyButton.clicked += CopyRoomId;
            if (_difficultyBeginner != null) _difficultyBeginner.clicked += SelectBeginner;
            if (_difficultyStandard != null) _difficultyStandard.clicked += SelectStandard;
            if (_templateAggressive != null) _templateAggressive.clicked += SelectAggressive;
            if (_templateStable != null) _templateStable.clicked += SelectStable;
            if (_templateSynergy != null) _templateSynergy.clicked += SelectSynergy;
            if (_templateCopySelf != null) _templateCopySelf.clicked += SelectCopySelf;
            if (_overwriteAccept != null) _overwriteAccept.clicked += ConfirmTemplateOverwrite;
            if (_overwriteCancel != null) _overwriteCancel.clicked += CancelTemplateOverwrite;
            if (_applyButton != null) _applyButton.clicked += ApplyAiConfig;
            if (_advancedButton != null) _advancedButton.clicked += OpenAdvancedEditor;
            if (_removeButton != null) _removeButton.clicked += RemoveAi;
            if (_readyButton != null) _readyButton.clicked += ToggleReady;
            if (_leaveButton != null) _leaveButton.clicked += LeaveRoom;
            _callbacksBound = true;
        }

        private void UnbindCallbacks()
        {
            if (!_callbacksBound) return;
            for (int i = 0; i < _seatCards.Length; i++)
            {
                _seatCards[i]?.Root?.UnregisterCallback(_seatClickCallbacks[i]);
                if (_seatCards[i]?.Action != null) _seatCards[i].Action.clicked -= _seatActionCallbacks[i];
            }
            if (_copyButton != null) _copyButton.clicked -= CopyRoomId;
            if (_difficultyBeginner != null) _difficultyBeginner.clicked -= SelectBeginner;
            if (_difficultyStandard != null) _difficultyStandard.clicked -= SelectStandard;
            if (_templateAggressive != null) _templateAggressive.clicked -= SelectAggressive;
            if (_templateStable != null) _templateStable.clicked -= SelectStable;
            if (_templateSynergy != null) _templateSynergy.clicked -= SelectSynergy;
            if (_templateCopySelf != null) _templateCopySelf.clicked -= SelectCopySelf;
            if (_overwriteAccept != null) _overwriteAccept.clicked -= ConfirmTemplateOverwrite;
            if (_overwriteCancel != null) _overwriteCancel.clicked -= CancelTemplateOverwrite;
            if (_applyButton != null) _applyButton.clicked -= ApplyAiConfig;
            if (_advancedButton != null) _advancedButton.clicked -= OpenAdvancedEditor;
            if (_removeButton != null) _removeButton.clicked -= RemoveAi;
            if (_readyButton != null) _readyButton.clicked -= ToggleReady;
            if (_leaveButton != null) _leaveButton.clicked -= LeaveRoom;
            _callbacksBound = false;
        }

        private void TryBindService()
        {
            ClientRoomService candidate = NetworkManager.Instance?.RoomService;
            if (ReferenceEquals(candidate, _service)) return;
            UnbindService();
            _service = candidate;
            if (_service == null) return;
            _service.RoomJoined += HandleRoomJoined;
            _service.SeatSnapshotChanged += HandleSeatsChanged;
            _service.RoomNotice += HandleRoomNotice;
            _service.RoomError += HandleRoomError;
            _service.RoomClosed += HandleRoomClosed;
            _service.ReconnectSnapshotApplied += HandleReconnect;
            _service.ConnectionDiagnosticsChanged += HandleConnectionChanged;
        }

        private void UnbindService()
        {
            if (_service == null) return;
            _service.RoomJoined -= HandleRoomJoined;
            _service.SeatSnapshotChanged -= HandleSeatsChanged;
            _service.RoomNotice -= HandleRoomNotice;
            _service.RoomError -= HandleRoomError;
            _service.RoomClosed -= HandleRoomClosed;
            _service.ReconnectSnapshotApplied -= HandleReconnect;
            _service.ConnectionDiagnosticsChanged -= HandleConnectionChanged;
            _service = null;
        }

        private void HandleRoomJoined(RoomJoinedMessage message)
        {
            _notice = "房间已创建，选择空席可手动添加 AI。";
            _selectedSeatIndex = message?.seatIndex ?? -1;
            lobbyController?.ShowRoom();
            Refresh();
        }

        private void HandleSeatsChanged(RoomSeatMessage[] seats)
        {
            string nextSignature = GetSelectedAuthoritySignature();
            if (!string.Equals(_authoritySignature, nextSignature, StringComparison.Ordinal))
            {
                deckEditorToolkit?.CloseRoomAiEditorForAuthorityChange();
                _quickConfig.Clear();
                _authoritySignature = nextSignature;
            }
            if (_service?.HasRoom == true) lobbyController?.ShowRoom();
            Refresh();
        }

        private void HandleRoomNotice(RoomNoticeMessage message)
        {
            _notice = string.IsNullOrWhiteSpace(message?.message) ? message?.code : message.message;
            Refresh();
        }

        private void HandleRoomError(string message)
        {
            _notice = message;
            lobbyController?.SetRoomStatus(message);
            Refresh();
        }

        private void HandleRoomClosed(string reason)
        {
            deckEditorToolkit?.CloseRoomAiEditorForAuthorityChange();
            _quickConfig.Clear();
            _selectedSeatIndex = -1;
            lobbyController?.ShowHome(reason);
        }

        private void HandleReconnect(RoomGameSnapshot snapshot)
        {
            if (snapshot == null || ClientRecoverySceneRoutingPolicy.GetTarget((RoomState)snapshot.roomState) != ClientRecoverySceneTarget.Lobby) return;
            _notice = $"已恢复房间 {snapshot.roomId} 的权威状态。";
            _selectedSeatIndex = snapshot.requestingSeatIndex;
            lobbyController?.ShowRoom();
            Refresh();
        }

        private void HandleConnectionChanged(ClientConnectionDiagnostics diagnostics)
        {
            if (_roomConnection != null)
                _roomConnection.text = diagnostics?.Phase == ClientConnectionPhase.Ready
                    ? "权威状态已同步"
                    : "连接状态变化中";
        }

        public void Refresh()
        {
            if (_service?.HasRoom != true)
            {
                if (_viewRoom != null) _viewRoom.style.display = DisplayStyle.None;
                return;
            }

            RoomPanelViewModel view = RoomPanelViewModel.Build(
                _service.RoomId, _service.RoomState, _service.Seats, _service.SeatIndex, _notice);
            if (_selectedSeatIndex < 0 || _selectedSeatIndex > 3) _selectedSeatIndex = _service.SeatIndex;
            if (_roomId != null) _roomId.text = $"房间 {_service.RoomId}";
            if (_roomMode != null) _roomMode.text = GetModeText(_service.GameMode);
            if (_roomPreset != null) _roomPreset.text = GetPresetText(_service.AlienationPreset);
            RoomSeatViewModel host = view.Seats.FirstOrDefault(seat => seat.IsHost);
            if (_roomHost != null) _roomHost.text = host?.DisplayName ?? "等待转交";
            if (_roomState != null) _roomState.text = RoomPanelViewModel.GetRoomStateText(_service.RoomState);
            if (_roomConnection != null)
                _roomConnection.text = _service.ConnectionDiagnostics?.Phase == ClientConnectionPhase.Ready
                    ? "权威状态已同步"
                    : "正在同步连接状态";
            if (_noticeTitle != null) _noticeTitle.text = view.NoticeText;
            if (_noticeCopy != null)
                _noticeCopy.text = view.EmptyCount > 0
                    ? $"真人 {view.HumanCount} | 永久 AI {view.AiCount} | 空席 {view.EmptyCount}。移除 AI 后会立即阻止开局。"
                    : $"真人 {view.HumanCount} | 永久 AI {view.AiCount} | 空席 0。所有真人准备后进入对局。";
            if (_readyButton != null)
            {
                _readyButton.text = view.ReadyButtonText;
                _readyButton.SetEnabled(view.CanToggleReady && _service.CanSubmitCommands);
                _readyButton.tooltip = view.ReadyBlockedReason ?? "开局前可随时切换准备状态。";
            }

            for (int i = 0; i < _seatCards.Length; i++) RefreshSeatCard(_seatCards[i], view.Seats[i]);
            RefreshSelectedSeat(view);
        }

        private void RefreshSeatCard(SeatCardView card, RoomSeatViewModel seat)
        {
            if (card == null) return;
            card.Root?.EnableInClassList("room-seat-card-selected", seat.SeatIndex == _selectedSeatIndex);
            card.Root?.EnableInClassList("room-seat-card-local", seat.IsLocal);
            card.Root?.EnableInClassList("room-seat-card-empty", seat.State == RoomSeatVisualState.Empty);
            card.Root?.EnableInClassList("room-seat-card-offline",
                seat.State == RoomSeatVisualState.HumanOffline || seat.State == RoomSeatVisualState.TemporaryAiControl);
            card.Root?.EnableInClassList("room-seat-card-ai", seat.State == RoomSeatVisualState.PermanentAi);
            if (card.Number != null) card.Number.text = $"席位 {seat.SeatIndex + 1}";
            if (card.Name != null) card.Name.text = seat.DisplayName;
            if (card.Badges != null) card.Badges.text = BuildBadges(seat);
            if (card.Status != null) card.Status.text = seat.StatusText;
            if (card.Summary != null) card.Summary.text = BuildSeatSummary(seat);
            if (card.Action != null)
            {
                card.Action.style.display = seat.CanAddAi || seat.CanEditAi ? DisplayStyle.Flex : DisplayStyle.None;
                card.Action.text = seat.IsPermanentAi ? "配置 AI" : "添加 AI";
            }
        }

        private void RefreshSelectedSeat(RoomPanelViewModel roomView)
        {
            RoomSeatViewModel seat = roomView.Seats[_selectedSeatIndex];
            RoomSeatMessage authority = GetSeat(_selectedSeatIndex);
            if (_detailName != null) _detailName.text = seat.DisplayName;
            if (_detailMeta != null) _detailMeta.text = $"席位 {seat.SeatIndex + 1} | {seat.StatusText}";

            bool showAi = seat.IsPermanentAi || (seat.IsEmpty && seat.CanAddAi);
            if (!showAi)
            {
                _quickRoot.style.display = DisplayStyle.None;
                _readOnlyRoot.style.display = DisplayStyle.Flex;
                _detailAccess.text = seat.IsLocal ? "本家" : "只读";
                _readOnlyText.text = seat.IsEmpty
                    ? "当前是空席。只有房主可以在等待阶段添加永久 AI。"
                    : seat.IsLocal
                        ? $"本家构筑已通过服务端验证，总异化值 {_service.OwnTotalAlienation}。真人完整构筑不会向其他席位公开。"
                        : "真人构筑保持私有；这里只展示在线、准备和临时托管状态。";
                _quickConfig.Clear();
                return;
            }

            EnsureDraftForSelection(seat, authority);
            _quickRoot.style.display = DisplayStyle.Flex;
            _readOnlyRoot.style.display = DisplayStyle.None;
            bool editable = roomView.IsLocalHost
                            && (_service.RoomState == RoomState.WaitingForPlayers
                                || _service.RoomState == RoomState.WaitingForMatchReady);
            _detailAccess.text = editable ? "房主可编辑" : "公开只读";
            SetQuickControlsEnabled(editable);
            RefreshQuickDraft();
        }

        private void EnsureDraftForSelection(RoomSeatViewModel seat, RoomSeatMessage authority)
        {
            if (_quickConfig.SeatIndex == seat.SeatIndex && _quickConfig.Draft != null) return;
            if (seat.IsPermanentAi && authority?.aiConfig?.loadout != null)
            {
                _quickConfig.Select(seat.SeatIndex, false, new AiLoadoutDraft(
                    (AiDifficulty)authority.aiConfig.difficulty,
                    (AiLoadoutTemplate)authority.aiConfig.template,
                    authority.aiConfig.loadout,
                    _service.AlienationPreset));
            }
            else
            {
                PlayerLoadoutMessage loadout = AiTalentLoadoutFactory.Create(
                    _service.AlienationPreset, AiLoadoutTemplate.Stable, seat.SeatIndex, GetRoomSeed());
                _quickConfig.Select(seat.SeatIndex, true, new AiLoadoutDraft(
                    AiDifficulty.Standard, AiLoadoutTemplate.Stable, loadout, _service.AlienationPreset));
            }
            _authoritySignature = GetSelectedAuthoritySignature();
        }

        private void RefreshQuickDraft()
        {
            AiLoadoutDraft draft = _quickConfig.Draft;
            if (draft == null) return;
            AiLoadoutValidation validation = draft.Validate();
            PlayerLoadoutMessage message = draft.ToMessage();
            int mainCount = message.mainTalentSlotIds?.Count(id => !string.IsNullOrWhiteSpace(id)) ?? 0;
            int reserveCount = message.reserveTalentSlotIds?.Count(id => !string.IsNullOrWhiteSpace(id)) ?? 0;
            _difficultyBeginner.EnableInClassList("room-option-selected", draft.Difficulty == AiDifficulty.Beginner);
            _difficultyStandard.EnableInClassList("room-option-selected", draft.Difficulty == AiDifficulty.Standard);
            _templateAggressive.EnableInClassList("room-option-selected", draft.Template == AiLoadoutTemplate.Aggressive);
            _templateStable.EnableInClassList("room-option-selected", draft.Template == AiLoadoutTemplate.Stable);
            _templateSynergy.EnableInClassList("room-option-selected", draft.Template == AiLoadoutTemplate.TalentSynergy);
            _templateCopySelf.EnableInClassList("room-option-selected", draft.Template == AiLoadoutTemplate.Custom);
            _deckSummary.text = $"牌库：{validation.TotalTiles} / 34 张 | 异化值 {validation.DeckAlienation}";
            _talentSummary.text = $"天赋：主槽 {mainCount} / 6 | 备选 {reserveCount} / 3 | 主槽成本 {validation.TalentAlienation}";
            _budgetSummary.text = validation.IsValid
                ? $"总预算：{validation.TotalAlienation} / {validation.BudgetLimit} | 可应用"
                : $"总预算：{validation.TotalAlienation} / {validation.BudgetLimit} | {GetValidationText(validation)}";
            _budgetSummary.EnableInClassList("room-budget-error", !validation.IsValid);
            _applyButton.SetEnabled(_quickEditable && validation.IsValid && _service.CanSubmitCommands);
            _overwriteConfirm.style.display = _quickConfig.HasPendingOverwrite ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetQuickControlsEnabled(bool enabled)
        {
            _quickEditable = enabled;
            _difficultyBeginner.SetEnabled(enabled);
            _difficultyStandard.SetEnabled(enabled);
            _templateAggressive.SetEnabled(enabled);
            _templateStable.SetEnabled(enabled);
            _templateSynergy.SetEnabled(enabled);
            _templateCopySelf.SetEnabled(enabled);
            _applyButton.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            _advancedButton.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            _removeButton.style.display = enabled && !_quickConfig.IsAdding ? DisplayStyle.Flex : DisplayStyle.None;
            _applyButton.SetEnabled(enabled);
            _advancedButton.SetEnabled(enabled);
        }

        private void SelectSeat(int seatIndex)
        {
            if (_service?.HasRoom != true || seatIndex < 0 || seatIndex > 3) return;
            if (_selectedSeatIndex != seatIndex)
            {
                deckEditorToolkit?.CloseRoomAiEditorForAuthorityChange();
                _quickConfig.Clear();
            }
            _selectedSeatIndex = seatIndex;
            _authoritySignature = GetSelectedAuthoritySignature();
            Refresh();
        }

        private void SelectBeginner() { _quickConfig.Draft?.SetDifficulty(AiDifficulty.Beginner); RefreshQuickDraft(); }
        private void SelectStandard() { _quickConfig.Draft?.SetDifficulty(AiDifficulty.Standard); RefreshQuickDraft(); }
        private void SelectAggressive() => RequestTemplate(AiLoadoutTemplate.Aggressive);
        private void SelectStable() => RequestTemplate(AiLoadoutTemplate.Stable);
        private void SelectSynergy() => RequestTemplate(AiLoadoutTemplate.TalentSynergy);

        private void SelectCopySelf()
        {
            SavedDeck deck = GetSelectedSavedDeck();
            if (deck?.Config == null)
            {
                _notice = "当前没有可复制的本家已保存卡组。";
                Refresh();
                return;
            }
            PlayerLoadoutMessage message = PlayerLoadoutCodec.CreateMessage(
                deck.Config, deck.Talents ?? new TalentSlotConfig(), _service.AlienationPreset);
            ApplyTemplateRequest(AiLoadoutTemplate.Custom, message);
        }

        private void RequestTemplate(AiLoadoutTemplate template)
        {
            PlayerLoadoutMessage message = AiTalentLoadoutFactory.Create(
                _service.AlienationPreset, template, _selectedSeatIndex, GetRoomSeed());
            ApplyTemplateRequest(template, message);
        }

        private void ApplyTemplateRequest(AiLoadoutTemplate template, PlayerLoadoutMessage message)
        {
            _quickConfig.RequestTemplate(template, message);
            RefreshQuickDraft();
        }

        private void ConfirmTemplateOverwrite()
        {
            _quickConfig.ConfirmOverwrite();
            RefreshQuickDraft();
        }

        private void CancelTemplateOverwrite()
        {
            _quickConfig.CancelOverwrite();
            RefreshQuickDraft();
        }

        private void ApplyAiConfig()
        {
            AiLoadoutDraft draft = _quickConfig.Draft;
            if (draft == null || !draft.Validate().IsValid) return;
            bool sent = _quickConfig.IsAdding
                ? _service.AddAiSeat(_quickConfig.SeatIndex, draft.Difficulty, draft.Template, draft.ToMessage())
                : _service.UpdateAiSeat(_quickConfig.SeatIndex, draft.Difficulty, draft.Template, draft.ToMessage());
            if (!sent) return;
            _notice = _quickConfig.IsAdding ? "正在添加永久 AI..." : "正在更新永久 AI...";
            _applyButton.SetEnabled(false);
            Refresh();
        }

        private void RemoveAi()
        {
            if (_quickConfig.IsAdding || !_service.RemoveAiSeat(_quickConfig.SeatIndex)) return;
            deckEditorToolkit?.CloseRoomAiEditorForAuthorityChange();
            _notice = "正在移除永久 AI；出现空席后将阻止开局。";
            _removeButton.SetEnabled(false);
        }

        private void OpenAdvancedEditor()
        {
            if (_quickConfig.Draft == null || deckEditorToolkit == null) return;
            string name = GetSeat(_selectedSeatIndex)?.displayName ?? $"席位 {_selectedSeatIndex + 1} AI";
            deckEditorToolkit.OpenRoomAiDraft(
                _quickConfig.Draft,
                name,
                draft =>
                {
                    _quickConfig.AdoptAdvancedDraft(draft);
                    RefreshQuickDraft();
                });
        }

        private void ToggleReady()
        {
            RoomPanelViewModel view = RoomPanelViewModel.Build(
                _service.RoomId, _service.RoomState, _service.Seats, _service.SeatIndex, _notice);
            if (!view.CanToggleReady) return;
            _service.SetMatchReady(view.ReadyTarget);
            _notice = view.ReadyTarget ? "正在确认准备..." : "正在取消准备...";
            _readyButton.SetEnabled(false);
        }

        private void LeaveRoom()
        {
            deckEditorToolkit?.CloseRoomAiEditorForAuthorityChange();
            _service?.LeaveRoom();
            lobbyController?.ShowHome("已离开房间。");
        }

        private void CopyRoomId()
        {
            if (string.IsNullOrWhiteSpace(_service?.RoomId)) return;
            GUIUtility.systemCopyBuffer = _service.RoomId;
            _notice = "房间号已复制。";
            Refresh();
        }

        private RoomSeatMessage GetSeat(int seatIndex)
        {
            return (_service?.Seats ?? Array.Empty<RoomSeatMessage>())
                .FirstOrDefault(seat => seat != null && seat.seatIndex == seatIndex);
        }

        private string GetSelectedAuthoritySignature()
        {
            RoomSeatMessage seat = GetSeat(_selectedSeatIndex);
            return seat?.aiConfig == null ? $"{_selectedSeatIndex}:{seat?.isOccupied}" : JsonUtility.ToJson(seat.aiConfig);
        }

        private int GetRoomSeed() => string.IsNullOrEmpty(_service?.RoomId)
            ? 0
            : StringComparer.Ordinal.GetHashCode(_service.RoomId);

        private static string BuildBadges(RoomSeatViewModel seat)
        {
            string[] badges =
            {
                seat.IsHost ? "房主" : null,
                seat.IsLocal ? "本家" : null,
                seat.IsPermanentAi ? seat.DifficultyText : null,
                seat.IsReady ? "已准备" : null
            };
            return string.Join(" | ", badges.Where(text => !string.IsNullOrEmpty(text)));
        }

        private string BuildSeatSummary(RoomSeatViewModel seat)
        {
            if (seat.IsEmpty) return "不创建虚假玩家；真人加入不会替换已配置 AI。";
            if (seat.IsPermanentAi)
            {
                RoomSeatMessage authority = GetSeat(seat.SeatIndex);
                if (authority?.aiConfig?.loadout == null) return "等待 AI 公开构筑投影。";
                var draft = new AiLoadoutDraft(
                    (AiDifficulty)authority.aiConfig.difficulty,
                    (AiLoadoutTemplate)authority.aiConfig.template,
                    authority.aiConfig.loadout,
                    _service.AlienationPreset);
                AiLoadoutValidation validation = draft.Validate();
                return $"34 张牌库 | 总异化 {validation.TotalAlienation} / {validation.BudgetLimit}\n完整 6+3 构筑向房内真人公开";
            }
            if (seat.State == RoomSeatVisualState.TemporaryAiControl)
                return "身份与构筑保持真人所有；重连后在安全边界取回控制。";
            return seat.IsLocal
                ? $"本家构筑总异化 {_service.OwnTotalAlienation}；完整内容仅本家可见。"
                : "构筑已通过服务端验证；完整内容保持私有。";
        }

        private static string GetModeText(GameMode mode) => mode switch
        {
            GameMode.EastOnly => "东风局 4 局",
            GameMode.HalfGame => "半庄 8 局",
            GameMode.FullGame => "全庄 16 局",
            _ => "单局 1 局"
        };

        private static string GetPresetText(AlienationPreset preset) => preset switch
        {
            AlienationPreset.Low => "低档 40",
            AlienationPreset.High => "高档 120",
            _ => "标准 80"
        };

        private static string GetValidationText(AiLoadoutValidation validation)
        {
            if (validation.TotalTiles != 34) return $"牌数 {validation.TotalTiles}，必须为 34";
            if (validation.TotalAlienation > validation.BudgetLimit) return "预算超限";
            return "槽位或构筑无效";
        }

        private static SavedDeck GetSelectedSavedDeck()
        {
            var profile = ProfileManager.Instance?.CurrentProfile;
            if (profile?.SavedDecks == null || profile.SavedDecks.Count == 0) return null;
            int index = profile.SelectedDeckIndex;
            return index >= 0 && index < profile.SavedDecks.Count ? profile.SavedDecks[index] : null;
        }

        private sealed class SeatCardView
        {
            public VisualElement Root { get; }
            public Label Number { get; }
            public Label Name { get; }
            public Label Badges { get; }
            public Label Status { get; }
            public Label Summary { get; }
            public Button Action { get; }

            public SeatCardView(VisualElement root, Label number, Label name, Label badges,
                Label status, Label summary, Button action)
            {
                Root = root;
                Number = number;
                Name = name;
                Badges = badges;
                Status = status;
                Summary = summary;
                Action = action;
            }
        }
    }
}
