using UnityEngine;
using TMPro; // 引用 TextMeshPro 命名空间

namespace MahjongGame.Core
{
    public class TileVisual : MonoBehaviour
    {
        // 引用 View 组件
        [Header("UI Components")]
        [SerializeField] private TextMeshPro _faceText;  // 显示牌面文字
        [SerializeField] private Renderer _renderer;     // 控制底色（可选）

        // 持有 Model 数据
        public TileData Data { get; private set; }

        // 持有控制器的引用 (类似于 C++ 的 Parent Pointer)
        private HandController _ownerController;

        /// <summary>
        /// 初始化函数：注入数据
        /// 类似于 C++ 的 SetData / Bind
        /// </summary>
        public void Initialize(TileData data, HandController controller)
        {
            this.Data = data;
            this._ownerController = controller; // 绑定控制器
            RefreshVisual();
        }

        /// <summary>
        /// 根据数据刷新显示
        /// </summary>
        private void RefreshVisual()
        {
            if (Data == null) return;

            // --- 简单的显示逻辑 (占位符) ---
            _faceText.text = Data.GetName(true);

            // --- 特色玩法反馈 ---
            // 如果这张牌被修改过（比如天赋加持），改变文字颜色为红色
            if (Data.IsModified)
            {
                _faceText.color = Color.red;
            }
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