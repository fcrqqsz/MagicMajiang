using System;
using UnityEngine;
using MahjongGame.Core;

namespace MahjongGame.Core.Network.Messages
{
    [Serializable]
    public class NetworkMessageEnvelope
    {
        public string type;
        public int seq;
        public string data; // JSON string of the actual payload
    }

    [Serializable]
    public class DrawGameMessage
    {
        public int[] scores;
        public int completedRounds;
    }

    [Serializable]
    public class RoundStartMessage
    {
        public int roundNumber;
        public int prevalentWind; // WindDirection int value
        public int seatWind;     // WindDirection int value
        public int dealerIndex;
    }

    [Serializable]
    public class TalentInfoMessage
    {
        public int bonusFan;
        public bool relaxedPureStraight;
    }

    [Serializable]
    public class SimpleTileData
    {
        public int suit;
        public int value;
        public int ownerId;
        public bool isValid;

        public SimpleTileData() 
        {
            isValid = false;
        }

        public SimpleTileData(TileData tile)
        {
            if (tile == null)
            {
                isValid = false;
                return;
            }
            suit = (int)tile.TileSuit;
            value = tile.Value;
            ownerId = tile.OriginalOwnerID;
            isValid = true;
        }

        public TileData ToTileData()
        {
            if (!isValid) return null;
            return new TileData((Suit)suit, value, ownerId);
        }
    }

    [Serializable]
    public class GameStartMessage
    {
        public SimpleTileData[] tiles;
    }

    [Serializable]
    public class PeekWallMessage
    {
        public SimpleTileData[] tiles;
    }

    [Serializable]
    public class TileDrawnMessage
    {
        public SimpleTileData tile;
    }

    [Serializable]
    public class PlayerDrewMessage
    {
        public int playerId;
    }

    [Serializable]
    public class DiscardedMessage
    {
        public int playerId;
        public SimpleTileData tile;
    }

    [Serializable]
    public class ActionResolvedMessage
    {
        public int playerId;
        public int actionType; // ClientActionType
        public SimpleTileData tile;
        public int[] chiCombinations;
    }

    [Serializable]
    public class TimeoutMessage
    {
        public SimpleTileData tile;
    }

    [Serializable]
    public class PlayerWinMessage
    {
        public int winnerId;
        public int totalFan;
        public string[] fanDetails;
        public bool isSelfDraw;
        public int[] scores;
        public int completedRounds;
    }

    [Serializable]
    public class SessionEndMessage
    {
        public int[] scores;
    }

    // Client -> Server Action
    [Serializable]
    public class ClientActionMessage
    {
        public int actionType; // ClientActionType
        public SimpleTileData targetTile;
        public int[] chiCombinations;
        public int totalFan;
        public string[] fanDetails;
    }
}
