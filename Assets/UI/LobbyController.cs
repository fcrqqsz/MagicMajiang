using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using MahjongGame.Core.Network.Data;
using MahjongGame.Systems;

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
        private Button matchmakingButton;
        
        // Settings Elements
        private Slider masterVolumeSlider;
        private Slider musicVolumeSlider;
        private Slider sfxVolumeSlider;
        private Toggle debugModeToggle;

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
            matchmakingButton = root.Q<Button>("MatchmakingButton");
            
            masterVolumeSlider = root.Q<Slider>("MasterVolumeSlider");
            musicVolumeSlider = root.Q<Slider>("MusicVolumeSlider");
            sfxVolumeSlider = root.Q<Slider>("SFXVolumeSlider");
            debugModeToggle = root.Q<Toggle>("DebugModeToggle");

            if (tabHome != null) tabHome.clicked += OnTabHomeClicked;
            if (tabWorkshop != null) tabWorkshop.clicked += OnTabWorkshopClicked;
            if (tabCollection != null) tabCollection.clicked += OnTabCollectionClicked;
            if (tabSettings != null) tabSettings.clicked += OnTabSettingsClicked;

            if (matchmakingButton != null) matchmakingButton.clicked += OnMatchmakingClicked;

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
            }

            ShowTab("Home");
        }

        private void OnDisable()
        {
            if (tabHome != null) tabHome.clicked -= OnTabHomeClicked;
            if (tabWorkshop != null) tabWorkshop.clicked -= OnTabWorkshopClicked;
            if (tabCollection != null) tabCollection.clicked -= OnTabCollectionClicked;
            if (tabSettings != null) tabSettings.clicked -= OnTabSettingsClicked;
            if (matchmakingButton != null) matchmakingButton.clicked -= OnMatchmakingClicked;

            if (deckEditorToolkit != null)
            {
                deckEditorToolkit.OnDeckSaved -= HandleDeckSaved;
            }
        }

        private void HandleDeckSaved(DeckConfig newConfig)
        {
            Debug.Log("Deck Saved! Alienation Score: " + newConfig.AlienationScore);
            
            if (ProfileManager.Instance != null && ProfileManager.Instance.CurrentProfile != null)
            {
                if (ProfileManager.Instance.CurrentProfile.SavedDecks.Count == 0)
                {
                    ProfileManager.Instance.CurrentProfile.SavedDecks.Add(new SavedDeck { DeckId = System.Guid.NewGuid().ToString(), DeckName = "My Custom Deck", AlienationScore = newConfig.AlienationScore, Config = newConfig });
                }
                else
                {
                    ProfileManager.Instance.CurrentProfile.SavedDecks[0].AlienationScore = newConfig.AlienationScore;
                    ProfileManager.Instance.CurrentProfile.SavedDecks[0].Config = newConfig;
                }
                ProfileManager.Instance.SaveProfile();
            }

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
            if (masterVolumeSlider != null) masterVolumeSlider.RegisterValueChangedCallback(evt => {
                ProfileManager.Instance.CurrentProfile.Settings.MasterVolume = evt.newValue;
                ProfileManager.Instance.SaveProfile();
            });
            if (musicVolumeSlider != null) musicVolumeSlider.RegisterValueChangedCallback(evt => {
                ProfileManager.Instance.CurrentProfile.Settings.MusicVolume = evt.newValue;
                ProfileManager.Instance.SaveProfile();
            });
            if (sfxVolumeSlider != null) sfxVolumeSlider.RegisterValueChangedCallback(evt => {
                ProfileManager.Instance.CurrentProfile.Settings.SFXVolume = evt.newValue;
                ProfileManager.Instance.SaveProfile();
            });
            if (debugModeToggle != null) debugModeToggle.RegisterValueChangedCallback(evt => {
                ProfileManager.Instance.CurrentProfile.Settings.DebugMode = evt.newValue;
                ProfileManager.Instance.SaveProfile();
            });
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

            // Handle independent DeckEditor UI
            if (deckEditorToolkit != null)
            {
                deckEditorToolkit.gameObject.SetActive(tabName == "Workshop");
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

        private async void OnMatchmakingClicked()
        {
            if (NetworkManager.Instance == null)
            {
                Debug.LogError("NetworkManager not ready");
                return;
            }

            if (matchmakingButton != null) matchmakingButton.SetEnabled(false);

            string roomId = await NetworkManager.Instance.MatchmakingService.FindRoomAsync();

            if (!string.IsNullOrEmpty(roomId))
            {
                Debug.Log($"Joining Room: {roomId}");
                await NetworkManager.Instance.LoadSceneAndUnloadCurrentAsync(SceneNames.Game, SceneNames.MainLobby);
            }
            else
            {
                Debug.LogError("Matchmaking failed.");
                if (matchmakingButton != null) matchmakingButton.SetEnabled(true);
            }
        }
    }
}