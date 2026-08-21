using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Systems;

namespace MahjongGame.UI
{
    public class RoomListController : MonoBehaviour
    {
        [SerializeField]
        private UIDocument document;

        [SerializeField]
        private VisualTreeAsset roomCardTemplate;

        [SerializeField]
        private LobbyController lobbyController;

        // Visual Elements
        private VisualElement rootElement;
        private VisualElement roomListOverlay;
        private Label roomCountLabel;
        private Label loadoutDeckName;
        private Label loadoutAlienation;
        private Button btnLinkWorkshop;
        private Toggle toggleHideUnavailable;
        private Button tagModeAll;
        private Button tagModeSingle;
        private Button tagModeEastOnly;
        private Button tagModeHalfGame;
        private Button tagModeFullGame;
        private Button btnRefresh;
        private Button btnClose;
        private ScrollView roomScrollView;
        private VisualElement emptyState;
        private TextField directRoomInput;
        private Button btnDirectJoin;
        private Button btnCreateShortcut;

        // State
        private int? _selectedModeFilter = null; // null = all
        private bool _hideUnavailable = false;
        private RoomSummaryMessage[] _cachedRooms = Array.Empty<RoomSummaryMessage>();
        private bool _isSubscribed = false;
        private bool _isUIInitialized = false;
        private bool _isJoining = false;
        private IVisualElementScheduledItem _refreshScheduleItem;

        private void Awake()
        {
            if (document == null)
                document = GetComponent<UIDocument>();

            if (document != null)
                document.sortingOrder = 50;
        }

        private void OnEnable()
        {
            InitializeUI();
            SubscribeNetworkEvents();
            // Start hidden by default
            Hide();
        }

        private void OnDisable()
        {
            UnsubscribeNetworkEvents();
            _refreshScheduleItem?.Pause();
            _isJoining = false;
        }

        private void InitializeUI()
        {
            if (document == null)
                document = GetComponent<UIDocument>();
            if (document == null) return;
            rootElement = document.rootVisualElement;
            if (rootElement == null || _isUIInitialized) return;

            roomListOverlay = rootElement.Q<VisualElement>("RoomListOverlay");
            roomCountLabel = rootElement.Q<Label>("RoomCountLabel");
            loadoutDeckName = rootElement.Q<Label>("LoadoutDeckName");
            loadoutAlienation = rootElement.Q<Label>("LoadoutAlienation");
            btnLinkWorkshop = rootElement.Q<Button>("BtnLinkWorkshop");
            toggleHideUnavailable = rootElement.Q<Toggle>("ToggleHideUnavailable");
            tagModeAll = rootElement.Q<Button>("TagModeAll");
            tagModeSingle = rootElement.Q<Button>("TagModeSingle");
            tagModeEastOnly = rootElement.Q<Button>("TagModeEastOnly");
            tagModeHalfGame = rootElement.Q<Button>("TagModeHalfGame");
            tagModeFullGame = rootElement.Q<Button>("TagModeFullGame");
            btnRefresh = rootElement.Q<Button>("BtnRefresh");
            btnClose = rootElement.Q<Button>("BtnClose");
            roomScrollView = rootElement.Q<ScrollView>("RoomScrollView");
            emptyState = rootElement.Q<VisualElement>("EmptyState");
            directRoomInput = rootElement.Q<TextField>("DirectRoomInput");
            btnDirectJoin = rootElement.Q<Button>("BtnDirectJoin");
            btnCreateShortcut = rootElement.Q<Button>("BtnCreateShortcut");

            // Event Bindings
            if (btnClose != null) btnClose.clicked += Close;
            if (btnRefresh != null) btnRefresh.clicked += RefreshList;
            if (btnLinkWorkshop != null) btnLinkWorkshop.clicked += OnLinkWorkshopClicked;
            if (btnDirectJoin != null) btnDirectJoin.clicked += OnDirectJoinClicked;
            if (btnCreateShortcut != null) btnCreateShortcut.clicked += OnCreateShortcutClicked;

            if (toggleHideUnavailable != null)
            {
                toggleHideUnavailable.RegisterValueChangedCallback(evt =>
                {
                    _hideUnavailable = evt.newValue;
                    RenderRoomList();
                });
            }

            if (tagModeAll != null) tagModeAll.clicked += () => SetModeFilter(null);
            if (tagModeSingle != null) tagModeSingle.clicked += () => SetModeFilter((int)GameMode.Single);
            if (tagModeEastOnly != null) tagModeEastOnly.clicked += () => SetModeFilter((int)GameMode.EastOnly);
            if (tagModeHalfGame != null) tagModeHalfGame.clicked += () => SetModeFilter((int)GameMode.HalfGame);
            if (tagModeFullGame != null) tagModeFullGame.clicked += () => SetModeFilter((int)GameMode.FullGame);

            _isUIInitialized = true;
        }

