using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core
{
    public sealed class SideboardPanelViewState
    {
        public static SideboardPanelViewState Closed { get; } = new SideboardPanelViewState(
            false, false, false, 0, 0, null, Array.Empty<bool>(), null, false);

        public bool IsVisible { get; }
        public bool IsReadOnly { get; }
        public bool IsSubmissionPending { get; }
        public bool IsEditable => IsVisible && !IsReadOnly && PrivateDraft != null;
        public long DecisionId { get; }
        public long DeadlineUnixMilliseconds { get; }
        public SideboardDraft PrivateDraft { get; }
        public IReadOnlyList<bool> SeatLocked { get; }
        public string LockReason { get; }
        public bool IsComplete { get; }

        internal SideboardPanelViewState(
            bool isVisible,
            bool isReadOnly,
            bool isSubmissionPending,
            long decisionId,
            long deadlineUnixMilliseconds,
            SideboardDraft privateDraft,
            IEnumerable<bool> seatLocked,
            string lockReason,
            bool isComplete = false)
        {
            IsVisible = isVisible;
            IsReadOnly = isReadOnly;
            IsSubmissionPending = isSubmissionPending;
            DecisionId = decisionId;
            DeadlineUnixMilliseconds = deadlineUnixMilliseconds;
            PrivateDraft = privateDraft;
            SeatLocked = new ReadOnlyCollection<bool>((seatLocked ?? Array.Empty<bool>()).ToArray());
            LockReason = lockReason;
            IsComplete = isComplete;
        }

        internal SideboardPanelViewState WithDraft(SideboardDraft draft) => new SideboardPanelViewState(
            IsVisible,
            IsReadOnly,
            IsSubmissionPending,
            DecisionId,
            DeadlineUnixMilliseconds,
            draft,
            SeatLocked,
            LockReason,
            IsComplete);
    }

    public static class SideboardPanelStatePolicy
    {
        public static SideboardPanelViewState OpenStarted(
            SideboardPanelViewState current,
            SideboardStartedMessage started,
            int receivedSeatIndex,
            int localSeatIndex)
        {
            current ??= SideboardPanelViewState.Closed;
            if (started == null || receivedSeatIndex != localSeatIndex) return current;
            if (current.IsReadOnly
                && current.DecisionId == started.decisionId
                && current.LockReason != "recovery_pending_private_state") return current;

            SideboardDraft draft = SideboardDraftPolicy.Create(started);
            return new SideboardPanelViewState(
                true,
                draft.IsReadOnly,
                false,
                started.decisionId,
                started.deadlineUnixMilliseconds,
                draft,
                Array.Empty<bool>(),
                draft.ErrorCode);
        }

        public static SideboardPanelViewState UpdateDraft(
            SideboardPanelViewState current,
            SideboardDraft draft)
        {
            if (current?.IsVisible != true || current.IsReadOnly || draft == null
                || current.DecisionId != draft.DecisionId)
            {
                return current ?? SideboardPanelViewState.Closed;
            }
            return current.WithDraft(draft);
        }

        public static bool TryBeginSubmit(
            SideboardPanelViewState current,
            out SideboardPanelViewState pending,
            out string[] activeTalentIds)
        {
            current ??= SideboardPanelViewState.Closed;
            pending = current;
            activeTalentIds = Array.Empty<string>();
            if (!current.IsEditable || current.IsSubmissionPending || current.PrivateDraft?.CanLock != true)
                return false;

            activeTalentIds = current.PrivateDraft.ActiveTalentIds.ToArray();
            pending = new SideboardPanelViewState(
                true,
                true,
                true,
                current.DecisionId,
                current.DeadlineUnixMilliseconds,
                current.PrivateDraft,
                current.SeatLocked,
                null);
            return true;
        }

        public static SideboardPanelViewState ApplyLocked(
            SideboardPanelViewState current,
            SideboardLockedMessage locked)
        {
            current ??= SideboardPanelViewState.Closed;
            if (locked == null || (current.DecisionId > 0 && current.DecisionId != locked.decisionId))
                return current;
            return new SideboardPanelViewState(
                true,
                true,
                false,
                locked.decisionId,
                current.DeadlineUnixMilliseconds,
                null,
                current.SeatLocked,
                locked.reason);
        }

        public static SideboardPanelViewState ApplyProgress(
            SideboardPanelViewState current,
            SideboardProgressMessage progress)
        {
            current ??= SideboardPanelViewState.Closed;
            if (progress == null || (current.DecisionId > 0 && current.DecisionId != progress.decisionId))
                return current;

            var seatLocked = new bool[4];
            foreach (SideboardSeatLockStateMessage seat in progress.seats ?? Array.Empty<SideboardSeatLockStateMessage>())
            {
                if (seat != null && seat.seatIndex >= 0 && seat.seatIndex < seatLocked.Length)
                    seatLocked[seat.seatIndex] = seat.locked;
            }

            return new SideboardPanelViewState(
                current.IsVisible,
                current.IsReadOnly,
                current.IsSubmissionPending,
                progress.decisionId,
                current.DeadlineUnixMilliseconds,
                current.PrivateDraft,
                seatLocked,
                current.LockReason,
                progress.isComplete);
        }

        public static SideboardPanelViewState Reset(SideboardPanelViewState current) =>
            SideboardPanelViewState.Closed;

        public static SideboardPanelViewState Recover(SnapshotSideboardState state)
        {
            if (state?.isActive != true) return SideboardPanelViewState.Closed;
            return new SideboardPanelViewState(
                true,
                true,
                false,
                state.decisionId,
                state.deadlineUnixMilliseconds,
                null,
                state.seatLocked,
                state.ownLocked ? "recovery" : "recovery_pending_private_state");
        }
    }
}
