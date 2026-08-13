using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core
{
    public sealed class TalentResultRow
    {
        public string Text { get; set; }
        public bool IsNegative { get; set; }
        public bool ShouldLogWarning { get; set; }
    }

    public sealed class TalentResultView
    {
        public bool IsVisible { get; set; }
        public int FinalFan { get; set; }
        public string FinalFanText { get; set; } = string.Empty;
        public IReadOnlyList<TalentResultRow> Rows { get; set; }
            = Array.Empty<TalentResultRow>();
        public bool HasMismatchDiagnostic { get; set; }
    }

    public readonly struct ResultOverlayPresentation
    {
        public bool ShowImmediately { get; }
        public bool Animate { get; }

        public ResultOverlayPresentation(bool showImmediately, bool animate)
        {
            ShowImmediately = showImmediately;
            Animate = animate;
        }
    }

    public static class ResultOverlayPresentationPolicy
    {
        public static ResultOverlayPresentation Build(bool isRecovery) =>
            new ResultOverlayPresentation(
                showImmediately: isRecovery,
                animate: !isRecovery);
    }

    public static class ResultSessionPresentationPolicy
    {
        public static string GetContinueButtonText(Network.GameSession session)
        {
            if (session == null) return "返回主菜单";
            return session.Mode == GameMode.Single || session.IsSessionOver()
                ? "查看总结算"
                : "下一局";
        }
    }

    public static class TalentResultPresentationPolicy
    {
        public static TalentResultView BuildAcceptedWin(
            int acceptedFinalFan,
            TalentFanBreakdownMessage breakdown,
            TalentRegistry registry)
        {
            if (breakdown == null)
            {
                return new TalentResultView
                {
                    IsVisible = true,
                    FinalFan = acceptedFinalFan,
                    FinalFanText = $"最终番 {acceptedFinalFan}"
                };
            }

            TalentResultView result = Build(breakdown, registry);
            result.HasMismatchDiagnostic = result.HasMismatchDiagnostic
                                           || breakdown.finalFan != acceptedFinalFan;
            return result;
        }

        public static TalentResultView Build(
            TalentFanBreakdownMessage breakdown,
            TalentRegistry registry)
        {
            if (breakdown == null) return new TalentResultView();
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var rows = new List<TalentResultRow>
            {
                new TalentResultRow { Text = $"基础番 {breakdown.baseFan}" }
            };

            TalentFanContributionMessage[] contributions =
                (breakdown.contributions ?? Array.Empty<TalentFanContributionMessage>())
                .Where(row => row != null)
                .ToArray();
            foreach (TalentFanContributionMessage contribution in contributions
                         .Where(row => row.fanDelta != 0)
                         .OrderBy(row => row.sequence))
            {
                bool isKnown = !string.IsNullOrWhiteSpace(contribution.talentId)
                               && registry.HasTalent(contribution.talentId);
                string displayName = isKnown
                    ? registry.GetDisplayName(contribution.talentId)
                    : "未知天赋";
                string signedDelta = contribution.fanDelta > 0
                    ? $"+{contribution.fanDelta}"
                    : contribution.fanDelta.ToString();
                rows.Add(new TalentResultRow
                {
                    Text = $"{displayName} {signedDelta}",
                    IsNegative = contribution.fanDelta < 0,
                    ShouldLogWarning = !isKnown
                });
            }

            long attributedFinal = (long)breakdown.baseFan
                                   + contributions.Sum(row => (long)row.fanDelta);
            return new TalentResultView
            {
                IsVisible = true,
                FinalFan = breakdown.finalFan,
                FinalFanText = $"最终番 {breakdown.finalFan}",
                Rows = rows,
                HasMismatchDiagnostic = attributedFinal != breakdown.finalFan
            };
        }
    }
}