        private void SubscribeNetworkEvents()
        {
            if (_isSubscribed) return;
            var service = NetworkManager.Instance?.RoomService;
            if (service == null) return;

            service.RoomListReceived += HandleRoomListReceived;
            service.RoomJoined += HandleRoomJoined;
            _isSubscribed = true;
        }

        private void UnsubscribeNetworkEvents()
        {
            if (!_isSubscribed) return;
            var service = NetworkManager.Instance?.RoomService;
            if (service != null)
            {
                service.RoomListReceived -= HandleRoomListReceived;
                service.RoomJoined -= HandleRoomJoined;
            }
            _isSubscribed = false;
        }

        public void Open()
        {
            gameObject.SetActive(true);
            if (document == null)
                document = GetComponent<UIDocument>();
            if (document != null)
                document.sortingOrder = 50;

            if (rootElement == null || roomListOverlay == null)
                InitializeUI();

            SubscribeNetworkEvents();
            if (rootElement != null)
                rootElement.style.display = DisplayStyle.Flex;
            if (roomListOverlay != null)
                roomListOverlay.style.display = DisplayStyle.Flex;

            UpdatePlayerLoadoutInfo();
            RenderRoomList();
            RefreshList();
        }

        public void Close()
        {
            Hide();
        }

        private void Hide()
        {
            _refreshScheduleItem?.Pause();
            _isJoining = false;
            if (rootElement != null)
                rootElement.style.display = DisplayStyle.None;
            if (roomListOverlay != null)
                roomListOverlay.style.display = DisplayStyle.None;
        }

        public void RefreshList()
        {
            if (NetworkManager.Instance?.RoomService == null) return;
            string nickname = ProfileManager.Instance?.CurrentProfile?.Nickname ?? "Player";
            NetworkManager.Instance.RoomService.QueryRoomList(nickname);

            if (btnRefresh != null)
            {
                btnRefresh.text = "刷新中...";
                btnRefresh.SetEnabled(false);
                _refreshScheduleItem?.Pause();
                _refreshScheduleItem = rootElement?.schedule.Execute(() =>
                {
                    if (btnRefresh != null)
                    {
                        btnRefresh.text = "刷新列表";
                        btnRefresh.SetEnabled(true);
                    }
                }).StartingIn(600);
            }
        }

        private void HandleRoomListReceived(RoomSummaryMessage[] rooms)
        {
            _cachedRooms = rooms ?? Array.Empty<RoomSummaryMessage>();
            RenderRoomList();
        }

        private void HandleRoomJoined(RoomJoinedMessage message)
        {
            Close();
        }

        private void SetModeFilter(int? mode)
        {
            _selectedModeFilter = mode;

            UpdateTagStyle(tagModeAll, mode == null);
            UpdateTagStyle(tagModeSingle, mode == (int)GameMode.Single);
            UpdateTagStyle(tagModeEastOnly, mode == (int)GameMode.EastOnly);
            UpdateTagStyle(tagModeHalfGame, mode == (int)GameMode.HalfGame);
            UpdateTagStyle(tagModeFullGame, mode == (int)GameMode.FullGame);

            RenderRoomList();
        }

        private void UpdateTagStyle(Button tag, bool isActive)
        {
            if (tag == null) return;
            tag.EnableInClassList("active", isActive);
        }

