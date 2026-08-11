using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;

namespace MahjongGame.Core.Network
{
    public enum NetworkDecisionPhase
    {
        MainTurn,
        Response,
        RobKong
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

        /// <summary>Opens the Hu-or-skip window after a player declares an added kong.</summary>
        public NetworkDecisionContext OpenRobKong(int declaringSeatIndex, TileData targetTile, IEnumerable<int> eligibleSeats, long deadlineUnixMilliseconds)
        {
            EnsureNoActiveDecision();
            _active = new NetworkDecisionContext(++_nextDecisionId, NetworkDecisionPhase.RobKong,
                -1, declaringSeatIndex, targetTile, eligibleSeats, Array.Empty<int>(), -1, deadlineUnixMilliseconds);
            return Active;
        }

        public bool TrySubmitNetworkAction(long decisionId, int seatIndex, ClientActionType actionType, out string errorCode)
        {
            errorCode = ValidateActiveDecision(decisionId);
            if (errorCode != null) return false;

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

                bool isAllowedResponse = _active.Phase == NetworkDecisionPhase.RobKong
                    ? TurnActionPolicy.IsRobKongResponseAction(actionType)
                    : TurnActionPolicy.IsResponseAction(actionType);
                if (!isAllowedResponse)
                {
                    errorCode = NetworkErrorCodes.WrongPhase;
                    return false;
                }

                if (_active.Phase == NetworkDecisionPhase.Response
                    && actionType == ClientActionType.Chi
                    && seatIndex != (_active.DiscardingSeatIndex + 1) % 4)
                {
                    errorCode = NetworkErrorCodes.NotEligible;
                    return false;
                }
            }

            _active = _active.WithSubmittedSeat(seatIndex);
            return true;
        }

        /// <summary>
        /// Checks whether a talent action may execute alongside the active base decision.
        /// This method deliberately does not mark the seat as having submitted its base action.
        /// </summary>
        public bool TryValidateSupplementalAction(
            long decisionId,
            int seatIndex,
            NetworkDecisionPhase requiredPhase,
            out string errorCode)
        {
            errorCode = ValidateActiveDecision(decisionId);
            if (errorCode != null) return false;

            if (_active.Phase != requiredPhase)
            {
                errorCode = NetworkErrorCodes.WrongPhase;
                return false;
            }

            if (requiredPhase == NetworkDecisionPhase.MainTurn
                && seatIndex != _active.ControllerSeatIndex)
            {
                errorCode = NetworkErrorCodes.WrongController;
                return false;
            }

            if (requiredPhase != NetworkDecisionPhase.MainTurn
                && !_active.EligibleSeats.Contains(seatIndex))
            {
                errorCode = NetworkErrorCodes.NotEligible;
                return false;
            }

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

        private string ValidateActiveDecision(long decisionId)
        {
            if (_active == null)
                return NetworkErrorCodes.NoActiveDecision;

            if (decisionId != _active.DecisionId)
                return NetworkErrorCodes.StaleDecision;

            if (_active.DeadlineUnixMilliseconds > 0
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= _active.DeadlineUnixMilliseconds)
            {
                return NetworkErrorCodes.DecisionExpired;
            }

            return null;
        }
    }
}
