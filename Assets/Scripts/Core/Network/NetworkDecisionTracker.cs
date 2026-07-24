using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;

namespace MahjongGame.Core.Network
{
    public enum NetworkDecisionPhase
    {
        MainTurn,
        Response
    }

    /// <summary>Immutable view of one active, room-scoped action decision.</summary>
    public sealed class NetworkDecisionContext
    {
        public long DecisionId { get; }
        public NetworkDecisionPhase Phase { get; }
        public int ActingSeatIndex { get; }
        public int DiscardingSeatIndex { get; }
        public TileData TargetTile { get; }
        public int[] EligibleSeats { get; }
        public int[] SubmittedSeats { get; }
        public int ControllerSeatIndex { get; }
        public long DeadlineUnixMilliseconds { get; }

        internal NetworkDecisionContext(
            long decisionId,
            NetworkDecisionPhase phase,
            int actingSeatIndex,
            int discardingSeatIndex,
            TileData targetTile,
            IEnumerable<int> eligibleSeats,
            IEnumerable<int> submittedSeats,
            int controllerSeatIndex,
            long deadlineUnixMilliseconds)
        {
            DecisionId = decisionId;
            Phase = phase;
            ActingSeatIndex = actingSeatIndex;
            DiscardingSeatIndex = discardingSeatIndex;
            TargetTile = CloneTile(targetTile);
            EligibleSeats = (eligibleSeats ?? Array.Empty<int>()).Distinct().OrderBy(seat => seat).ToArray();
            SubmittedSeats = (submittedSeats ?? Array.Empty<int>()).Distinct().OrderBy(seat => seat).ToArray();
            ControllerSeatIndex = controllerSeatIndex;
            DeadlineUnixMilliseconds = deadlineUnixMilliseconds;
        }

        internal NetworkDecisionContext WithSubmittedSeat(int seatIndex)
        {
            return new NetworkDecisionContext(DecisionId, Phase, ActingSeatIndex, DiscardingSeatIndex, TargetTile,
                EligibleSeats, SubmittedSeats.Append(seatIndex), ControllerSeatIndex, DeadlineUnixMilliseconds);
        }

        internal NetworkDecisionContext Clone()
        {
            return new NetworkDecisionContext(DecisionId, Phase, ActingSeatIndex, DiscardingSeatIndex, TargetTile,
                EligibleSeats, SubmittedSeats, ControllerSeatIndex, DeadlineUnixMilliseconds);
        }

        private static TileData CloneTile(TileData tile)
        {
            if (tile == null) return null;
            return new TileData(tile.TileSuit, tile.Value, tile.OriginalOwnerID)
            {
                ID = tile.ID,
                IsModified = tile.IsModified,
                SpecialEffectID = tile.SpecialEffectID
            };
        }
    }

    /// <summary>Owns the monotonic, room-session decision lineage and network action admission checks.</summary>
    public sealed class NetworkDecisionTracker
    {
        private long _nextDecisionId;
        private NetworkDecisionContext _active;

        public NetworkDecisionContext Active => _active?.Clone();

        public NetworkDecisionContext OpenMainTurn(int controllerSeatIndex, long deadlineUnixMilliseconds)
        {
            EnsureNoActiveDecision();
            _active = new NetworkDecisionContext(++_nextDecisionId, NetworkDecisionPhase.MainTurn,
                controllerSeatIndex, -1, null, new[] { controllerSeatIndex }, Array.Empty<int>(),
                controllerSeatIndex, deadlineUnixMilliseconds);
            return Active;
        }

        public NetworkDecisionContext OpenResponse(int discardingSeatIndex, TileData targetTile, IEnumerable<int> eligibleSeats, long deadlineUnixMilliseconds)
        {
            EnsureNoActiveDecision();
            _active = new NetworkDecisionContext(++_nextDecisionId, NetworkDecisionPhase.Response,
                -1, discardingSeatIndex, targetTile, eligibleSeats, Array.Empty<int>(), -1, deadlineUnixMilliseconds);
            return Active;
        }

        public bool TrySubmitNetworkAction(long decisionId, int seatIndex, ClientActionType actionType, out string errorCode)
        {
            errorCode = null;
            if (_active == null)
            {
                errorCode = NetworkErrorCodes.NoActiveDecision;
                return false;
            }

            if (decisionId != _active.DecisionId)
            {
                errorCode = NetworkErrorCodes.StaleDecision;
                return false;
            }

            if (_active.DeadlineUnixMilliseconds > 0
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= _active.DeadlineUnixMilliseconds)
            {
                errorCode = NetworkErrorCodes.DecisionExpired;
                return false;
            }

            if (_active.SubmittedSeats.Contains(seatIndex))
            {
                errorCode = NetworkErrorCodes.DuplicateAction;
                return false;
            }

            if (_active.Phase == NetworkDecisionPhase.MainTurn)
            {
                if (seatIndex != _active.ControllerSeatIndex)
                {
                    errorCode = NetworkErrorCodes.WrongController;
                    return false;
                }

                if (!TurnActionPolicy.IsMainTurnAction(actionType))
                {
                    errorCode = NetworkErrorCodes.WrongPhase;
                    return false;
                }
            }
            else
            {
                if (!_active.EligibleSeats.Contains(seatIndex))
                {
                    errorCode = NetworkErrorCodes.NotEligible;
                    return false;
                }

                if (!TurnActionPolicy.IsResponseAction(actionType))
                {
                    errorCode = NetworkErrorCodes.WrongPhase;
                    return false;
                }

                if (actionType == ClientActionType.Chi
                    && seatIndex != (_active.DiscardingSeatIndex + 1) % 4)
                {
                    errorCode = NetworkErrorCodes.NotEligible;
                    return false;
                }
            }

            _active = _active.WithSubmittedSeat(seatIndex);
            return true;
        }

        public bool Close(long decisionId)
        {
            if (_active == null || _active.DecisionId != decisionId) return false;
            _active = null;
            return true;
        }

        private void EnsureNoActiveDecision()
        {
            if (_active != null)
                throw new InvalidOperationException("Close the active network decision before opening another one.");
        }
    }
}
