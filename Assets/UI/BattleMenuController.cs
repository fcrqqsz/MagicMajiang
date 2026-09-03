using System;
using System.Collections.Generic;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Systems;
using MahjongGame.Systems.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace MahjongGame.UI
{
    /// <summary>Independent battle-scene modal; network authority continues behind the menu.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattleMenuController : MonoBehaviour
    {
        public static BattleMenuController Instance { get; private set; }
        [SerializeField] private UIDocument _document;
        private readonly BattleMenuState _state = new BattleMenuState();
        private readonly Dictionary<Button, Action> _callbacks = new Dictionary<Button, Action>();
        private VisualElement _root;
        private VisualElement _home, _settings, _confirm, _leaving;
        private Label _title, _keyHint, _notice, _exitHint, _leavingStatus;
        private Button _continueButton, _exitButton, _cancelButton, _retryButton;
        private ClientRoomService _room;
        private AudioSettingsView _audioView;
        private IVisualElementScheduledItem _focusSchedule;
        private bool _exitInFlight;
        private bool _disposed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (_document == null) _document = GetComponent<UIDocument>();
            _document.sortingOrder = 200;
            _root = _document.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("[BattleMenu] Missing UIDocument root.");
                return;
            }
            _root.style.display = DisplayStyle.None;
            _root.RegisterCallback<KeyDownEvent>(ConsumeEscape, TrickleDown.TrickleDown);
            _home = _root.Q("HomePage");
            _settings = _root.Q("SettingsPage");
            _confirm = _root.Q("ConfirmPage");
            _leaving = _root.Q("LeavingPage");
            _title = _root.Q<Label>("MenuTitle");
            _keyHint = _root.Q<Label>("KeyHint");
            _notice = _root.Q<Label>("MenuNotice");
            _exitHint = _root.Q<Label>("ExitHint");
            _leavingStatus = _root.Q<Label>("LeavingStatus");
            _continueButton = Bind("ContinueButton", CloseMenu);
            Bind("SettingsButton", () => { _state.ShowSettings(); Render(); });
            Bind("SettingsBackButton", Back);
            _exitButton = Bind("ExitButton", RequestExit);
            _cancelButton = Bind("CancelExitButton", CloseMenu);
            Bind("ConfirmExitButton", ConfirmExit);
            _retryButton = Bind("RetryExitButton", BeginExit);
            Render();
        }

        private void Start()
        {
            _room = NetworkManager.Instance?.RoomService;
            _audioView = new AudioSettingsView(_settings, AudioManager.Instance);
            if (_room != null)
            {
                _room.AcceptedSequenceEnvelope += HandleEnvelope;
                _room.RecoveryProgressChanged += HandleRecovery;
                _room.ReconnectSnapshotApplied += HandleSnapshot;
                _room.RoomClosed += HandleRoomClosed;
            }
            Render();
        }

        private void Update()
        {
            if (_root == null || _disposed) return;
            if (LoadingScreenController.Instance?.IsVisible == true)
            {
                if (_state.IsOpen && !_state.IsLeaving) CloseMenu();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (!_state.IsOpen && !CanOpen()) return;
                Back();
            }
        }

        public void OpenMenu()
        {
            if (!CanOpen()) return;
            _state.Open();
            Render();
        }

        private bool CanOpen() => !_disposed && _root != null
            && (_room?.HasRoom == true || _room?.IsSessionCompleted == true)
            && LoadingScreenController.Instance?.IsVisible != true;

        private Button Bind(string name, Action callback)
        {
            Button button = _root.Q<Button>(name);
            if (button == null) return null;
            button.clicked += callback;
            _callbacks.Add(button, callback);
            return button;
        }

        private static void ConsumeEscape(KeyDownEvent evt)
        {
            // Global Input owns navigation. Prevent the same key reaching a lower document.
            if (evt.keyCode == KeyCode.Escape) evt.StopImmediatePropagation();
        }

        private void Back() { _state.Escape(); Render(); }
        private void CloseMenu() { _state.Close(); Render(); }

        private void RequestExit()
        {
            bool leave = _state.RequestExit(_room?.IsSessionCompleted == true);
            Render();
            if (leave) BeginExit();
        }

        private void ConfirmExit()
        {
            if (!_state.ConfirmExit()) return;
            Render();
            BeginExit();
        }

        private async void BeginExit()
        {
            if (_exitInFlight || !_state.IsLeaving || _disposed) return;
            _exitInFlight = true;
            _audioView?.SetVisible(false);
            if (_retryButton != null) _retryButton.style.display = DisplayStyle.None;
            if (_leavingStatus != null) _leavingStatus.text = "正在返回大厅...";
            try
            {
                NetworkManager manager = NetworkManager.Instance;
                if (manager == null) throw new InvalidOperationException("NetworkManager is unavailable.");
                await manager.LeaveBattleToLobbyAsync();
            }
            catch (Exception exception)
            {
                Debug.LogError("[BattleMenu] Return to lobby failed: " + exception.Message);
                if (!_disposed)
                {
                    if (_leavingStatus != null) _leavingStatus.text = "返回大厅失败，请重试。";
                    if (_retryButton != null) _retryButton.style.display = DisplayStyle.Flex;
                }
            }
            finally { _exitInFlight = false; }
        }

        private void HandleEnvelope(NetworkMessageEnvelope envelope)
        {
            switch (envelope?.type)
            {
                case "RoundStart":
                case "PlayerWin":
                case "DrawGame":
                case "SideboardStarted":
                case "SessionEnd":
                    ResetForBoundary();
                    break;
            }
        }

        private void HandleRecovery(ClientRecoveryProgress progress)
        {
            if (progress != null && progress.Stage != ClientRecoveryStage.None) ResetForBoundary();
        }
        private void HandleSnapshot(RoomGameSnapshot snapshot) => ResetForBoundary();
        private void HandleRoomClosed(string reason) => ResetForBoundary();
        private void ResetForBoundary() { _state.OnAuthoritativeBoundary(); Render(); }

        private void Render()
        {
            if (_root == null || _disposed) return;
            _focusSchedule?.Pause();
            _focusSchedule = null;
            bool completed = _room?.IsSessionCompleted == true;
            _root.style.display = _state.IsOpen ? DisplayStyle.Flex : DisplayStyle.None;
            BattleMenuInputGate.Instance.SetBlocked(_state.IsOpen, Time.frameCount);
            SetVisible(_home, _state.Page == BattleMenuPage.Home);
            SetVisible(_settings, _state.Page == BattleMenuPage.Settings);
            SetVisible(_confirm, _state.Page == BattleMenuPage.ConfirmExit);
            SetVisible(_leaving, _state.IsLeaving);
            _audioView?.SetVisible(_state.Page == BattleMenuPage.Settings);
            if (_title != null) _title.text = _state.Page == BattleMenuPage.Settings ? "设置"
                : _state.Page == BattleMenuPage.ConfirmExit ? "退出本场对战？"
                : _state.IsLeaving ? "退出对战" : "对战菜单";
            if (_keyHint != null) _keyHint.text = _state.IsLeaving ? string.Empty
                : _state.Page == BattleMenuPage.Home ? "Esc 关闭" : "Esc 返回";
            if (_notice != null) _notice.text = completed ? "本场对战已结束。" : "菜单打开期间，对局仍在继续。";
            if (_exitButton != null) _exitButton.text = completed ? "返回大厅" : "退出对战";
            if (_exitHint != null) _exitHint.text = completed ? "返回大厅开始新的对战" : "退出前将再次确认";
            Button focus = _state.Page == BattleMenuPage.ConfirmExit ? _cancelButton
                : _state.Page == BattleMenuPage.Home ? _continueButton : null;
            if (focus != null) _focusSchedule = _root.schedule.Execute(() => focus.Focus());
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            _disposed = true;
            _focusSchedule?.Pause();
            _audioView?.Dispose();
            foreach (var pair in _callbacks) pair.Key.clicked -= pair.Value;
            _callbacks.Clear();
            _root?.UnregisterCallback<KeyDownEvent>(ConsumeEscape, TrickleDown.TrickleDown);
            if (_room != null)
            {
                _room.AcceptedSequenceEnvelope -= HandleEnvelope;
                _room.RecoveryProgressChanged -= HandleRecovery;
                _room.ReconnectSnapshotApplied -= HandleSnapshot;
                _room.RoomClosed -= HandleRoomClosed;
            }
            BattleMenuInputGate.Instance.SetBlocked(false, Time.frameCount);
            Instance = null;
        }
    }
}
