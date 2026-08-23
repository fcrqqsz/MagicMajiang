namespace MahjongGame.Core.Network
{
    /// <summary>Supplies a stable player identity without coupling rooms to an account implementation.</summary>
    public interface IAccountAuthenticator
    {
        bool TryAuthenticate(string username, out AuthenticatedIdentity identity, out string errorCode);
    }

    public sealed class AuthenticatedIdentity
    {
        public readonly string PlayerId;
        public readonly string DisplayName;

        public AuthenticatedIdentity(string playerId, string displayName)
        {
            PlayerId = playerId;
            DisplayName = displayName;
        }
    }

    public static class NetworkProtocol
    {
        public const int Version = 8;

        public static bool IsSupported(int protocolVersion) => protocolVersion == Version;
    }

    public static class NetworkErrorCodes
    {
        public const string InvalidUsername = "InvalidUsername";
        public const string IdentityInUse = "IdentityInUse";
        public const string ReconnectRequired = "ReconnectRequired";
        public const string ProtocolMismatch = "ProtocolMismatch";
        public const string AuthenticationRequired = "AuthenticationRequired";
        public const string MessageTooLarge = "MessageTooLarge";
        public const string RoomNotFound = "RoomNotFound";
        public const string SeatExpired = "SeatExpired";
        public const string StreamMismatch = "StreamMismatch";
        public const string InvalidAction = "InvalidAction";
        public const string NoActiveDecision = "NoActiveDecision";
        public const string StaleDecision = "StaleDecision";
        public const string DecisionExpired = "DecisionExpired";
        public const string DuplicateAction = "DuplicateAction";
        public const string WrongController = "WrongController";
        public const string WrongPhase = "WrongPhase";
        public const string NotEligible = "NotEligible";
        public const string RoundAborted = "RoundAborted";
    }
}
