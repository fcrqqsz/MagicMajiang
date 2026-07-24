using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    /// <summary>Builds the initial development-authentication message for room connections.</summary>
    public static class ClientHelloProtocol
    {
        public static HelloMessage Create(string username) => new HelloMessage
        {
            protocolVersion = NetworkProtocol.Version,
            username = username
        };
    }
}
