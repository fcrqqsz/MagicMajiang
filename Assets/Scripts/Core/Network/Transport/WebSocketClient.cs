using System;
using UnityEngine;
using WebSocketSharp;

namespace MahjongGame.Core.Network.Transport
{
    public class WebSocketClient : MonoBehaviour
    {
        public static WebSocketClient Instance { get; private set; }

        private WebSocket _ws;

        public event Action OnConnected;
        public event Action<string> OnMessageReceived;
        public event Action<string> OnDisconnected;

        public WebSocketState ReadyState
        {
            get
            {
                return _ws != null ? _ws.ReadyState : WebSocketState.Closed;
            }
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                // 确保在主线程上提前初始化 Dispatcher 单例
                var dispatcher = MainThreadDispatcher.Instance;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void Connect(string url)
        {
            if (_ws != null && _ws.ReadyState == WebSocketState.Open)
            {
                Debug.LogWarning("[WebSocketClient] Already connected.");
                return;
            }

            _ws = new WebSocket(url);

            _ws.OnOpen += (sender, e) =>
            {
                Debug.Log($"[WebSocketClient] Connection opened successfully to {url}");
                MainThreadDispatcher.Instance.Enqueue(() => OnConnected?.Invoke());
            };

            _ws.OnMessage += (sender, e) =>
            {
                if (e.IsText)
                {
                    string data = e.Data;
                    MainThreadDispatcher.Instance.Enqueue(() => OnMessageReceived?.Invoke(data));
                }
            };

            _ws.OnClose += (sender, e) =>
            {
                Debug.LogWarning($"[WebSocketClient] Connection closed. Reason: {e.Reason}, Code: {e.Code}, WasClean: {e.WasClean}");
                MainThreadDispatcher.Instance.Enqueue(() => OnDisconnected?.Invoke(e.Reason));
            };

            _ws.OnError += (sender, e) =>
            {
                Debug.LogError($"[WebSocketClient] Socket error: {e.Message}, Exception: {e.Exception}");
            };

            Debug.Log($"[WebSocketClient] Initiating connection request to {url}...");
            _ws.ConnectAsync();
        }

        public void SendNetworkMessage(string msg)
        {
            if (_ws != null && _ws.ReadyState == WebSocketState.Open)
            {
                _ws.SendAsync(msg, null);
            }
            else
            {
                Debug.LogWarning("[WebSocketClient] Cannot send message, not connected.");
            }
        }

        public void Disconnect()
        {
            if (_ws != null)
            {
                _ws.CloseAsync();
                _ws = null;
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}
