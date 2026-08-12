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

            if (error.code == PlayerLoadoutErrorCodes.AlienationPresetMismatch)
                return $"所选构筑的异化档位（{error.loadoutAlienationPreset}）与房间档位（{error.roomAlienationPreset}）不一致。";
            if (error.code == PlayerLoadoutErrorCodes.AlienationLimitExceeded)
                return $"所选构筑异化值 {error.actual} 超过房间上限 {error.limit}。";

            return string.IsNullOrWhiteSpace(error.message)
                ? error.code
                : $"{error.code}: {error.message}";
        }
    }
}
