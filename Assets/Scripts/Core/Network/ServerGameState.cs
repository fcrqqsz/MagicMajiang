using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MahjongGame.Core.Network
{
    public class PlayerSnapshot
    {
        public List<TileData> Hand = new List<TileData>();
        public List<Meld> Melds = new List<Meld>();
        public List<TileData> River = new List<TileData>();
    }

    public class ServerGameState
    {
        private PlayerSnapshot[] _players;
        public int PlayerCount => _players.Length;

        public ServerGameState(int playerCount)
        {
            _players = new PlayerSnapshot[playerCount];
            for (int i = 0; i < playerCount; i++)
                _players[i] = new PlayerSnapshot();
        }

        public void InitHand(int playerId, List<TileData> tiles)
        {
            _players[playerId].Hand = new List<TileData>(tiles);
            _players[playerId].Melds.Clear();
            _players[playerId].River.Clear();
        }

        public bool TryReplaceInitialHands(
            IReadOnlyDictionary<int, List<TileData>> replacements,
            out string error)
        {
            error = null;
            if (replacements == null || replacements.Count != PlayerCount)
            {
                error = "Initial-hand replacement must contain every seat.";
                return false;
            }

            var validated = new List<TileData>[PlayerCount];
            for (int seatIndex = 0; seatIndex < PlayerCount; seatIndex++)
            {
                if (!replacements.TryGetValue(seatIndex, out List<TileData> replacement)
                    || replacement == null
                    || replacement.Count != _players[seatIndex].Hand.Count
                    || replacement.Any(tile => tile == null || string.IsNullOrWhiteSpace(tile.ID)))
                {
                    error = $"Initial-hand replacement for seat {seatIndex} is incomplete.";
                    return false;
                }

                Dictionary<string, TileData> originalById;
                try
                {
                    originalById = _players[seatIndex].Hand.ToDictionary(
                        tile => tile.ID,
                        tile => tile,
                        System.StringComparer.Ordinal);
                }
                catch (System.ArgumentException)
                {
                    error = $"Initial hand for seat {seatIndex} contains duplicate physical ids.";
                    return false;
                }

                var replacementIds = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (TileData tile in replacement)
                {
                    if (!replacementIds.Add(tile.ID)
                        || !originalById.TryGetValue(tile.ID, out TileData original)
                        || original.OriginalOwnerID != tile.OriginalOwnerID
                        || !IsValidTileShape(tile.TileSuit, tile.Value))
                    {
                        error = $"Initial-hand replacement for seat {seatIndex} changed physical identity or shape.";
                        return false;
                    }
                }
                validated[seatIndex] = replacement.Select(CloneTile).ToList();
            }

            for (int seatIndex = 0; seatIndex < PlayerCount; seatIndex++)
                _players[seatIndex].Hand = validated[seatIndex];
            return true;
        }

        public void AddTile(int playerId, TileData tile)
        {
            _players[playerId].Hand.Add(tile);
        }

        public void RemoveTile(int playerId, TileData tile)
        {
            var hand = _players[playerId].Hand;
            var match = hand.FirstOrDefault(
                t => t.TileSuit == tile.TileSuit && t.Value == tile.Value);
            if (match != null)
                hand.Remove(match);
            else
                Debug.LogWarning($"[ServerGameState] RemoveTile: 玩家{playerId}手牌中未找到 {tile.TileSuit} {tile.Value}");
        }

        /// <summary>Records a discard in the authoritative public river after it leaves the hand.</summary>
        public void RecordDiscard(int playerId, TileData tile)
        {
            if (tile == null) return;
            _players[playerId].River.Add(CloneTile(tile));
        }

        /// <summary>Consumes only the latest matching discard from the specified player's public river.</summary>
        public bool TryClaimDiscard(int playerId, TileData tile)
        {
            if (tile == null) return false;
            var river = _players[playerId].River;
            if (river.Count == 0) return false;

            var lastDiscard = river[river.Count - 1];
            if (!TilesMatch(lastDiscard, tile)) return false;
            river.RemoveAt(river.Count - 1);
            return true;
        }

        public void ApplyMeld(int playerId, ClientActionType type, TileData targetTile, int[] chiCombinations)
        {
            var snapshot = _players[playerId];

            switch (type)
            {
                case ClientActionType.Chi:
                    if (chiCombinations != null && chiCombinations.Length == 2)
                    {
                        foreach (var val in chiCombinations)
                        {
                            var match = snapshot.Hand.FirstOrDefault(
                                t => t.TileSuit == targetTile.TileSuit && t.Value == val);
                            if (match != null) snapshot.Hand.Remove(match);
                        }
                        var meldTiles = new List<TileData>
                        {
                            CloneTile(targetTile),
                            new TileData(targetTile.TileSuit, chiCombinations[0], targetTile.OriginalOwnerID),
                            new TileData(targetTile.TileSuit, chiCombinations[1], targetTile.OriginalOwnerID)
                        };
                        snapshot.Melds.Add(new Meld(MeldType.Chi, meldTiles, targetTile.OriginalOwnerID));
                    }
                    break;

                case ClientActionType.Pon:
                    RemoveMatching(snapshot.Hand, targetTile, 2);
                    snapshot.Melds.Add(new Meld(MeldType.Pon,
                        new List<TileData> { CloneTile(targetTile), CloneTile(targetTile), CloneTile(targetTile) },
                        targetTile.OriginalOwnerID));
                    break;

                case ClientActionType.MingGan:
                    RemoveMatching(snapshot.Hand, targetTile, 3);
                    snapshot.Melds.Add(new Meld(MeldType.Kan_Exposed,
                        new List<TileData> { CloneTile(targetTile), CloneTile(targetTile), CloneTile(targetTile), CloneTile(targetTile) },
                        targetTile.OriginalOwnerID));
                    break;

                case ClientActionType.AnGan:
                    TryCommitConcealedKong(playerId, targetTile, out _);
                    break;

                case ClientActionType.JiaGang:
                    TryResolveAddedKong(playerId, targetTile, wasRobbed: false, out _);
                    break;
            }
        }

        public bool TryCommitConcealedKong(
            int playerId,
            TileData targetTile,
            out List<TileData> publicTiles)
        {
            publicTiles = new List<TileData>();
            if (targetTile == null) return false;

            PlayerSnapshot snapshot = _players[playerId];
            List<TileData> authoritativeTiles = snapshot.Hand
                .Where(tile => tile.TileSuit == targetTile.TileSuit && tile.Value == targetTile.Value)
                .Take(4)
                .ToList();
            if (authoritativeTiles.Count != 4) return false;

            foreach (TileData tile in authoritativeTiles)
                snapshot.Hand.Remove(tile);
            publicTiles = authoritativeTiles.Select(CloneTile).ToList();
            snapshot.Melds.Add(new Meld(
                MeldType.Kan_Concealed,
                publicTiles.Select(CloneTile).ToList(),
                authoritativeTiles[0].OriginalOwnerID,
                true));
            return true;
        }

        public bool TryGetAddedKongDeclarationTile(
            int playerId,
            TileData targetTile,
            out TileData authoritativeTile)
        {
            authoritativeTile = null;
            if (targetTile == null) return false;

            PlayerSnapshot snapshot = _players[playerId];
            bool hasPon = snapshot.Melds.Any(
                meld => meld.Type == MeldType.Pon
                        && meld.FirstTile.TileSuit == targetTile.TileSuit
                        && meld.FirstTile.Value == targetTile.Value);
            if (!hasPon) return false;

            TileData handTile = snapshot.Hand.FirstOrDefault(
                tile => tile.TileSuit == targetTile.TileSuit && tile.Value == targetTile.Value);
            if (handTile == null) return false;
            authoritativeTile = CloneTile(handTile);
            return true;
        }

        public bool TryCommitAddedKong(
            int playerId,
            TileData authoritativeTile,
            out TileData publicTile)
        {
            publicTile = null;
            if (authoritativeTile == null || string.IsNullOrEmpty(authoritativeTile.ID)) return false;

            PlayerSnapshot snapshot = _players[playerId];
            TileData handTile = snapshot.Hand.FirstOrDefault(tile => tile.ID == authoritativeTile.ID);
            Meld ponMeld = snapshot.Melds.FirstOrDefault(
                meld => meld.Type == MeldType.Pon
                        && meld.FirstTile.TileSuit == authoritativeTile.TileSuit
                        && meld.FirstTile.Value == authoritativeTile.Value);
            if (handTile == null || ponMeld == null) return false;

            snapshot.Hand.Remove(handTile);
            ponMeld.Type = MeldType.Kan_Added;
            ponMeld.Tiles.Add(CloneTile(handTile));
            publicTile = CloneTile(handTile);
            return true;
        }

        /// <summary>
        /// Resolves the pending added-kong against authoritative hand/meld state. The returned tile is
        /// available for public-talent notification only after the meld transition has committed.
        /// </summary>
        public bool TryResolveAddedKong(
            int playerId,
            TileData targetTile,
            bool wasRobbed,
            out TileData publicTile)
        {
            publicTile = null;
            if (wasRobbed || targetTile == null) return false;
            return TryGetAddedKongDeclarationTile(playerId, targetTile, out TileData authoritativeTile)
                   && TryCommitAddedKong(playerId, authoritativeTile, out publicTile);
        }

        public TileData GetAutoDiscardTile(int playerId, TileData lastDrawn)
        {
            var hand = _players[playerId].Hand;
            if (hand.Count == 0) return null;

            // 优先打刚摸的牌
            if (lastDrawn != null)
            {
                var match = hand.FirstOrDefault(
                    t => t.TileSuit == lastDrawn.TileSuit && t.Value == lastDrawn.Value);
                if (match != null) return match;
            }

            // 否则打手牌末尾
            return hand[hand.Count - 1];
        }

        public List<TileData> GetHand(int playerId) => _players[playerId].Hand.Select(CloneTile).ToList();
        public List<Meld> GetMelds(int playerId) => _players[playerId].Melds.Select(CloneMeld).ToList();
        public List<TileData> GetRiver(int playerId) => _players[playerId].River.Select(CloneTile).ToList();

        private void RemoveMatching(List<TileData> hand, TileData target, int count)
        {
            int removed = 0;
            for (int i = hand.Count - 1; i >= 0 && removed < count; i--)
            {
                if (hand[i].TileSuit == target.TileSuit && hand[i].Value == target.Value)
                {
                    hand.RemoveAt(i);
                    removed++;
                }
            }
        }

        private static bool TilesMatch(TileData left, TileData right)
        {
            if (left == null || right == null) return false;
            if (!string.IsNullOrEmpty(left.ID) && !string.IsNullOrEmpty(right.ID))
                return left.ID == right.ID;
            return left.TileSuit == right.TileSuit && left.Value == right.Value;
        }

        private static bool IsValidTileShape(Suit suit, int value) => suit switch
        {
            Suit.Man => value >= 1 && value <= 9,
            Suit.Pin => value >= 1 && value <= 9,
            Suit.Sou => value >= 1 && value <= 9,
            Suit.Wind => value >= 1 && value <= 4,
            Suit.Dragon => value >= 1 && value <= 3,
            _ => false
        };

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

        private static Meld CloneMeld(Meld meld)
        {
            if (meld == null) return null;
            return new Meld(meld.Type, meld.Tiles.Select(CloneTile).ToList(), meld.SourcePlayerID, meld.IsConcealed);
        }
    }
}
