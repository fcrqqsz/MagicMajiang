using UnityEngine;
using UnityEngine.UIElements;
using SuperMajiang.Systems;

namespace SuperMajiang.UI
{
    public class LoginPanelController : MonoBehaviour
    {
        [SerializeField]
        private UIDocument document;

        private TextField usernameInput;
        private TextField passwordInput;
        private Button loginButton;

        private void OnEnable()
        {
            if (document == null)
                document = GetComponent<UIDocument>();

            var root = document.rootVisualElement;
            if (root == null) return;

            usernameInput = root.Q<TextField>("UsernameInput");
            passwordInput = root.Q<TextField>("PasswordInput");
            loginButton = root.Q<Button>("LoginButton");

            if (loginButton != null)
            {
                loginButton.clicked += OnLoginClicked;
            }
        }

        private void OnDisable()
        {
            if (loginButton != null)
            {
                loginButton.clicked -= OnLoginClicked;
            }
        }

        private async void OnLoginClicked()
        {
            if (loginButton != null) loginButton.SetEnabled(false);
            
            string user = usernameInput != null ? usernameInput.value : "";
            string pass = passwordInput != null ? passwordInput.value : "";

            bool success = await NetworkManager.Instance.AuthService.LoginAsync(user, pass);

            if (success)
            {
                // Unload login and load main lobby
                await NetworkManager.Instance.LoadSceneAndUnloadCurrentAsync("02_MainLobby", "01_Login");
            }
            else
            {
                Debug.LogError("Login failed.");
                if (loginButton != null) loginButton.SetEnabled(true);
            }
        }
    }
}
