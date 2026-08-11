using System;
using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core.Network
{
    public static class SideboardPhasePolicy
    {
        public static bool ShouldOpen(GameMode gameMode, int completedRounds)
        {
            return (gameMode == GameMode.HalfGame || gameMode == GameMode.FullGame)
                   && completedRounds == 4;
        }
    }

    public static class SideboardErrorCodes
    {
        public const string AlreadyLocked = "SideboardAlreadyLocked";
        public const string InvalidSelection = "SideboardInvalidSelection";
        public const string LockedTalentMissing = "SideboardLockedTalentMissing";
        public const string AlienationLimitExceeded = "SideboardAlienationLimitExceeded";
        public const string StaleDecision = "SideboardStaleDecision";
        public const string WrongPhase = "SideboardWrongPhase";
    }

    /// <summary>One-shot, four-seat decision data. Runtime mutation remains a Room responsibility.</summary>
    public sealed class SideboardDecisionTracker
    {
        private const int SeatCount = 4;
        private readonly SeatDecision[] _seats = new SeatDecision[SeatCount];

        public long DecisionId { get; }
        public long DeadlineUnixMilliseconds { get; }
        public bool AllLocked => _seats.All(seat => seat.IsLocked);

        public SideboardDecisionTracker(
            long decisionId,
            long deadlineUnixMilliseconds,
            IReadOnlyList<IReadOnlyCollection<string>> originalActiveTalentIds)
        {
            if (originalActiveTalentIds == null || originalActiveTalentIds.Count != SeatCount)
                throw new ArgumentException("Sideboard decisions require exactly four original active sets.", nameof(originalActiveTalentIds));

            DecisionId = decisionId;
            DeadlineUnixMilliseconds = deadlineUnixMilliseconds;
            for (int seatIndex = 0; seatIndex < SeatCount; seatIndex++)
            {
                _seats[seatIndex] = new SeatDecision(
                    (originalActiveTalentIds[seatIndex] ?? Array.Empty<string>()).ToArray());
            }
        }

        public bool IsLocked(int seatIndex) => GetSeat(seatIndex).IsLocked;

        public bool TrySubmit(int seatIndex, string[] activeTalentIds, out string errorCode)
        {
            SeatDecision seat = GetSeat(seatIndex);
            if (seat.IsLocked)
            {
                errorCode = SideboardErrorCodes.AlreadyLocked;
                return false;
            }
            if (activeTalentIds == null)
            {
                errorCode = SideboardErrorCodes.InvalidSelection;
                return false;
            }

            seat.SelectedActiveTalentIds = activeTalentIds.ToArray();
            seat.IsLocked = true;
            seat.AcceptedSelection = true;
            seat.Reason = "accepted";
            errorCode = null;
            return true;
        }

        public void LockOriginal(int seatIndex, string reason)
        {
            SeatDecision seat = GetSeat(seatIndex);
            if (seat.IsLocked) return;

            seat.SelectedActiveTalentIds = seat.OriginalActiveTalentIds.ToArray();
            seat.IsLocked = true;
            seat.AcceptedSelection = false;
            seat.Reason = reason;
        }

        public IReadOnlyList<string> GetOriginalActiveTalentIds(int seatIndex) =>
            Array.AsReadOnly(GetSeat(seatIndex).OriginalActiveTalentIds);

        public IReadOnlyList<string> GetSelectedActiveTalentIds(int seatIndex)
        {
            SeatDecision seat = GetSeat(seatIndex);
            return Array.AsReadOnly(seat.SelectedActiveTalentIds ?? Array.Empty<string>());
        }

        public bool WasSelectionAccepted(int seatIndex) => GetSeat(seatIndex).AcceptedSelection;

        public string GetLockReason(int seatIndex) => GetSeat(seatIndex).Reason;

        private SeatDecision GetSeat(int seatIndex)
        {
            if (seatIndex < 0 || seatIndex >= SeatCount)
                throw new ArgumentOutOfRangeException(nameof(seatIndex));
            return _seats[seatIndex];
        }

        private sealed class SeatDecision
        {
            public string[] OriginalActiveTalentIds { get; }
            public string[] SelectedActiveTalentIds { get; set; }
            public bool IsLocked { get; set; }
            public bool AcceptedSelection { get; set; }
            public string Reason { get; set; }

            public SeatDecision(string[] originalActiveTalentIds)
            {
                OriginalActiveTalentIds = originalActiveTalentIds;
            }
        }
    }
}
