using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;

namespace MahjongGame.UI
{
    public class WaitHintController : MonoBehaviour
    {
        public static WaitHintController Instance;

        [SerializeField] private UIDocument _document; // 显式引用
        private VisualElement _root;
        private ScrollView _scrollList;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            // 如果没有在 Inspector 中拖拽赋值，则尝试获取自身的 UIDocument
            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            if (_document != null && _document.rootVisualElement != null)
            {
                _root = _document.rootVisualElement.Q<VisualElement>("WaitHintRoot");
                _scrollList = _document.rootVisualElement.Q<ScrollView>("WaitListScroll");
                HideHint();
            }
            else
            {
                Debug.LogWarning("[WaitHintController] Cannot find UIDocument or its rootVisualElement.");
            }
        }

        public void ShowHint(List<MahjongLogic.WaitDetail> details)
        {
            if (_root == null)
            {
                Debug.LogError("[WaitHintController] ShowHint failed: _root (WaitHintRoot) is null!");
                return;
            }
            
            _scrollList.Clear();
            
            foreach (var detail in details)
            {
                var item = new VisualElement();
                item.AddToClassList("wait-item");

                var image = new VisualElement();
                image.AddToClassList("wait-item-image");
                
                string imagePath = GetTileImagePath(detail.WaitTile.TileSuit, detail.WaitTile.Value);
                Sprite tileSprite = Resources.Load<Sprite>(imagePath);
                if (tileSprite != null)
                {
                    image.style.backgroundImage = new StyleBackground(tileSprite);
                }
                else
                {
                    Debug.LogWarning($"[WaitHintController] Failed to load sprite at {imagePath}");
                }
                
                var fanText = new Label($"{detail.MaxFan}番");
                fanText.AddToClassList("wait-item-fan");

                item.Add(image);
                item.Add(fanText);
                
                _scrollList.Add(item);
            }

            // 修改为 Flex，确保它能被 UI Toolkit 渲染
            _root.style.display = DisplayStyle.Flex;
        }

        public void HideHint()
        {
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        private string GetTileImagePath(Suit suit, int value)
        {
            string prefix = "Art/FlatTile/f";
            string suffix = "";
            string valueStr = value.ToString();

            switch (suit)
            {
                case Suit.Man: suffix = "m"; break;
                case Suit.Pin: suffix = "p"; break;
                case Suit.Sou: suffix = "s"; break;
                case Suit.Wind: suffix = "z"; break;
                case Suit.Dragon:
                    suffix = "z";
                    if (value == 1) valueStr = "7"; // 中
                    else if (value == 2) valueStr = "6"; // 发
                    else if (value == 3) valueStr = "5"; // 白
                    break;
            }

            return $"{prefix}{valueStr}{suffix}";
        }
    }
}