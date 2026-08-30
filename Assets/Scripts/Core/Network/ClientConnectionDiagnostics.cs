using System;

namespace MahjongGame.Core.Network
{
    public enum ClientServerEnvironment { Online, Local }

    public static class ClientServerEndpointPolicy
    {
        public static string Resolve(ClientServerEnvironment environment) =>
            environment == ClientServerEnvironment.Local
                ? "ws://127.0.0.1:9876/game"
                : "ws://123.207.13.148:9876/game";
    }

    public enum ClientConnectionPhase { Disconnected, Connecting, Authenticating, Ready, Failed }

    /// <summary>Immutable, non-secret client connection state for lobby presentation and diagnostics.</summary>
    public sealed class ClientConnectionDiagnostics
    {
        public string Address { get; }
        public ClientConnectionPhase Phase { get; }
        public int ProtocolVersion { get; }
        public int? RoundTripTimeMilliseconds { get; }
        public DateTime? LastCheckedUtc { get; }
        public string LastError { get; }

        public ClientConnectionDiagnostics(string address, ClientConnectionPhase phase, int protocolVersion,
            int? roundTripTimeMilliseconds, DateTime? lastCheckedUtc, string lastError)
        {
            Address = address;
            Phase = phase;
            ProtocolVersion = protocolVersion;
            RoundTripTimeMilliseconds = roundTripTimeMilliseconds;
            LastCheckedUtc = lastCheckedUtc;
            LastError = lastError;
        }
    }
}
