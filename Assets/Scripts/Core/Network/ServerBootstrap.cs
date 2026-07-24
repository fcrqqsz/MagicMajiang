using System;
using MahjongGame.Core.Network.Transport;
using UnityEngine;

namespace MahjongGame.Core.Network
{
    /// <summary>
    /// Dedicated-server scene entry point. Room and match lifecycle are added in Phase C.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class ServerBootstrap : MonoBehaviour
    {
        public int Port { get; private set; }
        public int MaxRooms { get; private set; }
        public bool AiFill { get; private set; }
        public int ReconnectWindowSeconds { get; private set; }
        public int MessageCacheSize { get; private set; }
        public int HeartbeatTimeoutSeconds { get; private set; }
        private ConnectionRegistry _connectionRegistry;
        private RoomManager _roomManager;

        private void Awake()
        {
            Application.targetFrameRate = 30;
            ParseCommandLineArguments(Environment.GetCommandLineArgs());
        }

        private void Start()
        {
            var service = WebSocketService.Instance;
            if (service == null)
            {
                var serviceObject = new GameObject("WebSocketService");
                service = serviceObject.AddComponent<WebSocketService>();
            }

            service.Port = Port;
            service.StartServer();
            _connectionRegistry = new ConnectionRegistry(HeartbeatTimeoutSeconds);
            _roomManager = new RoomManager(MaxRooms, AiFill, _connectionRegistry, messageCacheSize: MessageCacheSize,
                reconnectWindowSeconds: ReconnectWindowSeconds);
            Debug.Log($"[ServerBootstrap] ServerBootstrap started. Port={Port}, MaxRooms={MaxRooms}, AiFill={AiFill}, ReconnectWindowSeconds={ReconnectWindowSeconds}, MessageCacheSize={MessageCacheSize}, HeartbeatTimeoutSeconds={HeartbeatTimeoutSeconds}");
        }

        private void OnDestroy()
        {
            _roomManager?.Dispose();
            _roomManager = null;
            _connectionRegistry = null;
        }

        private void Update()
        {
            _roomManager?.Tick(DateTime.UtcNow);
        }

        private void ParseCommandLineArguments(string[] args)
        {
            var options = ServerBootstrapOptions.Parse(args);
            Port = options.Port;
            MaxRooms = options.MaxRooms;
            AiFill = options.AiFill;
            ReconnectWindowSeconds = options.ReconnectWindowSeconds;
            MessageCacheSize = options.MessageCacheSize;
            HeartbeatTimeoutSeconds = options.HeartbeatTimeoutSeconds;
        }
    }
}
