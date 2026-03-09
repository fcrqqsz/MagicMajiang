using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using MahjongGame.Systems;

namespace MahjongGame.UI
{
    public class LobbyController : MonoBehaviour
    {
        [SerializeField]
        private UIDocument document;

        // Tabs
        private Button tabHome;
        private Button tabWorkshop;
        private Button tabSettings;

        // Views
        private VisualElement viewHome;
        private VisualElement viewWorkshop;
        private VisualElement viewSettings;

        // Elements
        private Label welcomeLabel;
        private Button matchmakingButton;

        private void OnTabHomeClicked() => ShowTab("Home");
        private void OnTabWorkshopClicked() => ShowTab("Workshop");
        private void OnTabSettingsClicked() => ShowTab("Settings");

        private void OnEnable()
        {
            if (document == null)
                document = GetComponent<UIDocument>();

            var root = document.rootVisualElement;
            if (root == null) return;

            tabHome = root.Q<Button>("TabHome");
            tabWorkshop = root.Q<Button>("TabWorkshop");
            tabSettings = root.Q<Button>("TabSettings");

            viewHome = root.Q<VisualElement>("ViewHome");
            viewWorkshop = root.Q<VisualElement>("ViewWorkshop");
            viewSettings = root.Q<VisualElement>("ViewSettings");

            welcomeLabel = root.Q<Label>("WelcomeLabel");
            matchmakingButton = root.Q<Button>("MatchmakingButton");

            if (tabHome != null) tabHome.clicked += OnTabHomeClicked;
            if (tabWorkshop != null) tabWorkshop.clicked += OnTabWorkshopClicked;
            if (tabSettings != null) tabSettings.clicked += OnTabSettingsClicked;

            if (matchmakingButton != null) matchmakingButton.clicked += OnMatchmakingClicked;

            if (ProfileManager.Instance != null && ProfileManager.Instance.CurrentProfile != null)
            {
                if (welcomeLabel != null)
                    welcomeLabel.text = $"Welcome, {ProfileManager.Instance.CurrentProfile.Nickname}";
            }

            ShowTab("Home");
        }

        private void OnDisable()
        {
            if (tabHome != null) tabHome.clicked -= OnTabHomeClicked;
            if (tabWorkshop != null) tabWorkshop.clicked -= OnTabWorkshopClicked;
            if (tabSettings != null) tabSettings.clicked -= OnTabSettingsClicked;
            if (matchmakingButton != null) matchmakingButton.clicked -= OnMatchmakingClicked;
        }

        private void ShowTab(string tabName)
        {
            if (viewHome != null) viewHome.style.display = tabName == "Home" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewWorkshop != null) viewWorkshop.style.display = tabName == "Workshop" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewSettings != null) viewSettings.style.display = tabName == "Settings" ? DisplayStyle.Flex : DisplayStyle.None;

            if (tabHome != null) UpdateTabStyle(tabHome, tabName == "Home");
            if (tabWorkshop != null) UpdateTabStyle(tabWorkshop, tabName == "Workshop");
            if (tabSettings != null) UpdateTabStyle(tabSettings, tabName == "Settings");
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
