using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using MahjongGame.Systems;

namespace MahjongGame.UI
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
            if (NetworkManager.Instance == null)
            {
                Debug.LogError("NetworkManager not ready");
                return;
            }

            if (loginButton != null) loginButton.SetEnabled(false);

            string user = usernameInput != null ? usernameInput.value : "";
            string pass = passwordInput != null ? passwordInput.value : "";

            bool success = await NetworkManager.Instance.AuthService.LoginAsync(user, pass);

            if (success)
            {
                var profile = ProfileManager.Instance.CurrentProfile;
                if (profile != null)
                {
                    // Temporary identity bridge until the account service owns the player profile.
                    profile.Nickname = LoginUsernamePolicy.Normalize(user);
                    ProfileManager.Instance.SaveProfile();
                }
                await NetworkManager.Instance.LoadSceneAndUnloadCurrentAsync(SceneNames.MainLobby, SceneNames.Login);
            }
            else
            {
                Debug.LogError("Login failed.");
                if (loginButton != null) loginButton.SetEnabled(true);
            }
        }
    }
}
