using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;

namespace MahjongGame.UI
{
    public class WaitHintController : MonoBehaviour
    {
        private const float WaitItemWidth = 54f;
        private const float WaitStripFixedWidth = 88f;
        private const float WaitStripMinimumWidth = 196f;
        private const float WaitStripMaximumWidth = 790f;

        public static WaitHintController Instance;

        [SerializeField] private UIDocument _document; // 显式引用
        private VisualElement _documentRoot;
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
                _documentRoot = _document.rootVisualElement;
                _root = _documentRoot.Q<VisualElement>("WaitHintRoot");
                _scrollList = _documentRoot.Q<ScrollView>("WaitListScroll");
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
                
                string imagePath = TileImageHelper.GetTileImagePath(detail.WaitTile.TileSuit, detail.WaitTile.Value);
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

            _root.style.width = GameTableLayoutPolicy.GetWaitHintWidth(
                details.Count,
                WaitItemWidth,
                WaitStripFixedWidth,
                WaitStripMinimumWidth,
                WaitStripMaximumWidth);

            // 修改为 Flex，确保它能被 UI Toolkit 渲染
            _root.style.display = DisplayStyle.Flex;
            _documentRoot.style.display = DisplayStyle.Flex;
        }

        public void HideHint()
        {
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
            if (_documentRoot != null)
            {
                _documentRoot.style.display = DisplayStyle.None;
            }
        }

    }
}