        private void UpdatePlayerLoadoutInfo()
        {
            var profile = ProfileManager.Instance?.CurrentProfile;
            string deckName = "标准牌库（默认）";
            int alienation = 0;

            if (profile != null && profile.SavedDecks.Count > 0)
            {
                int idx = Mathf.Clamp(profile.SelectedDeckIndex, 0, profile.SavedDecks.Count - 1);
                var deck = profile.SavedDecks[idx];
                deckName = deck.DeckName;
                alienation = DeckConfig.CalculateTotalAlienation(deck.Config, deck.Talents);
            }

            if (loadoutDeckName != null) loadoutDeckName.text = deckName;
            if (loadoutAlienation != null) loadoutAlienation.text = $"{alienation} pt";
        }

        private int GetCurrentPlayerAlienation()
        {
            var profile = ProfileManager.Instance?.CurrentProfile;
            if (profile != null && profile.SavedDecks.Count > 0)
            {
                int idx = Mathf.Clamp(profile.SelectedDeckIndex, 0, profile.SavedDecks.Count - 1);
                var deck = profile.SavedDecks[idx];
                return DeckConfig.CalculateTotalAlienation(deck.Config, deck.Talents);
            }
            return 0;
        }

        private void RenderRoomList()
        {
            if (roomScrollView == null) return;
            roomScrollView.Clear();

            int playerAlienation = GetCurrentPlayerAlienation();
            int visibleCount = 0;

            var filteredRooms = _cachedRooms.Where(room =>
            {
                if (room == null) return false;
                if (_selectedModeFilter.HasValue && room.gameMode != _selectedModeFilter.Value)
                    return false;
                if (_hideUnavailable && (room.isFull || room.state != (int)RoomState.WaitingForPlayers && room.state != (int)RoomState.WaitingForMatchReady))
                    return false;
                return true;
            }).ToList();

            foreach (var room in filteredRooms)
            {
                var card = CreateRoomCard(room, playerAlienation);
                if (card != null)
                {
                    roomScrollView.Add(card);
                    visibleCount++;
                }
            }

            if (roomCountLabel != null)
                roomCountLabel.text = $"开放房间: {visibleCount}";

            if (emptyState != null)
                emptyState.style.display = visibleCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement CreateRoomCard(RoomSummaryMessage room, int playerAlienation)
        {
            VisualElement card;
            if (roomCardTemplate != null)
            {
                card = roomCardTemplate.Instantiate();
            }
            else
            {
                Debug.LogWarning("[RoomListController] roomCardTemplate is not assigned in Inspector, using fallback layout.");
                card = new VisualElement();
                card.AddToClassList("room-card");
            }

            var cardRoot = card.Q<VisualElement>("Root") ?? card;

            var roomIdBadge = card.Q<Label>("RoomIdBadge");
            var hostName = card.Q<Label>("HostName");
            var stateTag = card.Q<Label>("StateTag");
            var modeValue = card.Q<Label>("ModeValue");
            var presetValue = card.Q<Label>("PresetValue");
            var seatsCount = card.Q<Label>("SeatsCount");
            var compatibilityTag = card.Q<Label>("CompatibilityTag");
            var btnJoin = card.Q<Button>("BtnJoin");

            if (roomIdBadge != null) roomIdBadge.text = room.roomId;
            if (hostName != null) hostName.text = room.hostDisplayName;

            // Room State Tag
            bool isWaiting = room.state == (int)RoomState.WaitingForPlayers || room.state == (int)RoomState.WaitingForMatchReady;
            if (stateTag != null)
            {
                stateTag.ClearClassList();
                stateTag.AddToClassList("room-state-tag");

                if (room.isFull)
                {
                    stateTag.text = "席位已满";
                    stateTag.AddToClassList("full");
                }
                else if (isWaiting)
                {
                    stateTag.text = "等待加入中";
                    stateTag.AddToClassList("waiting");
                }
                else
                {
                    stateTag.text = "正在对局中";
                    stateTag.AddToClassList("playing");
                }
            }

            // Mode
            if (modeValue != null)
                modeValue.text = GetModeDisplayName((GameMode)room.gameMode);

            // Alienation Preset
            var preset = (AlienationPreset)room.alienationPreset;
            if (presetValue != null)
            {
                presetValue.text = GetPresetDisplayName(preset);
                presetValue.ClearClassList();
                presetValue.AddToClassList("meta-preset-badge");
                presetValue.AddToClassList(GetPresetCssClass(preset));
            }

            // Seats
            if (seatsCount != null)
                seatsCount.text = $"{room.currentPlayers}/{room.maxPlayers}";

            // Alienation Limit & Compatibility
            int limit = AlienationBudgetPolicy.GetLimit(preset);
            bool isCompatible = playerAlienation <= limit;

            if (compatibilityTag != null)
            {
                compatibilityTag.ClearClassList();
                compatibilityTag.AddToClassList("compatibility-tag");

                if (isCompatible)
                {
                    compatibilityTag.text = $"构筑适配 ({playerAlienation} <= {limit})";
                    compatibilityTag.AddToClassList("ok");
                }
                else
                {
                    compatibilityTag.text = $"异化超标 ({playerAlienation} > {limit})";
                    compatibilityTag.AddToClassList("exceeded");
                }
            }

            // Join Button
            if (btnJoin != null)
            {
                if (!isCompatible)
                {
                    btnJoin.text = "异化超标";
                    btnJoin.SetEnabled(false);
                    cardRoot.AddToClassList("disabled");
                }
                else if (room.isFull)
                {
                    btnJoin.text = "席位已满";
                    btnJoin.SetEnabled(false);
                    cardRoot.AddToClassList("disabled");
                }
                else if (!isWaiting)
                {
                    btnJoin.text = "游戏中";
                    btnJoin.SetEnabled(false);
                    cardRoot.AddToClassList("disabled");
                }
                else
                {
                    btnJoin.text = "加入房间";
                    btnJoin.SetEnabled(true);
                    string targetRoomId = room.roomId;
                    btnJoin.clicked += () => JoinRoom(targetRoomId);
                }
            }

            return card;
        }

        private void JoinRoom(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId) || _isJoining) return;
            _isJoining = true;
            string nickname = ProfileManager.Instance?.CurrentProfile?.Nickname ?? "Player";
            NetworkManager.Instance?.RoomService?.JoinRoom(roomId, nickname);
            rootElement?.schedule.Execute(() => _isJoining = false).StartingIn(1500);
        }

