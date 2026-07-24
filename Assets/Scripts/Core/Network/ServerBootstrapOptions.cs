using System;

namespace MahjongGame.Core.Network
{
    /// <summary>Dedicated-server command-line options that do not depend on Unity runtime state.</summary>
    public sealed class ServerBootstrapOptions
    {
        public const int DefaultPort = 9876;
        public const int DefaultMaxRooms = 1;
        public const bool DefaultAiFill = true;
        public const int DefaultReconnectWindowSeconds = 120;
        public const int DefaultMessageCacheSize = 256;

        public int Port { get; private set; } = DefaultPort;
        public int MaxRooms { get; private set; } = DefaultMaxRooms;
        public bool AiFill { get; private set; } = DefaultAiFill;
        public int ReconnectWindowSeconds { get; private set; } = DefaultReconnectWindowSeconds;
        public int MessageCacheSize { get; private set; } = DefaultMessageCacheSize;
        public int HeartbeatTimeoutSeconds { get; private set; } = ConnectionLivenessPolicy.DefaultHeartbeatTimeoutSeconds;

        public static ServerBootstrapOptions Parse(string[] args)
        {
            var options = new ServerBootstrapOptions();
            if (args == null) return options;

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                if (TryReadValue(argument, "--port", args, ref i, out string portValue))
                {
                    if (int.TryParse(portValue, out int port) && port > 0 && port <= 65535) options.Port = port;
                }
                else if (TryReadValue(argument, "--maxRooms", args, ref i, out string maxRoomsValue))
                {
                    if (int.TryParse(maxRoomsValue, out int maxRooms) && maxRooms > 0) options.MaxRooms = maxRooms;
                }
                else if (TryReadValue(argument, "--aiFill", args, ref i, out string aiFillValue))
                {
                    if (bool.TryParse(aiFillValue, out bool aiFill)) options.AiFill = aiFill;
                }
                else if (TryReadValue(argument, "--reconnectWindowSeconds", args, ref i, out string reconnectWindowValue))
                {
                    if (int.TryParse(reconnectWindowValue, out int reconnectWindow) && reconnectWindow > 0)
                        options.ReconnectWindowSeconds = reconnectWindow;
                }
                else if (TryReadValue(argument, "--messageCacheSize", args, ref i, out string messageCacheValue))
                {
                    if (int.TryParse(messageCacheValue, out int messageCacheSize) && messageCacheSize > 0)
                        options.MessageCacheSize = messageCacheSize;
                }
                else if (TryReadValue(argument, "--heartbeatTimeoutSeconds", args, ref i, out string heartbeatTimeoutValue))
                {
                    if (int.TryParse(heartbeatTimeoutValue, out int heartbeatTimeout) && heartbeatTimeout > 0)
                        options.HeartbeatTimeoutSeconds = heartbeatTimeout;
                }
            }

            return options;
        }

        private static bool TryReadValue(string argument, string option, string[] args, ref int index, out string value)
        {
            string prefix = option + "=";
            if (argument != null && argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
