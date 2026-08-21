using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents
{
    public sealed class TalentActionCommittedFacts
    {
        private readonly ReadOnlyCollection<int> _chiCombinations;

        public long DecisionId { get; }
        public int ActorSeatIndex { get; }
        public int? SourceSeatIndex { get; }
        public ClientActionType ActionType { get; }
        public TalentTileFacts TargetTile { get; }
        public IReadOnlyList<int> ChiCombinations => _chiCombinations;
        public bool WasAutomatic { get; }
        public TalentWinFacts WinFacts { get; }

        private TalentActionCommittedFacts(
            long decisionId,
            int actorSeatIndex,
            int? sourceSeatIndex,
            ClientActionType actionType,
            TileData targetTile,
            IEnumerable<int> chiCombinations,
            bool wasAutomatic,
            TalentWinFacts winFacts)
        {
            if (decisionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(decisionId));
            ValidateSeat(actorSeatIndex, nameof(actorSeatIndex));
            if (sourceSeatIndex.HasValue)
                ValidateSeat(sourceSeatIndex.Value, nameof(sourceSeatIndex));
            if (actionType == ClientActionType.Skip)
                throw new ArgumentException("Skip is a submission outcome, not a committed action.", nameof(actionType));
            if (actionType == ClientActionType.Hu && winFacts == null)
                throw new ArgumentNullException(nameof(winFacts), "Committed Hu requires immutable win facts.");
            if (winFacts != null && winFacts.WinnerSeatIndex != actorSeatIndex)
                throw new ArgumentException("Win facts must belong to the committed action actor.", nameof(winFacts));

            DecisionId = decisionId;
            ActorSeatIndex = actorSeatIndex;
            SourceSeatIndex = sourceSeatIndex;
            ActionType = actionType;
            TargetTile = targetTile == null ? null : new TalentTileFacts(targetTile);
            _chiCombinations = Array.AsReadOnly((chiCombinations ?? Enumerable.Empty<int>()).ToArray());
            WasAutomatic = wasAutomatic;
            WinFacts = winFacts;
        }

        internal static TalentActionCommittedFacts Create(
            long decisionId,
            int actorSeatIndex,
            int? sourceSeatIndex,
            ClientActionType actionType,
            TileData targetTile,
            IEnumerable<int> chiCombinations,
            bool wasAutomatic,
            TalentWinFacts winFacts) => new TalentActionCommittedFacts(
                decisionId,
                actorSeatIndex,
                sourceSeatIndex,
                actionType,
                targetTile,
                chiCombinations,
                wasAutomatic,
                winFacts);

        private static void ValidateSeat(int seatIndex, string parameterName)
        {
            if (seatIndex < 0 || seatIndex > 3)
                throw new ArgumentOutOfRangeException(parameterName, seatIndex, "Seat index must be 0..3.");
        }
    }

    public sealed class TalentRoundActionLedgerSnapshot
    {
        private readonly ReadOnlyCollection<TalentActionCommittedFacts> _actions;

        public static TalentRoundActionLedgerSnapshot Empty { get; } =
            new TalentRoundActionLedgerSnapshot(Array.Empty<TalentActionCommittedFacts>());

        public IReadOnlyList<TalentActionCommittedFacts> Actions => _actions;

        internal TalentRoundActionLedgerSnapshot(
            IEnumerable<TalentActionCommittedFacts> actions)
        {
            _actions = Array.AsReadOnly((actions ?? Enumerable.Empty<TalentActionCommittedFacts>())
                .Where(action => action != null)
                .ToArray());
        }

        public int GetCount(int seatIndex, ClientActionType actionType)
        {
            if (seatIndex < 0 || seatIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(seatIndex));
            return _actions.Count(action => action.ActorSeatIndex == seatIndex
                                            && action.ActionType == actionType);
        }

        public IReadOnlyList<TalentActionCommittedFacts> GetSeatActions(int seatIndex)
        {
            if (seatIndex < 0 || seatIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(seatIndex));
            return _actions.Where(action => action.ActorSeatIndex == seatIndex).ToArray();
        }
    }
}
