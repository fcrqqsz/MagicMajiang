using System;
using System.Linq;
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

        // Retained only for serialized legacy data; the current protocol authenticates with username.
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
        public int alienationPreset;
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
    public class QueryRoomListMessage { }

    [Serializable]
    public class RoomSummaryMessage
    {
        public string roomId;
        public string hostDisplayName;
        public int gameMode;
        public int alienationPreset;
        public int currentPlayers;
        public int maxPlayers;
        public int state;
        public bool isFull;
    }

    [Serializable]
    public class RoomListMessage
    {
        public RoomSummaryMessage[] rooms;
    }

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
        public int loadoutAlienationPreset;
        public int roomAlienationPreset;
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
        public int minimumFan = 8;
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
        public string selectedChoiceId;
    }

    [Serializable]
    public sealed class TalentActionResolvedMessage
    {
        public long decisionId;
        public int ownerSeatIndex;
        public string talentId;
        public bool accepted;
        public bool effectApplied;
        public string errorCode;
    }

    [Serializable]
    public class SimpleTileData
    {
        // Opaque physical identity, present only in owner-private projections
        // and that owner's action requests.
        public string instanceId;
        public int suit;
        public int value;
        public int ownerId;
        public bool isModified;
        public bool isValid;

        public SimpleTileData() 
        {
            isValid = false;
        }

        public SimpleTileData(TileData tile) : this(tile, false)
        {
        }

        public SimpleTileData(TileData tile, bool includeOwnerPrivateState)
        {
            if (tile == null)
            {
                isValid = false;
                return;
            }
            suit = (int)tile.TileSuit;
            value = tile.Value;
            ownerId = tile.OriginalOwnerID;
            instanceId = includeOwnerPrivateState ? tile.ID : null;
            isModified = includeOwnerPrivateState && tile.IsModified;
            isValid = true;
        }

        public TileData ToTileData()
        {
            if (!isValid) return null;
            var tile = new TileData((Suit)suit, value, ownerId)
            {
                ID = instanceId,
                IsModified = isModified
            };
            return tile;
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
        public SimpleTileData[] meldTiles;
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
    public sealed class TalentFanContributionMessage
    {
        public string talentId;
        public int fanDelta;
        public int category;
        public int sequence;
    }

    [Serializable]
    public sealed class TalentFanBreakdownMessage
    {
        public int baseFan;
        public int finalFan;
        public TalentFanContributionMessage[] contributions;

        public static TalentFanBreakdownMessage Clone(TalentFanBreakdownMessage source)
        {
            if (source == null) return null;
            return new TalentFanBreakdownMessage
            {
                baseFan = source.baseFan,
                finalFan = source.finalFan,
                contributions = (source.contributions ?? Array.Empty<TalentFanContributionMessage>())
                    .Where(row => row != null)
                    .Select(row => new TalentFanContributionMessage
                    {
                        talentId = row.talentId,
                        fanDelta = row.fanDelta,
                        category = row.category,
                        sequence = row.sequence
                    })
                    .ToArray()
            };
        }

        public static TalentFanBreakdownMessage FromResolution(TalentFanResolution source)
        {
            if (source?.IsAttributionComplete != true) return null;
            TalentFanContribution[] contributions =
                (source.Contributions ?? Array.Empty<TalentFanContribution>()).ToArray();
            if (source.BaseFan + contributions.Sum(row => row.FanDelta) != source.FinalFan)
                return null;
            return new TalentFanBreakdownMessage
            {
                baseFan = source.BaseFan,
                finalFan = source.FinalFan,
                contributions = contributions
                    .Select(row => new TalentFanContributionMessage
                    {
                        talentId = row.TalentId,
                        fanDelta = row.FanDelta,
                        category = (int)row.Category,
                        sequence = row.Sequence
                    })
                    .ToArray()
            };
        }
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
        public TalentFanBreakdownMessage talentFanBreakdown;
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

    [Serializable]
    public sealed class SnapshotRevealedTile
    {
        public int suit;
        public int value;
        public bool isModified;
    }

    [Serializable]
    public sealed class PrivateTileRevealMessage
    {
        public string talentId;
        public int viewerSeatIndex;
        public int targetSeatIndex;
        public int roundNumber;
        public SnapshotRevealedTile[] tiles;
    }

    [Serializable]
    public sealed class SnapshotPrivateTileReveal
    {
        public string talentId;
        public int viewerSeatIndex;
        public int targetSeatIndex;
        public int roundNumber;
        public SnapshotRevealedTile[] tiles;
    }

    [Serializable]
    public sealed class SnapshotKnownTile
    {
        public int suit;
        public int value;
        public bool isModified;
    }

    [Serializable]
    public sealed class SnapshotKnownHand
    {
        public int targetSeatIndex;
        public SnapshotKnownTile[] tiles;
    }

    [Serializable]
    public sealed class PrivateKnownTilesMessage
    {
        public int viewerSeatIndex;
        public SnapshotKnownHand[] hands;
    }
}
