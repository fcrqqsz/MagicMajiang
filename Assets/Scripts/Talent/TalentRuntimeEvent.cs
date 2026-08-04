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

        internal TalentRuntimeEvent Copy()
        {
            return new TalentRuntimeEvent
            {
                EventId = EventId,
                OwnerSeatIndex = OwnerSeatIndex,
                TalentId = TalentId,
                EventType = EventType,
                Visibility = Visibility,
                Value = Value
            };
        }
    }
}
