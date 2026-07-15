using UnityEngine;
using UnityEngine.UIElements;
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

        // Elements
        private Label welcomeLabel;
        private Label deckNameLabel;
        private Label scoreLabel;
        private Button matchmakingButton;
        private Button btnDeckPrev;
        private Button btnDeckNext;
        private TextField roomIdInput;
        private Button joinRoomButton;
        private Button readyRoomButton;
        private Label roomStatusLabel;

        // GameMode Selector
        private Label modeNameLabel;
        private Button btnModePrev;
        private Button btnModeNext;
        private static readonly string[] GameModeNames = { "单局", "东风局", "半庄", "全庄" };
        
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

            welcomeLabel = root.Q<Label>("WelcomeLabel");
            deckNameLabel = root.Q<Label>("DeckNameLabel");
            scoreLabel = root.Q<Label>("ScoreLabel");
            matchmakingButton = root.Q<Button>("MatchmakingButton");
            btnDeckPrev = root.Q<Button>("BtnDeckPrev");
            btnDeckNext = root.Q<Button>("BtnDeckNext");
            roomIdInput = root.Q<TextField>("RoomIdInput");
            joinRoomButton = root.Q<Button>("JoinRoomButton");
            readyRoomButton = root.Q<Button>("ReadyRoomButton");
            roomStatusLabel = root.Q<Label>("RoomStatusLabel");

            modeNameLabel = root.Q<Label>("ModeNameLabel");
            btnModePrev = root.Q<Button>("BtnModePrev");
            btnModeNext = root.Q<Button>("BtnModeNext");

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
            if (readyRoomButton != null) readyRoomButton.clicked += OnReadyRoomClicked;
            if (readyRoomButton != null) readyRoomButton.SetEnabled(NetworkManager.Instance?.RoomService?.HasRoom == true);
            if (btnDeckPrev != null) btnDeckPrev.clicked += OnDeckPrevClicked;
            if (btnDeckNext != null) btnDeckNext.clicked += OnDeckNextClicked;
            if (btnModePrev != null) btnModePrev.clicked += OnModePrevClicked;
            if (btnModeNext != null) btnModeNext.clicked += OnModeNextClicked;

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

            ShowTab("Home");
            SubscribeRoomService();
            if (!string.IsNullOrWhiteSpace(NetworkManager.Instance?.RoomService?.LastRoomClosureReason))
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
            if (readyRoomButton != null) readyRoomButton.clicked -= OnReadyRoomClicked;
            UnsubscribeRoomService();
            if (btnDeckPrev != null) btnDeckPrev.clicked -= OnDeckPrevClicked;
            if (btnDeckNext != null) btnDeckNext.clicked -= OnDeckNextClicked;
            if (btnModePrev != null) btnModePrev.clicked -= OnModePrevClicked;
            if (btnModeNext != null) btnModeNext.clicked -= OnModeNextClicked;

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

            NetworkManager.Instance.RoomService.CreateRoom(GetSelectedGameMode(), GetNickname());
            SetRoomStatus("正在创建新房间；房间号由服务器分配。");
        }

        private void OnJoinRoomClicked()
        {
            if (NetworkManager.Instance == null || string.IsNullOrWhiteSpace(roomIdInput?.value)) { SetRoomStatus("请输入要加入的房间号。"); return; }
            NetworkManager.Instance.RoomService.JoinRoom(roomIdInput.value, GetNickname());
            SetRoomStatus("正在加入房间...");
        }

        private void OnReadyRoomClicked()
        {
            if (NetworkManager.Instance?.RoomService?.HasRoom != true) { SetRoomStatus("请先创建或加入房间。"); return; }
            NetworkManager.Instance.RoomService.SendReady(ReadyPhase.MatchStart);
            SetRoomStatus("已准备，等待当前房间内所有真人玩家准备。");
        }

        private GameMode GetSelectedGameMode()
        {
            int mode = ProfileManager.Instance?.CurrentProfile?.Settings.SelectedGameMode ?? 0;
            return mode >= 0 && mode <= 3 ? (GameMode)mode : GameMode.Single;
        }

        private string GetNickname() => ProfileManager.Instance?.CurrentProfile?.Nickname ?? "Player";

        private void SubscribeRoomService()
        {
            var service = NetworkManager.Instance?.RoomService;
            if (service == null) return;
            service.RoomJoined += HandleRoomJoined;
            service.SeatSnapshotChanged += HandleSeatSnapshotChanged;
            service.RoomError += HandleRoomError;
        }

        private void UnsubscribeRoomService()
        {
            var service = NetworkManager.Instance?.RoomService;
            if (service == null) return;
            service.RoomJoined -= HandleRoomJoined;
            service.SeatSnapshotChanged -= HandleSeatSnapshotChanged;
            service.RoomError -= HandleRoomError;
        }

        private void HandleRoomJoined(RoomJoinedMessage message)
        {
            if (readyRoomButton != null) readyRoomButton.SetEnabled(true);
            if (matchmakingButton != null) matchmakingButton.SetEnabled(false);
            if (joinRoomButton != null) joinRoomButton.SetEnabled(false);
            if (roomIdInput != null) roomIdInput.value = message.roomId;
            SetRoomStatus($"当前房间：{message.roomId} ｜ 我的席位：{message.seatIndex + 1} ｜ 请点击“确认准备”。");
        }
        private void HandleSeatSnapshotChanged(RoomSeatMessage[] seats)
        {
            var room = NetworkManager.Instance?.RoomService;
            if (room == null) return;
            int humanCount = System.Array.FindAll(seats, seat => seat != null && seat.isOccupied && !seat.isAi).Length;
            SetRoomStatus($"当前房间：{room.RoomId} ｜ 我的席位：{room.SeatIndex + 1} ｜ 真人玩家：{humanCount}/4");
        }
        private void HandleRoomError(string message) => SetRoomStatus(message);
        private void SetRoomStatus(string message) { if (roomStatusLabel != null) roomStatusLabel.text = message; }
    }
}
