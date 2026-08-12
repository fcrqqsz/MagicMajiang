using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core
{
    public enum TalentFeedbackLevel
    {
        Silent,
        Weak,
        Medium,
        Strong
    }

    public sealed class TalentFeedbackView
    {
        public TalentFeedbackLevel Level { get; set; }
        public string Copy { get; set; }
        public bool ShowToast { get; set; }
        public bool AppendFeed { get; set; }
        public bool PulseChip { get; set; }
        public bool PlayAudio { get; set; }
        public bool ShouldLogWarning { get; set; }
        public bool IsSilent => Level == TalentFeedbackLevel.Silent;
    }

    /// <summary>Maps trusted event identifiers to local, fixed presentation copy.</summary>
    public static class TalentEventPresentationPolicy
    {
        public static TalentFeedbackView Build(TalentRuntimeEventMessage runtimeEvent, bool isRecovery)
        {
            if (isRecovery)
                return new TalentFeedbackView { Level = TalentFeedbackLevel.Silent, Copy = string.Empty };

            string eventType = runtimeEvent?.eventType;
            string talentName = GetLocalTalentName(runtimeEvent?.talentId);
            switch (eventType)
            {
                case "active_talent_applied":
                    return Create(TalentFeedbackLevel.Strong, talentName + "已生效", true, true, true, true);
                case "talent_revealed":
                    return Create(TalentFeedbackLevel.Medium, talentName + "已揭示", false, true, true, false);
                case "blocked_negative_effect":
                    return Create(TalentFeedbackLevel.Medium, talentName + "已阻止负面效果", false, true, true, false);
                case "public_charge_reduced":
                    return Create(TalentFeedbackLevel.Medium, talentName + "的充能已变化", false, true, true, false);
                case "public_counter_changed":
                case "public_uses_changed":
                    return Create(TalentFeedbackLevel.Medium, talentName + "的状态已变化", false, true, true, false);
                case "private_state_refresh":
                case "state_updated":
                    return Create(TalentFeedbackLevel.Weak, "天赋状态已更新", false, false, false, false);
                default:
                    return new TalentFeedbackView
                    {
                        Level = TalentFeedbackLevel.Weak,
                        Copy = "天赋状态已更新",
                        ShouldLogWarning = true
                    };
            }
        }

        private static TalentFeedbackView Create(
            TalentFeedbackLevel level,
            string copy,
            bool showToast,
            bool appendFeed,
            bool pulseChip,
            bool playAudio) => new TalentFeedbackView
        {
            Level = level,
            Copy = copy,
            ShowToast = showToast,
            AppendFeed = appendFeed,
            PulseChip = pulseChip,
            PlayAudio = playAudio
        };

        private static string GetLocalTalentName(string talentId) =>
            !string.IsNullOrWhiteSpace(talentId) && TalentRegistry.Instance.HasTalent(talentId)
                ? TalentRegistry.Instance.GetDisplayName(talentId)
                : "天赋";
    }

    public sealed class TalentFeedbackHistory
    {
        private long _highestEventId;

        public bool TryBuild(
            TalentRuntimeEventMessage runtimeEvent,
            bool isRecovery,
            out TalentFeedbackView feedback)
        {
            feedback = TalentEventPresentationPolicy.Build(runtimeEvent, isRecovery);
            return runtimeEvent != null
                && !feedback.IsSilent
                && TryAccept(runtimeEvent.eventId);
        }

        public bool TryAccept(long eventId)
        {
            if (eventId <= 0 || eventId <= _highestEventId) return false;
            _highestEventId = eventId;
            return true;
        }

        public void ResetForNewMatch() => _highestEventId = 0;
    }
}
