using System;
using UnityEngine;
using WebSocketSharp;

namespace MahjongGame.Core.Network.Transport
{
    public class WebSocketClient : MonoBehaviour
    {
        public static WebSocketClient Instance { get; private set; }

        private WebSocket _ws;
        private long _connectionGeneration;
        private string _activeAddress;

        public event Action OnConnected;
        public event Action<string> OnMessageReceived;
        public event Action<string> OnDisconnected;
        public event Action<string> OnError;
        public event Action<string> OnMessageSent;

        public string ActiveAddress => _activeAddress;

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

            var previousSocket = _ws;
            _ws = null;
            if (previousSocket != null)
            {
                _connectionGeneration++;
                previousSocket.CloseAsync();
            }

            var socket = new WebSocket(url);
            long generation = ++_connectionGeneration;
            _ws = socket;
            _activeAddress = url;

            socket.OnOpen += (sender, e) =>
            {
                MainThreadDispatcher.Instance.Enqueue(() =>
                {
                    if (!IsCurrentSocket(socket, generation)) return;
                    Debug.Log($"[WebSocketClient] Connection opened successfully to {url}");
                    OnConnected?.Invoke();
                });
            };

            socket.OnMessage += (sender, e) =>
            {
                if (e.IsText)
                {
                    string data = e.Data;
                    MainThreadDispatcher.Instance.Enqueue(() =>
                    {
                        if (!IsCurrentSocket(socket, generation)) return;
                        OnMessageReceived?.Invoke(data);
                    });
                }
            };

            socket.OnClose += (sender, e) =>
            {
                MainThreadDispatcher.Instance.Enqueue(() =>
                {
                    if (!IsCurrentSocket(socket, generation)) return;
                    Debug.LogWarning($"[WebSocketClient] Connection closed. Reason: {e.Reason}, Code: {e.Code}, WasClean: {e.WasClean}");
                    OnDisconnected?.Invoke(e.Reason);
                });
            };

            socket.OnError += (sender, e) =>
            {
                MainThreadDispatcher.Instance.Enqueue(() =>
                {
                    if (!IsCurrentSocket(socket, generation)) return;
                    Debug.LogError($"[WebSocketClient] Socket error: {e.Message}, Exception: {e.Exception}");
                    OnError?.Invoke(string.IsNullOrWhiteSpace(e.Message) ? e.Exception?.Message : e.Message);
                });
            };

            Debug.Log($"[WebSocketClient] Initiating connection request to {url}...");
            socket.ConnectAsync();
        }

        public void SendNetworkMessage(string msg)
        {
            if (_ws != null && _ws.ReadyState == WebSocketState.Open)
            {
                var socket = _ws;
                long generation = _connectionGeneration;
                socket.SendAsync(msg, completed =>
                {
                    MainThreadDispatcher.Instance.Enqueue(() =>
                    {
                        if (!completed || !IsCurrentSocket(socket, generation)) return;
                        OnMessageSent?.Invoke(msg);
                    });
                });
            }
            else
            {
                Debug.LogWarning("[WebSocketClient] Cannot send message, not connected.");
            }
        }

        public void Disconnect()
        {
            var socket = _ws;
            _ws = null;
            _activeAddress = null;
            _connectionGeneration++;
            socket?.CloseAsync();
        }

        private bool IsCurrentSocket(WebSocket socket, long generation)
        {
            return generation == _connectionGeneration && ReferenceEquals(_ws, socket);
        }

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}
