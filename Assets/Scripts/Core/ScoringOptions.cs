namespace MahjongGame.Core
{
    public sealed class FanEvaluation
    {
        public bool HasWinningShape { get; set; }
        public int Fan { get; set; }
        public System.Collections.Generic.List<string> FanDetails { get; set; }
    }

    public class ScoringOptions
    {
        public int BonusFan = 0;                // 快人一步: +2
        public bool RelaxedPureStraight = false; // 如龙: 宽松清龙
        internal object ExcludedTalentEntryIdentity { get; set; }
    }

    public enum TalentFanContributionCategory
    {
        Eligibility = 0,
        PostLegal = 1,
        Negative = 2
    }

    public sealed class TalentFanContribution
    {
        public string TalentId { get; set; }
        public int FanDelta { get; set; }
        public TalentFanContributionCategory Category { get; set; }
        public int Sequence { get; set; }
    }

    public sealed class TalentFanResolution
    {
        public bool IsAttributionComplete { get; set; }
        public int BaseFan { get; set; }
        public int EligibilityFan { get; set; }
        public int PostLegalBonusFan { get; set; }
        public int NegativeFan { get; set; }
        public int FinalFan { get; set; }
        public System.Collections.Generic.IReadOnlyList<TalentFanContribution> Contributions { get; set; } =
            System.Array.Empty<TalentFanContribution>();
    }
}
