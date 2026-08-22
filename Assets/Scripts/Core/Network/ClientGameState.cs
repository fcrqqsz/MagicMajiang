using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    public sealed class ClientTalentRecoveryProjection
    {
        public bool CloseTransientPicker { get; set; }
        public long DecisionId { get; set; }
        public TalentActionOption[] AvailableActions { get; set; } = Array.Empty<TalentActionOption>();
    }

    /// <summary>
    /// Pure client-side projection of authoritative room game data. It owns no Unity
    /// scene objects, timers, animations, or network connection behavior.
    /// </summary>
    public sealed class ClientGameState
    {
        private RoomGameSnapshot _snapshot;

        public int LastSequence { get; private set; }
        public bool IsResyncRequired { get; private set; }
        public RoomGameSnapshot Snapshot => CloneSnapshot(_snapshot);
        public TalentActionOption[] AvailableTalentActions =>
            IsOwnMainDecision(_snapshot?.activeDecision)
                ? ToTalentActionOptions(_snapshot?.privateSeat?.availableTalentActions)
                : Array.Empty<TalentActionOption>();

        public ClientTalentRecoveryProjection CreateTalentRecoveryProjection()
        {
            SnapshotDecision decision = _snapshot?.activeDecision;
            bool isOwnMainDecision = IsOwnMainDecision(decision);
            return new ClientTalentRecoveryProjection
            {
                CloseTransientPicker = true,
                DecisionId = isOwnMainDecision ? decision.decisionId : 0,
                AvailableActions = isOwnMainDecision ? AvailableTalentActions : Array.Empty<TalentActionOption>()
            };
        }

        /// <summary>Clears a completed or abandoned room projection before another room stream is accepted.</summary>
        public void Reset()
        {
            _snapshot = null;
            LastSequence = 0;
            IsResyncRequired = false;
        }

        /// <summary>Binds an incrementally-created projection to the authoritative room that created its stream.</summary>
        public void SetRoomIdentity(string roomId, int requestingSeatIndex)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return;
            _snapshot ??= CreateEmptySnapshot();
            _snapshot.roomId = roomId;
            _snapshot.requestingSeatIndex = requestingSeatIndex;
            if (_snapshot.privateSeat != null) _snapshot.privateSeat.seatIndex = requestingSeatIndex;
        }

        /// <summary>Applies the room's public budget and this seat's exact total as one private projection update.</summary>
        public bool ApplyRoomJoined(RoomJoinedMessage joined)
        {
            if (joined == null || string.IsNullOrWhiteSpace(joined.roomId)
                || joined.seatIndex < 0 || joined.seatIndex >= 4) return false;

            var next = CloneSnapshot(_snapshot) ?? CreateEmptySnapshot();
            next.roomId = joined.roomId;
            next.roomState = joined.roomState;
            next.gameMode = joined.gameMode;
            next.alienationPreset = joined.alienationPreset;
            next.requestingSeatIndex = joined.seatIndex;
            var privateSeat = EnsurePrivateSeat(next);
            privateSeat.seatIndex = joined.seatIndex;
            privateSeat.ownTotalAlienation = joined.ownTotalAlienation;
            _snapshot = next;
            return true;
        }

        public bool ApplySnapshot(RoomGameSnapshot snapshot, int baselineSequence)
        {
            if (snapshot == null || baselineSequence < 0) return false;

            RoomGameSnapshot replacement;
            try
            {
                replacement = CloneSnapshot(snapshot);
            }
            catch
            {
                return false;
            }

            _snapshot = replacement;
            LastSequence = baselineSequence;
            IsResyncRequired = false;
            return true;
        }

        public ClientSequenceDisposition ApplyEnvelope(NetworkMessageEnvelope envelope)
        {
            if (envelope == null || envelope.seq <= 0 || envelope.seq <= LastSequence)
                return ClientSequenceDisposition.IgnoredDuplicate;
            if (IsResyncRequired || envelope.seq != LastSequence + 1)
            {
                IsResyncRequired = true;
                return ClientSequenceDisposition.ResyncRequired;
            }

            var next = CloneSnapshot(_snapshot) ?? CreateEmptySnapshot();
            try
            {
                ApplyEnvelopeCore(next, envelope);
            }
            catch
            {
                return ClientSequenceDisposition.IgnoredDuplicate;
            }

            _snapshot = next;
            LastSequence = envelope.seq;
            return ClientSequenceDisposition.Accepted;
        }

        private static void ApplyEnvelopeCore(RoomGameSnapshot snapshot, NetworkMessageEnvelope envelope)
        {
            switch (envelope.type)
            {
                case "RoundStart":
                {
                    var message = MessageSerializer.DeserializePayload<RoundStartMessage>(envelope.data);
                    if (message == null) return;
                    snapshot.roundNumber = message.roundNumber;
                    snapshot.prevalentWind = message.prevalentWind;
                    snapshot.requestingSeatWind = message.seatWind;
                    snapshot.dealerIndex = message.dealerIndex;
                    snapshot.scores = CloneInts(message.scores);
                    return;
                }
                case "GameStart":
                {
                    var message = MessageSerializer.DeserializePayload<GameStartMessage>(envelope.data);
                    if (message == null) return;
                    EnsurePrivateSeat(snapshot).concealedHand = CloneTiles(message.tiles);
                    return;
                }
                case "TalentInfo":
                {
                    var message = MessageSerializer.DeserializePayload<TalentInfoMessage>(envelope.data);
                    if (message == null) return;
                    EnsurePrivateSeat(snapshot).scoringOptions = new SnapshotScoringOptions
                    {
                        bonusFan = message.bonusFan,
                        relaxedPureStraight = message.relaxedPureStraight
                    };
                    return;
                }
                case "PeekWall":
                {
                    var message = MessageSerializer.DeserializePayload<PeekWallMessage>(envelope.data);
                    if (message == null) return;
                    EnsurePrivateSeat(snapshot).peekWallTiles = CloneTiles(message.tiles);
                    return;
                }
                case "TalentRuntimeEvent":
                {
                    var message = MessageSerializer.DeserializePayload<TalentRuntimeEventMessage>(envelope.data);
                    if (message == null || message.ownerSeatIndex < 0 || message.ownerSeatIndex >= 4) return;

                    if (message.visibility == (int)TalentEventVisibility.Public
                        && message.ownerSeatIndex != snapshot.requestingSeatIndex
                        && !string.IsNullOrWhiteSpace(message.talentId))
                    {
                        UpsertKnownTalent(snapshot, message);
                    }

                    if (message.isScoreDelta)
                    {
                        snapshot.scores ??= new int[4];
                        if (snapshot.scores.Length == 4)
                        snapshot.scores[message.ownerSeatIndex] += message.value;
                    }
                    return;
                }
                case "TalentPrivateState":
                {
                    var message = MessageSerializer.DeserializePayload<TalentPrivateStateMessage>(envelope.data);
                    if (message == null || message.ownerSeatIndex != snapshot.requestingSeatIndex) return;
                    SnapshotPrivateSeat privateSeat = EnsurePrivateSeat(snapshot);
                    privateSeat.ownTalents = CloneOwnTalents(message.talents);
                    privateSeat.availableTalentActions = CloneTalentActionOptions(message.availableTalentActions);
                    return;
                }
                case "SideboardStarted":
                {
                    var message = MessageSerializer.DeserializePayload<SideboardStartedMessage>(envelope.data);
                    if (message == null) return;
                    SnapshotSideboardState sideboard = EnsureSideboard(snapshot);
                    sideboard.isActive = true;
                    sideboard.decisionId = message.decisionId;
                    sideboard.deadlineUnixMilliseconds = message.deadlineUnixMilliseconds;
                    sideboard.ownLocked = false;
                    return;
                }
                case "SideboardLocked":
                {
                    var message = MessageSerializer.DeserializePayload<SideboardLockedMessage>(envelope.data);
                    if (message == null) return;
                    SnapshotSideboardState sideboard = EnsureSideboard(snapshot);
                    sideboard.decisionId = message.decisionId;
                    sideboard.ownLocked = true;
                    EnsurePrivateSeat(snapshot).ownTotalAlienation = message.ownTotalAlienation;
                    return;
                }
                case "SideboardProgress":
                {
                    var message = MessageSerializer.DeserializePayload<SideboardProgressMessage>(envelope.data);
                    if (message == null) return;
                    SnapshotSideboardState sideboard = EnsureSideboard(snapshot);
                    sideboard.decisionId = message.decisionId;
                    sideboard.isActive = !message.isComplete;
                    sideboard.seatLocked = new bool[4];
                    foreach (SideboardSeatLockStateMessage seat in message.seats ?? Array.Empty<SideboardSeatLockStateMessage>())
                    {
                        if (seat != null && seat.seatIndex >= 0 && seat.seatIndex < 4)
                            sideboard.seatLocked[seat.seatIndex] = seat.locked;
                    }
                    return;
                }
                case "TileDrawn":
                {
                    var message = MessageSerializer.DeserializePayload<TileDrawnMessage>(envelope.data);
                    if (message == null) return;
                    var privateSeat = EnsurePrivateSeat(snapshot);
                    privateSeat.concealedHand = (privateSeat.concealedHand ?? Array.Empty<SimpleTileData>())
                        .Append(CloneTile(message.tile)).Where(tile => tile != null).ToArray();
                    snapshot.mainTurnDrawnTile = CloneTile(message.tile);
                    SetMainTurnDecision(snapshot, message.decision, message.decisionId);
                    return;
                }
                case "PlayerDrew":
                {
                    var message = MessageSerializer.DeserializePayload<PlayerDrewMessage>(envelope.data);
                    if (message == null) return;
                    var seat = GetSeat(snapshot, message.playerId);
                    if (seat != null) seat.concealedTileCount++;
                    return;
                }
                case "TurnWithoutDraw":
                {
                    var message = MessageSerializer.DeserializePayload<TurnWithoutDrawMessage>(envelope.data);
                    snapshot.mainTurnDrawnTile = null;
                    SetMainTurnDecision(snapshot, message?.decision, message?.decisionId ?? 0);
                    return;
                }
                case "WallCount":
                {
                    var message = MessageSerializer.DeserializePayload<WallCountMessage>(envelope.data);
                    if (message != null) snapshot.remainingWallCount = message.remainingCount;
                    return;
                }
                case "Discarded":
                {
                    var message = MessageSerializer.DeserializePayload<DiscardedMessage>(envelope.data);
                    if (message == null || message.playerId < 0 || message.playerId >= 4) return;
                    ClearCachedTalentActions(snapshot);
                    EnsureRivers(snapshot);
                    var river = GetRiver(snapshot, message.playerId);
                    river.tiles = (river.tiles ?? Array.Empty<SimpleTileData>())
                        .Append(CloneTile(message.tile)).Where(tile => tile != null).ToArray();
                    snapshot.mainTurnDrawnTile = null;
                    var seat = GetSeat(snapshot, message.playerId);
                    if (seat != null && seat.concealedTileCount > 0) seat.concealedTileCount--;
                    if (message.playerId == snapshot.requestingSeatIndex)
                    {
                        RemovePrivateTiles(EnsurePrivateSeat(snapshot), message.tile, 1);
                        snapshot.activeDecision = null;
                        return;
                    }
                    snapshot.activeDecision = CloneDecision(message.decision) ?? new SnapshotDecision
                    {
                        decisionId = message.decisionId,
                        phase = (int)NetworkDecisionPhase.Response,
                        actingSeatIndex = -1,
                        discardingSeatIndex = message.playerId,
                        targetTile = CloneTile(message.tile),
                        eligibleSeats = Array.Empty<int>(),
                        submittedSeats = Array.Empty<int>(),
                        controllerSeatIndex = -1,
                        deadlineUnixMilliseconds = 0
                    };
                    return;
                }
                case "AddedKongDeclared":
                {
                    var message = MessageSerializer.DeserializePayload<AddedKongDeclaredMessage>(envelope.data);
                    if (message == null || message.playerId < 0 || message.playerId >= 4) return;
                    ClearCachedTalentActions(snapshot);
                    snapshot.activeDecision = CloneDecision(message.decision) ?? new SnapshotDecision
                    {
                        decisionId = message.decisionId,
                        phase = (int)NetworkDecisionPhase.RobKong,
                        actingSeatIndex = -1,
                        discardingSeatIndex = message.playerId,
                        targetTile = CloneTile(message.tile),
                        eligibleSeats = Array.Empty<int>(),
                        submittedSeats = Array.Empty<int>(),
                        controllerSeatIndex = -1,
                        deadlineUnixMilliseconds = 0
                    };
                    return;
                }
                case "ActionResolved":
                {
                    var message = MessageSerializer.DeserializePayload<ActionResolvedMessage>(envelope.data);
                    if (message == null) return;
                    RemoveClaimedDiscard(snapshot, message);
                    ApplyResolvedMeld(snapshot, message);
                    snapshot.activeDecision = null;
                    snapshot.mainTurnDrawnTile = null;
                    ClearCachedTalentActions(snapshot);
                    return;
                }
                case "Timeout":
                    snapshot.activeDecision = null;
                    snapshot.mainTurnDrawnTile = null;
                    ClearCachedTalentActions(snapshot);
                    return;
                case "PlayerWin":
                {
                    var message = MessageSerializer.DeserializePayload<PlayerWinMessage>(envelope.data);
                    if (message == null) return;
                    var winResult = WinResultNormalizer.Normalize(
                        message.winKind, message.isSelfDraw, message.loserId);
                    snapshot.scores = CloneInts(message.scores);
                    snapshot.result = new RoundResultSnapshot
                    {
                        winnerId = message.winnerId,
                        fanCount = message.totalFan,
                        fanDetails = message.fanDetails?.ToArray() ?? Array.Empty<string>(),
                        isSelfDraw = winResult.IsSelfDraw,
                        winKind = winResult.Kind,
                        loserId = winResult.LoserId,
                        isDrawGame = false,
                        isSessionOver = false,
                        winningHand = WinningHandSnapshotCodec.Normalize(message.winningHand),
                        talentFanBreakdown = TalentFanBreakdownMessage.Clone(message.talentFanBreakdown)
                    };
                    snapshot.activeDecision = null;
                    snapshot.mainTurnDrawnTile = null;
                    ClearCachedTalentActions(snapshot);
                    return;
                }
                case "DrawGame":
                {
                    var message = MessageSerializer.DeserializePayload<DrawGameMessage>(envelope.data);
                    if (message == null) return;
                    snapshot.scores = CloneInts(message.scores);
                    snapshot.result = new RoundResultSnapshot
                    {
                        winnerId = -1,
                        fanCount = 0,
                        isSelfDraw = false,
                        loserId = -1,
                        isDrawGame = true,
                        isSessionOver = false
                    };
                    snapshot.activeDecision = null;
                    snapshot.mainTurnDrawnTile = null;
                    ClearCachedTalentActions(snapshot);
                    return;
                }
                case "SessionEnd":
                {
                    var message = MessageSerializer.DeserializePayload<SessionEndMessage>(envelope.data);
                    if (message == null) return;
                    snapshot.scores = CloneInts(message.scores);
                    var result = snapshot.result ?? new RoundResultSnapshot();
                    result.isSessionOver = true;
                    snapshot.result = result;
                    snapshot.activeDecision = null;
                    snapshot.mainTurnDrawnTile = null;
                    ClearCachedTalentActions(snapshot);
                    return;
                }
            }
        }

        private static void SetMainTurnDecision(RoomGameSnapshot snapshot, SnapshotDecision decision, long decisionId)
        {
            if (decision != null)
            {
                snapshot.activeDecision = CloneDecision(decision);
                return;
            }
            if (decisionId <= 0) return;
            int seatIndex = snapshot.requestingSeatIndex;
            snapshot.activeDecision = new SnapshotDecision
            {
                decisionId = decisionId,
                phase = (int)NetworkDecisionPhase.MainTurn,
                actingSeatIndex = seatIndex,
                discardingSeatIndex = -1,
                eligibleSeats = new[] { seatIndex },
                submittedSeats = Array.Empty<int>(),
                controllerSeatIndex = seatIndex,
                deadlineUnixMilliseconds = 0
            };
        }

        private static void ApplyResolvedMeld(RoomGameSnapshot snapshot, ActionResolvedMessage message)
        {
            var seat = GetSeat(snapshot, message.playerId);
            if (seat == null) return;
            var actionType = (ClientActionType)message.actionType;
            if (ToMeldType(actionType) < 0) return;
            var meld = new SnapshotMeld
            {
                meldType = ToMeldType(actionType),
                sourceSeatIndex = -1,
                isConcealed = actionType == ClientActionType.AnGan,
                tileCount = GetMeldTileCount(actionType),
                tiles = BuildResolvedMeldTiles(message)
            };
            seat.publicMelds = AddOrUpgradeMeld(seat.publicMelds, meld, actionType, message.tile);
            int consumedTileCount = GetConsumedHandTileCount(actionType);
            seat.concealedTileCount = Math.Max(0, seat.concealedTileCount - consumedTileCount);

            if (message.playerId != snapshot.requestingSeatIndex) return;
            var privateSeat = EnsurePrivateSeat(snapshot);
            if (actionType == ClientActionType.Chi && message.chiCombinations?.Length == 2)
            {
                RemovePrivateTiles(privateSeat, new SimpleTileData
                {
                    suit = message.tile?.suit ?? 0,
                    value = message.chiCombinations[0],
                    ownerId = message.tile?.ownerId ?? 0,
                    isValid = message.tile?.isValid ?? false
                }, 1);
                RemovePrivateTiles(privateSeat, new SimpleTileData
                {
                    suit = message.tile?.suit ?? 0,
                    value = message.chiCombinations[1],
                    ownerId = message.tile?.ownerId ?? 0,
                    isValid = message.tile?.isValid ?? false
                }, 1);
            }
            else
            {
                RemovePrivateTiles(privateSeat, message.tile, consumedTileCount);
            }
            privateSeat.melds = AddOrUpgradeMeld(privateSeat.melds, meld, actionType, message.tile);
        }

        private static SnapshotMeld[] AddOrUpgradeMeld(SnapshotMeld[] melds, SnapshotMeld meld, ClientActionType actionType, SimpleTileData targetTile)
        {
            var next = CloneMelds(melds);
            if (actionType == ClientActionType.JiaGang)
            {
                var matchingPon = next.FirstOrDefault(existing => existing != null
                    && existing.meldType == (int)MeldType.Pon
                    && existing.tiles != null
                    && existing.tiles.Any(tile => SameTile(tile, targetTile)));
                if (matchingPon != null)
                {
                    matchingPon.meldType = (int)MeldType.Kan_Added;
                    matchingPon.tileCount = 4;
                    matchingPon.tiles = CloneTiles(meld.tiles);
                    return next;
                }
            }
            return next.Append(CloneMelds(new[] { meld })[0]).ToArray();
        }

        private static int GetConsumedHandTileCount(ClientActionType actionType)
        {
            switch (actionType)
            {
                case ClientActionType.Chi:
                case ClientActionType.Pon:
                    return 2;
                case ClientActionType.MingGan:
                    return 3;
                case ClientActionType.AnGan:
                    return 4;
                case ClientActionType.JiaGang:
                    return 1;
                default:
                    return 0;
            }
        }

        private static void RemoveClaimedDiscard(RoomGameSnapshot snapshot, ActionResolvedMessage message)
        {
            var actionType = (ClientActionType)message.actionType;
            if (actionType == ClientActionType.AnGan || actionType == ClientActionType.JiaGang) return;
            int discardingSeatIndex = snapshot.activeDecision?.discardingSeatIndex ?? -1;
            if (discardingSeatIndex < 0 || discardingSeatIndex >= 4 || snapshot.rivers == null) return;
            var river = GetRiver(snapshot, discardingSeatIndex);
            var tiles = river?.tiles ?? Array.Empty<SimpleTileData>();
            if (tiles.Length == 0 || !SameTile(tiles[tiles.Length - 1], message.tile)) return;
            river.tiles = tiles.Take(tiles.Length - 1).Select(CloneTile).ToArray();
        }

        private static void RemovePrivateTiles(SnapshotPrivateSeat privateSeat, SimpleTileData targetTile, int count)
        {
            if (privateSeat == null || targetTile == null || count <= 0) return;
            var remaining = new List<SimpleTileData>(privateSeat.concealedHand ?? Array.Empty<SimpleTileData>());
            for (int index = remaining.Count - 1; index >= 0 && count > 0; index--)
            {
                if (!SameTile(remaining[index], targetTile)) continue;
                remaining.RemoveAt(index);
                count--;
            }
            privateSeat.concealedHand = remaining.Select(CloneTile).ToArray();
        }

        private static int ToMeldType(ClientActionType actionType)
        {
            switch (actionType)
            {
                case ClientActionType.Chi: return (int)MeldType.Chi;
                case ClientActionType.Pon: return (int)MeldType.Pon;
                case ClientActionType.MingGan: return (int)MeldType.Kan_Exposed;
                case ClientActionType.AnGan: return (int)MeldType.Kan_Concealed;
                case ClientActionType.JiaGang: return (int)MeldType.Kan_Added;
                default: return -1;
            }
        }

        private static int GetMeldTileCount(ClientActionType actionType)
        {
            return actionType == ClientActionType.Chi || actionType == ClientActionType.Pon ? 3 : 4;
        }

        private static SimpleTileData[] BuildResolvedMeldTiles(ActionResolvedMessage message)
        {
            var actionType = (ClientActionType)message.actionType;
            if (actionType == ClientActionType.Chi && message.chiCombinations?.Length == 2 && message.tile != null)
            {
                return new[]
                {
                    CloneTile(message.tile),
                    new SimpleTileData { suit = message.tile.suit, value = message.chiCombinations[0], ownerId = message.tile.ownerId, isValid = true },
                    new SimpleTileData { suit = message.tile.suit, value = message.chiCombinations[1], ownerId = message.tile.ownerId, isValid = true }
                };
            }

            return Enumerable.Range(0, GetMeldTileCount(actionType)).Select(_ => CloneTile(message.tile))
                .Where(tile => tile != null).ToArray();
        }

        private static RoomGameSnapshot CreateEmptySnapshot()
        {
            return new RoomGameSnapshot
            {
                requestingSeatIndex = -1,
                seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeat { seatIndex = index }).ToArray(),
                privateSeat = new SnapshotPrivateSeat { seatIndex = -1 },
                rivers = CreateEmptyRivers(),
                knownTalents = Array.Empty<SnapshotKnownTalent>(),
                sideboard = new SnapshotSideboardState { seatLocked = new bool[4] }
            };
        }

        private static void ClearCachedTalentActions(RoomGameSnapshot snapshot) =>
            EnsurePrivateSeat(snapshot).availableTalentActions = Array.Empty<SnapshotTalentActionOption>();

        private static SnapshotPrivateSeat EnsurePrivateSeat(RoomGameSnapshot snapshot)
        {
            if (snapshot.privateSeat == null)
                snapshot.privateSeat = new SnapshotPrivateSeat { seatIndex = snapshot.requestingSeatIndex };
            return snapshot.privateSeat;
        }

        private static SnapshotSideboardState EnsureSideboard(RoomGameSnapshot snapshot)
        {
            snapshot.sideboard ??= new SnapshotSideboardState { seatLocked = new bool[4] };
            return snapshot.sideboard;
        }

        private bool IsOwnMainDecision(SnapshotDecision decision) =>
            decision != null
            && decision.decisionId > 0
            && decision.phase == (int)NetworkDecisionPhase.MainTurn
            && decision.actingSeatIndex == _snapshot.requestingSeatIndex;

        private static void UpsertKnownTalent(RoomGameSnapshot snapshot, TalentRuntimeEventMessage message)
        {
            var talents = (snapshot.knownTalents ?? Array.Empty<SnapshotKnownTalent>()).ToList();
            SnapshotKnownTalent known = talents.FirstOrDefault(talent => talent != null
                && talent.ownerSeatIndex == message.ownerSeatIndex
                && string.Equals(talent.talentId, message.talentId, StringComparison.Ordinal));
            if (known == null)
            {
                known = new SnapshotKnownTalent
                {
                    ownerSeatIndex = message.ownerSeatIndex,
                    talentId = message.talentId,
                    isKnown = true,
                    isActive = true
                };
                talents.Add(known);
            }
            known.lastPublicEventType = message.eventType;
            known.lastPublicValue = message.value;
            snapshot.knownTalents = talents.ToArray();
        }

        private static void EnsureRivers(RoomGameSnapshot snapshot)
        {
            if (snapshot.rivers == null || snapshot.rivers.Length != 4)
                snapshot.rivers = CreateEmptyRivers();

            for (int index = 0; index < snapshot.rivers.Length; index++)
            {
                if (snapshot.rivers[index] == null)
                    snapshot.rivers[index] = new SeatRiverSnapshot { seatIndex = index, tiles = Array.Empty<SimpleTileData>() };
            }
        }

        private static SeatRiverSnapshot[] CreateEmptyRivers()
        {
            return Enumerable.Range(0, 4).Select(index => new SeatRiverSnapshot
            {
                seatIndex = index,
                tiles = Array.Empty<SimpleTileData>()
            }).ToArray();
        }

        private static SeatRiverSnapshot GetRiver(RoomGameSnapshot snapshot, int seatIndex)
        {
            if (snapshot == null || seatIndex < 0 || seatIndex >= 4) return null;
            EnsureRivers(snapshot);
            return snapshot.rivers[seatIndex];
        }

        private static RoomSnapshotSeat GetSeat(RoomGameSnapshot snapshot, int seatIndex)
        {
            if (seatIndex < 0 || seatIndex >= 4) return null;
            if (snapshot.seats == null || snapshot.seats.Length != 4)
                snapshot.seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeat { seatIndex = index }).ToArray();
            return snapshot.seats[seatIndex];
        }

        private static bool SameTile(SimpleTileData left, SimpleTileData right)
        {
            return left != null && right != null && left.suit == right.suit && left.value == right.value;
        }

        private static int[] CloneInts(int[] values) => values?.ToArray() ?? Array.Empty<int>();
        private static SimpleTileData CloneTile(SimpleTileData tile) => tile == null ? null : new SimpleTileData
        {
            suit = tile.suit,
            value = tile.value,
            ownerId = tile.ownerId,
            isValid = tile.isValid
        };
        private static SimpleTileData[] CloneTiles(SimpleTileData[] tiles) => (tiles ?? Array.Empty<SimpleTileData>()).Select(CloneTile).ToArray();

        private static RoomGameSnapshot CloneSnapshot(RoomGameSnapshot snapshot)
        {
            if (snapshot == null) return null;
            return new RoomGameSnapshot
            {
                roomId = snapshot.roomId,
                roomState = snapshot.roomState,
                gameMode = snapshot.gameMode,
                alienationPreset = snapshot.alienationPreset,
                requestingSeatIndex = snapshot.requestingSeatIndex,
                seats = (snapshot.seats ?? Array.Empty<RoomSnapshotSeat>()).Select(CloneSeat).ToArray(),
                knownTalents = CloneKnownTalents(snapshot.knownTalents),
                roundNumber = snapshot.roundNumber,
                prevalentWind = snapshot.prevalentWind,
                requestingSeatWind = snapshot.requestingSeatWind,
                dealerIndex = snapshot.dealerIndex,
                scores = CloneInts(snapshot.scores),
                privateSeat = ClonePrivateSeat(snapshot.privateSeat),
                rivers = (snapshot.rivers ?? Array.Empty<SeatRiverSnapshot>()).Select(CloneRiver).ToArray(),
                remainingWallCount = snapshot.remainingWallCount,
                activeDecision = CloneDecision(snapshot.activeDecision),
                mainTurnDrawnTile = CloneTile(snapshot.mainTurnDrawnTile),
                sideboard = CloneSideboard(snapshot.sideboard),
                result = CloneResult(snapshot.result)
            };
        }

        private static SeatRiverSnapshot CloneRiver(SeatRiverSnapshot river)
        {
            return river == null ? null : new SeatRiverSnapshot
            {
                seatIndex = river.seatIndex,
                tiles = CloneTiles(river.tiles)
            };
        }

        private static RoomSnapshotSeat CloneSeat(RoomSnapshotSeat seat)
        {
            if (seat == null) return null;
            return new RoomSnapshotSeat
            {
                seatIndex = seat.seatIndex,
                isOccupied = seat.isOccupied,
                isAi = seat.isAi,
                isOnline = seat.isOnline,
                controller = seat.controller,
                displayName = seat.displayName,
                concealedTileCount = seat.concealedTileCount,
                publicMelds = CloneMelds(seat.publicMelds)
            };
        }

        private static SnapshotPrivateSeat ClonePrivateSeat(SnapshotPrivateSeat seat)
        {
            if (seat == null) return null;
            return new SnapshotPrivateSeat
            {
                seatIndex = seat.seatIndex,
                ownTotalAlienation = seat.ownTotalAlienation,
                concealedHand = CloneTiles(seat.concealedHand),
                melds = CloneMelds(seat.melds),
                scoringOptions = seat.scoringOptions == null ? null : new SnapshotScoringOptions
                {
                    bonusFan = seat.scoringOptions.bonusFan,
                    relaxedPureStraight = seat.scoringOptions.relaxedPureStraight
                },
                peekWallTiles = CloneTiles(seat.peekWallTiles),
                ownTalents = CloneOwnTalents(seat.ownTalents),
                availableTalentActions = CloneTalentActionOptions(seat.availableTalentActions)
            };
        }

        private static SnapshotKnownTalent[] CloneKnownTalents(SnapshotKnownTalent[] talents) =>
            (talents ?? Array.Empty<SnapshotKnownTalent>()).Select(talent => talent == null ? null : new SnapshotKnownTalent
            {
                ownerSeatIndex = talent.ownerSeatIndex,
                talentId = talent.talentId,
                isKnown = talent.isKnown,
                isActive = talent.isActive,
                lastPublicEventType = talent.lastPublicEventType,
                lastPublicValue = talent.lastPublicValue
            }).ToArray();

        private static SnapshotOwnTalent[] CloneOwnTalents(SnapshotOwnTalent[] talents) =>
            (talents ?? Array.Empty<SnapshotOwnTalent>()).Select(talent => talent == null ? null : new SnapshotOwnTalent
            {
                talentId = talent.talentId,
                isActive = talent.isActive,
                privateValue = talent.privateValue
            }).ToArray();

        private static SnapshotTalentActionOption[] CloneTalentActionOptions(SnapshotTalentActionOption[] options) =>
            (options ?? Array.Empty<SnapshotTalentActionOption>())
                .Select(TalentActionSnapshotCodec.CloneSnapshot)
                .ToArray();

        private static TalentActionOption[] ToTalentActionOptions(SnapshotTalentActionOption[] options) =>
            (options ?? Array.Empty<SnapshotTalentActionOption>())
                .Select(TalentActionSnapshotCodec.FromSnapshot)
                .Where(option => option != null)
                .ToArray();

        private static SnapshotSideboardState CloneSideboard(SnapshotSideboardState sideboard) =>
            sideboard == null ? null : new SnapshotSideboardState
            {
                isActive = sideboard.isActive,
                decisionId = sideboard.decisionId,
                deadlineUnixMilliseconds = sideboard.deadlineUnixMilliseconds,
                ownLocked = sideboard.ownLocked,
                seatLocked = sideboard.seatLocked?.ToArray() ?? Array.Empty<bool>()
            };

        private static SnapshotMeld[] CloneMelds(SnapshotMeld[] melds)
        {
            return (melds ?? Array.Empty<SnapshotMeld>()).Select(meld => meld == null ? null : new SnapshotMeld
            {
                meldType = meld.meldType,
                sourceSeatIndex = meld.sourceSeatIndex,
                isConcealed = meld.isConcealed,
                tileCount = meld.tileCount,
                tiles = CloneTiles(meld.tiles)
            }).ToArray();
        }

        private static SnapshotDecision CloneDecision(SnapshotDecision decision)
        {
            if (decision == null) return null;
            return new SnapshotDecision
            {
                decisionId = decision.decisionId,
                phase = decision.phase,
                actingSeatIndex = decision.actingSeatIndex,
                discardingSeatIndex = decision.discardingSeatIndex,
                targetTile = CloneTile(decision.targetTile),
                eligibleSeats = CloneInts(decision.eligibleSeats),
                submittedSeats = CloneInts(decision.submittedSeats),
                controllerSeatIndex = decision.controllerSeatIndex,
                deadlineUnixMilliseconds = decision.deadlineUnixMilliseconds,
                isKongReplacementDraw = decision.isKongReplacementDraw
            };
        }

        private static RoundResultSnapshot CloneResult(RoundResultSnapshot result)
        {
            if (result == null) return null;
            var winResult = result.winnerId >= 0
                ? WinResultNormalizer.Normalize(result.winKind, result.isSelfDraw, result.loserId, true)
                : new NormalizedWinResult(WinKind.Unknown, -1);
            return new RoundResultSnapshot
            {
                winnerId = result.winnerId,
                fanCount = result.fanCount,
                fanDetails = result.fanDetails?.ToArray() ?? Array.Empty<string>(),
                isSelfDraw = winResult.IsSelfDraw,
                winKind = winResult.Kind,
                loserId = winResult.LoserId,
                isDrawGame = result.isDrawGame,
                isSessionOver = result.isSessionOver,
                winningHand = WinningHandSnapshotCodec.Normalize(result.winningHand),
                talentFanBreakdown = TalentFanBreakdownMessage.Clone(result.talentFanBreakdown)
            };
        }
    }
}
