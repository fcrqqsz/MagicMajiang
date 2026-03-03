using UnityEngine;

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

        private void Awake()
        {
            // 如果你在 Inspector 里忘了拖，代码会自动尝试在子物体里找
            if (faceRenderer == null)
            {
                faceRenderer = GetComponentInChildren<SpriteRenderer>();
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