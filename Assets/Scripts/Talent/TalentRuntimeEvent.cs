namespace MahjongGame.Talents
{
    public enum TalentEventVisibility
    {
        OwnerOnly,
        Public
    }

    public sealed class TalentRuntimeEvent
    {
        public long EventId { get; set; }
        public int OwnerSeatIndex { get; set; }
        public string TalentId { get; set; }
        public string EventType { get; set; }
        public TalentEventVisibility Visibility { get; set; }
        public int Value { get; set; }
        public bool IsScoreDelta { get; set; }

        internal TalentRuntimeEvent Copy()
        {
            return new TalentRuntimeEvent
            {
                EventId = EventId,
                OwnerSeatIndex = OwnerSeatIndex,
                TalentId = TalentId,
                EventType = EventType,
                Visibility = Visibility,
                Value = Value,
                IsScoreDelta = IsScoreDelta
            };
        }
    }

    public sealed class TalentSnapshotEntry
    {
        public int OwnerSeatIndex { get; set; }
        public string TalentId { get; set; }
        public bool IsActive { get; set; }
        public bool IsRevealed { get; set; }
        public int PrivateValue { get; set; }
        public string PrivateStatusKey { get; set; }
        public string LastPublicEventType { get; set; }
        public int LastPublicValue { get; set; }
    }
}