        private void OnDirectJoinClicked()
        {
            if (directRoomInput == null) return;
            string roomId = directRoomInput.value?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(roomId)) return;
            JoinRoom(roomId);
        }

        private void OnLinkWorkshopClicked()
        {
            Close();
            if (lobbyController != null)
                lobbyController.ShowTab("Workshop");
        }

        private void OnCreateShortcutClicked()
        {
            Close();
            if (lobbyController != null)
            {
                lobbyController.CreateRoomWithCurrentSettings();
            }
            else
            {
                var lobby = FindObjectOfType<LobbyController>(true);
                lobby?.CreateRoomWithCurrentSettings();
            }
        }

        private static string GetModeDisplayName(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Single: return "单局";
                case GameMode.EastOnly: return "东风局";
                case GameMode.HalfGame: return "半庄";
                case GameMode.FullGame: return "全庄";
                default: return mode.ToString();
            }
        }

        private static string GetPresetDisplayName(AlienationPreset preset)
        {
            switch (preset)
            {
                case AlienationPreset.Low: return "低档 40";
                case AlienationPreset.Standard: return "标准 80";
                case AlienationPreset.High: return "高档 120";
                default: return preset.ToString();
            }
        }

        private static string GetPresetCssClass(AlienationPreset preset)
        {
            switch (preset)
            {
                case AlienationPreset.Low: return "preset-low";
                case AlienationPreset.Standard: return "preset-standard";
                case AlienationPreset.High: return "preset-high";
                default: return "preset-standard";
            }
        }
    }
}
