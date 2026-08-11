namespace MahjongGame.Talents
{
    public static class TalentActionErrorCodes
    {
        public const string NotAvailable = "NotAvailable";
        public const string InvalidTarget = "InvalidTarget";
        public const string InsufficientResource = "InsufficientResource";
        public const string AlreadyUsedThisTurn = "AlreadyUsedThisTurn";
        public const string NotCarriedOrInactive = "NotCarriedOrInactive";
    }

    public sealed class TalentActionOption
    {
        public string TalentId { get; set; }
        public int TargetSeatIndex { get; set; } = -1;
        public string TargetTalentId { get; set; }
    }

    public sealed class TalentActionRequest
    {
        public long DecisionId { get; set; }
        public string TalentId { get; set; }
        public int TargetSeatIndex { get; set; } = -1;
        public string TargetTalentId { get; set; }
    }

    public sealed class TalentActionResult
    {
        public bool Accepted { get; private set; }
        public string ErrorCode { get; private set; }

        public static TalentActionResult Success() => new TalentActionResult { Accepted = true };

        public static TalentActionResult Reject(string code) => new TalentActionResult
        {
            Accepted = false,
            ErrorCode = code
        };

        public static TalentActionResult NotSupported() => Reject(TalentActionErrorCodes.NotAvailable);
    }
}
