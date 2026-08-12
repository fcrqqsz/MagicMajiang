using MahjongGame.Core.Network;

namespace MahjongGame.Core
{
    public sealed class RoomLoadoutAdmissionView
    {
        public bool CanEnter { get; }
        public string Code { get; }
        public string Message { get; }

        public RoomLoadoutAdmissionView(bool canEnter, string code, string message)
        {
            CanEnter = canEnter;
            Code = code;
            Message = message;
        }
    }

    public static class RoomLoadoutAdmissionPresentationPolicy
    {
        public static RoomLoadoutAdmissionView Validate(
            AlienationPreset loadoutPreset,
            AlienationPreset roomPreset,
            int total)
        {
            AlienationPreset displayLoadoutPreset = NormalizeForDisplay(loadoutPreset);
            AlienationPreset displayRoomPreset = NormalizeForDisplay(roomPreset);
            int safeTotal = total < 0 ? 0 : total;

            if (displayLoadoutPreset != displayRoomPreset)
            {
                return new RoomLoadoutAdmissionView(
                    false,
                    PlayerLoadoutErrorCodes.AlienationPresetMismatch,
                    $"构筑档位 {GetDisplayName(displayLoadoutPreset)} 与房间档位 {GetDisplayName(displayRoomPreset)} 不一致，无法进入房间。");
            }

            int limit = AlienationBudgetPolicy.GetLimit(displayRoomPreset);
            if (safeTotal > limit)
            {
                return new RoomLoadoutAdmissionView(
                    false,
                    PlayerLoadoutErrorCodes.AlienationLimitExceeded,
                    $"构筑异化值 {safeTotal} 超过房间档位 {GetDisplayName(displayRoomPreset)}，超限 {safeTotal - limit}。");
            }

            return new RoomLoadoutAdmissionView(
                true,
                string.Empty,
                $"构筑符合房间档位 {GetDisplayName(displayRoomPreset)}。");
        }

        public static string GetDisplayName(AlienationPreset preset)
        {
            AlienationPreset displayPreset = NormalizeForDisplay(preset);
            return displayPreset switch
            {
                AlienationPreset.Low => "低异化 40",
                AlienationPreset.High => "高异化 120",
                _ => "标准 80"
            };
        }

        private static AlienationPreset NormalizeForDisplay(AlienationPreset preset) =>
            AlienationBudgetPolicy.IsDefined(preset) ? preset : AlienationPreset.Standard;
    }
}
