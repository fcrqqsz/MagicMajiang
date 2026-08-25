using UnityEngine;
using DG.Tweening;

namespace MahjongGame.Core
{
    public class TileVisual : MonoBehaviour
    {
        // 引用 View 组件
        [Header("UI Components")]
        public SpriteRenderer faceRenderer; // 牌面
        [SerializeField] private Renderer _renderer;     // 控制底色（可选）
        // 我们需要一种方式获取 Config。
        // 方法A：单例管理器 (推荐)
        // 方法B：直接拖拽 (如果 Config 是静态的)
        // 这里为了简单，我们在 Initialize 时动态加载，或者建议做一个单例 ResourceManager
        
        // 假设我们在场景里有个单例持有 Config，或者简单点，直接在这里引用
        // 更好的方式是放在 GameManager 或 DeckManager 里，Init 时传进来
        // 但为了少改代码，我们用 Resources.Load 或者简单的静态引用

        // 持有 Model 数据
        public TileData Data { get; private set; }

        // 持有控制器的引用 (类似于 C++ 的 Parent Pointer)
        private MahjongHandViewBase _ownerController;
        
        private Material _instanceMaterial;
        private Color _defaultBaseColor;
        private bool _hasDefaultBaseColor;

        private Material GetInstanceMaterial()
        {
            if (_renderer == null) return null;
            if (_instanceMaterial == null)
            {
                _instanceMaterial = _renderer.material;
                _defaultBaseColor = _instanceMaterial.color;
                _hasDefaultBaseColor = true;
            }
            return _instanceMaterial;
        }

        public void SetHighlight(bool enable)
        {
            Material material = GetInstanceMaterial();
            if (material == null) return;

            if (enable)
            {
                material.EnableKeyword("_EMISSION");
                material.DOKill();
                // 提高颜色强度，Unity 中发光强度可以通过将 RGB 值设置大于 1 来实现 Bloom 泛光效果 (需要开启 PostProcessing)
                // 如果没有 PostProcessing，纯色如 Color.cyan 或 Color.yellow 会更明显
                Color startColor = new Color(0.1f, 0.3f, 0.3f);
                Color endColor = new Color(0.3f, 1.0f, 1.0f); // 明亮的青色/蓝绿色
                
                material.SetColor("_EmissionColor", startColor);
                material.DOColor(endColor, "_EmissionColor", 0.5f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine);
            }
            else
            {
                material.DOKill();
                material.SetColor("_EmissionColor", Color.black);
                material.DisableKeyword("_EMISSION");
            }
        }

        /// <summary>Changes only the 3D tile base; the face SpriteRenderer remains untouched.</summary>
        public void SetObservationHighlight(bool enable)
        {
            // Default-off observation must not instantiate one material per tile.
            if (!enable && _instanceMaterial == null) return;
            Material material = GetInstanceMaterial();
            if (material == null) return;
            material.color = enable
                ? new Color(0.72f, 0.08f, 0.08f, 1f)
                : (_hasDefaultBaseColor ? _defaultBaseColor : Color.white);
        }

        private void OnDestroy()
        {
            if (_instanceMaterial != null)
            {
                _instanceMaterial.DOKill();
                Destroy(_instanceMaterial);
            }
        }

        private void Awake()
        {
            // 如果你在 Inspector 里忘了拖，代码会自动尝试在子物体里找
            if (faceRenderer == null)
            {
                faceRenderer = GetComponentInChildren<SpriteRenderer>();
            }
            
            // 自动寻找底座 Renderer
            if (_renderer == null)
            {
                Renderer[] renderers = GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    // 排除掉牌面的 SpriteRenderer
                    if (r != faceRenderer && !(r is SpriteRenderer))
                    {
                        _renderer = r;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 初始化函数：注入数据
        /// 类似于 C++ 的 SetData / Bind
        /// </summary>
        // [优化] 直接接收 Sprite，而不是自己在内部去查
        public void Initialize(TileData data, MahjongHandViewBase owner, Sprite faceSprite)
        {
            Data = data;
            _ownerController = owner;

            faceRenderer.sprite = faceSprite;
            // 如果没图，需要关闭 renderer 以节省性能，或者防止紫色方块
            faceRenderer.enabled = (faceSprite != null);
            
            gameObject.name = $"Tile_{Data.TileSuit}_{Data.Value}";
        }

        /// <summary>
        /// Unity 内置鼠标点击事件
        /// 要求物体必须有 Collider 组件
        /// </summary>
        private void OnMouseDown()
        {
            // 只有当牌属于某个控制器时，点击才有效
            if (_ownerController != null)
            {
                // 通知控制器：我被点了
                _ownerController.OnTileClicked(this);
            }
        }
    }
}
