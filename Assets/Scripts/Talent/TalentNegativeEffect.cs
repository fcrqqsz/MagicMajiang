using System;

namespace MahjongGame.Talents
{
    public static class TalentNegativeEffectTypes
    {
        public const string ReducePublicChargeLayer = "ReducePublicChargeLayer";
    }

    public sealed class TalentNegativeEffect
    {
        public int SourceSeatIndex { get; set; }
        public string SourceTalentId { get; set; }
        public int TargetSeatIndex { get; set; }
        public string TargetTalentId { get; set; }
        public string EffectType { get; set; }

        // This callback is created by server runtime code after it resolves the target's public state.
        // It must not capture client objects, concealed hands, or arbitrary room state.
        public Action Apply { get; set; }
    }

    public sealed class TalentNegativeEffectResult
    {
        public bool WasBlocked { get; set; }
        public bool WasApplied { get; set; }
        public string BlockingTalentId { get; set; }
    }

    public sealed class TalentNegativeEffectContext
    {
        private readonly Action<TalentRuntimeEvent> _eventSink;

        public TalentRuntimeState State { get; }
        internal bool HasPublicEffect { get; private set; }

        internal TalentNegativeEffectContext(
            TalentRuntimeState state,
            Action<TalentRuntimeEvent> eventSink)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            _eventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));
        }

        public void Reveal(string eventType, int value)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("A negative-effect event type is required.", nameof(eventType));

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
