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

    public enum ReadyPhase
    {
        MatchStart,
        GameSceneLoaded,
        NextRound
    }

    [Serializable]
    public class HelloMessage
    {
        public int protocolVersion = NetworkProtocol.Version;
        public string username;

        // Retained only for serialized legacy data; protocol v3 authenticates with username.
        public string nickname;
    }

    [Serializable]
    public class HelloAcceptedMessage
    {
        public int protocolVersion = NetworkProtocol.Version;
        public string playerId;
        public string displayName;
    }

    [Serializable]
    public class ReconnectMessage
    {
        public string roomId;
        public string streamId;
        public int lastSeq;
        public bool hasProjection;
    }

    [Serializable]
    public class ResyncMessage
    {
        public string roomId;
        public string streamId;
        public int lastSeq;
    }

    [Serializable]
    public class ReconnectStateMessage
    {
        public int baselineSeq;
        public RoomGameSnapshot snapshot;
        public NetworkMessageEnvelope[] missedMessages;
    }

    [Serializable]
    public class ReconnectRejectedMessage
    {
        public string code;
        public string message;
    }

    [Serializable]
    public class DeckTileCountMessage
    {
        public int suit;
        public int value;
        public int count;
    }

    [Serializable]
    public class PlayerLoadoutMessage
    {
        public int schemaVersion;
        public DeckTileCountMessage[] deckEntries;
        public string[] mainTalentSlotIds;
        public string[] reserveTalentSlotIds;
    }

    [Serializable]
    public class CreateRoomMessage
    {
        public int gameMode;
        public int alienationPreset;
        public PlayerLoadoutMessage loadout;
    }

    [Serializable]
    public class JoinRoomMessage
    {
        public string roomId;
        public PlayerLoadoutMessage loadout;
    }

    [Serializable]
    public class LeaveRoomMessage { }

    [Serializable]
    public class HeartbeatMessage { }

    [Serializable]
    public class HeartbeatAckMessage { }

    [Serializable]
    public class ReadyMessage { public int phase; }

    [Serializable]
    public sealed class SideboardStartedMessage
    {
        public long decisionId;
        public long deadlineUnixMilliseconds;
        public string[] carriedMainTalentIds;
        public string[] carriedReserveTalentIds;
        public string[] currentActiveTalentIds;
        public int alienationLimit;
        public int currentTotalAlienation;
    }

    [Serializable]
    public sealed class SideboardSubmitMessage
    {
        public long decisionId;
        public string[] activeTalentIds;
    }

    [Serializable]
    public sealed class SideboardLockedMessage
    {
        public long decisionId;
        public bool acceptedSelection;
        public string reason;
        public int ownTotalAlienation;
    }

    [Serializable]
    public sealed class SideboardSeatLockStateMessage
    {
        public int seatIndex;
        public bool locked;
    }

    [Serializable]
    public sealed class SideboardProgressMessage
    {
        public long decisionId;
        public bool isComplete;
        public SideboardSeatLockStateMessage[] seats;
    }

    [Serializable]
    public class RoomSeatMessage
    {
        public int seatIndex;
        public bool isOccupied;
        public bool isAi;
        public bool isOnline;
        public bool isTemporarilyAiControlled;
        public string controlState;
        public bool isReady;
        public string displayName;
    }

    [Serializable]
    public class RoomJoinedMessage
    {
        public string roomId;
        public string streamId;
        public int seatIndex;
        public int gameMode;
        public int alienationPreset;
        public int roomState;
        public bool isHost;
        public bool aiFillEnabled;
        public int acceptedSchemaVersion;
        public int ownTotalAlienation;
        public RoomSeatMessage[] seats;
    }

    [Serializable]
    public class PlayerJoinedMessage
    {
        public string roomId;
        public RoomSeatMessage seat;
    }

    [Serializable]
    public class PlayerLeftMessage
    {
        public string roomId;
        public int seatIndex;
        public string reason;
        public RoomSeatMessage seat;
    }

    [Serializable]
    public class RoomSeatUpdatedMessage
    {
        public string roomId;
        public RoomSeatMessage seat;
    }

    [Serializable]
    public class RoomReadyMessage { public string roomId; }
    [Serializable]
    public class RoomErrorMessage
    {
        public string code;
        public string message;
        public int actual;
        public int limit;
    }

    [Serializable]
    public class RoomClosedMessage { public string roomId; public string reason; }

    [Serializable]
    public class TurnWithoutDrawMessage
    {
        public long decisionId;
        public SnapshotDecision decision;
    }

    [Serializable]
    public class WallCountMessage { public int remainingCount; }

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
        public int[] scores;
    }

    [Serializable]
    public class TalentInfoMessage
    {
        public int bonusFan;
        public bool relaxedPureStraight;
    }

    [Serializable]
    public class TalentRuntimeEventMessage
    {
        public long eventId;
        public int ownerSeatIndex;
        public string talentId;
        public string eventType;
        public int visibility;
        public int value;
        public bool isScoreDelta;
    }

    [Serializable]
    public sealed class TalentPrivateStateMessage
    {
        public int ownerSeatIndex;
        public SnapshotOwnTalent[] talents;
        public SnapshotTalentActionOption[] availableTalentActions;
    }

    [Serializable]
    public sealed class TalentActionMessage
    {
        public long decisionId;
        public string talentId;
        public int targetSeatIndex = -1;
        public string targetTalentId;
    }

    [Serializable]
    public sealed class TalentActionResolvedMessage
    {
        public long decisionId;
        public int ownerSeatIndex;
        public string talentId;
        public bool accepted;
        public string errorCode;
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
        public long decisionId;
        public SnapshotDecision decision;
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
        public long decisionId;
        public SnapshotDecision decision;
        public int playerId;
        public SimpleTileData tile;
    }

    [Serializable]
    public class AddedKongDeclaredMessage
    {
        public long decisionId;
        public SnapshotDecision decision;
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
    public sealed class WinningHandSnapshot
    {
        public SimpleTileData[] concealedTiles;
        public SimpleTileData winningTile;
        public SnapshotMeld[] melds;
    }

    public enum WinKind
    {
        Unknown = 0,
        Discard = 1,
        SelfDraw = 2,
        RobKong = 3
    }

    [Serializable]
    public class PlayerWinMessage
    {
        public int winnerId;
        public int totalFan;
        public string[] fanDetails;
        public bool isSelfDraw;
        public WinKind winKind;
        public int loserId = -1;
        public WinningHandSnapshot winningHand;
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
        public long decisionId;
        public int actionType; // ClientActionType
        public SimpleTileData targetTile;
        public int[] chiCombinations;
        public int totalFan;
        public string[] fanDetails;
    }
}
