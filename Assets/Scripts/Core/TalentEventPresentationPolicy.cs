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
        public static TalentFeedbackView Build(
            TalentRuntimeEventMessage runtimeEvent,
            bool isRecovery,
            string playerName = null)
        {
            if (isRecovery)
                return new TalentFeedbackView { Level = TalentFeedbackLevel.Silent, Copy = string.Empty };

            string eventType = runtimeEvent?.eventType;
            string talentName = GetLocalTalentName(runtimeEvent?.talentId);
            string prefix = GetPlayerPrefix(playerName, runtimeEvent?.ownerSeatIndex ?? -1);

            switch (eventType)
            {
                case "active_talent_applied":
                    return Create(TalentFeedbackLevel.Strong, prefix + talentName + "已生效", true, true, true, true);
                case "talent_revealed":
                    return Create(TalentFeedbackLevel.Medium, prefix + talentName + "已揭示", false, true, true, false);
                case "blocked_negative_effect":
                    return Create(TalentFeedbackLevel.Medium, prefix + talentName + "已阻止负面效果", false, true, true, false);
                case "public_charge_reduced":
                    return Create(TalentFeedbackLevel.Medium, prefix + talentName + "的充能已变化", false, true, true, false);
                case "public_counter_changed":
                case "public_uses_changed":
                    return Create(TalentFeedbackLevel.Medium, prefix + talentName + "的状态已变化", false, true, true, false);
                case "private_state_refresh":
                case "state_updated":
                    return Create(TalentFeedbackLevel.Weak, "天赋状态已更新", false, false, false, false);
                default:
                    return new TalentFeedbackView
                    {
                        Level = TalentFeedbackLevel.Weak,
                        Copy = prefix + "天赋状态已更新",
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

        private static string GetPlayerPrefix(string playerName, int ownerSeatIndex)
        {
            if (!string.IsNullOrWhiteSpace(playerName))
                return $"【{playerName}】";
            if (ownerSeatIndex >= 0 && ownerSeatIndex < 4)
                return $"【AI {ownerSeatIndex + 1}】";
            return string.Empty;
        }
    }

    public sealed class TalentFeedbackHistory
    {
        private long _highestEventId;

        public bool TryBuild(
            TalentRuntimeEventMessage runtimeEvent,
            bool isRecovery,
            out TalentFeedbackView feedback)
        {
            return TryBuild(runtimeEvent, isRecovery, null, out feedback);
        }

        public bool TryBuild(
            TalentRuntimeEventMessage runtimeEvent,
            bool isRecovery,
            string playerName,
            out TalentFeedbackView feedback)
        {
            feedback = TalentEventPresentationPolicy.Build(runtimeEvent, isRecovery, playerName);
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

    /// <summary>
    /// Pure lifecycle model for talent-only transient presentation. Recovery clears this state
    /// without changing event history, so a later ordered live event can present normally.
    /// </summary>
    public sealed class TalentTransientPresentationState
    {
        public int FeedCount { get; private set; }
        public bool IsToastVisible { get; private set; }
        public bool HasToastSchedule { get; private set; }
        public bool HasChipTween { get; private set; }
        public bool HasToastTween { get; private set; }
        public bool HasOpenDrawer { get; private set; }

        public void RecordLiveFeedback(TalentFeedbackView feedback)
        {
            if (feedback == null || feedback.IsSilent) return;
            if (feedback.AppendFeed) FeedCount = System.Math.Min(4, FeedCount + 1);
            if (feedback.PulseChip) HasChipTween = true;
            if (feedback.ShowToast)
            {
                IsToastVisible = true;
                HasToastSchedule = true;
                HasToastTween = true;
            }
        }

        public void OpenDrawer() => HasOpenDrawer = true;

        public void CloseDrawers() => HasOpenDrawer = false;

        public void HideToast()
        {
            IsToastVisible = false;
            HasToastSchedule = false;
        }

        public void ResetForRecovery()
        {
            FeedCount = 0;
            IsToastVisible = false;
            HasToastSchedule = false;
            HasChipTween = false;
            HasToastTween = false;
            HasOpenDrawer = false;
        }
    }
}
