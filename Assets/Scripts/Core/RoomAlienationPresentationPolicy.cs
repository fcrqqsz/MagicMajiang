namespace MahjongGame.Core
{
    public sealed class RoomAlienationVisibilityView
    {
        public string PublicSummary { get; }
        public string OwnSummary { get; }
        public string SeatSummary { get; }

        public RoomAlienationVisibilityView(string publicSummary, string ownSummary)
        {
            PublicSummary = publicSummary;
            OwnSummary = ownSummary;
            SeatSummary = string.Empty;
        }
    }

    public static class RoomAlienationPresentationPolicy
    {
        public static RoomAlienationVisibilityView Build(AlienationPreset preset, int ownTotal)
        {
            string displayPreset = RoomLoadoutAdmissionPresentationPolicy.GetDisplayName(preset);
            int displayLimit = AlienationBudgetPolicy.GetLimit(
                AlienationBudgetPolicy.IsDefined(preset) ? preset : AlienationPreset.Standard);
            int displayTotal = ownTotal < 0 ? 0 : ownTotal;
            return new RoomAlienationVisibilityView(
                $"异化档位：{displayPreset}",
                $"本家异化：{displayTotal} / {displayLimit}");
        }
    }
}
