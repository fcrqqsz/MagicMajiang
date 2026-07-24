using System;
using System.Threading;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace MahjongGame.Core.Network.Transport
{
    public class GameEndpoint : WebSocketBehavior
    {
        private static long _nextConnectionGeneration;

        public static event Action<string, GameEndpoint, long> OnClientConnected;
        public static event Action<string, string, GameEndpoint, long> OnMessageReceived; // connectionId, json, endpoint, generation
        public static event Action<string, GameEndpoint, long> OnClientDisconnected;

        public string ConnectionId { get; private set; }
        public long ConnectionGeneration { get; private set; }

        protected override void OnOpen()
        {
            ConnectionId = ID; // websocket-sharp provides a unique ID
            long generation = Interlocked.Increment(ref _nextConnectionGeneration);
            ConnectionGeneration = generation;
            MainThreadDispatcher.Instance.Enqueue(() => OnClientConnected?.Invoke(ConnectionId, this, generation));
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.IsText)
            {
                string data = e.Data;
                long generation = ConnectionGeneration;
                MainThreadDispatcher.Instance.Enqueue(() => OnMessageReceived?.Invoke(ConnectionId, data, this, generation));
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Debug.LogWarning($"[GameEndpoint] Connection closed. ID: {ID}, Reason: {e.Reason}, Code: {e.Code}");
            long generation = ConnectionGeneration;
            MainThreadDispatcher.Instance.Enqueue(() => OnClientDisconnected?.Invoke(ConnectionId, this, generation));
        }

        protected override void OnError(WebSocketSharp.ErrorEventArgs e)
        {
            Debug.LogError($"[GameEndpoint] Error: {e.Message}, Exception: {e.Exception}");
        }

        public void SendMessage(string msg)
        {
            if (State == WebSocketState.Open)
            {
                Send(msg);
            }
        }
    }

    public class WebSocketService : MonoBehaviour
    {
        public static WebSocketService Instance { get; private set; }
        private WebSocketServer _wss;

        public int Port = 9876;

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

        public void StartServer()
        {
            if (_wss != null && _wss.IsListening)
            {
                Debug.LogWarning("[WebSocketService] Server is already running.");
                return;
            }

            _wss = new WebSocketServer(Port);
            _wss.AddWebSocketService<GameEndpoint>("/game");
            _wss.Start();

            Debug.Log($"[WebSocketService] Server started on ws://0.0.0.0:{Port}/game");
        }

        private void OnDestroy()
        {
            if (_wss != null)
            {
                _wss.Stop();
                _wss = null;
            }
        }
    }
}
