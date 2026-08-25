using System;

namespace MahjongGame.Core
{
    public static class TalentChipStatusPolicy
    {
        public static string Build(
            string talentId,
            int privateValue,
            string privateStatusKey,
            int observedTileCount,
            string targetPlayerDisplayName = null)
        {
            switch (talentId)
            {
                case "chromatic_composition":
                    int count = Math.Max(0, observedTileCount);
                    if (count < 4) return $"异化 {count}/4";
                    return $"异化 {count} +{Math.Min(count, 8) * 3}番";
                case "fading_color":
                    return $"墨 {Clamp(privateValue, 0, 2)}/2";
                case "prune_the_excess":
                    return $"弃牌 {Clamp(privateValue, 0, 3)}/3";
                case "bide_the_tide":
                    return $"弃牌 {Clamp(privateValue, 0, 6)}/6";
                case "last_stand_formation":
                    return $"副露 {Clamp(privateValue, 0, 2)}/2";
                case "set_the_tone":
                    return FormatSuit(privateStatusKey, string.Empty);
                case "foretell_outcome":
                    return privateStatusKey switch
                    {
                        "self_draw" => "自摸",
                        "ron" => "荣和",
                        _ => string.Empty
                    };
                case "prepare_for_risk":
                    return privateStatusKey switch
                    {
                        "protect_self_draw" => "防自摸",
                        "protect_ron" => "防放铳",
                        _ => string.Empty
                    };
                case "call_the_mark":
                    return privateStatusKey switch
                    {
                        "pending" => $"目标 {NormalizePlayerName(targetPlayerDisplayName)}",
                        "success" => "成功",
                        "failed" => "失败",
                        _ => string.Empty
                    };
                case "suit_convergence":
                    string suit = FormatSuit(privateStatusKey, string.Empty);
                    return string.IsNullOrEmpty(suit)
                        ? string.Empty
                        : $"{suit} 剩{Math.Max(0, privateValue)}次";
                default:
                    return string.Empty;
            }
        }

        private static string FormatSuit(string key, string prefix) => key switch
        {
            "man" => prefix + "万",
            "pin" => prefix + "饼",
            "sou" => prefix + "条",
            _ => string.Empty
        };

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;

        private static string NormalizePlayerName(string displayName) =>
            string.IsNullOrWhiteSpace(displayName) ? "未知玩家" : displayName.Trim();
    }
}
