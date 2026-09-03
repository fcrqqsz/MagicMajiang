using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core.Network;

namespace MahjongGame.Systems
{
    public class LoadingScreenController : MonoBehaviour
    {
        public static LoadingScreenController Instance { get; private set; }

        [SerializeField]
        private UIDocument document;

        private VisualElement root;
        private VisualElement loadingContainer;
        private VisualElement reconnectContainer;
        private Label reconnectStatusLabel;
        private Button reconnectLeaveButton;
        private bool _loadingVisible;
        private bool _reconnectVisible;
        public bool IsVisible => _loadingVisible || _reconnectVisible;

        public event System.Action ReconnectLeaveRequested;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (document == null)
                document = GetComponent<UIDocument>();

            if (document != null)
            {
                document.sortingOrder = 1000;

                if (document.rootVisualElement != null)
                {
                    root = document.rootVisualElement;
                    loadingContainer = root.Q<VisualElement>("LoadingContainer");
                    reconnectContainer = root.Q<VisualElement>("ReconnectContainer");
                    reconnectStatusLabel = root.Q<Label>("ReconnectStatusLabel");
                    reconnectLeaveButton = root.Q<Button>("ReconnectLeaveButton");
                    if (reconnectLeaveButton != null)
                        reconnectLeaveButton.clicked += HandleReconnectLeaveClicked;
                    Hide();
                    HideReconnect();
                }
            }
        }

        public void Show()
        {
            _loadingVisible = true;
            if (loadingContainer != null)
            {
                loadingContainer.style.display = DisplayStyle.Flex;
            }
            RefreshVisibility();
        }

        public void Hide()
        {
            _loadingVisible = false;
            if (loadingContainer != null)
            {
                loadingContainer.style.display = DisplayStyle.None;
            }
            RefreshVisibility();
        }

        public void ShowReconnect(ClientRecoveryProgress progress)
        {
            if (progress == null || progress.Stage == ClientRecoveryStage.None || progress.Stage == ClientRecoveryStage.Restored)
            {
                HideReconnect();
                return;
            }

            _reconnectVisible = true;
            if (reconnectContainer != null)
                reconnectContainer.style.display = DisplayStyle.Flex;
            if (reconnectStatusLabel != null)
                reconnectStatusLabel.text = progress.Message;
            if (reconnectLeaveButton != null)
                reconnectLeaveButton.text = progress.Stage == ClientRecoveryStage.TerminalFailure ? "返回大厅" : "退出对战";
            RefreshVisibility();
        }

        public void HideReconnect()
        {
            _reconnectVisible = false;
            if (reconnectContainer != null)
                reconnectContainer.style.display = DisplayStyle.None;
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            if (root != null) root.style.display = IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HandleReconnectLeaveClicked()
        {
            ReconnectLeaveRequested?.Invoke();
        }

        private void OnDestroy()
        {
            if (reconnectLeaveButton != null)
                reconnectLeaveButton.clicked -= HandleReconnectLeaveClicked;
            if (Instance == this) Instance = null;
        }
    }
}
