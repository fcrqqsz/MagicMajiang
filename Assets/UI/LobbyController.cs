using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Fan;
using MahjongGame.Core.Network.Data;
using MahjongGame.Systems;
using MahjongGame.Systems.Audio;
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

        [SerializeField]
        private RoomListController roomListController;

        // Tabs
        private Button tabHome;
        private Button tabWorkshop;
        private Button tabCompendium;
        private Button tabCollection;
        private Button tabSettings;

        // Views
        private VisualElement viewHome;
        private VisualElement viewWorkshop;
        private VisualElement viewCompendium;
        private VisualElement viewCollection;
        private VisualElement viewSettings;
        private VisualElement viewRoom;
        private VisualElement sidebar;

        // Elements
        private Label welcomeLabel;
        private Label deckNameLabel;
        private Label scoreLabel;
        private Button matchmakingButton;
        private Button btnViewRoomList;
        private Button btnDeckPrev;
        private Button btnDeckNext;
        private TextField roomIdInput;
        private Button joinRoomButton;
        private Label roomStatusLabel;
        private VisualElement roomAdmissionBlocker;
        private Label roomAdmissionBlockerLabel;

        // GameMode Selector
        private Label modeNameLabel;
        private Button btnModePrev;
        private Button btnModeNext;
        private static readonly string[] GameModeNames = { "单局", "东风局", "半庄", "全庄" };
        private Label roomPresetLabel;
        private Button btnRoomPresetPrev;
        private Button btnRoomPresetNext;
        private AlienationPreset _pendingRoomAlienationPreset = AlienationPreset.Standard;
        
        // Connection settings elements
        private Toggle localServerToggle;
        private Label connectionStatusPill;
        private Label connectionAddressLabel;
        private Button retestConnectionButton;
        private Label connectionSocketPhaseLabel;
        private Label connectionHandshakeLabel;
        private Label connectionRttLabel;
        private Label connectionLastCheckedLabel;
        private Label connectionErrorLabel;
        private Label connectionReadinessLabel;
        private AudioSettingsView audioSettingsView;

        // Compendium Elements
        private Button subtabBtnMcr;
        private Button subtabBtnTalent;
        private Button subtabBtnRules;
        private VisualElement compendiumMcrView;
        private VisualElement compendiumTalentView;
        private VisualElement compendiumRulesView;

        private Button fanFilterAll;
        private Button fanFilterTop;
        private Button fanFilterHigh;
        private Button fanFilterMid;
        private Button fanFilterLow;
        private TextField fanSearchInput;
        private Label fanCountLabel;
        private ScrollView fanCardScrollView;
        private string _currentFanFilter = "all";

        private Button talentFilterAll;
        private Button talentFilterMajor;
        private Button talentFilterMedium;
        private Button talentFilterMinor;
        private Button talentFilterActive;
        private TextField talentSearchInput;
        private Label talentCountLabel;
        private ScrollView talentCardScrollView;
        private string _currentTalentFilter = "all";

        private bool _compendiumInitialized = false;
        private List<FanRule> _cachedSortedFanRules;
        private List<string> _cachedSortedTalentIds;

        // Cached callbacks for unregistration
        private EventCallback<ChangeEvent<bool>> _onLocalServerChanged;
        private EventCallback<ChangeEvent<string>> _onFanSearchChanged;
        private EventCallback<ChangeEvent<string>> _onTalentSearchChanged;
        private EventCallback<FocusInEvent> _onFanSearchFocusIn;
        private EventCallback<FocusOutEvent> _onFanSearchFocusOut;
        private EventCallback<FocusInEvent> _onTalentSearchFocusIn;
        private EventCallback<FocusOutEvent> _onTalentSearchFocusOut;

        private void OnTabHomeClicked() => ShowTab("Home");
        private void OnTabWorkshopClicked() => ShowTab("Workshop");
        private void OnTabCompendiumClicked() => ShowTab("Compendium");
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
            btnViewRoomList = root.Q<Button>("BtnViewRoomList");
            btnDeckPrev = root.Q<Button>("BtnDeckPrev");
            btnDeckNext = root.Q<Button>("BtnDeckNext");
            roomIdInput = root.Q<TextField>("RoomIdInput");
            joinRoomButton = root.Q<Button>("JoinRoomButton");
            roomStatusLabel = root.Q<Label>("RoomStatusLabel");
            roomAdmissionBlocker = root.Q<VisualElement>("RoomAdmissionBlocker");
            roomAdmissionBlockerLabel = root.Q<Label>("RoomAdmissionBlockerLabel");

            modeNameLabel = root.Q<Label>("ModeNameLabel");
            btnModePrev = root.Q<Button>("BtnModePrev");
            btnModeNext = root.Q<Button>("BtnModeNext");
            roomPresetLabel = root.Q<Label>("RoomPresetLabel");
            btnRoomPresetPrev = root.Q<Button>("BtnRoomPresetPrev");
            btnRoomPresetNext = root.Q<Button>("BtnRoomPresetNext");

            localServerToggle = root.Q<Toggle>("LocalServerToggle");
            connectionStatusPill = root.Q<Label>("ConnectionStatusPill");
            connectionAddressLabel = root.Q<Label>("ConnectionAddressLabel");
            retestConnectionButton = root.Q<Button>("RetestConnectionButton");
            connectionSocketPhaseLabel = root.Q<Label>("ConnectionSocketPhaseLabel");
            connectionHandshakeLabel = root.Q<Label>("ConnectionHandshakeLabel");
            connectionRttLabel = root.Q<Label>("ConnectionRttLabel");
            connectionLastCheckedLabel = root.Q<Label>("ConnectionLastCheckedLabel");
            connectionErrorLabel = root.Q<Label>("ConnectionErrorLabel");
            connectionReadinessLabel = root.Q<Label>("ConnectionReadinessLabel");

            if (tabHome != null) tabHome.clicked += OnTabHomeClicked;
            if (tabWorkshop != null) tabWorkshop.clicked += OnTabWorkshopClicked;
            if (tabCollection != null) tabCollection.clicked += OnTabCollectionClicked;
            if (tabSettings != null) tabSettings.clicked += OnTabSettingsClicked;

            if (matchmakingButton != null) matchmakingButton.clicked += OnMatchmakingClicked;
            if (btnViewRoomList != null) btnViewRoomList.clicked += OnViewRoomListClicked;
            if (joinRoomButton != null) joinRoomButton.clicked += OnJoinRoomClicked;
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
                    
            }

            RegisterConnectionSettingsCallbacks();
            audioSettingsView = new AudioSettingsView(root, AudioManager.Instance);

            InitializeCompendiumElements(root);

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
            if (tabCompendium != null) tabCompendium.clicked -= OnTabCompendiumClicked;
            if (tabCollection != null) tabCollection.clicked -= OnTabCollectionClicked;
            if (tabSettings != null) tabSettings.clicked -= OnTabSettingsClicked;
            if (matchmakingButton != null) matchmakingButton.clicked -= OnMatchmakingClicked;
            if (btnViewRoomList != null) btnViewRoomList.clicked -= OnViewRoomListClicked;
            if (joinRoomButton != null) joinRoomButton.clicked -= OnJoinRoomClicked;
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

            UnregisterConnectionSettingsCallbacks();
            UnregisterCompendiumCallbacks();
            audioSettingsView?.Dispose();
            audioSettingsView = null;
        }

        private void InitializeCompendiumElements(VisualElement root)
        {
            if (root == null) return;

            tabCompendium = root.Q<Button>("TabCompendium");
            viewCompendium = root.Q<VisualElement>("ViewCompendium");

            subtabBtnMcr = root.Q<Button>("SubtabBtnMcr");
            subtabBtnTalent = root.Q<Button>("SubtabBtnTalent");
            subtabBtnRules = root.Q<Button>("SubtabBtnRules");
            compendiumMcrView = root.Q<VisualElement>("CompendiumMcrView");
            compendiumTalentView = root.Q<VisualElement>("CompendiumTalentView");
            compendiumRulesView = root.Q<VisualElement>("CompendiumRulesView");

            fanFilterAll = root.Q<Button>("FanFilterAll");
            fanFilterTop = root.Q<Button>("FanFilterTop");
            fanFilterHigh = root.Q<Button>("FanFilterHigh");
            fanFilterMid = root.Q<Button>("FanFilterMid");
            fanFilterLow = root.Q<Button>("FanFilterLow");
            fanSearchInput = root.Q<TextField>("FanSearchInput");
            fanCountLabel = root.Q<Label>("FanCountLabel");
            fanCardScrollView = root.Q<ScrollView>("FanCardScrollView");

            talentFilterAll = root.Q<Button>("TalentFilterAll");
            talentFilterMajor = root.Q<Button>("TalentFilterMajor");
            talentFilterMedium = root.Q<Button>("TalentFilterMedium");
            talentFilterMinor = root.Q<Button>("TalentFilterMinor");
            talentFilterActive = root.Q<Button>("TalentFilterActive");
            talentSearchInput = root.Q<TextField>("TalentSearchInput");
            talentCountLabel = root.Q<Label>("TalentCountLabel");
            talentCardScrollView = root.Q<ScrollView>("TalentCardScrollView");

            if (tabCompendium != null) tabCompendium.clicked += OnTabCompendiumClicked;

            if (subtabBtnMcr != null) subtabBtnMcr.clicked += OnSubtabMcrClicked;
            if (subtabBtnTalent != null) subtabBtnTalent.clicked += OnSubtabTalentClicked;
            if (subtabBtnRules != null) subtabBtnRules.clicked += OnSubtabRulesClicked;

            if (fanFilterAll != null) fanFilterAll.clicked += OnFanFilterAllClicked;
            if (fanFilterTop != null) fanFilterTop.clicked += OnFanFilterTopClicked;
            if (fanFilterHigh != null) fanFilterHigh.clicked += OnFanFilterHighClicked;
            if (fanFilterMid != null) fanFilterMid.clicked += OnFanFilterMidClicked;
            if (fanFilterLow != null) fanFilterLow.clicked += OnFanFilterLowClicked;

            if (talentFilterAll != null) talentFilterAll.clicked += OnTalentFilterAllClicked;
            if (talentFilterMajor != null) talentFilterMajor.clicked += OnTalentFilterMajorClicked;
            if (talentFilterMedium != null) talentFilterMedium.clicked += OnTalentFilterMediumClicked;
            if (talentFilterMinor != null) talentFilterMinor.clicked += OnTalentFilterMinorClicked;
            if (talentFilterActive != null) talentFilterActive.clicked += OnTalentFilterActiveClicked;

            _onFanSearchChanged = _ => RenderFanCards();
            _onTalentSearchChanged = _ => RenderTalentCards();

            _onFanSearchFocusIn = _ => fanSearchInput?.AddToClassList("compendium-search-focused");
            _onFanSearchFocusOut = _ => fanSearchInput?.RemoveFromClassList("compendium-search-focused");
            _onTalentSearchFocusIn = _ => talentSearchInput?.AddToClassList("compendium-search-focused");
            _onTalentSearchFocusOut = _ => talentSearchInput?.RemoveFromClassList("compendium-search-focused");

            if (fanSearchInput != null)
            {
                fanSearchInput.RegisterValueChangedCallback(_onFanSearchChanged);
                fanSearchInput.RegisterCallback(_onFanSearchFocusIn);
                fanSearchInput.RegisterCallback(_onFanSearchFocusOut);
            }
            if (talentSearchInput != null)
            {
                talentSearchInput.RegisterValueChangedCallback(_onTalentSearchChanged);
                talentSearchInput.RegisterCallback(_onTalentSearchFocusIn);
                talentSearchInput.RegisterCallback(_onTalentSearchFocusOut);
            }
        }

        private void OnSubtabMcrClicked() => SwitchCompendiumSubtab("mcr");
        private void OnSubtabTalentClicked() => SwitchCompendiumSubtab("talent");
        private void OnSubtabRulesClicked() => SwitchCompendiumSubtab("rules");

        private void OnFanFilterAllClicked() => SetFanFilter("all");
        private void OnFanFilterTopClicked() => SetFanFilter("top");
        private void OnFanFilterHighClicked() => SetFanFilter("high");
        private void OnFanFilterMidClicked() => SetFanFilter("mid");
        private void OnFanFilterLowClicked() => SetFanFilter("low");

        private void OnTalentFilterAllClicked() => SetTalentFilter("all");
        private void OnTalentFilterMajorClicked() => SetTalentFilter("major");
        private void OnTalentFilterMediumClicked() => SetTalentFilter("medium");
        private void OnTalentFilterMinorClicked() => SetTalentFilter("minor");
        private void OnTalentFilterActiveClicked() => SetTalentFilter("active");

        private void UnregisterCompendiumCallbacks()
        {
            // tabCompendium 已在 OnDisable 中统一注销，此处仅处理图鉴子元素
            if (subtabBtnMcr != null) subtabBtnMcr.clicked -= OnSubtabMcrClicked;
            if (subtabBtnTalent != null) subtabBtnTalent.clicked -= OnSubtabTalentClicked;
            if (subtabBtnRules != null) subtabBtnRules.clicked -= OnSubtabRulesClicked;

            if (fanFilterAll != null) fanFilterAll.clicked -= OnFanFilterAllClicked;
            if (fanFilterTop != null) fanFilterTop.clicked -= OnFanFilterTopClicked;
            if (fanFilterHigh != null) fanFilterHigh.clicked -= OnFanFilterHighClicked;
            if (fanFilterMid != null) fanFilterMid.clicked -= OnFanFilterMidClicked;
            if (fanFilterLow != null) fanFilterLow.clicked -= OnFanFilterLowClicked;

            if (talentFilterAll != null) talentFilterAll.clicked -= OnTalentFilterAllClicked;
            if (talentFilterMajor != null) talentFilterMajor.clicked -= OnTalentFilterMajorClicked;
            if (talentFilterMedium != null) talentFilterMedium.clicked -= OnTalentFilterMediumClicked;
            if (talentFilterMinor != null) talentFilterMinor.clicked -= OnTalentFilterMinorClicked;
            if (talentFilterActive != null) talentFilterActive.clicked -= OnTalentFilterActiveClicked;

            if (fanSearchInput != null)
            {
                fanSearchInput.RemoveFromClassList("compendium-search-focused");
                if (_onFanSearchChanged != null) fanSearchInput.UnregisterValueChangedCallback(_onFanSearchChanged);
                if (_onFanSearchFocusIn != null) fanSearchInput.UnregisterCallback(_onFanSearchFocusIn);
                if (_onFanSearchFocusOut != null) fanSearchInput.UnregisterCallback(_onFanSearchFocusOut);
            }
            if (talentSearchInput != null)
            {
                talentSearchInput.RemoveFromClassList("compendium-search-focused");
                if (_onTalentSearchChanged != null) talentSearchInput.UnregisterValueChangedCallback(_onTalentSearchChanged);
                if (_onTalentSearchFocusIn != null) talentSearchInput.UnregisterCallback(_onTalentSearchFocusIn);
                if (_onTalentSearchFocusOut != null) talentSearchInput.UnregisterCallback(_onTalentSearchFocusOut);
            }
        }

        private void SwitchCompendiumSubtab(string subtab)
        {
            if (compendiumMcrView != null) compendiumMcrView.style.display = subtab == "mcr" ? DisplayStyle.Flex : DisplayStyle.None;
            if (compendiumTalentView != null) compendiumTalentView.style.display = subtab == "talent" ? DisplayStyle.Flex : DisplayStyle.None;
            if (compendiumRulesView != null) compendiumRulesView.style.display = subtab == "rules" ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateSubtabBtnStyle(subtabBtnMcr, subtab == "mcr");
            UpdateSubtabBtnStyle(subtabBtnTalent, subtab == "talent");
            UpdateSubtabBtnStyle(subtabBtnRules, subtab == "rules");
        }

        private static void UpdateSubtabBtnStyle(Button btn, bool isActive)
        {
            if (btn == null) return;
            if (isActive) btn.AddToClassList("compendium-subtab-active");
            else btn.RemoveFromClassList("compendium-subtab-active");
        }

        private void SetFanFilter(string filter)
        {
            _currentFanFilter = filter;
            UpdateFilterBtnStyle(fanFilterAll, filter == "all");
            UpdateFilterBtnStyle(fanFilterTop, filter == "top");
            UpdateFilterBtnStyle(fanFilterHigh, filter == "high");
            UpdateFilterBtnStyle(fanFilterMid, filter == "mid");
            UpdateFilterBtnStyle(fanFilterLow, filter == "low");
            RenderFanCards();
        }

        private void SetTalentFilter(string filter)
        {
            _currentTalentFilter = filter;
            UpdateFilterBtnStyle(talentFilterAll, filter == "all");
            UpdateFilterBtnStyle(talentFilterMajor, filter == "major");
            UpdateFilterBtnStyle(talentFilterMedium, filter == "medium");
            UpdateFilterBtnStyle(talentFilterMinor, filter == "minor");
            UpdateFilterBtnStyle(talentFilterActive, filter == "active");
            RenderTalentCards();
        }

        private static void UpdateFilterBtnStyle(Button btn, bool isActive)
        {
            if (btn == null) return;
            if (isActive) btn.AddToClassList("compendium-filter-active");
            else btn.RemoveFromClassList("compendium-filter-active");
        }

        private void RefreshCompendiumData()
        {
            if (!_compendiumInitialized)
            {
                _compendiumInitialized = true;
                SwitchCompendiumSubtab("mcr");
                // SetFanFilter / SetTalentFilter 内部已调用 Render，无需重复渲染
                SetFanFilter("all");
                SetTalentFilter("all");
                return;
            }
            RenderFanCards();
            RenderTalentCards();
        }

        private void RenderFanCards()
        {
            if (fanCardScrollView == null) return;
            fanCardScrollView.Clear();

            var allRules = FanRuleRegistry.Instance?.ActiveRules;
            if (allRules == null) return;

            if (fanFilterAll != null)
                fanFilterAll.text = $"全部 ({allRules.Count})";

            string searchKeyword = fanSearchInput?.value?.Trim().ToLowerInvariant() ?? "";
            int count = 0;

            // 缓存排序结果，避免每次搜索输入都重新排序
            if (_cachedSortedFanRules == null || _cachedSortedFanRules.Count != allRules.Count)
            {
                _cachedSortedFanRules = allRules
                    .OrderByDescending(r => r.FanValue)
                    .ThenBy(r => r.Name)
                    .ToList();
            }

            foreach (var rule in _cachedSortedFanRules)
            {
                bool matchTier = _currentFanFilter switch
                {
                    "top" => rule.FanValue >= 48,
                    "high" => rule.FanValue >= 16 && rule.FanValue < 48,
                    "mid" => rule.FanValue >= 6 && rule.FanValue < 16,
                    "low" => rule.FanValue < 6,
                    _ => true
                };

                if (!matchTier) continue;

                if (!string.IsNullOrEmpty(searchKeyword))
                {
                    bool matchName = rule.Name != null && rule.Name.ToLowerInvariant().Contains(searchKeyword);
                    bool matchDesc = rule.Description != null && rule.Description.ToLowerInvariant().Contains(searchKeyword);
                    if (!matchName && !matchDesc) continue;
                }

                count++;
                var card = CreateFanCardElement(rule);
                fanCardScrollView.Add(card);
            }

            if (fanCountLabel != null)
                fanCountLabel.text = $"显示 {count} 个番种";
        }

        private static VisualElement CreateFanCardElement(FanRule rule)
        {
            var card = new VisualElement();
            card.AddToClassList("compendium-card");

            var header = new VisualElement();
            header.AddToClassList("compendium-card-header");

            var titleGroup = new VisualElement();
            titleGroup.AddToClassList("compendium-card-title-group");

            var title = new Label(rule.Name ?? "未知番种");
            title.AddToClassList("compendium-card-title");
            titleGroup.Add(title);

            var badge = new Label($"{rule.FanValue} 番");
            badge.AddToClassList("compendium-card-badge");
            if (rule.FanValue >= 48) badge.AddToClassList("badge-fan-top");
            else if (rule.FanValue >= 16) badge.AddToClassList("badge-fan-high");
            else if (rule.FanValue >= 6) badge.AddToClassList("badge-fan-mid");
            else badge.AddToClassList("badge-fan-low");
            titleGroup.Add(badge);

            header.Add(titleGroup);

            var metaRight = new VisualElement();
            metaRight.AddToClassList("compendium-card-meta-right");
            var mcrTag = new Label("国标 MCR");
            mcrTag.AddToClassList("compendium-meta-tag");
            metaRight.Add(mcrTag);
            header.Add(metaRight);

            card.Add(header);

            var desc = new Label(rule.Description ?? "");
            desc.AddToClassList("compendium-card-desc");
            card.Add(desc);

            if (rule.ExcludedRuleIds != null && rule.ExcludedRuleIds.Length > 0)
            {
                var excludedNames = new List<string>();
                foreach (var id in rule.ExcludedRuleIds)
                {
                    var exRule = FanRuleRegistry.Instance?.ActiveRules?.Find(r => r.Id == id);
                    excludedNames.Add(exRule != null ? exRule.Name : id);
                }
                var excludedLabel = new Label($"排斥番种：{string.Join("、", excludedNames)}");
                excludedLabel.AddToClassList("compendium-card-extra");
                card.Add(excludedLabel);
            }

            return card;
        }

        private void RenderTalentCards()
        {
            if (talentCardScrollView == null) return;
            talentCardScrollView.Clear();

            var registry = TalentRegistry.Instance;
            if (registry == null) return;

            var talentIds = registry.GetAllIds();
            if (talentIds == null) return;

            if (talentFilterAll != null)
                talentFilterAll.text = $"全部天赋 ({talentIds.Count})";

            string searchKeyword = talentSearchInput?.value?.Trim().ToLowerInvariant() ?? "";
            int count = 0;

            // 缓存排序结果，避免每次搜索输入都重新排序
            if (_cachedSortedTalentIds == null || _cachedSortedTalentIds.Count != talentIds.Count)
            {
                _cachedSortedTalentIds = talentIds
                    .OrderByDescending(id => (int)registry.GetTier(id))
                    .ThenByDescending(id => registry.GetCost(id))
                    .ToList();
            }

            foreach (var id in _cachedSortedTalentIds)
            {
                var tier = registry.GetTier(id);
                int cost = registry.GetCost(id);
                string name = registry.GetDisplayName(id);
                string desc = registry.GetDescription(id);
                var meta = registry.GetMetadata(id);
                bool isActive = meta.ActivationWindow != TalentActivationWindow.None;

                bool matchTier = _currentTalentFilter switch
                {
                    "major" => tier == TalentTier.Large,
                    "medium" => tier == TalentTier.Medium,
                    "minor" => tier == TalentTier.Small,
                    "active" => isActive,
                    _ => true
                };

                if (!matchTier) continue;

                if (!string.IsNullOrEmpty(searchKeyword))
                {
                    bool matchName = name != null && name.ToLowerInvariant().Contains(searchKeyword);
                    bool matchDesc = desc != null && desc.ToLowerInvariant().Contains(searchKeyword);
                    if (!matchName && !matchDesc) continue;
                }

                count++;
                var card = CreateTalentCardElement(id, tier, cost, name, desc, meta, isActive);
                talentCardScrollView.Add(card);
            }

            if (talentCountLabel != null)
                talentCountLabel.text = $"显示 {count} 个天赋规则";
        }

        private static VisualElement CreateTalentCardElement(
            string id, TalentTier tier, int cost, string name, string desc, TalentMetadata meta, bool isActive)
        {
            var card = new VisualElement();
            card.AddToClassList("compendium-card");

            var header = new VisualElement();
            header.AddToClassList("compendium-card-header");

            var titleGroup = new VisualElement();
            titleGroup.AddToClassList("compendium-card-title-group");

            var title = new Label(name);
            title.AddToClassList("compendium-card-title");
            titleGroup.Add(title);

            string tierText = tier switch
            {
                TalentTier.Large => "大天赋",
                TalentTier.Medium => "中天赋",
                _ => "小天赋"
            };
            var badge = new Label(tierText);
            badge.AddToClassList("compendium-card-badge");
            badge.AddToClassList(tier switch
            {
                TalentTier.Large => "badge-tier-major",
                TalentTier.Medium => "badge-tier-medium",
                _ => "badge-tier-minor"
            });
            titleGroup.Add(badge);
            header.Add(titleGroup);

            var metaRight = new VisualElement();
            metaRight.AddToClassList("compendium-card-meta-right");

            var costTag = new Label($"异化消耗: {cost}点");
            costTag.AddToClassList("compendium-meta-tag");
            metaRight.Add(costTag);

            if (isActive)
            {
                var activeTag = new Label("主动技能");
                activeTag.AddToClassList("compendium-meta-tag");
                activeTag.AddToClassList("compendium-meta-tag-active");
                metaRight.Add(activeTag);
            }

            header.Add(metaRight);
            card.Add(header);

            var descLabel = new Label(desc);
            descLabel.AddToClassList("compendium-card-desc");
            card.Add(descLabel);

            var detailText = GetTalentDetailSummary(id, meta);
            if (!string.IsNullOrEmpty(detailText))
            {
                var detailLabel = new Label(detailText);
                detailLabel.AddToClassList("compendium-card-extra");
                detailLabel.AddToClassList("compendium-card-extra-talent");
                card.Add(detailLabel);
            }

            return card;
        }

        private static string GetTalentDetailSummary(string id, TalentMetadata meta) => id switch
        {
            "sheathed_edge" => "机制细节：未获胜时积攒锋刃；小局开始公开揭示，胡牌时消耗所有锋提供超高番数加成。",
            "midas_touch" => "机制细节：摸牌管道钩子；将抓到的字牌（风牌/箭牌）自动转化为发财并置位异化标记。",
            "dragon_ascent" => "机制细节：算番管道钩子；放宽清龙顺子连贯判定限制，允许一张牌±1浮动。",
            "head_start" => "机制细节：算番管道直接注入+2番，使实际6番手牌即可满足国标8番起胡门槛。",
            "interception" => "机制细节：主动动作协议；削弱目标玩家公开充能（如藏锋），受定心等防御管道拦截。",
            "composure" => "机制细节：防御管道首位拦截，每小局首次受到的负面天赋效果直接抵消并广播战术反馈。",
            "starting_capital" => "机制细节：整场比赛开始时一次性给本家注入+30底分筹码，跨小局累计计分。",
            "peek" => "机制细节：发牌结束后私有消息流下发牌山顶4张牌，客户端弹出悬浮面板展示，仅本家可见。",
            "draw_reward" => "机制细节：小局流局结算钩子触发，本家独享额外+5分补偿，不影响其他三家结算。",
            _ => meta.ActivationWindow != TalentActivationWindow.None ? "主动天赋动作" : "被动天赋规则"
        };

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

            var mode = (GameMode)modeIdx;
            modeNameLabel.text = $"对战模式: {GameModeNames[modeIdx]} · {SessionScoreRules.GetInitialScore(mode)} 分";
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

        private void RegisterConnectionSettingsCallbacks()
        {
            _onLocalServerChanged = OnLocalServerChanged;
            if (localServerToggle != null)
                localServerToggle.RegisterValueChangedCallback(_onLocalServerChanged);
            if (retestConnectionButton != null)
                retestConnectionButton.clicked += OnRetestConnectionClicked;

            RefreshConnectionSettings(NetworkManager.Instance?.RoomService?.ConnectionDiagnostics);
        }

        private void UnregisterConnectionSettingsCallbacks()
        {
            if (localServerToggle != null && _onLocalServerChanged != null)
                localServerToggle.UnregisterValueChangedCallback(_onLocalServerChanged);
            if (retestConnectionButton != null)
                retestConnectionButton.clicked -= OnRetestConnectionClicked;
        }

        private void OnLocalServerChanged(ChangeEvent<bool> evt)
        {
            var network = NetworkManager.Instance;
            if (network == null
                || !network.SelectServerEnvironment(
                    evt.newValue ? ClientServerEnvironment.Local : ClientServerEnvironment.Online,
                    GetNickname()))
            {
                RefreshConnectionSettings(network?.RoomService?.ConnectionDiagnostics);
            }
        }

        private void OnRetestConnectionClicked()
        {
            var roomService = NetworkManager.Instance?.RoomService;
            if (roomService == null || !roomService.TryReconnectSelectedServer(GetNickname()))
                RefreshConnectionSettings(roomService?.ConnectionDiagnostics);
        }

        private void HandleConnectionDiagnosticsChanged(ClientConnectionDiagnostics diagnostics) =>
            RefreshConnectionSettings(diagnostics);

        private void RefreshConnectionSettings(ClientConnectionDiagnostics diagnostics)
        {
            LobbyConnectionPresentationView view = LobbyConnectionPresentationPolicy.Build(diagnostics);
            if (localServerToggle != null)
            {
                bool localSelected = NetworkManager.Instance?.SelectedServerEnvironment == ClientServerEnvironment.Local;
                localServerToggle.SetValueWithoutNotify(localSelected);
                localServerToggle.SetEnabled(!view.ActionsDisabled && NetworkManager.Instance != null);
            }

            if (connectionStatusPill != null)
            {
                connectionStatusPill.text = view.StatusText;
                connectionStatusPill.RemoveFromClassList("connection-status-gray");
                connectionStatusPill.RemoveFromClassList("connection-status-yellow");
                connectionStatusPill.RemoveFromClassList("connection-status-blue");
                connectionStatusPill.RemoveFromClassList("connection-status-green");
                connectionStatusPill.RemoveFromClassList("connection-status-red");
                connectionStatusPill.AddToClassList(view.StatusClass);
            }

            if (connectionAddressLabel != null)
                connectionAddressLabel.text = $"当前地址：{(string.IsNullOrWhiteSpace(diagnostics?.Address) ? "--" : diagnostics.Address)}";
            if (connectionSocketPhaseLabel != null) connectionSocketPhaseLabel.text = view.SocketPhaseText;
            if (connectionHandshakeLabel != null) connectionHandshakeLabel.text = view.HandshakeText;
            if (connectionRttLabel != null) connectionRttLabel.text = view.RoundTripTimeText;
            if (connectionLastCheckedLabel != null) connectionLastCheckedLabel.text = view.LastCheckedText;
            if (connectionErrorLabel != null) connectionErrorLabel.text = view.LastErrorText;
            if (connectionReadinessLabel != null) connectionReadinessLabel.text = view.ReadinessText;
            if (retestConnectionButton != null)
                retestConnectionButton.SetEnabled(!view.ActionsDisabled && NetworkManager.Instance?.RoomService != null);
        }

        public void ShowTab(string tabName)
        {
            if (NetworkManager.Instance?.RoomService?.HasRoom == true)
            {
                ShowRoom();
                return;
            }

            if (viewRoom != null) viewRoom.style.display = DisplayStyle.None;
            if (viewHome != null) viewHome.style.display = tabName == "Home" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewWorkshop != null) viewWorkshop.style.display = tabName == "Workshop" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewCompendium != null) viewCompendium.style.display = tabName == "Compendium" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewCollection != null) viewCollection.style.display = tabName == "Collection" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewSettings != null) viewSettings.style.display = tabName == "Settings" ? DisplayStyle.Flex : DisplayStyle.None;
            audioSettingsView?.SetVisible(tabName == "Settings");

            if (tabHome != null) UpdateTabStyle(tabHome, tabName == "Home");
            if (tabWorkshop != null) UpdateTabStyle(tabWorkshop, tabName == "Workshop");
            if (tabCompendium != null) UpdateTabStyle(tabCompendium, tabName == "Compendium");
            if (tabCollection != null) UpdateTabStyle(tabCollection, tabName == "Collection");
            if (tabSettings != null) UpdateTabStyle(tabSettings, tabName == "Settings");

            // Handle independent DeckEditor UI — 用 display 切换避免 UIDocument 重建
            if (deckEditorToolkit != null)
            {
                if (tabName == "Workshop") deckEditorToolkit.ShowProfileEditor();
                else deckEditorToolkit.HideEditor();
            }

            if (tabName == "Home")
            {
                RefreshHomeDeckInfo();
                RefreshGameModeDisplay();
            }
            else if (tabName == "Compendium")
            {
                RefreshCompendiumData();
            }
        }

        public void ShowHome(string statusMessage = null)
        {
            if (sidebar != null) sidebar.style.display = DisplayStyle.Flex;
            if (viewRoom != null) viewRoom.style.display = DisplayStyle.None;
            if (matchmakingButton != null) matchmakingButton.SetEnabled(true);
            if (joinRoomButton != null) joinRoomButton.SetEnabled(true);
            ShowTab("Home");
            if (!string.IsNullOrWhiteSpace(statusMessage)) SetRoomStatus(statusMessage);
        }

        public void ShowRoom()
        {
            audioSettingsView?.SetVisible(false);
            if (sidebar != null) sidebar.style.display = DisplayStyle.None;
            if (viewHome != null) viewHome.style.display = DisplayStyle.None;
            if (viewWorkshop != null) viewWorkshop.style.display = DisplayStyle.None;
            if (viewCompendium != null) viewCompendium.style.display = DisplayStyle.None;
            if (viewCollection != null) viewCollection.style.display = DisplayStyle.None;
            if (viewSettings != null) viewSettings.style.display = DisplayStyle.None;
            if (viewRoom != null) viewRoom.style.display = DisplayStyle.Flex;

            if (deckEditorToolkit?.IsRoomAiEditorOpen != true)
                deckEditorToolkit?.HideEditor();
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

        public void CreateRoomWithCurrentSettings()
        {
            OnMatchmakingClicked();
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
            int total = selectedDeck?.CalculateCurrentAlienation() ?? 0;
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

        private void OnViewRoomListClicked()
        {
            if (roomListController != null)
            {
                roomListController.Open();
                return;
            }

            var controller = FindObjectOfType<RoomListController>(true);
            if (controller != null)
            {
                roomListController = controller;
                controller.Open();
                return;
            }

            Debug.LogWarning("[LobbyController] RoomListController is not assigned or found in scene.");
        }

        private void OnJoinRoomClicked()
        {
            HideRoomAdmissionBlocker();
            if (NetworkManager.Instance == null || string.IsNullOrWhiteSpace(roomIdInput?.value)) { SetRoomStatus("请输入要加入的房间号。"); return; }
            if (!NetworkManager.Instance.RoomService.JoinRoom(roomIdInput.value, GetNickname())) return;
            SetRoomStatus("正在加入房间...");
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
            service.RoomError += HandleRoomError;
            service.ConnectionDiagnosticsChanged += HandleConnectionDiagnosticsChanged;
            RefreshConnectionSettings(service.ConnectionDiagnostics);
        }

        private void UnsubscribeRoomService()
        {
            var service = NetworkManager.Instance?.RoomService;
            if (service == null) return;
            service.RoomError -= HandleRoomError;
            service.ConnectionDiagnosticsChanged -= HandleConnectionDiagnosticsChanged;
        }

        private void HandleRoomError(string message)
        {
            if (NetworkManager.Instance?.RoomService?.HasRoom != true)
                ShowRoomAdmissionBlocker(message);
            SetRoomStatus(message);
        }
        public void SetRoomStatus(string message) { if (roomStatusLabel != null) roomStatusLabel.text = message; }
    }
}
