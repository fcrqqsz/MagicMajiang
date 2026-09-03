using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using MahjongGame.Talents;

namespace MahjongGame.UI
{
    public class FloatingTilePanelController : MonoBehaviour
    {
        public static FloatingTilePanelController Instance;

        [SerializeField] private UIDocument _document;

        private VisualElement _documentRoot;
        private VisualElement _root;
        private VisualElement _body;
        private Label _titleLabel;
        private ScrollView _scrollView;
        private Button _closeBtn;

        private Coroutine _autoCloseCoroutine;
        private Action _closeClicked;
        private bool _isOptionSelection;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            if (_document == null)
                _document = GetComponent<UIDocument>();

            if (_document != null && _document.rootVisualElement != null)
            {
                _documentRoot = _document.rootVisualElement;
                _documentRoot.pickingMode = PickingMode.Ignore;
                _root = _documentRoot.Q<VisualElement>("FloatingTilePanelRoot");
                _body = _documentRoot.Q<VisualElement>("FloatingPanelBody");
                _titleLabel = _documentRoot.Q<Label>("FloatingPanelTitle");
                _scrollView = _documentRoot.Q<ScrollView>("FloatingTileScroll");
                _closeBtn = _documentRoot.Q<Button>("FloatingPanelCloseBtn");

                ConfigureClose(Hide);
                HideImmediate();
            }
        }

        private void OnDestroy()
        {
            StopAutoClose();
            if (_closeBtn != null && _closeClicked != null)
                _closeBtn.clicked -= _closeClicked;
            _closeClicked = null;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 展示模式：显示牌面信息，autoCloseSeconds=0 表示不自动关闭
        /// </summary>
        public void ShowTiles(string title, IEnumerable<TileData> tiles, float autoCloseSeconds = 5f)
        {
            if (_root == null) return;

            StopAutoClose();
            _isOptionSelection = false;
            PopulateTiles(title, (tiles ?? Array.Empty<TileData>()).ToList(), false, null);
            ConfigureClose(Hide);
            _closeBtn.text = "知道了";
            _closeBtn.style.display = DisplayStyle.Flex;
            ShowPanel();

            if (autoCloseSeconds > 0)
            {
                _autoCloseCoroutine = StartCoroutine(AutoCloseAfter(autoCloseSeconds));
            }
        }

        /// <summary>
        /// 选择模式：显示牌面让玩家选一张，选中后回调
        /// </summary>
        public void ShowSelection(string title, List<TileData> tiles, Action<int> onSelected)
        {
            if (_root == null) return;

            StopAutoClose();
            _isOptionSelection = false;
            PopulateTiles(title, tiles, true, onSelected);
            _closeBtn.style.display = DisplayStyle.None;
            ShowPanel();
        }

        public void ShowOptionSelection(
            string title,
            IReadOnlyList<TalentActionTargetPresentation> options,
            Action<TalentActionOption> onSelected,
            Action onCancelled)
        {
            if (_root == null) return;

            StopAutoClose();
            _isOptionSelection = true;
            _titleLabel.text = title;
            _scrollView.Clear();
            foreach (TalentActionTargetPresentation target in options ?? Array.Empty<TalentActionTargetPresentation>())
            {
                if (target?.Option == null) continue;
                TalentActionOption selected = TalentActionPanelPolicy.CloneOption(target.Option);
                string buttonText = string.IsNullOrWhiteSpace(target.TalentDisplayName)
                    ? target.SeatDisplayName
                    : $"{target.SeatDisplayName}\n{target.TalentDisplayName} · 充能 {target.PublicCharge}";
                var item = new Button
                {
                    text = buttonText
                };
                item.AddToClassList("floating-tile-item");
                item.AddToClassList("selectable");
                item.clicked += () =>
                {
                    if (BattleMenuInputGate.Instance.IsBlocked(Time.frameCount)) return;
                    onSelected?.Invoke(TalentActionPanelPolicy.CloneOption(selected));
                    Hide();
                };
                _scrollView.Add(item);
            }

            ConfigureClose(() =>
            {
                Hide();
                onCancelled?.Invoke();
            });
            _closeBtn.text = "取消";
            _closeBtn.style.display = DisplayStyle.Flex;
            ShowPanel();
        }

        /// <summary>
        /// 关闭面板
        /// </summary>
        public void Hide()
        {
            if (_root == null) return;

            StopAutoClose();
            _isOptionSelection = false;
            _body.RemoveFromClassList("panel--visible");
            _root.AddToClassList("panel--hidden");
            SetDocumentVisibility(false);
        }

        public void HideOptionSelection()
        {
            if (!_isOptionSelection) return;
            Hide();
        }

        private void HideImmediate()
        {
            _body.RemoveFromClassList("panel--visible");
            _root.AddToClassList("panel--hidden");
            SetDocumentVisibility(false);
        }

        private void ShowPanel()
        {
            SetDocumentVisibility(true);
            _root.RemoveFromClassList("panel--hidden");
            // 延迟一帧添加 visible class，让 opacity transition 生效
            _root.schedule.Execute(() =>
            {
                _body.AddToClassList("panel--visible");
            });
        }

        private void SetDocumentVisibility(bool visible)
        {
            if (_documentRoot == null) return;
            _documentRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void PopulateTiles(string title, List<TileData> tiles, bool selectable, Action<int> onSelected)
        {
            _titleLabel.text = title;
            _scrollView.Clear();

            if (tiles == null || tiles.Count == 0)
            {
                var emptyLabel = new Label("没有可展示的牌");
                emptyLabel.AddToClassList("floating-panel-empty-hint");
                emptyLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                emptyLabel.style.alignSelf = Align.Center;
                _scrollView.Add(emptyLabel);
                return;
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                int index = i;

                var item = new VisualElement();
                item.AddToClassList("floating-tile-item");

                if (selectable)
                {
                    item.AddToClassList("selectable");
                }

                var image = new VisualElement();
                image.AddToClassList("floating-tile-image");

                string imagePath = TileImageHelper.GetTileImagePath(tile);
                Sprite tileSprite = Resources.Load<Sprite>(imagePath);
                if (tileSprite != null)
                {
                    image.style.backgroundImage = new StyleBackground(tileSprite);
                }

                item.Add(image);
                _scrollView.Add(item);

                if (selectable)
                {
                    item.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (BattleMenuInputGate.Instance.IsBlocked(Time.frameCount)) return;
                        onSelected?.Invoke(index);
                        Hide();
                    });
                }
            }
        }

        private IEnumerator AutoCloseAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Hide();
        }

        private void StopAutoClose()
        {
            if (_autoCloseCoroutine != null)
            {
                StopCoroutine(_autoCloseCoroutine);
                _autoCloseCoroutine = null;
            }
        }

        private void ConfigureClose(Action callback)
        {
            if (_closeBtn == null) return;
            if (_closeClicked != null) _closeBtn.clicked -= _closeClicked;
            _closeClicked = callback;
            if (_closeClicked != null) _closeBtn.clicked += _closeClicked;
        }
    }
}
