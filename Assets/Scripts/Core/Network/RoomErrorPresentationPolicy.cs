using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    /// <summary>Keeps stable server error codes visible during development-authentication flows.</summary>
    public static class RoomErrorPresentationPolicy
    {
        public static string GetDisplayMessage(RoomErrorMessage error)
        {
            if (error == null) return "Room request failed.";
            if (string.IsNullOrWhiteSpace(error.code))
                return string.IsNullOrWhiteSpace(error.message) ? "Room request failed." : error.message;

            return string.IsNullOrWhiteSpace(error.message)
                ? error.code
                : $"{error.code}: {error.message}";
        }
    }
}
