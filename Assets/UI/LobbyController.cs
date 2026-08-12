using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using MahjongGame.Core;
using MahjongGame.Core.Network.Data;
using MahjongGame.Systems;
using MahjongGame.Talents;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.UI
{
    public class LobbyController : MonoBehaviour
    {
        [SerializeField]
        private UIDocument document;

        [SerializeField]
        private DeckEditorToolkit deckEditorToolkit;

        // Tabs
        private Button tabHome;
        private Button tabWorkshop;
        private Button tabCollection;
        private Button tabSettings;

        // Views
        private VisualElement viewHome;
        private VisualElement viewWorkshop;
        private VisualElement viewCollection;
        private VisualElement viewSettings;
        private VisualElement viewRoom;
        private VisualElement sidebar;

        // Elements
        private Label welcomeLabel;
        private Label deckNameLabel;
        private Label scoreLabel;
        private Button matchmakingButton;
        private Button btnDeckPrev;
        private Button btnDeckNext;
        private TextField roomIdInput;
        private Button joinRoomButton;
        private Label roomStatusLabel;
        private VisualElement roomAdmissionBlocker;
        private Label roomAdmissionBlockerLabel;

        // Dedicated room view
        private VisualElement roomSeatRows;
        private Label roomIdLabel;
        private Label roomStateLabel;
        private Label roomModeLabel;
        private Label roomPresetPublicLabel;
        private Label roomAiFillLabel;
        private Label roomHumanCountLabel;
        private Label roomLocalDeckLabel;
        private Label roomLocalAlienationLabel;
        private Label roomLoadoutLockLabel;
        private Label roomWaitingLabel;
        private Button leaveRoomButton;
        private Button roomReadyButton;
        private readonly List<RoomSeatRowView> roomSeatRowViews = new List<RoomSeatRowView>();

        // GameMode Selector
        private Label modeNameLabel;
        private Button btnModePrev;
        private Button btnModeNext;
        private static readonly string[] GameModeNames = { "单局", "东风局", "半庄", "全庄" };
        private Label roomPresetLabel;
        private Button btnRoomPresetPrev;
        private Button btnRoomPresetNext;
        private AlienationPreset _pendingRoomAlienationPreset = AlienationPreset.Standard;
        
        // Settings Elements
        private Slider masterVolumeSlider;
        private Slider musicVolumeSlider;
        private Slider sfxVolumeSlider;
        private Toggle debugModeToggle;

        // Cached callbacks for unregistration
        private EventCallback<ChangeEvent<float>> _onMasterVolumeChanged;
        private EventCallback<ChangeEvent<float>> _onMusicVolumeChanged;
        private EventCallback<ChangeEvent<float>> _onSfxVolumeChanged;
        private EventCallback<ChangeEvent<bool>> _onDebugModeChanged;

        private void OnTabHomeClicked() => ShowTab("Home");
        private void OnTabWorkshopClicked() => ShowTab("Workshop");
        private void OnTabCollectionClicked() => ShowTab("Collection");
        private void OnTabSettingsClicked() => ShowTab("Settings");

        private void OnEnable()
        {
            if (document == null)
                document = GetComponent<UIDocument>();

            var root = document.rootVisualElement;
            if (root == null) return;

            tabHome = root.Q<Button>("TabHome");
            tabWorkshop = root.Q<Button>("TabWorkshop");
            tabCollection = root.Q<Button>("TabCollection");
            tabSettings = root.Q<Button>("TabSettings");

            viewHome = root.Q<VisualElement>("ViewHome");
            viewWorkshop = root.Q<VisualElement>("ViewWorkshop");
            viewCollection = root.Q<VisualElement>("ViewCollection");
            viewSettings = root.Q<VisualElement>("ViewSettings");
            viewRoom = root.Q<VisualElement>("ViewRoom");
            sidebar = root.Q<VisualElement>("Sidebar");

            welcomeLabel = root.Q<Label>("WelcomeLabel");
            deckNameLabel = root.Q<Label>("DeckNameLabel");
            scoreLabel = root.Q<Label>("ScoreLabel");
            matchmakingButton = root.Q<Button>("MatchmakingButton");
            btnDeckPrev = root.Q<Button>("BtnDeckPrev");
            btnDeckNext = root.Q<Button>("BtnDeckNext");
            roomIdInput = root.Q<TextField>("RoomIdInput");
            joinRoomButton = root.Q<Button>("JoinRoomButton");
            roomStatusLabel = root.Q<Label>("RoomStatusLabel");
            roomAdmissionBlocker = root.Q<VisualElement>("RoomAdmissionBlocker");
            roomAdmissionBlockerLabel = root.Q<Label>("RoomAdmissionBlockerLabel");

            roomSeatRows = root.Q<VisualElement>("RoomSeatRows");
            roomIdLabel = root.Q<Label>("RoomIdLabel");
            roomStateLabel = root.Q<Label>("RoomStateLabel");
            roomModeLabel = root.Q<Label>("RoomModeLabel");
            roomPresetPublicLabel = root.Q<Label>("RoomPresetPublicLabel");
            roomAiFillLabel = root.Q<Label>("RoomAiFillLabel");
            roomHumanCountLabel = root.Q<Label>("RoomHumanCountLabel");
            roomLocalDeckLabel = root.Q<Label>("RoomLocalDeckLabel");
            roomLocalAlienationLabel = root.Q<Label>("RoomLocalAlienationLabel");
            roomLoadoutLockLabel = root.Q<Label>("RoomLoadoutLockLabel");
            roomWaitingLabel = root.Q<Label>("RoomWaitingLabel");
            leaveRoomButton = root.Q<Button>("LeaveRoomButton");
            roomReadyButton = root.Q<Button>("RoomReadyButton");

            modeNameLabel = root.Q<Label>("ModeNameLabel");
            btnModePrev = root.Q<Button>("BtnModePrev");
            btnModeNext = root.Q<Button>("BtnModeNext");
            roomPresetLabel = root.Q<Label>("RoomPresetLabel");
            btnRoomPresetPrev = root.Q<Button>("BtnRoomPresetPrev");
            btnRoomPresetNext = root.Q<Button>("BtnRoomPresetNext");

            masterVolumeSlider = root.Q<Slider>("MasterVolumeSlider");
            musicVolumeSlider = root.Q<Slider>("MusicVolumeSlider");
            sfxVolumeSlider = root.Q<Slider>("SFXVolumeSlider");
            debugModeToggle = root.Q<Toggle>("DebugModeToggle");

            if (tabHome != null) tabHome.clicked += OnTabHomeClicked;
            if (tabWorkshop != null) tabWorkshop.clicked += OnTabWorkshopClicked;
            if (tabCollection != null) tabCollection.clicked += OnTabCollectionClicked;
            if (tabSettings != null) tabSettings.clicked += OnTabSettingsClicked;

            if (matchmakingButton != null) matchmakingButton.clicked += OnMatchmakingClicked;
            if (joinRoomButton != null) joinRoomButton.clicked += OnJoinRoomClicked;
            if (leaveRoomButton != null) leaveRoomButton.clicked += OnLeaveRoomClicked;
            if (roomReadyButton != null) roomReadyButton.clicked += OnReadyRoomClicked;
            if (btnDeckPrev != null) btnDeckPrev.clicked += OnDeckPrevClicked;
            if (btnDeckNext != null) btnDeckNext.clicked += OnDeckNextClicked;
            if (btnModePrev != null) btnModePrev.clicked += OnModePrevClicked;
            if (btnModeNext != null) btnModeNext.clicked += OnModeNextClicked;
            if (btnRoomPresetPrev != null) btnRoomPresetPrev.clicked += OnRoomPresetPrevClicked;
            if (btnRoomPresetNext != null) btnRoomPresetNext.clicked += OnRoomPresetNextClicked;

            if (ProfileManager.Instance != null && ProfileManager.Instance.CurrentProfile != null)
            {
                if (welcomeLabel != null)
                    welcomeLabel.text = $"Welcome, {ProfileManager.Instance.CurrentProfile.Nickname}";
                    
                LoadSettingsUI();
            }

            RegisterSettingsCallbacks();

            if (deckEditorToolkit != null)
            {
                deckEditorToolkit.OnDeckSaved += HandleDeckSaved;
                deckEditorToolkit.OnExitRequested += HandleDeckEditorExit;
            }

            SubscribeRoomService();
            if (NetworkManager.Instance?.RoomService?.HasRoom == true)
            {
                ShowRoom();
            }
            else
            {
                ShowHome();
            }
            if (!string.IsNullOrWhiteSpace(NetworkManager.Instance?.RoomService?.LastRoomClosureReason) && NetworkManager.Instance.RoomService.HasRoom == false)
                SetRoomStatus(NetworkManager.Instance.RoomService.LastRoomClosureReason);
        }

        private void OnDisable()
        {
            if (tabHome != null) tabHome.clicked -= OnTabHomeClicked;
            if (tabWorkshop != null) tabWorkshop.clicked -= OnTabWorkshopClicked;
            if (tabCollection != null) tabCollection.clicked -= OnTabCollectionClicked;
            if (tabSettings != null) tabSettings.clicked -= OnTabSettingsClicked;
            if (matchmakingButton != null) matchmakingButton.clicked -= OnMatchmakingClicked;
            if (joinRoomButton != null) joinRoomButton.clicked -= OnJoinRoomClicked;
            if (leaveRoomButton != null) leaveRoomButton.clicked -= OnLeaveRoomClicked;
            if (roomReadyButton != null) roomReadyButton.clicked -= OnReadyRoomClicked;
            UnsubscribeRoomService();
            if (btnDeckPrev != null) btnDeckPrev.clicked -= OnDeckPrevClicked;
            if (btnDeckNext != null) btnDeckNext.clicked -= OnDeckNextClicked;
            if (btnModePrev != null) btnModePrev.clicked -= OnModePrevClicked;
            if (btnModeNext != null) btnModeNext.clicked -= OnModeNextClicked;
            if (btnRoomPresetPrev != null) btnRoomPresetPrev.clicked -= OnRoomPresetPrevClicked;
            if (btnRoomPresetNext != null) btnRoomPresetNext.clicked -= OnRoomPresetNextClicked;

            if (deckEditorToolkit != null)
            {
                deckEditorToolkit.OnDeckSaved -= HandleDeckSaved;
                deckEditorToolkit.OnExitRequested -= HandleDeckEditorExit;
            }

            UnregisterSettingsCallbacks();
        }

        private void HandleDeckSaved(DeckConfig newConfig)
        {
            Debug.Log("Deck Saved! Alienation Score: " + newConfig.AlienationScore);
            RefreshHomeDeckInfo();
        }

        private void RefreshHomeDeckInfo()
        {
            if (ProfileManager.Instance?.CurrentProfile == null) return;

            var profile = ProfileManager.Instance.CurrentProfile;
            string deckName = "标准牌库（默认）";
            int alienation = 0;

            if (profile.SavedDecks.Count > 0)
            {
                int idx = profile.SelectedDeckIndex;
                if (idx < 0 || idx >= profile.SavedDecks.Count)
                {
                    idx = 0;
                    profile.SelectedDeckIndex = idx;
                }

                var deck = profile.SavedDecks[idx];
                deckName = deck.DeckName;
                alienation = DeckConfig.CalculateTotalAlienation(deck.Config, deck.Talents);
            }

            if (deckNameLabel != null)
                deckNameLabel.text = $"当前牌库: {deckName}";
            if (scoreLabel != null)
                scoreLabel.text = $"异化值: {alienation}";

            bool canCycle = profile.SavedDecks.Count > 1;
            if (btnDeckPrev != null) btnDeckPrev.SetEnabled(canCycle);
            if (btnDeckNext != null) btnDeckNext.SetEnabled(canCycle);
        }

        private void OnDeckPrevClicked() => CycleDeck(-1);
        private void OnDeckNextClicked() => CycleDeck(1);

        private void OnModePrevClicked() => CycleMode(-1);
        private void OnModeNextClicked() => CycleMode(1);

        private void CycleMode(int direction)
        {
            if (ProfileManager.Instance?.CurrentProfile == null) return;

            var settings = ProfileManager.Instance.CurrentProfile.Settings;
            int count = GameModeNames.Length;
            settings.SelectedGameMode = ((settings.SelectedGameMode + direction) % count + count) % count;
            ProfileManager.Instance.SaveProfile();
            RefreshGameModeDisplay();
            RefreshPendingRoomPresetDisplay();
        }

        private void RefreshGameModeDisplay()
        {
            if (modeNameLabel == null) return;

            int modeIdx = 0;
            if (ProfileManager.Instance?.CurrentProfile != null)
                modeIdx = ProfileManager.Instance.CurrentProfile.Settings.SelectedGameMode;

            if (modeIdx < 0 || modeIdx >= GameModeNames.Length)
                modeIdx = 0;

            modeNameLabel.text = $"对战模式: {GameModeNames[modeIdx]}";
        }

        private void OnRoomPresetPrevClicked() => CyclePendingRoomPreset(-1);
        private void OnRoomPresetNextClicked() => CyclePendingRoomPreset(1);

        private void CyclePendingRoomPreset(int direction)
        {
            AlienationPreset[] presets = { AlienationPreset.Low, AlienationPreset.Standard, AlienationPreset.High };
            int index = System.Array.IndexOf(presets, _pendingRoomAlienationPreset);
            if (index < 0) index = 1;
            _pendingRoomAlienationPreset = presets[((index + direction) % presets.Length + presets.Length) % presets.Length];
            HideRoomAdmissionBlocker();
            RefreshPendingRoomPresetDisplay();
        }

        private void RefreshPendingRoomPresetDisplay()
        {
            if (roomPresetLabel != null)
                roomPresetLabel.text = $"房间异化档位: {RoomLoadoutAdmissionPresentationPolicy.GetDisplayName(_pendingRoomAlienationPreset)}";
        }

        private void CycleDeck(int direction)
        {
            if (ProfileManager.Instance?.CurrentProfile == null) return;

            var profile = ProfileManager.Instance.CurrentProfile;
            if (profile.SavedDecks.Count <= 1) return;

            int count = profile.SavedDecks.Count;
            int newIndex = ((profile.SelectedDeckIndex + direction) % count + count) % count;
            profile.SelectedDeckIndex = newIndex;
            ProfileManager.Instance.SaveProfile();
            RefreshHomeDeckInfo();
        }

        private void HandleDeckEditorExit()
        {
            ShowTab("Home");
        }

        private void LoadSettingsUI()
        {
            var settings = ProfileManager.Instance.CurrentProfile.Settings;
            if (masterVolumeSlider != null) masterVolumeSlider.value = settings.MasterVolume;
            if (musicVolumeSlider != null) musicVolumeSlider.value = settings.MusicVolume;
            if (sfxVolumeSlider != null) sfxVolumeSlider.value = settings.SFXVolume;
            if (debugModeToggle != null) debugModeToggle.value = settings.DebugMode;
        }

        private void RegisterSettingsCallbacks()
        {
            _onMasterVolumeChanged = evt => {
                ProfileManager.Instance.CurrentProfile.Settings.MasterVolume = evt.newValue;
                ProfileManager.Instance.SaveProfile();
            };
            _onMusicVolumeChanged = evt => {
                ProfileManager.Instance.CurrentProfile.Settings.MusicVolume = evt.newValue;
                ProfileManager.Instance.SaveProfile();
            };
            _onSfxVolumeChanged = evt => {
                ProfileManager.Instance.CurrentProfile.Settings.SFXVolume = evt.newValue;
                ProfileManager.Instance.SaveProfile();
            };
            _onDebugModeChanged = evt => {
                ProfileManager.Instance.CurrentProfile.Settings.DebugMode = evt.newValue;
                ProfileManager.Instance.SaveProfile();
            };

            if (masterVolumeSlider != null) masterVolumeSlider.RegisterValueChangedCallback(_onMasterVolumeChanged);
            if (musicVolumeSlider != null) musicVolumeSlider.RegisterValueChangedCallback(_onMusicVolumeChanged);
            if (sfxVolumeSlider != null) sfxVolumeSlider.RegisterValueChangedCallback(_onSfxVolumeChanged);
            if (debugModeToggle != null) debugModeToggle.RegisterValueChangedCallback(_onDebugModeChanged);
        }

        private void UnregisterSettingsCallbacks()
        {
            if (masterVolumeSlider != null) masterVolumeSlider.UnregisterValueChangedCallback(_onMasterVolumeChanged);
            if (musicVolumeSlider != null) musicVolumeSlider.UnregisterValueChangedCallback(_onMusicVolumeChanged);
            if (sfxVolumeSlider != null) sfxVolumeSlider.UnregisterValueChangedCallback(_onSfxVolumeChanged);
            if (debugModeToggle != null) debugModeToggle.UnregisterValueChangedCallback(_onDebugModeChanged);
        }

        private void ShowTab(string tabName)
        {
            if (NetworkManager.Instance?.RoomService?.HasRoom == true)
            {
                ShowRoom();
                return;
            }

            if (viewRoom != null) viewRoom.style.display = DisplayStyle.None;
            if (viewHome != null) viewHome.style.display = tabName == "Home" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewWorkshop != null) viewWorkshop.style.display = tabName == "Workshop" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewCollection != null) viewCollection.style.display = tabName == "Collection" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewSettings != null) viewSettings.style.display = tabName == "Settings" ? DisplayStyle.Flex : DisplayStyle.None;

            if (tabHome != null) UpdateTabStyle(tabHome, tabName == "Home");
            if (tabWorkshop != null) UpdateTabStyle(tabWorkshop, tabName == "Workshop");
            if (tabCollection != null) UpdateTabStyle(tabCollection, tabName == "Collection");
            if (tabSettings != null) UpdateTabStyle(tabSettings, tabName == "Settings");

            // Handle independent DeckEditor UI — 用 display 切换避免 UIDocument 重建
            if (deckEditorToolkit != null)
            {
                var deckRoot = deckEditorToolkit.GetComponent<UIDocument>()?.rootVisualElement;
                if (deckRoot != null)
                    deckRoot.style.display = tabName == "Workshop" ? DisplayStyle.Flex : DisplayStyle.None;

                if (tabName == "Workshop")
                    deckEditorToolkit.RefreshDeckList();
            }

            if (tabName == "Home")
            {
                RefreshHomeDeckInfo();
                RefreshGameModeDisplay();
            }
        }

        private void ShowHome(string statusMessage = null)
        {
            if (sidebar != null) sidebar.style.display = DisplayStyle.Flex;
            if (viewRoom != null) viewRoom.style.display = DisplayStyle.None;
            if (matchmakingButton != null) matchmakingButton.SetEnabled(true);
            if (joinRoomButton != null) joinRoomButton.SetEnabled(true);
            ShowTab("Home");
            if (!string.IsNullOrWhiteSpace(statusMessage)) SetRoomStatus(statusMessage);
        }

        private void ShowRoom()
        {
            if (sidebar != null) sidebar.style.display = DisplayStyle.None;
            if (viewHome != null) viewHome.style.display = DisplayStyle.None;
            if (viewWorkshop != null) viewWorkshop.style.display = DisplayStyle.None;
            if (viewCollection != null) viewCollection.style.display = DisplayStyle.None;
            if (viewSettings != null) viewSettings.style.display = DisplayStyle.None;
            if (viewRoom != null) viewRoom.style.display = DisplayStyle.Flex;

            if (deckEditorToolkit != null)
            {
                var deckRoot = deckEditorToolkit.GetComponent<UIDocument>()?.rootVisualElement;
                if (deckRoot != null) deckRoot.style.display = DisplayStyle.None;
            }

            RefreshRoomView();
        }

        private void RefreshRoomView()
        {
            var room = NetworkManager.Instance?.RoomService;
            if (room?.HasRoom != true) return;

            if (roomIdLabel != null) roomIdLabel.text = $"房间 {room.RoomId}";
            if (roomStateLabel != null) roomStateLabel.text = GetRoomStateText(room.RoomState);
            if (roomModeLabel != null) roomModeLabel.text = $"对局模式：{GetGameModeText(room.GameMode)}";
            if (roomPresetPublicLabel != null) roomPresetPublicLabel.text = $"异化档位：{RoomLoadoutAdmissionPresentationPolicy.GetDisplayName(room.AlienationPreset)}";
            if (roomAiFillLabel != null) roomAiFillLabel.text = $"AI 补位：{(room.AiFillEnabled ? "已开启" : "已关闭")}";

            int humanCount = 0;
            foreach (var seat in room.Seats)
            {
                if (seat != null && seat.isOccupied && !seat.isAi) humanCount++;
            }
            if (roomHumanCountLabel != null) roomHumanCountLabel.text = $"真人玩家：{humanCount}/4";

            if (roomLocalDeckLabel != null) roomLocalDeckLabel.text = $"牌库：{GetSelectedDeckName()}";
            if (roomLocalAlienationLabel != null) roomLocalAlienationLabel.text = $"本家异化：{room.OwnTotalAlienation} / {AlienationBudgetPolicy.GetLimit(room.AlienationPreset)}";
            if (roomLoadoutLockLabel != null) roomLoadoutLockLabel.text = "构筑已锁定（服务器已确认）";

            bool localReady = room.SeatIndex >= 0 && room.SeatIndex < room.Seats.Length && room.Seats[room.SeatIndex] != null && room.Seats[room.SeatIndex].isReady;
            bool canReady = room.RoomState == RoomState.WaitingForPlayers || room.RoomState == RoomState.WaitingForMatchReady;
            if (roomReadyButton != null) roomReadyButton.SetEnabled(canReady && !localReady);
            if (roomWaitingLabel != null)
                roomWaitingLabel.text = localReady ? "已确认准备，等待其他真人玩家。" : "等待所有真人玩家确认准备。";

            EnsureRoomSeatRows();
            for (int index = 0; index < roomSeatRowViews.Count; index++)
            {
                RoomSeatMessage seat = index < room.Seats.Length ? room.Seats[index] : null;
                UpdateRoomSeatRow(roomSeatRowViews[index], index, seat, index == room.SeatIndex, room.AlienationPreset);
            }
        }

        private void EnsureRoomSeatRows()
        {
            if (roomSeatRows == null) return;
            while (roomSeatRowViews.Count < 4)
            {
                var root = new VisualElement();
                root.AddToClassList("room-seat-row");
                var number = new Label(); number.AddToClassList("room-seat-number"); root.Add(number);
                var name = new Label(); name.AddToClassList("room-seat-name"); root.Add(name);
                var kind = new Label(); kind.AddToClassList("room-seat-kind"); root.Add(kind);
                var alienation = new Label(); alienation.AddToClassList("room-seat-alienation"); root.Add(alienation);
                var ready = new Label(); ready.AddToClassList("room-seat-ready"); root.Add(ready);
                roomSeatRows.Add(root);
                roomSeatRowViews.Add(new RoomSeatRowView(root, number, name, kind, alienation, ready));
            }
        }

        private static void UpdateRoomSeatRow(RoomSeatRowView row, int seatIndex, RoomSeatMessage seat, bool isLocal,
            AlienationPreset alienationPreset)
        {
            if (isLocal) row.Root.AddToClassList("room-seat-row-local");
            else row.Root.RemoveFromClassList("room-seat-row-local");

            row.Number.text = $"席位 {seatIndex + 1}";
            if (seat == null || !seat.isOccupied)
            {
                row.Name.text = "空席";
                row.Kind.text = "--";
                row.Alienation.text = "异化：--";
                row.Ready.text = "等待";
                return;
            }

            row.Name.text = string.IsNullOrWhiteSpace(seat.displayName) ? $"玩家 {seatIndex + 1}" : seat.displayName;
            row.Kind.text = seat.isAi ? "AI" : "真人";
            row.Alienation.text = $"档位：{RoomLoadoutAdmissionPresentationPolicy.GetDisplayName(alienationPreset)}";
            row.Ready.text = seat.isReady ? "已准备" : "未准备";
        }

        private static string GetRoomStateText(RoomState state) => state switch
        {
            RoomState.WaitingForPlayers => "等待玩家加入",
            RoomState.WaitingForMatchReady => "等待准备",
            RoomState.LoadingGameScene => "正在进入对局",
            RoomState.WaitingForNextRound => "等待下一局",
            RoomState.InRound => "对局进行中",
            _ => "房间已关闭"
        };

        private static string GetGameModeText(GameMode mode) => mode switch
        {
            GameMode.EastOnly => "东风局",
            GameMode.HalfGame => "半庄",
            GameMode.FullGame => "全庄",
            _ => "单局"
        };

        private static string GetSelectedDeckName()
        {
            var profile = ProfileManager.Instance?.CurrentProfile;
            if (profile?.SavedDecks == null || profile.SavedDecks.Count == 0) return "标准牌库";
            int index = profile.SelectedDeckIndex;
            if (index < 0 || index >= profile.SavedDecks.Count) return "本地构筑";
            return string.IsNullOrWhiteSpace(profile.SavedDecks[index]?.DeckName) ? "未命名构筑" : profile.SavedDecks[index].DeckName;
        }

        private void UpdateTabStyle(Button btn, bool isActive)
        {
            if (isActive)
            {
                btn.AddToClassList("active-tab");
            }
            else
            {
                btn.RemoveFromClassList("active-tab");
            }
        }

        private void OnMatchmakingClicked()
        {
            if (NetworkManager.Instance == null)
            {
                Debug.LogError("NetworkManager not ready");
                return;
            }

            SavedDeck selectedDeck = GetSelectedSavedDeck();
            AlienationPreset loadoutPreset = selectedDeck?.AlienationPreset ?? AlienationPreset.Standard;
            int total = selectedDeck?.AlienationScore ?? 0;
            RoomLoadoutAdmissionView admission = RoomLoadoutAdmissionPresentationPolicy.Validate(
                loadoutPreset, _pendingRoomAlienationPreset, total);
            if (!admission.CanEnter)
            {
                ShowRoomAdmissionBlocker(admission.Message);
                return;
            }
            HideRoomAdmissionBlocker();
            if (!NetworkManager.Instance.RoomService.CreateRoom(GetSelectedGameMode(), _pendingRoomAlienationPreset, GetNickname())) return;
            SetRoomStatus("正在创建新房间；房间号由服务器分配。");
        }

        private void OnJoinRoomClicked()
        {
            HideRoomAdmissionBlocker();
            if (NetworkManager.Instance == null || string.IsNullOrWhiteSpace(roomIdInput?.value)) { SetRoomStatus("请输入要加入的房间号。"); return; }
            if (!NetworkManager.Instance.RoomService.JoinRoom(roomIdInput.value, GetNickname())) return;
            SetRoomStatus("正在加入房间...");
        }

        private void OnReadyRoomClicked()
        {
            if (NetworkManager.Instance?.RoomService?.HasRoom != true) { SetRoomStatus("请先创建或加入房间。"); return; }
            NetworkManager.Instance.RoomService.SendReady(ReadyPhase.MatchStart);
            if (roomReadyButton != null) roomReadyButton.SetEnabled(false);
            if (roomWaitingLabel != null) roomWaitingLabel.text = "已确认准备，等待当前房间内所有真人玩家。";
            SetRoomStatus("已准备，等待当前房间内所有真人玩家准备。");
        }

        private void OnLeaveRoomClicked()
        {
            var room = NetworkManager.Instance?.RoomService;
            if (room == null) return;
            room.LeaveRoom();
            ShowHome("已离开房间。");
        }

        private GameMode GetSelectedGameMode()
        {
            int mode = ProfileManager.Instance?.CurrentProfile?.Settings.SelectedGameMode ?? 0;
            return mode >= 0 && mode <= 3 ? (GameMode)mode : GameMode.Single;
        }

        private string GetNickname() => ProfileManager.Instance?.CurrentProfile?.Nickname ?? "Player";

        private static SavedDeck GetSelectedSavedDeck()
        {
            var profile = ProfileManager.Instance?.CurrentProfile;
            if (profile?.SavedDecks == null || profile.SavedDecks.Count == 0) return null;
            int index = profile.SelectedDeckIndex;
            return index >= 0 && index < profile.SavedDecks.Count ? profile.SavedDecks[index] : null;
        }

        private void ShowRoomAdmissionBlocker(string message)
        {
            if (roomAdmissionBlockerLabel != null) roomAdmissionBlockerLabel.text = message;
            if (roomAdmissionBlocker != null) roomAdmissionBlocker.style.display = DisplayStyle.Flex;
            SetRoomStatus(message);
        }

        private void HideRoomAdmissionBlocker()
        {
            if (roomAdmissionBlocker != null) roomAdmissionBlocker.style.display = DisplayStyle.None;
        }

        private void SubscribeRoomService()
        {
            var service = NetworkManager.Instance?.RoomService;
            if (service == null) return;
            service.RoomJoined += HandleRoomJoined;
            service.SeatSnapshotChanged += HandleSeatSnapshotChanged;
            service.RoomError += HandleRoomError;
            service.RoomClosed += HandleRoomClosed;
            service.ReconnectSnapshotApplied += HandleReconnectSnapshotApplied;
        }

        private void UnsubscribeRoomService()
        {
            var service = NetworkManager.Instance?.RoomService;
            if (service == null) return;
            service.RoomJoined -= HandleRoomJoined;
            service.SeatSnapshotChanged -= HandleSeatSnapshotChanged;
            service.RoomError -= HandleRoomError;
            service.RoomClosed -= HandleRoomClosed;
            service.ReconnectSnapshotApplied -= HandleReconnectSnapshotApplied;
        }

        private void HandleRoomJoined(RoomJoinedMessage message)
        {
            ShowRoom();
            SetRoomStatus($"当前房间：{message.roomId} ｜ 我的席位：{message.seatIndex + 1} ｜ 请点击“确认准备”。");
        }
        private void HandleSeatSnapshotChanged(RoomSeatMessage[] seats)
        {
            var room = NetworkManager.Instance?.RoomService;
            if (room?.HasRoom == true)
            {
                ShowRoom();
                RefreshRoomView();
                return;
            }
            if (room == null) return;
            int humanCount = System.Array.FindAll(seats, seat => seat != null && seat.isOccupied && !seat.isAi).Length;
            SetRoomStatus($"当前房间：{room.RoomId} ｜ 我的席位：{room.SeatIndex + 1} ｜ 真人玩家：{humanCount}/4");
        }
        private void HandleReconnectSnapshotApplied(RoomGameSnapshot snapshot)
        {
            if (snapshot == null || ClientRecoverySceneRoutingPolicy.GetTarget((RoomState)snapshot.roomState) != ClientRecoverySceneTarget.Lobby) return;
            ShowRoom();
            SetRoomStatus($"已恢复房间 {snapshot.roomId}，我的席位：{snapshot.requestingSeatIndex + 1}。");
        }
        private void HandleRoomClosed(string reason) => ShowHome(reason);

        private void HandleRoomError(string message)
        {
            ShowRoomAdmissionBlocker(message);
            SetRoomStatus(message);
            if (NetworkManager.Instance?.RoomService?.HasRoom == true && roomWaitingLabel != null)
                roomWaitingLabel.text = message;
        }
        private void SetRoomStatus(string message) { if (roomStatusLabel != null) roomStatusLabel.text = message; }

        private sealed class RoomSeatRowView
        {
            public VisualElement Root { get; }
            public Label Number { get; }
            public Label Name { get; }
            public Label Kind { get; }
            public Label Alienation { get; }
            public Label Ready { get; }

            public RoomSeatRowView(VisualElement root, Label number, Label name, Label kind, Label alienation, Label ready)
            {
                Root = root;
                Number = number;
                Name = name;
                Kind = kind;
                Alienation = alienation;
                Ready = ready;
            }
        }
    }
}
