using System;

namespace MahjongGame.Core
{
    public enum DeckEditorBudgetTone
    {
        Normal,
        NearLimit,
        OverLimit
    }

    public sealed class DeckEditorDraftView
    {
        public string Title { get; }
        public DeckEditorBudgetTone Tone { get; }
        public bool CanSave { get; }
        public string StatusText { get; }

        public DeckEditorDraftView(
            string title,
            DeckEditorBudgetTone tone,
            bool canSave,
            string statusText)
        {
            Title = title;
            Tone = tone;
            CanSave = canSave;
            StatusText = statusText;
        }
    }

    public sealed class DeckEditorLeavePromptView
    {
        public bool IsRequired { get; }
        public bool CanSave { get; }
        public string Message { get; }

        public DeckEditorLeavePromptView(bool isRequired, bool canSave, string message)
        {
            IsRequired = isRequired;
            CanSave = canSave;
            Message = message ?? string.Empty;
        }
    }

    public static class DeckEditorDraftPresentationPolicy
    {
        public static DeckEditorDraftView Build(
            AlienationGaugeView gauge,
            int tileCount,
            bool isDirty)
        {
            if (gauge == null) throw new ArgumentNullException(nameof(gauge));

            DeckEditorBudgetTone tone = gauge.IsOverLimit
                ? DeckEditorBudgetTone.OverLimit
                : gauge.Total * 5 >= gauge.Limit * 4
                    ? DeckEditorBudgetTone.NearLimit
                    : DeckEditorBudgetTone.Normal;

            bool canSave = tileCount == 34;
            string status = !canSave
                ? $"当前牌数 {tileCount} / 34，无法保存或进入房间"
                : gauge.IsOverLimit
                    ? $"超限 {gauge.Overflow}，仍可保存，不能进入该档位房间"
                    : "当前方案可进入该档位房间";

            return new DeckEditorDraftView(
                isDirty ? "当前构筑预算 · 未保存" : "当前构筑预算",
                tone,
                canSave,
                status);
        }

        public static DeckEditorLeavePromptView BuildLeavePrompt(bool isDirty, int tileCount)
        {
            if (!isDirty) return new DeckEditorLeavePromptView(false, false, string.Empty);
            if (tileCount != 34)
            {
                return new DeckEditorLeavePromptView(
                    true,
                    false,
                    $"当前牌数为 {tileCount} 张，不是 34 张，无法保存。是否放弃修改？");
            }

            return new DeckEditorLeavePromptView(
                true,
                true,
                "当前构筑有未保存修改。请选择保存、放弃或取消。");
        }
    }
}
