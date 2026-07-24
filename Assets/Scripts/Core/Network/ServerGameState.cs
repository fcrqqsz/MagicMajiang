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
                    RemoveMatching(snapshot.Hand, targetTile, 4);
                    snapshot.Melds.Add(new Meld(MeldType.Kan_Concealed,
                        new List<TileData> { CloneTile(targetTile), CloneTile(targetTile), CloneTile(targetTile), CloneTile(targetTile) },
                        targetTile.OriginalOwnerID, true));
                    break;

                case ClientActionType.JiaGang:
                    RemoveMatching(snapshot.Hand, targetTile, 1);
                    var ponMeld = snapshot.Melds.FirstOrDefault(
                        m => m.Type == MeldType.Pon
                          && m.FirstTile.TileSuit == targetTile.TileSuit
                          && m.FirstTile.Value == targetTile.Value);
                    if (ponMeld != null)
                    {
                        ponMeld.Type = MeldType.Kan_Added;
                        ponMeld.Tiles.Add(CloneTile(targetTile));
                    }
                    break;
            }
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
