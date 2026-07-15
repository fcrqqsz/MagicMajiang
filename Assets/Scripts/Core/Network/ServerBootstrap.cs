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
        [SerializeField] private int defaultPort = 9876;
        [SerializeField] private int defaultMaxRooms = 1;
        [SerializeField] private bool defaultAiFill = true;

        public int Port { get; private set; }
        public int MaxRooms { get; private set; }
        public bool AiFill { get; private set; }
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
            _connectionRegistry = new ConnectionRegistry();
            _roomManager = new RoomManager(MaxRooms, AiFill, _connectionRegistry);
            Debug.Log($"[ServerBootstrap] ServerBootstrap started. Port={Port}, MaxRooms={MaxRooms}, AiFill={AiFill}");
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
            Port = defaultPort;
            MaxRooms = defaultMaxRooms;
            AiFill = defaultAiFill;

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                if (TryReadValue(argument, "--port", args, ref i, out string portValue))
                {
                    if (int.TryParse(portValue, out int port) && port > 0 && port <= 65535)
                        Port = port;
                    else
                        Debug.LogWarning($"[ServerBootstrap] Ignoring invalid --port value '{portValue}'.");
                }
                else if (TryReadValue(argument, "--maxRooms", args, ref i, out string maxRoomsValue))
                {
                    if (int.TryParse(maxRoomsValue, out int maxRooms) && maxRooms > 0)
                        MaxRooms = maxRooms;
                    else
                        Debug.LogWarning($"[ServerBootstrap] Ignoring invalid --maxRooms value '{maxRoomsValue}'.");
                }
                else if (TryReadValue(argument, "--aiFill", args, ref i, out string aiFillValue))
                {
                    if (bool.TryParse(aiFillValue, out bool aiFill))
                        AiFill = aiFill;
                    else
                        Debug.LogWarning($"[ServerBootstrap] Ignoring invalid --aiFill value '{aiFillValue}'.");
                }
            }
        }

        private static bool TryReadValue(string argument, string option, string[] args, ref int index, out string value)
        {
            string prefix = option + "=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = argument.Substring(prefix.Length);
                return true;
            }

            if (string.Equals(argument, option, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                value = args[++index];
                return true;
            }

            value = null;
            return false;
        }
    }
}
