using UnityEngine;
using UnityEngine.UIElements;
using MahjongGame.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.UI
{
    public class ActionPanelController : MonoBehaviour
    {
        public static ActionPanelController Instance;

        [SerializeField] private UIDocument _document;
        private VisualElement _root;
        private VisualElement _btnContainer;
        private Button _btnChi, _btnPon, _btnGan, _btnHu, _btnSkip;

        // 当前的回调函数引用
        private Action<string> _currentCallback;
        private Action<int> _currentChiCallback;

        void Awake()
        {
            // 单例保护
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _root = _document.rootVisualElement;
            _btnContainer = _root.Q<VisualElement>("ButtonContainer");

            _btnChi = _root.Q<Button>("BtnChi");
            _btnPon = _root.Q<Button>("BtnPon");
            _btnGan = _root.Q<Button>("BtnGan");
            _btnHu =  _root.Q<Button>("BtnHu");
            _btnSkip = _root.Q<Button>("BtnSkip");

            // --- 关键修复：只在这里绑定一次事件 ---
            // 点击时，只调用 _currentCallback，不要在这里 += 具体的逻辑
            _btnChi.clicked += () => SafeInvoke(() => _currentCallback?.Invoke("Chi"));
            _btnPon.clicked += () => SafeInvoke(() => _currentCallback?.Invoke("Pon"));
            _btnGan.clicked += () => SafeInvoke(() => _currentCallback?.Invoke("Gan"));
            _btnHu.clicked +=  () => SafeInvoke(() => _currentCallback?.Invoke("Hu"));
            _btnSkip.clicked += () => SafeInvoke(() => _currentCallback?.Invoke("Skip"));

            Hide();
        }

        // 防抖动辅助：防止极短时间内双击导致调用两次
        private void SafeInvoke(Action action)
        {
            if (_root.style.display == DisplayStyle.None) return;
            action?.Invoke();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
            // 清理回调，防止意外触发
            _currentCallback = null;
            _currentChiCallback = null;
        }

        public void Show(AllowedActions actions, Action<string> callback)
        {
            // 1. 赋值回调
            _currentCallback = callback;

            // 2. 还原界面状态 (如果之前显示了二级菜单，这里要还原)
            RestoreMainButtons();

            // 3. 设置可见性
            SetVisible(_btnChi, actions.CanChiLeft || actions.CanChiMiddle || actions.CanChiRight);
            SetVisible(_btnPon, actions.CanPon);
            SetVisible(_btnGan, actions.CanGan);
            SetVisible(_btnHu, actions.CanHu);
            SetVisible(_btnSkip, true);

            _root.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// 显示“吃”的二级菜单
        /// </summary>
        public void ShowChiSelection(List<string> options, Action<int> callback)
        {
            _currentChiCallback = callback;
            
            // 1. 隐藏主按钮
            HideMainButtons();

            // 2. 关键修复：先清理旧的临时按钮，防止重复堆叠！
            ClearTempButtons();

            // 3. 生成新按钮
            for (int i = 0; i < options.Count; i++)
            {
                int index = i; 
                Button btn = new Button();
                btn.text = $"吃\n{options[i]}";
                btn.AddToClassList("action-btn"); // 复用 USS 样式
                btn.style.width = 160;
                btn.style.height = 100;
                btn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.6f, 0.2f));
                
                // 标记为临时按钮
                btn.userData = "temp_chi";

                // 绑定点击
                btn.clicked += () => 
                {
                    // 同样做防抖保护
                    if (_root.style.display == DisplayStyle.None) return;
                    
                    _currentChiCallback?.Invoke(index);
                    
                    // 选完后恢复并关闭
                    RestoreMainButtons(); 
                    Hide(); 
                };

                _btnContainer.Add(btn);
            }
        }

        private void SetVisible(VisualElement elem, bool visible)
        {
            elem.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HideMainButtons()
        {
            SetVisible(_btnChi, false);
            SetVisible(_btnPon, false);
            SetVisible(_btnGan, false);
            SetVisible(_btnHu, false);
            SetVisible(_btnSkip, false);
        }

        // 彻底清理临时按钮
        private void ClearTempButtons()
        {
            // 倒序遍历删除
            for (int i = _btnContainer.childCount - 1; i >= 0; i--)
            {
                var child = _btnContainer.ElementAt(i);
                if (child.userData as string == "temp_chi")
                {
                    child.RemoveFromHierarchy();
                }
            }
        }

        private void RestoreMainButtons()
        {
            ClearTempButtons();
            // 注意：SetVisible 会在下次 Show 时根据 Action 重新设置，这里只需清理临时按钮
        }
    }
}