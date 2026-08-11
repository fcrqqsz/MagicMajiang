namespace MahjongGame.Core
{
    public class ScoringOptions
    {
        public int BonusFan = 0;                // 快人一步: +2
        public bool RelaxedPureStraight = false; // 如龙: 宽松清龙
    }

    public sealed class TalentFanResolution
    {
        public int EligibilityFan { get; set; }
        public int PostLegalBonusFan { get; set; }
        public int NegativeFan { get; set; }
        public int FinalFan { get; set; }
    }
}
