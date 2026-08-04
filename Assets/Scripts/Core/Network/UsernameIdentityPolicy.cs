using System;
using System.Security.Cryptography;
using System.Text;

namespace MahjongGame.Core.Network
{
    /// <summary>Normalizes the insecure, development-only username credential used by protocol v3.</summary>
    public static class UsernameIdentityPolicy
    {
        public const int MaximumUsernameLength = 32;

        public static bool TryNormalize(string username, out string displayName, out string playerId, out string errorCode)
        {
            displayName = null;
            playerId = null;
            errorCode = null;

            string trimmedUsername = username?.Trim();
            if (string.IsNullOrEmpty(trimmedUsername) || trimmedUsername.Length > MaximumUsernameLength)
            {
                errorCode = NetworkErrorCodes.InvalidUsername;
                return false;
            }

            displayName = trimmedUsername;
            playerId = DeriveDevelopmentPlayerId(trimmedUsername);
            return true;
        }

        private static string DeriveDevelopmentPlayerId(string displayName)
        {
            string caseInsensitiveUsername = displayName.ToUpperInvariant();
            byte[] source = Encoding.UTF8.GetBytes("SuperMajiang:development:" + caseInsensitiveUsername);
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(source);
                return "dev:" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
