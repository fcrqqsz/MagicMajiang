namespace MahjongGame.Talents
{
    public static class TalentNegativeEffectTypes
    {
        public const string ReducePublicChargeLayer = "ReducePublicChargeLayer";
    }

    public sealed class TalentNegativeEffect
    {
        public int SourceSeatIndex { get; }
        public string SourceTalentId { get; }
        public int TargetSeatIndex { get; }
        public string TargetTalentId { get; }
        public string EffectType { get; }

        public TalentNegativeEffect(
            int sourceSeatIndex,
            string sourceTalentId,
            int targetSeatIndex,
            string targetTalentId,
            string effectType)
        {
            SourceSeatIndex = sourceSeatIndex;
            SourceTalentId = sourceTalentId;
            TargetSeatIndex = targetSeatIndex;
            TargetTalentId = targetTalentId;
            EffectType = effectType;
        }
    }

    public sealed class TalentNegativeEffectResult
    {
        public bool WasBlocked { get; set; }
        public bool WasApplied { get; set; }
        public string BlockingTalentId { get; set; }
    }

    internal interface IPublicChargeTalent
    {
        void ReducePublicChargeLayer(TalentPublicChargeContext context);
    }

    public sealed class TalentPublicChargeContext
    {
        public int OwnerSeatIndex { get; }
        internal TalentRuntimeState State { get; }

        internal TalentPublicChargeContext(int ownerSeatIndex, TalentRuntimeState state)
        {
            OwnerSeatIndex = ownerSeatIndex;
            State = state;
        }
    }

    public sealed class TalentNegativeEffectContext
    {
        private readonly System.Action<TalentRuntimeEvent> _eventSink;

        public TalentRuntimeState State { get; }
        internal bool HasPublicEffect { get; private set; }

        internal TalentNegativeEffectContext(
            TalentRuntimeState state,
            System.Action<TalentRuntimeEvent> eventSink)
        {
            State = state ?? throw new System.ArgumentNullException(nameof(state));
            _eventSink = eventSink ?? throw new System.ArgumentNullException(nameof(eventSink));
        }

        public void Reveal(string eventType, int value)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new System.ArgumentException("A negative-effect event type is required.", nameof(eventType));

            _eventSink(new TalentRuntimeEvent
            {
                EventType = eventType,
                Visibility = TalentEventVisibility.Public,
                Value = value
            });
            HasPublicEffect = true;
        }
    }
}
