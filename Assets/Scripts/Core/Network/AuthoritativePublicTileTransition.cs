using System;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    /// <summary>Coordinates public tile transitions with talent visibility.</summary>
    public sealed class AuthoritativePublicTileTransition
    {
        private readonly ServerGameState _gameState;
        private readonly TalentMatchRuntime _talentRuntime;
        private readonly GameSession _session;

        public AuthoritativePublicTileTransition(
            ServerGameState gameState,
            TalentMatchRuntime talentRuntime,
            GameSession session)
        {
            _gameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
            _talentRuntime = talentRuntime ?? throw new ArgumentNullException(nameof(talentRuntime));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public bool TryCommitConcealedKong(
            int playerId,
            TileData clientTarget,
            int[] chiCombinations,
            Action<ClientAction> publish,
            out ClientAction committedAction)
        {
            committedAction = null;
            if (!_gameState.TryCommitConcealedKong(
                    playerId,
                    clientTarget,
                    out System.Collections.Generic.List<TileData> publicTiles))
            {
                return false;
            }

            committedAction = new ClientAction(
                playerId,
                ClientActionType.AnGan,
                publicTiles[0],
                chiCombinations);
            publish?.Invoke(committedAction);
            foreach (TileData tile in publicTiles)
                NotifyPublic(playerId, tile);
            return true;
        }

        public void PublishWinningResult(int winnerSeatIndex, Action publish)
        {
            publish?.Invoke();
            foreach (TileData tile in _gameState.GetHand(winnerSeatIndex))
                NotifyPublic(winnerSeatIndex, tile);
        }

        public bool TryPrepareAddedKong(int playerId, TileData clientTarget, out TileData authoritativeTile)
        {
            return _gameState.TryGetAddedKongDeclarationTile(
                playerId,
                clientTarget,
                out authoritativeTile);
        }

        public void PublishAddedKongDeclaration(
            int playerId,
            TileData authoritativeTile,
            Action<TileData> publish)
        {
            publish?.Invoke(authoritativeTile);
            NotifyPublic(playerId, authoritativeTile);
        }

        public bool TryCommitAddedKong(
            int playerId,
            TileData authoritativeTile,
            int[] chiCombinations,
            Action<ClientAction> publish,
            out ClientAction committedAction)
        {
            committedAction = null;
            if (!_gameState.TryCommitAddedKong(
                    playerId,
                    authoritativeTile,
                    out TileData publicTile))
            {
                return false;
            }

            committedAction = new ClientAction(
                playerId,
                ClientActionType.JiaGang,
                publicTile,
                chiCombinations);
            publish?.Invoke(committedAction);
            return true;
        }

        private void NotifyPublic(int ownerSeatIndex, TileData tile)
        {
            _talentRuntime.NotifyTileBecamePublic(
                new TalentPublicTileContext(_session, ownerSeatIndex),
                tile);
        }
    }
}
