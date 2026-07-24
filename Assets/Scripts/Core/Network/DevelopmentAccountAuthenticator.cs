namespace MahjongGame.Core.Network
{
    /// <summary>
    /// Development-only identity bridge. It validates a username but deliberately stores no account data,
    /// credentials, or recovery material.
    /// </summary>
    public sealed class DevelopmentAccountAuthenticator : IAccountAuthenticator
    {
        public bool TryAuthenticate(string username, out AuthenticatedIdentity identity, out string errorCode)
        {
            identity = null;
            if (!UsernameIdentityPolicy.TryNormalize(username, out var displayName, out var playerId, out errorCode))
                return false;

            identity = new AuthenticatedIdentity(playerId, displayName);
            return true;
        }
    }
}
