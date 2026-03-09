using UnityEngine;
using UnityEngine.UIElements;

namespace MahjongGame.Systems
{
    public class LoadingScreenController : MonoBehaviour
    {
        public static LoadingScreenController Instance { get; private set; }

        [SerializeField]
        private UIDocument document;

        private VisualElement root;
        private VisualElement loadingContainer;

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
                    Hide();
                }
            }
        }

        public void Show()
        {
            if (loadingContainer != null)
            {
                loadingContainer.style.display = DisplayStyle.Flex;
            }
        }

        public void Hide()
        {
            if (loadingContainer != null)
            {
                loadingContainer.style.display = DisplayStyle.None;
            }
        }
    }
}
