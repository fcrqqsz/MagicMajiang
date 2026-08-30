using System;
using MahjongGame.Core.Network;

namespace MahjongGame.UI
{
    /// <summary>Pure mapping from authoritative connection diagnostics to lobby-facing strings and state.</summary>
    public static class LobbyConnectionPresentationPolicy
    {
        public static LobbyConnectionPresentationView Build(ClientConnectionDiagnostics diagnostics)
        {
            if (diagnostics == null)
            {
                return new LobbyConnectionPresentationView(
                    "未知", "connection-status-gray", "套接字阶段：未知", "v10 握手：--", "RTT：--",
                    "上次检查：--", "最近错误：--", "就绪状态：等待网络服务初始化", actionsDisabled: true);
            }

            string phaseText = GetPhaseText(diagnostics.Phase);
            return new LobbyConnectionPresentationView(
                phaseText,
                GetStatusClass(diagnostics.Phase),
                $"套接字阶段：{phaseText}",
                $"v{diagnostics.ProtocolVersion} 握手：{GetHandshakeText(diagnostics.Phase)}",
                diagnostics.RoundTripTimeMilliseconds.HasValue
                    ? $"RTT：{diagnostics.RoundTripTimeMilliseconds.Value} ms"
                    : "RTT：--",
                diagnostics.LastCheckedUtc.HasValue
                    ? $"上次检查：{diagnostics.LastCheckedUtc.Value:yyyy-MM-dd HH:mm:ss} UTC"
                    : "上次检查：--",
                $"最近错误：{(string.IsNullOrWhiteSpace(diagnostics.LastError) ? "--" : diagnostics.LastError)}",
                $"就绪状态：{GetReadinessText(diagnostics.Phase)}",
                diagnostics.Phase == ClientConnectionPhase.Connecting
                    || diagnostics.Phase == ClientConnectionPhase.Authenticating);
        }

        private static string GetPhaseText(ClientConnectionPhase phase) => phase switch
        {
            ClientConnectionPhase.Disconnected => "已断开",
            ClientConnectionPhase.Connecting => "连接中",
            ClientConnectionPhase.Authenticating => "身份验证",
            ClientConnectionPhase.Ready => "已就绪",
            ClientConnectionPhase.Failed => "连接失败",
            _ => "未知"
        };

        private static string GetStatusClass(ClientConnectionPhase phase) => phase switch
        {
            ClientConnectionPhase.Disconnected => "connection-status-gray",
            ClientConnectionPhase.Connecting => "connection-status-yellow",
            ClientConnectionPhase.Authenticating => "connection-status-blue",
            ClientConnectionPhase.Ready => "connection-status-green",
            ClientConnectionPhase.Failed => "connection-status-red",
            _ => "connection-status-gray"
        };

        private static string GetHandshakeText(ClientConnectionPhase phase) => phase switch
        {
            ClientConnectionPhase.Connecting => "等待套接字连接",
            ClientConnectionPhase.Authenticating => "验证中",
            ClientConnectionPhase.Ready => "已完成",
            ClientConnectionPhase.Failed => "失败",
            _ => "未开始"
        };

        private static string GetReadinessText(ClientConnectionPhase phase) => phase switch
        {
            ClientConnectionPhase.Ready => "可创建或加入房间",
            ClientConnectionPhase.Failed => "请检查服务器后重试",
            ClientConnectionPhase.Connecting => "正在建立套接字连接",
            ClientConnectionPhase.Authenticating => "正在验证协议身份",
            _ => "可选择服务器或重新测试"
        };
    }

    public sealed class LobbyConnectionPresentationView
    {
        public string StatusText { get; }
        public string StatusClass { get; }
        public string SocketPhaseText { get; }
        public string HandshakeText { get; }
        public string RoundTripTimeText { get; }
        public string LastCheckedText { get; }
        public string LastErrorText { get; }
        public string ReadinessText { get; }
        public bool ActionsDisabled { get; }

        public LobbyConnectionPresentationView(string statusText, string statusClass, string socketPhaseText,
            string handshakeText, string roundTripTimeText, string lastCheckedText, string lastErrorText,
            string readinessText, bool actionsDisabled)
        {
            StatusText = statusText;
            StatusClass = statusClass;
            SocketPhaseText = socketPhaseText;
            HandshakeText = handshakeText;
            RoundTripTimeText = roundTripTimeText;
            LastCheckedText = lastCheckedText;
            LastErrorText = lastErrorText;
            ReadinessText = readinessText;
            ActionsDisabled = actionsDisabled;
        }
    }
}
