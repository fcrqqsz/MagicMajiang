using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Services;
using MahjongGame.Systems;
using MahjongGame.Talents;

var failures = new List<string>();

var protocolAssembly = typeof(NetworkMessageEnvelope).Assembly;
Assert(protocolAssembly.GetType("MahjongGame.Core.Network.Messages.PlayerLoadoutMessage") != null,
    "PlayerLoadoutMessage must exist as an explicit network DTO.");
Assert(new HelloMessage().protocolVersion == 2,
    "Hello must default to protocol version 2.");
Assert(protocolAssembly.GetType("MahjongGame.Core.Network.Messages.HelloAcceptedMessage") != null,
    "Protocol v2 must expose HelloAcceptedMessage.");
Assert(protocolAssembly.GetType("MahjongGame.Core.Network.Messages.HeartbeatAckMessage") != null,
    "Protocol v2 must expose HeartbeatAckMessage.");
Assert(TryNormalizeUsername("  Alice  ", out var normalizedDisplayName, out var normalizedPlayerId, out var normalizeError)
       && normalizedDisplayName == "Alice" && !string.IsNullOrEmpty(normalizedPlayerId) && string.IsNullOrEmpty(normalizeError),
    "Username identity must trim accepted display names and derive a stable player ID.");
Assert(TryNormalizeUsername(" A ", out var oneCharacterDisplayName, out _, out _)
       && oneCharacterDisplayName == "A",
    "A one-character trimmed username must be accepted.");
Assert(TryNormalizeUsername(" " + new string('a', 32) + " ", out var maximumLengthDisplayName, out _, out _)
       && maximumLengthDisplayName.Length == 32,
    "An exactly-32-character trimmed username must be accepted.");
Assert(TryNormalizeUsername("Alice", out _, out var alicePlayerId, out _)
       && TryNormalizeUsername("alice", out _, out var lowerAlicePlayerId, out _)
       && alicePlayerId == lowerAlicePlayerId,
    "Username identity must derive the same player ID for case-insensitive matches.");
Assert(!TryNormalizeUsername("   ", out _, out _, out var emptyUsernameError) && emptyUsernameError == "InvalidUsername",
    "An empty username must be rejected with the stable InvalidUsername code.");
Assert(!TryNormalizeUsername(new string('a', 33), out _, out _, out var longUsernameError) && longUsernameError == "InvalidUsername",
    "A username longer than 32 characters must be rejected with the stable InvalidUsername code.");
Assert(TryAuthenticateUsername("  Alice  ", out var authenticatedPlayerId, out var authenticatedDisplayName, out var authenticationError)
       && authenticatedPlayerId == normalizedPlayerId && authenticatedDisplayName == "Alice" && string.IsNullOrEmpty(authenticationError),
    "The development authenticator must expose the normalized development identity without account storage.");
Assert(!IsProtocolVersionSupported(1) && IsProtocolVersionSupported(2),
    "Protocol validation must reject a v1 Hello before authentication and accept protocol v2.");
Assert(TryCreateClientHello("Alice", out var clientHelloVersion, out var clientHelloUsername)
       && clientHelloVersion == 2 && clientHelloUsername == "Alice",
    "The room client must create a v2 Hello that carries the entered username for development authentication.");
Assert(VerifyClientHelloHandshakeRules(),
    "The client must defer CreateRoom/JoinRoom until HelloAccepted and cancel the pending command when Hello is rejected.");
Assert(DescribeRoomError(NetworkErrorCodes.IdentityInUse, "The supplied identity cannot use this connection.")
       == "IdentityInUse: The supplied identity cannot use this connection.",
    "The client room error presentation must retain the server's stable error code for the lobby status UI.");
Assert(VerifyConnectionRegistryAuthenticationRules(),
    "Connection registration must reject unauthenticated room commands and concurrent online use of the same identity, while allowing reclaim after disconnect.");
Assert(VerifyConnectionGenerationRules(),
    "A stale endpoint or generation must be rejected after a connection ID is registered again.");
Assert(VerifyIngressGenerationIsPreserved(),
    "The registry must preserve a positive generation allocated at physical connection ingress so the first queued Hello is not treated as generation zero.");
Assert(IsClientMessageWithinLimit(new string('a', 64 * 1024))
       && !IsClientMessageWithinLimit(new string('a', (64 * 1024) + 1))
       && IsClientMessageWithinLimit(new string('\u4e2d', (64 * 1024) / 3))
       && !IsClientMessageWithinLimit(new string('\u4e2d', ((64 * 1024) / 3) + 1)),
    "Inbound client text must accept exactly 64 KiB and reject a larger message before envelope deserialization.");
Assert(VerifySeatMessageStreamRules(),
    "Each human seat must have an independent ordered stream with continuous room-lifetime sequences, a 256-envelope replay cache, endpoint-rebind continuity, and no private-message cross-cache leakage.");
Assert(VerifyClientSequenceGateRules(),
    "The client sequence gate must ignore duplicates, accept contiguous messages, and enter a resync-required state on a gap.");
Assert(!IsClientHeartbeatAckExpired(100f, 109.999f) && IsClientHeartbeatAckExpired(100f, 110f),
    "The client heartbeat watchdog must remain live before and expire exactly at the 10-second acknowledgement timeout.");
Assert(VerifyHeartbeatTimeoutReconnectContract(),
    "A heartbeat timeout must schedule reconnect before closing the stale socket, so generation-filtered close callbacks cannot suppress recovery.");
Assert(VerifyTerminalRecoveryCleanupContract(),
    "A terminal recovery failure must clear the Hello handshake and silently close the old socket so a missing ticket cannot retrigger failure every frame.");
Assert(VerifyServerBootstrapReconnectOptions(),
    "Dedicated Server options must preserve existing values and parse reconnectWindowSeconds=120, messageCacheSize=256, and heartbeatTimeoutSeconds=10.");
Assert(VerifyAuthoritativeTableState(),
    "ServerGameState must retain per-seat rivers, consume claimed discards, and expose isolated hand/meld/river snapshots.");
Assert(VerifyNetworkDecisionTracker(),
    "Network decisions must have session-monotonic IDs and reject stale, duplicate, wrong-phase, wrong-controller, and ineligible actions.");
Assert(typeof(ClientActionMessage).GetField("decisionId")?.FieldType == typeof(long),
    "Network action payloads must carry the decision ID established by the server.");
Assert(VerifyPerSeatSnapshotPrivacy(),
    "Room snapshots must restore authoritative table state while excluding every other seat's hand, deck, talents, and private peek result.");
Assert(VerifyJsonSafeRiverSnapshotContract(),
    "Room snapshots must encode each seat river through a JsonUtility-safe DTO and preserve its tiles through a protocol round trip.");
Assert(VerifyCompletedEastOnlySnapshotProjection(),
    "A completed EastOnly session snapshot must retain the final East 4 round projection instead of advancing to a non-existent fifth round.");
Assert(VerifyClientGameProjection(),
    "ClientGameState must apply ordered game envelopes idempotently and atomically restore full snapshot decision and result state.");
Assert(VerifyConcealedKanVisibility(),
    "Under MCR, every seat must receive the declared concealed-kan tile face in its stream and ClientGameState projection.");
Assert(VerifyReconnectProtocolContract(),
    "E3 must expose reconnect/resync payloads that identify a stream without trusting a client seat index.");
Assert(VerifyReconnectRecoveryStream(),
    "E3 reconnect must replay a contiguous cached suffix or atomically send a full snapshot baseline before flushing newer envelopes.");
Assert(VerifyRoomLifecyclePolicy(),
    "E3 lifecycle must reserve disconnected humans, keep an already-open human decision until its deadline, promote later control to AI, expire seats correctly, and close a room with no online humans.");
Assert(VerifySeatDecisionControlLatch(),
    "E3 seat control must latch the current human or AI owner for one decision and transfer ownership only at a later decision boundary.");
Assert(VerifyReconnectBaselineSequenceGate(),
    "E3 full-snapshot recovery must restore the client sequence baseline so only the next contiguous envelope is accepted.");
Assert(VerifyClientReconnectTicketPolicy(),
    "E3 must persist only the non-secret reconnect ticket fields and clear the ticket for terminal recovery outcomes.");
Assert(VerifyLoginReconnectTicketIdentityPolicy(),
    "A saved reconnect ticket must only auto-reconnect after the same development identity logs in.");
Assert(VerifyAutomaticLoginReconnectPolicy(),
    "A matching saved ticket must opt into automatic reconnect after login, while another development identity must stay in the lobby.");
Assert(VerifySnapshotFirstReconnectPolicy(),
    "A reconnect must request a fresh authoritative snapshot instead of treating a partial client projection as replay-safe.");
Assert(ClientReconnectRetryPolicy.GetDelaySeconds(0) == 0
       && ClientReconnectRetryPolicy.GetDelaySeconds(1) == 1
       && ClientReconnectRetryPolicy.GetDelaySeconds(2) == 2
       && ClientReconnectRetryPolicy.GetDelaySeconds(3) == 4
       && ClientReconnectRetryPolicy.GetDelaySeconds(4) == 8
       && ClientReconnectRetryPolicy.GetDelaySeconds(5) == 10
       && ClientReconnectRetryPolicy.GetDelaySeconds(99) == 10,
    "E4 reconnect retries must use the fixed 0, 1, 2, 4, 8, 10-second schedule and remain at 10 seconds.");
Assert(ClientReconnectRetryPolicy.ShouldRetryAfterError(NetworkErrorCodes.IdentityInUse)
       && !ClientReconnectRetryPolicy.ShouldRetryAfterError(NetworkErrorCodes.RoomNotFound)
       && !ClientReconnectRetryPolicy.ShouldRetryAfterError(NetworkErrorCodes.SeatExpired)
       && !ClientReconnectRetryPolicy.ShouldRetryAfterError(NetworkErrorCodes.StreamMismatch),
    "E4 must keep retrying transient identity contention but clear terminal room, seat, and stream failures.");
Assert(ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.WaitingForPlayers) == ClientRecoverySceneTarget.Lobby
       && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.WaitingForMatchReady) == ClientRecoverySceneTarget.Lobby
       && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.LoadingGameScene) == ClientRecoverySceneTarget.Game
       && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.InRound) == ClientRecoverySceneTarget.Game
       && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.WaitingForNextRound) == ClientRecoverySceneTarget.Game
       && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.SessionCompleted) == ClientRecoverySceneTarget.Game
       && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.Closed) == ClientRecoverySceneTarget.None,
    "E4 recovery routing must return waiting-room snapshots to the lobby and all active/completed table snapshots to the game scene.");
var activeRecoveryDecision = new SnapshotDecision
{
    decisionId = 99,
    controllerSeatIndex = 1,
    deadlineUnixMilliseconds = 1001
};
var localRecoverySeat = new RoomSnapshotSeat { seatIndex = 1, isOccupied = true, isAi = false, isOnline = true, controller = "OnlineHuman" };
Assert(ClientRecoveryInputPolicy.CanRestoreInput(activeRecoveryDecision, localRecoverySeat, 1, 1000)
       && !ClientRecoveryInputPolicy.CanRestoreInput(activeRecoveryDecision, localRecoverySeat, 1, 1001)
       && !ClientRecoveryInputPolicy.CanRestoreInput(activeRecoveryDecision, new RoomSnapshotSeat { seatIndex = 1, isOccupied = true, isAi = false, isOnline = true, controller = "AiControlled" }, 1, 1000),
    "E4 may restore local table input only for a still-live decision controlled by the online local human.");
Assert(ClientRecoveryInputPolicy.CanRestoreInput(new SnapshotDecision
       {
           decisionId = 100,
           phase = (int)NetworkDecisionPhase.Response,
           eligibleSeats = new[] { 1, 3 },
           submittedSeats = Array.Empty<int>(),
           deadlineUnixMilliseconds = 1001
       }, localRecoverySeat, 1, 1000),
    "E4 must also restore a still-live response prompt when the local online human remains eligible and has not submitted.");
Assert(VerifyReservedSeatMembershipPolicy(),
    "A disconnected logical human seat, including one temporarily AI-controlled for a decision, must require reclaim instead of allowing the identity to join a second seat.");
Assert(VerifyRecoveryPresentationContracts(),
    "Recovery presentation must restore self-action choices, remove a claimed recovery discard from its river, and use the snapshot main-turn draw context.");
Assert(VerifyRecoverySnapshotWithoutDecisionContract(),
    "A loading or waiting-room recovery snapshot without an active decision must rebuild presentation without dereferencing a nullable decision phase.");
Assert(VerifyWebSocketGenerationContract(),
    "A stale client socket callback must be ignored after a newer socket generation is installed.");
Assert(VerifySupersededConnectionDetection(),
    "A higher connection generation for the same connection ID must expose the old record for room detachment before replacement.");
Assert(VerifyPhysicalSeatPresencePolicy(),
    "E3 must keep physical online presence separate from temporary AI decision control so an offline reserved seat still expires and an online reconnected human counts as online.");
Assert(VerifyClientProjectionLifecycle(),
    "E3 must reset client game projection and bind it to the current room stream so stale state cannot make a new room request cached replay.");
Assert(VerifyOfflineNextRoundLifecyclePolicy(),
    "E3 must advance locked rooms after a member-state change regardless of aiFill and auto-ready only offline human seats between rounds.");
Assert(VerifyNetworkActionAdmissionPolicy(),
    "A network action already validated against its authenticated seat and decision must not be rejected by the temporary-AI direct-action guard.");
Assert(VerifyDirectActionDecisionAdmissionPolicy(),
    "A direct AI action must be discarded unless the active authoritative decision accepted it.");
Assert(protocolAssembly.GetType("MahjongGame.Core.Network.PlayerLoadoutCodec") != null,
    "PlayerLoadoutCodec must validate and reconstruct trusted player loadouts.");
Assert(protocolAssembly.GetType("MahjongGame.Core.Network.SessionTalentPolicy") != null,
    "SessionTalentPolicy must apply session-start talents exactly once.");
Assert(!RoomReadyPolicy.CanMarkMatchReady(false, 3),
    "AI-fill-disabled rooms with fewer than four humans must reject MatchReady before a seat changes.");
Assert(RoomReadyPolicy.CanMarkMatchReady(false, 4),
    "AI-fill-disabled rooms with four humans must allow MatchReady.");
Assert(RoomReadyPolicy.CanMarkMatchReady(true, 1),
    "AI-fill-enabled rooms must allow MatchReady with fewer than four humans.");

var standardDeck = DeckConfig.CreateStandard();
var emptyTalents = new TalentSlotConfig();
Assert(PlayerLoadoutCodec.TryCreateMessage(standardDeck, emptyTalents, out var standardLoadout, out _),
    "A standard deck with empty talents must create a valid loadout.");
Assert(standardLoadout.deckEntries.Length == 34 && standardLoadout.talentSlotIds.Length == 6,
    "A loadout must explicitly contain the 34 legal tile entries and six talent slots.");
Assert(PlayerLoadoutCodec.TryDecode(standardLoadout, out var decodedStandard, out _),
    "A standard loadout must round-trip through the server codec.");
Assert(decodedStandard.DeckConfig.GetCardCount(Suit.Man, 1) == 1 && decodedStandard.TotalAlienation == 0,
    "The trusted standard loadout must preserve tile counts and zero alienation.");

var singleTypeDeck = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
foreach (var entry in singleTypeDeck.deckEntries) entry.count = 0;
singleTypeDeck.deckEntries[0].count = 34;
Assert(PlayerLoadoutCodec.TryDecode(singleTypeDeck, out var decodedSingleType, out _),
    "Thirty-four copies of one legal tile type must remain valid under the current deck rules.");
Assert(decodedSingleType.DeckConfig.GetCardCount(Suit.Man, 1) == 34 && decodedSingleType.DeckConfig.GetCardCount(Suit.Pin, 1) == 0,
    "A custom deck round-trip must preserve each tile count.");

Assert(!PlayerLoadoutCodec.TryDecode(null, out _, out var missingLoadoutError) && missingLoadoutError == "MissingLoadout",
    "A missing loadout must use the stable MissingLoadout code.");
var wrongVersion = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
wrongVersion.schemaVersion = 2;
Assert(!PlayerLoadoutCodec.TryDecode(wrongVersion, out _, out var wrongVersionError) && wrongVersionError == "UnsupportedLoadoutVersion",
    "An unsupported loadout schema must use the stable UnsupportedLoadoutVersion code.");
var missingEntry = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
missingEntry.deckEntries = missingEntry.deckEntries.Take(33).ToArray();
Assert(!PlayerLoadoutCodec.TryDecode(missingEntry, out _, out var missingEntryError) && missingEntryError == "InvalidDeck",
    "A missing deck tile entry must be rejected.");
var extraEntry = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
extraEntry.deckEntries = extraEntry.deckEntries.Append(new DeckTileCountMessage { suit = 0, value = 1, count = 0 }).ToArray();
Assert(!PlayerLoadoutCodec.TryDecode(extraEntry, out _, out var extraEntryError) && extraEntryError == "InvalidDeck",
    "An extra deck tile entry must be rejected.");
var thirtyThreeTiles = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
thirtyThreeTiles.deckEntries[0].count = 0;
Assert(!PlayerLoadoutCodec.TryDecode(thirtyThreeTiles, out _, out var thirtyThreeTilesError) && thirtyThreeTilesError == "InvalidDeck",
    "A 33-tile deck must be rejected.");
var thirtyFiveTiles = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
thirtyFiveTiles.deckEntries[0].count = 2;
Assert(!PlayerLoadoutCodec.TryDecode(thirtyFiveTiles, out _, out var thirtyFiveTilesError) && thirtyFiveTilesError == "InvalidDeck",
    "A 35-tile deck must be rejected.");
var negativeCount = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
negativeCount.deckEntries[0].count = -1;
Assert(!PlayerLoadoutCodec.TryDecode(negativeCount, out _, out var negativeCountError) && negativeCountError == "InvalidDeck",
    "A negative tile count must be rejected.");
var duplicateEntry = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
duplicateEntry.deckEntries[1].suit = duplicateEntry.deckEntries[0].suit;
duplicateEntry.deckEntries[1].value = duplicateEntry.deckEntries[0].value;
Assert(!PlayerLoadoutCodec.TryDecode(duplicateEntry, out _, out var duplicateEntryError) && duplicateEntryError == "InvalidDeck",
    "A duplicate tile type must be rejected.");
var illegalTile = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
illegalTile.deckEntries[0].suit = 99;
Assert(!PlayerLoadoutCodec.TryDecode(illegalTile, out _, out var illegalTileError) && illegalTileError == "InvalidDeck",
    "An illegal suit or value must be rejected.");

var unknownTalent = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
unknownTalent.talentSlotIds[3] = "unknown";
Assert(!PlayerLoadoutCodec.TryDecode(unknownTalent, out _, out var unknownTalentError) && unknownTalentError == "InvalidTalent",
    "An unregistered talent must be rejected.");
var duplicateTalent = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
duplicateTalent.talentSlotIds[3] = "network_test_small";
duplicateTalent.talentSlotIds[4] = "network_test_small";
Assert(!PlayerLoadoutCodec.TryDecode(duplicateTalent, out _, out var duplicateTalentError) && duplicateTalentError == "InvalidTalent",
    "A duplicate talent must be rejected.");
var tierMismatchTalent = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
tierMismatchTalent.talentSlotIds[3] = "network_test_medium";
Assert(!PlayerLoadoutCodec.TryDecode(tierMismatchTalent, out _, out var tierMismatchTalentError) && tierMismatchTalentError == "InvalidTalent",
    "A talent placed above the slot tier allowance must be rejected.");
var invalidLocalSlots = new TalentSlotConfig { SlotTalentIds = new string[5] };
Assert(!PlayerLoadoutCodec.TryCreateMessage(standardDeck, invalidLocalSlots, out _, out var invalidLocalTalentError) && invalidLocalTalentError == "InvalidTalent",
    "A malformed selected local talent configuration must block room creation or joining.");

var sessionTalents = new Dictionary<int, TalentSlotConfig>
{
    [0] = new TalentSlotConfig { SlotTalentIds = new[] { null, null, null, "starting_capital", null, null } },
    [1] = new TalentSlotConfig { SlotTalentIds = new string[6] },
    [2] = new TalentSlotConfig { SlotTalentIds = new[] { null, null, null, null, null, "starting_capital" } },
    [3] = new TalentSlotConfig { SlotTalentIds = new string[6] }
};
var talentSession = new GameSession(GameMode.EastOnly);
bool startingCapitalApplied = false;
Assert(SessionTalentPolicy.ApplyStartingCapitalOnce(talentSession, sessionTalents, ref startingCapitalApplied),
    "Starting capital must be applied before the first round of a session.");
Assert(talentSession.Scores[0] == 30 && talentSession.Scores[2] == 30,
    "Every seat with starting_capital must receive the initial score bonus.");
Assert(!SessionTalentPolicy.ApplyStartingCapitalOnce(talentSession, sessionTalents, ref startingCapitalApplied)
       && talentSession.Scores[0] == 30 && talentSession.Scores[2] == 30,
    "Starting capital must not be applied again during later EastOnly rounds.");

var wallConfigs = new List<DeckConfig>
{
    DeckConfig.CreateStandard(),
    decodedSingleType.DeckConfig,
    DeckConfig.CreateStandard(),
    DeckConfig.CreateStandard()
};
var wallService = new WallService(seed: 1234);
wallService.BuildWall(wallConfigs);
var builtWall = wallService.GetWallTiles();
Assert(builtWall.Count == 136, "Four accepted 34-tile loadouts must build a 136-tile wall.");
for (int ownerId = 0; ownerId < 4; ownerId++)
{
    Assert(builtWall.Count(tile => tile.OriginalOwnerID == ownerId) == 34,
        $"The wall must retain exactly 34 tiles for owner {ownerId}.");
}
Assert(builtWall.Where(tile => tile.OriginalOwnerID == 1).All(tile => tile.TileSuit == Suit.Man && tile.Value == 1),
    "The wall must preserve the custom deck composition for its owner.");

Assert(TurnActionPolicy.IsMainTurnAction(ClientActionType.Discard), "Discard must be a main-turn action.");
Assert(TurnActionPolicy.IsMainTurnAction(ClientActionType.Hu), "Self-draw Hu must be a main-turn action.");
Assert(!TurnActionPolicy.IsMainTurnAction(ClientActionType.Skip), "A stale response Skip must never complete a main turn.");
Assert(!TurnActionPolicy.IsMainTurnAction(ClientActionType.Chi), "Chi must not complete a main turn.");
Assert(TurnActionPolicy.IsResponseAction(ClientActionType.Skip), "Skip must be a response action.");
Assert(TurnActionPolicy.IsResponseAction(ClientActionType.Chi), "Chi must be a response action.");
Assert(!TurnActionPolicy.IsResponseAction(ClientActionType.Discard), "Discard must not complete a response phase.");

var connectedAt = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
Assert(!ConnectionLivenessPolicy.IsExpired(connectedAt, connectedAt.AddSeconds(9)), "A connection must remain alive before the heartbeat timeout.");
Assert(ConnectionLivenessPolicy.IsExpired(connectedAt, connectedAt.AddSeconds(10)), "A connection must expire at the heartbeat timeout.");

// A room-close notification can arrive while another additive scene is active.
// Returning to the lobby must depend on the Game scene being loaded, not active.
Assert(SceneTransitionPolicy.ShouldUnloadGameScene(true), "A loaded Game scene must be unloaded after the room closes even when it is not the active scene.");
Assert(!SceneTransitionPolicy.ShouldUnloadGameScene(false), "No Game scene should be unloaded when it is not loaded.");

var completedRoom = new ClientRoomState();
completedRoom.ApplyJoined(new RoomJoinedMessage
{
    roomId = "R0001",
    seatIndex = 1,
    gameMode = (int)GameMode.Single,
    roomState = 3,
    aiFillEnabled = true,
    acceptedSchemaVersion = 1,
    acceptedTotalAlienation = 17,
    seats = new[]
    {
        new RoomSeatMessage { seatIndex = 0, isOccupied = true, displayName = "Host", totalAlienation = 9 },
        new RoomSeatMessage { seatIndex = 1, isOccupied = true, displayName = "Guest", totalAlienation = 17 }
    }
});
Assert(completedRoom.AiFillEnabled && completedRoom.AcceptedSchemaVersion == 1 && completedRoom.AcceptedTotalAlienation == 17,
    "RoomJoined must retain the server-accepted loadout summary without exposing private loadout data.");
completedRoom.CompleteSession();
Assert(completedRoom.HasRoom && completedRoom.RoomStateValue == (int)RoomState.SessionCompleted,
    "E3 SessionCompleted must retain the room binding so final results can be recovered and explicitly left.");
Assert(completedRoom.IsSessionCompleted, "A SessionEnd must enter the client completion state.");
Assert(completedRoom.ResultSeatIndex == 1, "The completion state must retain the local seat for result rendering.");
Assert(completedRoom.ResultSeats.Length == 2 && completedRoom.ResultSeats[0].displayName == "Host", "The completion state must retain the seat snapshot for result rendering.");
completedRoom.Reset();
Assert(!completedRoom.IsSessionCompleted && completedRoom.ResultSeats.Length == 0, "Returning to the lobby must clear the completed-room snapshot.");

var readyRoom = new ClientRoomState();
readyRoom.ApplyJoined(new RoomJoinedMessage
{
    roomId = "R0002",
    seatIndex = 1,
    seats = new[]
    {
        new RoomSeatMessage { seatIndex = 0, isOccupied = true, displayName = "Host", isReady = false },
        new RoomSeatMessage { seatIndex = 1, isOccupied = true, displayName = "Guest", isReady = false }
    }
});
Assert(readyRoom.ApplySeatUpdate(new RoomSeatMessage { seatIndex = 1, isOccupied = true, displayName = "Guest", isReady = true })
       && readyRoom.Seats[1].isReady,
    "A ready notification must update the existing room seat from not-ready to ready.");
Assert(new RoomSeatUpdatedMessage { roomId = "R0002" }.roomId == "R0002",
    "RoomSeatUpdatedMessage must provide an explicit server-to-client ready-state update.");

Assert(!ResponseActionPolicy.CanRespondToDiscard(2, 2), "A player must not be allowed to respond to their own discard.");
Assert(ResponseActionPolicy.CanRespondToDiscard(2, 1), "A different player must still be allowed to respond to a discard.");

var simultaneousResponses = new[]
{
    new ClientAction(2, ClientActionType.Hu),
    new ClientAction(3, ClientActionType.Pon),
    new ClientAction(1, ClientActionType.Hu)
};
var highestPriorityResponse = ResponseActionPolicy.SelectHighestPriorityResponse(simultaneousResponses, discarderId: 0, playerCount: 4);
Assert(highestPriorityResponse?.ActionType == ClientActionType.Hu && highestPriorityResponse.PlayerId == 1,
    "Among simultaneous Hu claims, the nearest seat after the discarder must win over later Hu claims and all Pon claims.");
Assert(!ResponseActionPolicy.AllPotentialHuRespondersAnswered(new[] { 1, 2 }, new[] { 1 }),
    "A Hu claim must wait for another Hu-eligible player who has not responded.");
Assert(ResponseActionPolicy.AllPotentialHuRespondersAnswered(new[] { 1, 2 }, new[] { 1, 2 }),
    "A Hu claim must resolve as soon as every Hu-eligible player has responded, without waiting for Pon or Chi players.");

var initialScoreSession = new GameSession(GameMode.EastOnly);
Assert(SessionScorePolicy.ApplyAuthoritativeScores(initialScoreSession, new[] { 30, 0, 30, 0 })
       && initialScoreSession.Scores.SequenceEqual(new[] { 30, 0, 30, 0 }),
    "The server's first-round score snapshot must be applied before gameplay begins.");
Assert(new RoundStartMessage { scores = new[] { 30, 0, 30, 0 } }.scores.Length == 4,
    "RoundStart must carry the authoritative initial score snapshot.");

Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.LoadingGameScene, true, true), "AI fill must preserve a loading room after a departure.");
Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.LoadingGameScene, true, false), "E3 must preserve a loading room with another online human even when AI fill is disabled.");
Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForNextRound, true, true), "AI fill must preserve a between-round room after a departure.");
Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForNextRound, true, false), "E3 must preserve a between-round room with another online human even when AI fill is disabled.");
Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForMatchReady, true, false), "A pre-match room without AI fill must retain an empty seat for a replacement human.");
Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.InRound, true, false), "E3 must reserve an in-round human seat while another human remains online.");
Assert(!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForMatchReady, false, true), "A room with no remaining humans must be closed.");
Assert(!RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.WaitingForPlayers, true),
    "An AI-fill room must leave an empty seat when a player leaves before match-ready.");
Assert(!RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.WaitingForMatchReady, true),
    "An AI-fill room must leave an empty seat when a player leaves after match-ready but before loading.");
Assert(RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.LoadingGameScene, false),
    "E3 emergency takeover during loading must not depend on AI fill.");
Assert(RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.InRound, false),
    "E3 emergency takeover in round must not depend on AI fill.");
Assert(RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.WaitingForNextRound, false),
    "E3 emergency takeover between rounds must not depend on AI fill.");

Assert(LoginUsernamePolicy.Normalize("  Test_Player_02  ") == "Test_Player_02",
    "The lobby nickname must be derived from the username entered on the login screen.");
Assert(LoginUsernamePolicy.Normalize("   ") == "Player",
    "An empty login username must resolve to a safe fallback nickname.");
Assert(VerifyUiToolkitTextCoreFontConfiguration(),
    "UI Toolkit must use the MSYH_UITK TextCore font asset with Panel Text Settings, not a raw TTC or TMP asset.");
Assert(VerifyFloatingTilePanelPickingModeConfiguration(),
    "FloatingTilePanel must use picking-mode as a UXML attribute and never as an unsupported USS or inline style property.");
Assert(VerifyE4RecoveryPresentationWiring(),
    "E4 must expose one ordered recovery path from ClientRoomService through scene routing, atomic game presentation, and the UI Toolkit reconnect overlay.");
Assert(VerifyMcrConcealedKanPresentation(),
    "MCR recovery presentation must show the declared tile faces of every concealed kong, including opponents' public melds.");
Assert(VerifyLiveNetworkSessionProjection(),
    "A live network RoundStart must restore the room-authoritative game mode and round before result UI decides whether the session is over.");
Assert(VerifyRecoveryHandSorting(),
    "A recovery snapshot must sort the rebuilt local concealed hand before placing it without animation.");
Assert(VerifyConcealedKanEligibilityConsistency(),
    "A self-draw concealed-kan prompt must use the same at-least-four matching-tile rule as the hand controller, including custom decks with five or more identical tiles.");

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("Network regression tests passed.");
return 0;

void Assert(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

bool TryNormalizeUsername(string username, out string displayName, out string playerId, out string errorCode)
{
    displayName = null;
    playerId = null;
    errorCode = null;
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.UsernameIdentityPolicy");
    var method = type?.GetMethod("TryNormalize", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    if (method == null) return false;

    var args = new object[] { username, null, null, null };
    var success = method.Invoke(null, args) is bool result && result;
    displayName = args[1] as string;
    playerId = args[2] as string;
    errorCode = args[3] as string;
    return success;
}

bool TryAuthenticateUsername(string username, out string playerId, out string displayName, out string errorCode)
{
    playerId = null;
    displayName = null;
    errorCode = null;
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.DevelopmentAccountAuthenticator");
    var method = type?.GetMethod("TryAuthenticate");
    if (type == null || method == null) return false;

    var args = new object[] { username, null, null };
    var success = method.Invoke(Activator.CreateInstance(type), args) is bool result && result;
    if (args[1] != null)
    {
        var identityType = args[1].GetType();
        playerId = identityType.GetField("PlayerId")?.GetValue(args[1]) as string;
        displayName = identityType.GetField("DisplayName")?.GetValue(args[1]) as string;
    }
    errorCode = args[2] as string;
    return success;
}

bool IsProtocolVersionSupported(int version)
{
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.NetworkProtocol");
    var method = type?.GetMethod("IsSupported", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    return method?.Invoke(null, new object[] { version }) is bool result && result;
}

bool TryCreateClientHello(string username, out int protocolVersion, out string serializedUsername)
{
    protocolVersion = 0;
    serializedUsername = null;
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.ClientHelloProtocol");
    var method = type?.GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    if (method?.Invoke(null, new object[] { username }) is not HelloMessage hello) return false;
    protocolVersion = hello.protocolVersion;
    serializedUsername = hello.username;
    return true;
}

bool VerifyClientHelloHandshakeRules()
{
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.ClientHelloHandshakePolicy");
    var begin = type?.GetMethod("BeginRoomCommand");
    var accept = type?.GetMethod("AcceptHello");
    var reject = type?.GetMethod("RejectHello");
    if (type == null || begin == null || accept == null || reject == null) return false;

    object rejectedHandshake = Activator.CreateInstance(type);
    if (begin.Invoke(rejectedHandshake, null)?.ToString() != "SendHello") return false;
    if (reject.Invoke(rejectedHandshake, null) is not bool rejected || !rejected) return false;
    if (accept.Invoke(rejectedHandshake, null) is not bool noRoomCommandAfterRejection || noRoomCommandAfterRejection) return false;

    object acceptedHandshake = Activator.CreateInstance(type);
    return begin.Invoke(acceptedHandshake, null)?.ToString() == "SendHello"
        && accept.Invoke(acceptedHandshake, null) is bool shouldSendPendingRoomCommand && shouldSendPendingRoomCommand
        && begin.Invoke(acceptedHandshake, null)?.ToString() == "SendRoomCommand";
}

string DescribeRoomError(string code, string message)
{
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.RoomErrorPresentationPolicy");
    var method = type?.GetMethod("GetDisplayMessage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    return method?.Invoke(null, new object[] { new RoomErrorMessage { code = code, message = message } }) as string;
}

bool VerifyConnectionRegistryAuthenticationRules()
{
    var registryType = protocolAssembly.GetType("MahjongGame.Core.Network.ConnectionRegistry");
    var endpointType = protocolAssembly.GetType("MahjongGame.Core.Network.Transport.GameEndpoint");
    var authenticate = registryType?.GetMethod("TryAuthenticate");
    var canSubmitRoomCommands = registryType?.GetMethod("CanSubmitRoomCommands");
    var register = registryType?.GetMethods().SingleOrDefault(candidate =>
        candidate.Name == "Register" && candidate.GetParameters().Length == 2);
    var remove = registryType?.GetMethods().SingleOrDefault(candidate =>
        candidate.Name == "Remove" && candidate.GetParameters().Length == 2);
    if (registryType == null || endpointType == null || authenticate == null || canSubmitRoomCommands == null || register == null || remove == null) return false;

    object registry = Activator.CreateInstance(registryType);
    object firstEndpoint = Activator.CreateInstance(endpointType);
    object secondEndpoint = Activator.CreateInstance(endpointType);
    if (!(register.Invoke(registry, new[] { (object)"C1", firstEndpoint }) is bool firstRegistered && firstRegistered)
        || !(register.Invoke(registry, new[] { (object)"C2", secondEndpoint }) is bool secondRegistered && secondRegistered)) return false;
    if (canSubmitRoomCommands.Invoke(registry, new[] { (object)"C1", firstEndpoint }) is not bool unauthenticated || unauthenticated) return false;

    var identity = new AuthenticatedIdentity("dev:alice", "Alice");
    var firstAuthenticationArgs = new object[] { "C1", firstEndpoint, identity, DateTime.UtcNow, null };
    if (!(authenticate.Invoke(registry, firstAuthenticationArgs) is bool firstAuthenticated && firstAuthenticated)) return false;
    if (canSubmitRoomCommands.Invoke(registry, new[] { (object)"C1", firstEndpoint }) is not bool authenticated || !authenticated) return false;

    var duplicateAuthenticationArgs = new object[] { "C2", secondEndpoint, identity, DateTime.UtcNow, null };
    if (authenticate.Invoke(registry, duplicateAuthenticationArgs) is not bool duplicateRejected || duplicateRejected
        || (duplicateAuthenticationArgs[4] as string) != NetworkErrorCodes.IdentityInUse) return false;

    var removeArgs = new object[] { "C1", null };
    if (remove.Invoke(registry, removeArgs) is not bool removed || !removed) return false;

    var reclaimAuthenticationArgs = new object[] { "C2", secondEndpoint, identity, DateTime.UtcNow, null };
    return authenticate.Invoke(registry, reclaimAuthenticationArgs) is bool reclaimed && reclaimed;
}

bool VerifyConnectionGenerationRules()
{
    var registryType = protocolAssembly.GetType("MahjongGame.Core.Network.ConnectionRegistry");
    var endpointType = protocolAssembly.GetType("MahjongGame.Core.Network.Transport.GameEndpoint");
    var register = registryType?.GetMethods().SingleOrDefault(candidate =>
        candidate.Name == "Register" && candidate.GetParameters().Length == 2);
    var getGeneration = registryType?.GetMethod("TryGetGeneration");
    var isActive = registryType?.GetMethod("IsActiveConnection");
    if (registryType == null || endpointType == null || register == null || getGeneration == null || isActive == null) return false;

    object registry = Activator.CreateInstance(registryType);
    object oldEndpoint = Activator.CreateInstance(endpointType);
    object newEndpoint = Activator.CreateInstance(endpointType);
    if (register.Invoke(registry, new[] { (object)"C1", oldEndpoint }) is not bool registered || !registered) return false;
    var oldGenerationArgs = new object[] { "C1", oldEndpoint, 0L };
    if (getGeneration.Invoke(registry, oldGenerationArgs) is not bool gotOldGeneration || !gotOldGeneration) return false;
    long oldGeneration = (long)oldGenerationArgs[2];

    if (register.Invoke(registry, new[] { (object)"C1", newEndpoint }) is not bool replaced || !replaced) return false;
    var newGenerationArgs = new object[] { "C1", newEndpoint, 0L };
    if (getGeneration.Invoke(registry, newGenerationArgs) is not bool gotNewGeneration || !gotNewGeneration) return false;
    long newGeneration = (long)newGenerationArgs[2];

    return newGeneration > oldGeneration
        && isActive.Invoke(registry, new object[] { "C1", oldEndpoint, oldGeneration }) is bool oldRejected && !oldRejected
        && isActive.Invoke(registry, new object[] { "C1", newEndpoint, newGeneration }) is bool newAccepted && newAccepted;
}

bool VerifyIngressGenerationIsPreserved()
{
    var registryType = protocolAssembly.GetType("MahjongGame.Core.Network.ConnectionRegistry");
    var endpointType = protocolAssembly.GetType("MahjongGame.Core.Network.Transport.GameEndpoint");
    var registerIngress = registryType?.GetMethods().SingleOrDefault(candidate =>
        candidate.Name == "Register" && candidate.GetParameters().Length == 3);
    var getGeneration = registryType?.GetMethod("TryGetGeneration");
    if (registryType == null || endpointType == null || registerIngress == null || getGeneration == null) return false;

    object registry = Activator.CreateInstance(registryType);
    object endpoint = Activator.CreateInstance(endpointType);
    if (registerIngress.Invoke(registry, new object[] { "C1", endpoint, 41L }) is not bool registered || !registered) return false;
    var generationArgs = new object[] { "C1", endpoint, 0L };
    return getGeneration.Invoke(registry, generationArgs) is bool found && found && (long)generationArgs[2] == 41L;
}

bool VerifySeatMessageStreamRules()
{
    var streamType = protocolAssembly.GetType("MahjongGame.Core.Network.SeatMessageStream");
    var endpointType = protocolAssembly.GetType("MahjongGame.Core.Network.Transport.GameEndpoint");
    var constructor = streamType?.GetConstructor(new[] { endpointType, typeof(int) });
    var send = streamType?.GetMethods().SingleOrDefault(candidate =>
        candidate.Name == "Send" && candidate.GetParameters().Length == 2);
    var rebind = streamType?.GetMethod("RebindEndpoint");
    var replay = streamType?.GetMethod("TryGetMessagesAfter");
    if (streamType == null || endpointType == null || constructor == null || send == null || rebind == null || replay == null) return false;

    object firstEndpoint = Activator.CreateInstance(endpointType);
    object reboundEndpoint = Activator.CreateInstance(endpointType);
    object stream = constructor.Invoke(new object[] { firstEndpoint, 256 });
    for (int i = 1; i <= 257; i++)
    {
        send.Invoke(stream, new object[] { "RoomSeatUpdated", new RoomErrorMessage { code = i.ToString(), message = "public" } });
    }

    var firstEndpointMessages = ((MahjongGame.Core.Network.Transport.GameEndpoint)firstEndpoint).SentMessages;
    if (firstEndpointMessages.Count != 257
        || MessageSerializer.DeserializeEnvelope(firstEndpointMessages[0])?.seq != 1
        || MessageSerializer.DeserializeEnvelope(firstEndpointMessages[256])?.seq != 257) return false;

    if (TryReplay(replay, stream, 0, out _)) return false;
    if (!TryReplay(replay, stream, 1, out var cachedMessages)
        || cachedMessages.Length != 256
        || cachedMessages[0].seq != 2
        || cachedMessages[255].seq != 257) return false;

    rebind.Invoke(stream, new[] { reboundEndpoint });
    send.Invoke(stream, new object[] { "RoomSeatUpdated", new RoomErrorMessage { code = "258", message = "public" } });
    var reboundMessages = ((MahjongGame.Core.Network.Transport.GameEndpoint)reboundEndpoint).SentMessages;
    if (firstEndpointMessages.Count != 257
        || reboundMessages.Count != 1
        || MessageSerializer.DeserializeEnvelope(reboundMessages[0])?.seq != 258) return false;

    object secondSeatEndpoint = Activator.CreateInstance(endpointType);
    object firstSeatStream = constructor.Invoke(new object[] { firstEndpoint, 4 });
    object secondSeatStream = constructor.Invoke(new object[] { secondSeatEndpoint, 4 });
    send.Invoke(firstSeatStream, new object[] { "RoomSeatUpdated", new RoomErrorMessage { code = "public", message = "public" } });
    send.Invoke(secondSeatStream, new object[] { "RoomSeatUpdated", new RoomErrorMessage { code = "public", message = "public" } });
    send.Invoke(firstSeatStream, new object[] { "GameStart", new GameStartMessage() });
    send.Invoke(firstSeatStream, new object[] { "TalentInfo", new TalentInfoMessage() });
    send.Invoke(firstSeatStream, new object[] { "PeekWall", new PeekWallMessage() });
    if (!TryReplay(replay, secondSeatStream, 0, out var secondSeatMessages) || secondSeatMessages.Length != 1) return false;

    return secondSeatMessages[0].seq == 1
        && secondSeatMessages[0].type == "RoomSeatUpdated"
        && secondSeatMessages.All(message => message.type != "GameStart" && message.type != "TalentInfo" && message.type != "PeekWall");
}

bool TryReplay(System.Reflection.MethodInfo replay, object stream, int lastSequence, out NetworkMessageEnvelope[] messages)
{
    var args = new object[] { lastSequence, null };
    bool success = replay.Invoke(stream, args) is bool result && result;
    messages = args[1] as NetworkMessageEnvelope[] ?? Array.Empty<NetworkMessageEnvelope>();
    return success;
}

bool VerifyClientSequenceGateRules()
{
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.ClientSequenceGate");
    var apply = type?.GetMethod("Apply");
    var resyncRequired = type?.GetProperty("IsResyncRequired");
    if (type == null || apply == null || resyncRequired == null) return false;

    object gate = Activator.CreateInstance(type);
    return apply.Invoke(gate, new object[] { 1 })?.ToString() == "Accepted"
        && apply.Invoke(gate, new object[] { 1 })?.ToString() == "IgnoredDuplicate"
        && apply.Invoke(gate, new object[] { 2 })?.ToString() == "Accepted"
        && apply.Invoke(gate, new object[] { 4 })?.ToString() == "ResyncRequired"
        && resyncRequired.GetValue(gate) is bool required && required;
}

bool IsClientHeartbeatAckExpired(float lastAcknowledgementTime, float now)
{
    var method = typeof(ConnectionLivenessPolicy).GetMethod(
        "IsClientAcknowledgementExpired",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    return method?.Invoke(null, new object[] { lastAcknowledgementTime, now }) is bool result && result;
}

bool VerifyHeartbeatTimeoutReconnectContract()
{
    string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts", "Core", "Network", "ClientRoomService.cs");
    if (!File.Exists(path)) return false;

    string source = File.ReadAllText(path);
    int timeoutStart = source.IndexOf("if (ConnectionLivenessPolicy.IsClientAcknowledgementExpired", StringComparison.Ordinal);
    int timeoutEnd = source.IndexOf("if (unscaledTime < _nextHeartbeatAt)", StringComparison.Ordinal);
    if (timeoutStart < 0 || timeoutEnd <= timeoutStart) return false;

    string timeoutBranch = source.Substring(timeoutStart, timeoutEnd - timeoutStart);
    int beginReconnect = timeoutBranch.IndexOf("BeginReconnect(ticket,", StringComparison.Ordinal);
    int disconnect = timeoutBranch.IndexOf("WebSocketClient.Instance?.Disconnect();", StringComparison.Ordinal);
    return beginReconnect >= 0 && disconnect > beginReconnect;
}

bool VerifyTerminalRecoveryCleanupContract()
{
    string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts", "Core", "Network", "ClientRoomService.cs");
    if (!File.Exists(path)) return false;

    string source = File.ReadAllText(path);
    int terminalStart = source.IndexOf("private void HandleTerminalReconnectFailure", StringComparison.Ordinal);
    int terminalEnd = source.IndexOf("private void PublishRecoveryProgress", StringComparison.Ordinal);
    if (terminalStart < 0 || terminalEnd <= terminalStart) return false;

    string terminal = source.Substring(terminalStart, terminalEnd - terminalStart);
    int clearHello = terminal.IndexOf("_hasHelloAccepted = false;", StringComparison.Ordinal);
    int resetHandshake = terminal.IndexOf("_helloHandshake.Reset();", StringComparison.Ordinal);
    int disconnect = terminal.IndexOf("WebSocketClient.Instance?.Disconnect();", StringComparison.Ordinal);
    int resetRoom = terminal.IndexOf("ResetRoomState(true);", StringComparison.Ordinal);
    return clearHello >= 0 && resetHandshake > clearHello && disconnect > resetHandshake && resetRoom > disconnect;
}

bool VerifyServerBootstrapReconnectOptions()
{
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.ServerBootstrapOptions");
    var parse = type?.GetMethod("Parse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    if (type == null || parse == null) return false;

    object defaults = parse.Invoke(null, new object[] { Array.Empty<string>() });
    object configured = parse.Invoke(null, new object[]
    {
        new[]
        {
            "--port", "9999", "--maxRooms", "3", "--aiFill", "false",
            "--reconnectWindowSeconds", "121", "--messageCacheSize", "257", "--heartbeatTimeoutSeconds", "11"
        }
    });
    return ReadOption<int>(defaults, "ReconnectWindowSeconds") == 120
        && ReadOption<int>(defaults, "MessageCacheSize") == 256
        && ReadOption<int>(defaults, "HeartbeatTimeoutSeconds") == 10
        && ReadOption<int>(configured, "Port") == 9999
        && ReadOption<int>(configured, "MaxRooms") == 3
        && !ReadOption<bool>(configured, "AiFill")
        && ReadOption<int>(configured, "ReconnectWindowSeconds") == 121
        && ReadOption<int>(configured, "MessageCacheSize") == 257
        && ReadOption<int>(configured, "HeartbeatTimeoutSeconds") == 11;
}

SeatRiverSnapshot[] CreateRivers(params SimpleTileData[][] rivers)
{
    return Enumerable.Range(0, 4).Select(index => new SeatRiverSnapshot
    {
        seatIndex = index,
        tiles = rivers != null && index < rivers.Length ? rivers[index] ?? Array.Empty<SimpleTileData>() : Array.Empty<SimpleTileData>()
    }).ToArray();
}

bool VerifyConcealedKanVisibility()
{
    var opponentEndpoint = new MahjongGame.Core.Network.Transport.GameEndpoint();
    var opponentRemote = new RemotePlayerClient(1, new SeatMessageStream(opponentEndpoint, 4));
    var concealedKanTile = new TileData(Suit.Pin, 8, 0);
    opponentRemote.OnActionResolved(0, ClientActionType.AnGan, concealedKanTile, null);
    var opponentEnvelope = MessageSerializer.DeserializeEnvelope(opponentEndpoint.SentMessages.SingleOrDefault());
    var opponentPayload = MessageSerializer.DeserializePayload<ActionResolvedMessage>(opponentEnvelope?.data);
    if (opponentPayload?.tile == null || opponentPayload.tile.suit != (int)Suit.Pin || opponentPayload.tile.value != 8) return false;

    var ownerEndpoint = new MahjongGame.Core.Network.Transport.GameEndpoint();
    var ownerRemote = new RemotePlayerClient(0, new SeatMessageStream(ownerEndpoint, 4));
    ownerRemote.OnActionResolved(0, ClientActionType.AnGan, concealedKanTile, null);
    var ownerEnvelope = MessageSerializer.DeserializeEnvelope(ownerEndpoint.SentMessages.SingleOrDefault());
    var ownerPayload = MessageSerializer.DeserializePayload<ActionResolvedMessage>(ownerEnvelope?.data);
    if (ownerPayload?.tile == null || ownerPayload.tile.suit != (int)Suit.Pin || ownerPayload.tile.value != 8) return false;

    var state = new ClientGameState();
    if (!state.ApplySnapshot(new RoomGameSnapshot
        {
            requestingSeatIndex = 1,
            seats = new[]
            {
                new RoomSnapshotSeat { seatIndex = 0, concealedTileCount = 4 },
                new RoomSnapshotSeat { seatIndex = 1 }, new RoomSnapshotSeat { seatIndex = 2 }, new RoomSnapshotSeat { seatIndex = 3 }
            },
            privateSeat = new SnapshotPrivateSeat { seatIndex = 1 },
            rivers = CreateRivers()
        }, 0)) return false;
    if (state.ApplyEnvelope(new NetworkMessageEnvelope
        {
            type = "ActionResolved",
            seq = 1,
            data = UnityEngine.JsonUtility.ToJson(opponentPayload)
        }) != ClientSequenceDisposition.Accepted) return false;
    var projectedMeld = state.Snapshot.seats[0].publicMelds.SingleOrDefault();
    return projectedMeld != null
        && projectedMeld.isConcealed
        && projectedMeld.tileCount == 4
        && projectedMeld.tiles.Length == 4
        && projectedMeld.tiles.All(tile => tile.suit == (int)Suit.Pin && tile.value == 8);
}

bool VerifyClientGameProjection()
{
    var state = new ClientGameState();
    var baseline = new RoomGameSnapshot
    {
        roomId = "projection-room",
        requestingSeatIndex = 1,
        scores = new[] { 1, 2, 3, 4 },
        privateSeat = new SnapshotPrivateSeat
        {
            seatIndex = 1,
            concealedHand = new[] { new SimpleTileData(new TileData(Suit.Man, 2, 1)) }
        },
        activeDecision = new SnapshotDecision
        {
            decisionId = 41,
            phase = (int)NetworkDecisionPhase.Response,
            discardingSeatIndex = 0,
            eligibleSeats = new[] { 1, 2 },
            submittedSeats = new[] { 2 },
            deadlineUnixMilliseconds = 9000
        },
        result = new RoundResultSnapshot
        {
            winnerId = 3,
            fanCount = 16,
            fanDetails = new[] { "baseline-fan" },
            isSelfDraw = false,
            loserId = 0
        }
    };
    if (!state.ApplySnapshot(baseline, 10)) return false;

    baseline.privateSeat.concealedHand[0].value = 9;
    baseline.activeDecision.submittedSeats[0] = 3;
    if (state.Snapshot.privateSeat.concealedHand[0].value != 2
        || state.Snapshot.activeDecision.submittedSeats[0] != 2
        || state.Snapshot.result.winnerId != 3
        || state.Snapshot.activeDecision.decisionId != 41
        || state.LastSequence != 10) return false;

    if (state.ApplySnapshot(null, 11) || state.LastSequence != 10 || state.Snapshot.roomId != "projection-room") return false;

    var drawEnvelope = new NetworkMessageEnvelope
    {
        type = "TileDrawn",
        seq = 11,
        data = UnityEngine.JsonUtility.ToJson(new TileDrawnMessage
        {
            decisionId = 42,
            tile = new SimpleTileData(new TileData(Suit.Pin, 5, 1))
        })
    };
    if (state.ApplyEnvelope(drawEnvelope) != ClientSequenceDisposition.Accepted) return false;
    if (state.ApplyEnvelope(drawEnvelope) != ClientSequenceDisposition.IgnoredDuplicate) return false;
    var drawnProjection = state.Snapshot;
    if (drawnProjection.mainTurnDrawnTile?.suit != (int)Suit.Pin
        || drawnProjection.mainTurnDrawnTile?.value != 5) return false;

    var winEnvelope = new NetworkMessageEnvelope
    {
        type = "PlayerWin",
        seq = 12,
        data = UnityEngine.JsonUtility.ToJson(new PlayerWinMessage
        {
            winnerId = 1,
            totalFan = 24,
            fanDetails = new[] { "event-fan" },
            isSelfDraw = true,
            scores = new[] { 100, -20, -30, -50 }
        })
    };
    if (state.ApplyEnvelope(winEnvelope) != ClientSequenceDisposition.Accepted) return false;

    var projected = state.Snapshot;
    bool envelopeAndSnapshotRulesPass = projected.privateSeat.concealedHand.Length == 2
        && projected.activeDecision == null
        && projected.result.winnerId == 1
        && projected.result.fanCount == 24
        && projected.result.fanDetails.SequenceEqual(new[] { "event-fan" })
        && projected.result.isSelfDraw
        && projected.scores.SequenceEqual(new[] { 100, -20, -30, -50 })
        && projected.mainTurnDrawnTile == null
        && state.LastSequence == 12
        && !state.IsResyncRequired;
    if (!envelopeAndSnapshotRulesPass) return false;

    var selfDiscardState = new ClientGameState();
    if (!selfDiscardState.ApplySnapshot(new RoomGameSnapshot
        {
            requestingSeatIndex = 0,
            privateSeat = new SnapshotPrivateSeat
            {
                seatIndex = 0,
                concealedHand = new[] { new SimpleTileData(new TileData(Suit.Man, 7, 0)) }
            },
            rivers = CreateRivers()
        }, 20)) return false;
    if (selfDiscardState.ApplyEnvelope(new NetworkMessageEnvelope
        {
            type = "Discarded",
            seq = 21,
            data = UnityEngine.JsonUtility.ToJson(new DiscardedMessage
            {
                decisionId = 51,
                playerId = 0,
                tile = new SimpleTileData(new TileData(Suit.Man, 7, 0))
            })
        }) != ClientSequenceDisposition.Accepted) return false;
    var selfDiscardProjection = selfDiscardState.Snapshot;
    if (selfDiscardProjection.privateSeat.concealedHand.Length != 0
        || selfDiscardProjection.rivers[0].tiles.Length != 1
        || selfDiscardProjection.activeDecision != null) return false;

    var claimState = new ClientGameState();
    if (!claimState.ApplySnapshot(new RoomGameSnapshot
        {
            requestingSeatIndex = 1,
            seats = new[]
            {
                new RoomSnapshotSeat { seatIndex = 0, concealedTileCount = 13 },
                new RoomSnapshotSeat { seatIndex = 1, concealedTileCount = 2 },
                new RoomSnapshotSeat { seatIndex = 2 }, new RoomSnapshotSeat { seatIndex = 3 }
            },
            privateSeat = new SnapshotPrivateSeat
            {
                seatIndex = 1,
                concealedHand = new[]
                {
                    new SimpleTileData(new TileData(Suit.Man, 3, 1)),
                    new SimpleTileData(new TileData(Suit.Man, 3, 1))
                },
                melds = Array.Empty<SnapshotMeld>()
            },
            rivers = CreateRivers()
        }, 30)) return false;
    if (claimState.ApplyEnvelope(new NetworkMessageEnvelope
        {
            type = "Discarded",
            seq = 31,
            data = UnityEngine.JsonUtility.ToJson(new DiscardedMessage
            {
                decisionId = 61,
                playerId = 0,
                tile = new SimpleTileData(new TileData(Suit.Man, 3, 0)),
                decision = new SnapshotDecision
                {
                    decisionId = 61,
                    phase = (int)NetworkDecisionPhase.Response,
                    discardingSeatIndex = 0,
                    eligibleSeats = new[] { 1, 2, 3 },
                    submittedSeats = new[] { 2 },
                    controllerSeatIndex = -1,
                    deadlineUnixMilliseconds = 5000
                }
            })
        }) != ClientSequenceDisposition.Accepted) return false;
    var activeClaimDecision = claimState.Snapshot.activeDecision;
    if (activeClaimDecision == null
        || !activeClaimDecision.eligibleSeats.SequenceEqual(new[] { 1, 2, 3 })
        || !activeClaimDecision.submittedSeats.SequenceEqual(new[] { 2 })
        || activeClaimDecision.deadlineUnixMilliseconds != 5000) return false;
    if (claimState.ApplyEnvelope(new NetworkMessageEnvelope
        {
            type = "ActionResolved",
            seq = 32,
            data = UnityEngine.JsonUtility.ToJson(new ActionResolvedMessage
            {
                playerId = 1,
                actionType = (int)ClientActionType.Pon,
                tile = new SimpleTileData(new TileData(Suit.Man, 3, 0))
            })
        }) != ClientSequenceDisposition.Accepted) return false;
    var claimProjection = claimState.Snapshot;
    bool ponProjectionPasses = claimProjection.rivers[0].tiles.Length == 0
        && claimProjection.privateSeat.concealedHand.Length == 0
        && claimProjection.privateSeat.melds.Length == 1
        && claimProjection.seats[1].publicMelds.Length == 1
        && claimProjection.seats[1].concealedTileCount == 0;
    if (!ponProjectionPasses) return false;

    bool VerifyProjectedMeld(ClientActionType actionType, int targetValue, int[] handValues, int[] chiValues = null, bool withExistingPon = false)
    {
        var target = new SimpleTileData(new TileData(Suit.Man, targetValue, 0));
        var existingPon = new SnapshotMeld
        {
            meldType = (int)MeldType.Pon,
            tileCount = 3,
            tiles = Enumerable.Range(0, 3).Select(_ => new SimpleTileData(new TileData(Suit.Man, targetValue, 1))).ToArray()
        };
        var projectedMeldState = new ClientGameState();
        if (!projectedMeldState.ApplySnapshot(new RoomGameSnapshot
            {
                requestingSeatIndex = 1,
                seats = new[]
                {
                    new RoomSnapshotSeat { seatIndex = 0 },
                    new RoomSnapshotSeat { seatIndex = 1, concealedTileCount = handValues.Length, publicMelds = withExistingPon ? new[] { existingPon } : Array.Empty<SnapshotMeld>() },
                    new RoomSnapshotSeat { seatIndex = 2 }, new RoomSnapshotSeat { seatIndex = 3 }
                },
                privateSeat = new SnapshotPrivateSeat
                {
                    seatIndex = 1,
                    concealedHand = handValues.Select(value => new SimpleTileData(new TileData(Suit.Man, value, 1))).ToArray(),
                    melds = withExistingPon ? new[] { existingPon } : Array.Empty<SnapshotMeld>()
                },
                rivers = CreateRivers(
                    actionType == ClientActionType.AnGan || actionType == ClientActionType.JiaGang
                        ? Array.Empty<SimpleTileData>()
                        : new[] { target },
                    Array.Empty<SimpleTileData>(), Array.Empty<SimpleTileData>(), Array.Empty<SimpleTileData>()),
                activeDecision = actionType == ClientActionType.AnGan || actionType == ClientActionType.JiaGang ? null : new SnapshotDecision { discardingSeatIndex = 0 }
            }, 40)) return false;
        if (projectedMeldState.ApplyEnvelope(new NetworkMessageEnvelope
            {
                type = "ActionResolved",
                seq = 41,
                data = UnityEngine.JsonUtility.ToJson(new ActionResolvedMessage
                {
                    playerId = 1,
                    actionType = (int)actionType,
                    tile = target,
                    chiCombinations = chiValues
                })
            }) != ClientSequenceDisposition.Accepted) return false;
        var projection = projectedMeldState.Snapshot;
        int expectedMeldType = actionType == ClientActionType.Chi ? (int)MeldType.Chi
            : actionType == ClientActionType.MingGan ? (int)MeldType.Kan_Exposed
            : actionType == ClientActionType.AnGan ? (int)MeldType.Kan_Concealed
            : (int)MeldType.Kan_Added;
        return projection.privateSeat.concealedHand.Length == 0
            && projection.privateSeat.melds.Length == 1
            && projection.privateSeat.melds[0].meldType == expectedMeldType
            && projection.seats[1].publicMelds.Length == 1
            && projection.seats[1].publicMelds[0].meldType == expectedMeldType
            && projection.seats[1].concealedTileCount == 0
            && projection.rivers[0].tiles.Length == 0;
    }

    return VerifyProjectedMeld(ClientActionType.Chi, 3, new[] { 1, 2 }, new[] { 1, 2 })
        && VerifyProjectedMeld(ClientActionType.MingGan, 5, new[] { 5, 5, 5 })
        && VerifyProjectedMeld(ClientActionType.AnGan, 6, new[] { 6, 6, 6, 6 })
        && VerifyProjectedMeld(ClientActionType.JiaGang, 7, new[] { 7 }, null, true);
}

bool VerifyJsonSafeRiverSnapshotContract()
{
    var riversField = typeof(RoomGameSnapshot).GetField("rivers");
    if (riversField?.FieldType.IsArray != true) return false;
    var riverType = riversField.FieldType.GetElementType();
    if (riverType?.Name != "SeatRiverSnapshot") return false;
    var seatIndexField = riverType.GetField("seatIndex");
    var tilesField = riverType.GetField("tiles");
    if (seatIndexField == null || tilesField == null) return false;

    var rivers = Array.CreateInstance(riverType, 4);
    for (int index = 0; index < rivers.Length; index++)
    {
        var river = Activator.CreateInstance(riverType);
        seatIndexField.SetValue(river, index);
        tilesField.SetValue(river, index == 0
            ? new[] { new SimpleTileData(new TileData(Suit.Man, 4, 0)) }
            : Array.Empty<SimpleTileData>());
        rivers.SetValue(river, index);
    }
    var source = new RoomGameSnapshot { roomId = "river-json-room" };
    riversField.SetValue(source, rivers);
    var envelope = MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize("RoomSnapshot", 7, source));
    var restored = envelope == null ? null : MessageSerializer.DeserializePayload<RoomGameSnapshot>(envelope.data);
    var restoredRivers = restored == null ? null : riversField.GetValue(restored) as Array;
    var firstRiver = restoredRivers != null && restoredRivers.Length == 4 ? restoredRivers.GetValue(0) : null;
    var tiles = firstRiver == null ? null : tilesField.GetValue(firstRiver) as SimpleTileData[];
    return firstRiver != null
        && (int)seatIndexField.GetValue(firstRiver) == 0
        && tiles?.Length == 1
        && tiles[0]?.suit == (int)Suit.Man
        && tiles[0]?.value == 4;
}

bool VerifyCompletedEastOnlySnapshotProjection()
{
    var session = new GameSession(GameMode.EastOnly);
    for (int round = 0; round < 4; round++) session.AdvanceRound();

    var snapshot = RoomGameSnapshotBuilder.Build(new RoomGameSnapshotSource
    {
        RoomId = "east-final",
        RoomState = RoomState.SessionCompleted,
        GameMode = GameMode.EastOnly,
        Session = session,
        Seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeatSource
        {
            SeatIndex = index,
            IsOccupied = true,
            IsOnline = true,
            Controller = "OnlineHuman"
        }).ToArray(),
        Hands = Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray(),
        Melds = Enumerable.Range(0, 4).Select(_ => new List<Meld>()).ToArray(),
        Rivers = Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray(),
        ScoringOptions = new ScoringOptions[4],
        PeekWallTiles = Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray()
    }, 3);

    return snapshot.roundNumber == 4
        && snapshot.prevalentWind == (int)WindDirection.East
        && snapshot.dealerIndex == 3
        && snapshot.requestingSeatWind == (int)WindDirection.East
        && snapshot.result?.isSessionOver == true;
}

bool VerifyPerSeatSnapshotPrivacy()
{
    var session = new GameSession(GameMode.EastOnly)
    {
        DealerIndex = 2,
        TotalRoundsPlayed = 1,
        Scores = new[] { 10, 20, 30, 40 }
    };
    var opponentConcealedKan = new Meld(
        MeldType.Kan_Concealed,
        Enumerable.Range(0, 4).Select(_ => new TileData(Suit.Pin, 9, 1)).ToList(),
        1,
        true);
    var decisionTracker = new NetworkDecisionTracker();
    var decision = decisionTracker.OpenResponse(0, new TileData(Suit.Sou, 3, 0), new[] { 1, 2, 3 }, 123456789L);
    var source = new RoomGameSnapshotSource
    {
        RoomId = "snapshot-room",
        RoomState = RoomState.InRound,
        GameMode = GameMode.EastOnly,
        Session = session,
        Seats = Enumerable.Range(0, 4).Select(seat => new RoomSnapshotSeatSource
        {
            SeatIndex = seat,
            IsOccupied = true,
            IsAi = seat == 3,
            IsOnline = seat != 3,
            DisplayName = $"Seat {seat}"
        }).ToArray(),
        Hands = new[]
        {
            new List<TileData> { new TileData(Suit.Man, 1, 0), new TileData(Suit.Man, 2, 0) },
            new List<TileData> { new TileData(Suit.Pin, 9, 1), new TileData(Suit.Pin, 8, 1) },
            new List<TileData> { new TileData(Suit.Sou, 7, 2) },
            new List<TileData> { new TileData(Suit.Wind, 1, 3) }
        },
        Melds = new[]
        {
            new List<Meld>(),
            new List<Meld> { opponentConcealedKan },
            new List<Meld>(),
            new List<Meld>()
        },
        Rivers = new[]
        {
            new List<TileData> { new TileData(Suit.Man, 3, 0) },
            new List<TileData> { new TileData(Suit.Pin, 4, 1) },
            new List<TileData>(),
            new List<TileData>()
        },
        RemainingWallCount = 67,
        ScoringOptions = new[]
        {
            new ScoringOptions { BonusFan = 2, RelaxedPureStraight = true },
            new ScoringOptions { BonusFan = 99, RelaxedPureStraight = false },
            new ScoringOptions(),
            new ScoringOptions()
        },
        PeekWallTiles = new[]
        {
            new List<TileData> { new TileData(Suit.Sou, 1, 0) },
            new List<TileData> { new TileData(Suit.Pin, 9, 1) },
            new List<TileData>(),
            new List<TileData>()
        },
        ActiveDecision = decision,
        WinnerId = -1,
        LoserId = -1,
        FanDetails = new[] { "snapshot-fan" }
    };

    var snapshot = RoomGameSnapshotBuilder.Build(source, 0);
    var exposedTiles = snapshot.privateSeat.concealedHand
        .Concat(snapshot.privateSeat.peekWallTiles)
        .Concat(snapshot.rivers.SelectMany(river => river?.tiles ?? Array.Empty<SimpleTileData>()))
        .Concat(snapshot.seats.SelectMany(seat => seat.publicMelds ?? Array.Empty<SnapshotMeld>())
            .SelectMany(meld => meld.tiles ?? Array.Empty<SimpleTileData>()))
        .Concat(snapshot.activeDecision.targetTile == null ? Array.Empty<SimpleTileData>() : new[] { snapshot.activeDecision.targetTile })
        .ToArray();
    var snapshotDtoTypes = new[]
    {
        typeof(RoomGameSnapshot), typeof(SeatRiverSnapshot), typeof(RoomSnapshotSeat), typeof(SnapshotPrivateSeat),
        typeof(SnapshotMeld), typeof(SnapshotScoringOptions), typeof(SnapshotDecision), typeof(RoundResultSnapshot)
    };
    var mainTurnTracker = new NetworkDecisionTracker();
    source.ActiveDecision = mainTurnTracker.OpenMainTurn(0, 123456790L);
    source.MainTurnDrawnTile = new TileData(Suit.Pin, 8, 0);
    var localMainTurnSnapshot = RoomGameSnapshotBuilder.Build(source, 0);
    var otherMainTurnSnapshot = RoomGameSnapshotBuilder.Build(source, 1);
    return snapshot.roomId == "snapshot-room"
        && snapshot.roundNumber == 2
        && snapshot.requestingSeatWind == (int)session.GetSeatWind(0)
        && snapshot.scores.SequenceEqual(new[] { 10, 20, 30, 40 })
        && snapshot.privateSeat.concealedHand.Length == 2
        && snapshot.privateSeat.concealedHand.All(tile => tile.suit == (int)Suit.Man)
        && snapshot.privateSeat.scoringOptions.bonusFan == 2
        && snapshot.privateSeat.scoringOptions.relaxedPureStraight
        && snapshot.privateSeat.peekWallTiles.Length == 1
        && snapshot.privateSeat.peekWallTiles[0].suit == (int)Suit.Sou
        && snapshot.seats[1].concealedTileCount == 2
        && snapshot.seats[1].publicMelds.Length == 1
        && snapshot.seats[1].publicMelds[0].isConcealed
        && snapshot.seats[1].publicMelds[0].tileCount == 4
        && snapshot.seats[1].publicMelds[0].tiles.Length == 4
        && snapshot.seats[1].publicMelds[0].tiles.All(tile => tile.suit == (int)Suit.Pin && tile.value == 9)
        && snapshot.rivers[0].tiles.Length == 1
        && snapshot.remainingWallCount == 67
        && snapshot.activeDecision.decisionId == decision.DecisionId
        && snapshot.activeDecision.discardingSeatIndex == 0
        && snapshot.activeDecision.deadlineUnixMilliseconds == 123456789L
        && snapshot.result.fanDetails.SequenceEqual(new[] { "snapshot-fan" })
        && !exposedTiles.Any(tile => tile.suit == (int)Suit.Pin && tile.value == 8)
        && localMainTurnSnapshot.mainTurnDrawnTile?.suit == (int)Suit.Pin
        && localMainTurnSnapshot.mainTurnDrawnTile?.value == 8
        && otherMainTurnSnapshot.mainTurnDrawnTile == null
        && !snapshotDtoTypes.SelectMany(type => type.GetFields())
            .Any(field => field.FieldType == typeof(DeckConfig) || field.FieldType == typeof(TalentSlotConfig));
}

bool VerifyAuthoritativeTableState()
{
    var stateType = typeof(ServerGameState);
    var recordDiscard = stateType.GetMethod("RecordDiscard");
    var claimDiscard = stateType.GetMethod("TryClaimDiscard");
    var getRiver = stateType.GetMethod("GetRiver");
    if (recordDiscard == null || claimDiscard == null || getRiver == null) return false;

    bool VerifyClaim(ClientActionType actionType, int discardedValue, int[] claimantValues, int[] chiValues = null)
    {
        var state = new ServerGameState(2);
        var discarded = new TileData(Suit.Man, discardedValue, 0);
        state.InitHand(0, new List<TileData> { discarded });
        state.InitHand(1, claimantValues.Select(value => new TileData(Suit.Man, value, 1)).ToList());
        state.RemoveTile(0, discarded);
        recordDiscard.Invoke(state, new object[] { 0, discarded });
        if (getRiver.Invoke(state, new object[] { 0 }) is not List<TileData> riverBeforeClaim || riverBeforeClaim.Count != 1) return false;
        if (claimDiscard.Invoke(state, new object[] { 0, discarded }) is not bool claimed || !claimed) return false;

        state.ApplyMeld(1, actionType, discarded, chiValues);
        return getRiver.Invoke(state, new object[] { 0 }) is List<TileData> riverAfterClaim
            && riverAfterClaim.Count == 0
            && state.GetHand(1).Count == 0
            && state.GetMelds(1).Count == 1;
    }

    if (!VerifyClaim(ClientActionType.Chi, 3, new[] { 2, 4 }, new[] { 2, 4 })
        || !VerifyClaim(ClientActionType.Pon, 5, new[] { 5, 5 })
        || !VerifyClaim(ClientActionType.MingGan, 6, new[] { 6, 6, 6 })) return false;

    var copyState = new ServerGameState(2);
    var handTile = new TileData(Suit.Pin, 7, 0);
    copyState.InitHand(0, new List<TileData> { handTile });
    var returnedHand = copyState.GetHand(0);
    returnedHand[0].Value = 1;
    if (copyState.GetHand(0)[0].Value != 7) return false;

    copyState.ApplyMeld(0, ClientActionType.AnGan, handTile, null);
    var returnedMelds = copyState.GetMelds(0);
    returnedMelds[0].Tiles.Clear();
    if (copyState.GetMelds(0)[0].Tiles.Count != 4) return false;

    recordDiscard.Invoke(copyState, new object[] { 0, handTile });
    var returnedRiver = getRiver.Invoke(copyState, new object[] { 0 }) as List<TileData>;
    if (returnedRiver == null || returnedRiver.Count != 1) return false;
    returnedRiver[0].Value = 9;
    return (getRiver.Invoke(copyState, new object[] { 0 }) as List<TileData>)?[0].Value == 7;
}

bool VerifyNetworkDecisionTracker()
{
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.NetworkDecisionTracker");
    var contextType = protocolAssembly.GetType("MahjongGame.Core.Network.NetworkDecisionContext");
    var openMain = type?.GetMethod("OpenMainTurn");
    var openResponse = type?.GetMethod("OpenResponse");
    var trySubmit = type?.GetMethod("TrySubmitNetworkAction");
    var close = type?.GetMethod("Close");
    var active = type?.GetProperty("Active");
    var decisionId = contextType?.GetProperty("DecisionId");
    var submittedSeats = contextType?.GetProperty("SubmittedSeats");
    if (type == null || contextType == null || openMain == null || openResponse == null || trySubmit == null || close == null || active == null || decisionId == null || submittedSeats == null) return false;

    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    object tracker = Activator.CreateInstance(type);
    object main = openMain.Invoke(tracker, new object[] { 2, now + 10000L });
    if (main == null || decisionId.GetValue(main) is not long firstId || firstId != 1) return false;

    bool TrySubmit(long id, int seat, ClientActionType actionType, string expectedError, bool expectedResult)
    {
        var args = new object[] { id, seat, actionType, null };
        bool accepted = trySubmit.Invoke(tracker, args) is bool result && result;
        return accepted == expectedResult && (args[3] as string) == expectedError;
    }

    if (!TrySubmit(firstId, 1, ClientActionType.Discard, "WrongController", false)
        || !TrySubmit(firstId, 2, ClientActionType.Pon, "WrongPhase", false)
        || !TrySubmit(firstId, 2, ClientActionType.Discard, null, true)
        || !TrySubmit(firstId, 2, ClientActionType.Discard, "DuplicateAction", false)) return false;
    if (submittedSeats.GetValue(active.GetValue(tracker)) is not int[] mainSubmitted || mainSubmitted.Length != 1 || mainSubmitted[0] != 2) return false;
    if (close.Invoke(tracker, new object[] { firstId }) is not bool mainClosed || !mainClosed) return false;

    object response = openResponse.Invoke(tracker, new object[] { 0, new TileData(Suit.Man, 3, 0), new[] { 1, 2, 3 }, now + 10000L });
    if (response == null || decisionId.GetValue(response) is not long secondId || secondId != 2) return false;
    if (!TrySubmit(firstId, 1, ClientActionType.Skip, "StaleDecision", false)
        || !TrySubmit(secondId, 0, ClientActionType.Skip, "NotEligible", false)
        || !TrySubmit(secondId, 1, ClientActionType.Discard, "WrongPhase", false)
        || !TrySubmit(secondId, 1, ClientActionType.Skip, null, true)) return false;

    if (close.Invoke(tracker, new object[] { secondId }) is not bool responseClosed || !responseClosed || active.GetValue(tracker) != null) return false;

    object expiredTracker = Activator.CreateInstance(type);
    object expired = openMain.Invoke(expiredTracker, new object[] { 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    if (expired == null || decisionId.GetValue(expired) is not long expiredId) return false;
    var expiredArgs = new object[] { expiredId, 0, ClientActionType.Discard, null };
    return trySubmit.Invoke(expiredTracker, expiredArgs) is bool expiredRejected
        && !expiredRejected
        && (expiredArgs[3] as string) == "DecisionExpired";
}

bool VerifyReconnectProtocolContract()
{
    bool HasExactlyFields(Type type, params string[] fields)
    {
        if (type == null) return false;
        var actual = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(field => field.Name).OrderBy(name => name).ToArray();
        return actual.SequenceEqual(fields.OrderBy(name => name));
    }

    var reconnect = protocolAssembly.GetType("MahjongGame.Core.Network.Messages.ReconnectMessage");
    var resync = protocolAssembly.GetType("MahjongGame.Core.Network.Messages.ResyncMessage");
    var reconnectState = protocolAssembly.GetType("MahjongGame.Core.Network.Messages.ReconnectStateMessage");
    var rejected = protocolAssembly.GetType("MahjongGame.Core.Network.Messages.ReconnectRejectedMessage");

    return HasExactlyFields(reconnect, "roomId", "streamId", "lastSeq", "hasProjection")
        && HasExactlyFields(resync, "roomId", "streamId", "lastSeq")
        && HasExactlyFields(reconnectState, "baselineSeq", "snapshot", "missedMessages")
        && HasExactlyFields(rejected, "code", "message")
        && reconnect.GetField("seatIndex") == null
        && resync.GetField("seatIndex") == null;
}

bool VerifyReconnectRecoveryStream()
{
    var streamType = protocolAssembly.GetType("MahjongGame.Core.Network.SeatMessageStream");
    var streamId = streamType?.GetProperty("StreamId");
    var send = streamType?.GetMethod("Send");
    var deliverRecovery = streamType?.GetMethod("DeliverReconnectState");
    if (streamType == null || streamId == null || send == null || deliverRecovery == null) return false;

    var firstEndpoint = new MahjongGame.Core.Network.Transport.GameEndpoint();
    var stream = Activator.CreateInstance(streamType, new object[] { firstEndpoint, 2 });
    send.Invoke(stream, new object[] { "One", new WallCountMessage { remainingCount = 30 } });
    send.Invoke(stream, new object[] { "Two", new WallCountMessage { remainingCount = 29 } });
    if (streamId.GetValue(stream) is not string firstStreamId || string.IsNullOrWhiteSpace(firstStreamId)) return false;

    bool replayRequestedSnapshot = false;
    var replayEndpoint = new MahjongGame.Core.Network.Transport.GameEndpoint();
    var replay = deliverRecovery.Invoke(stream, new object[]
    {
        replayEndpoint,
        1,
        true,
        new Func<RoomGameSnapshot>(() =>
        {
            replayRequestedSnapshot = true;
            return new RoomGameSnapshot { roomId = "must-not-build" };
        })
    });
    if (replay == null || replayRequestedSnapshot || streamId.GetValue(stream) as string != firstStreamId) return false;

    var replayType = replay.GetType();
    if (replayType.GetField("baselineSeq")?.GetValue(replay) is not int replayBaseline || replayBaseline != 1
        || replayType.GetField("snapshot")?.GetValue(replay) != null
        || replayType.GetField("missedMessages")?.GetValue(replay) is not NetworkMessageEnvelope[] replayed
        || replayed.Length != 1 || replayed[0].seq != 2
        || replayEndpoint.SentMessages.Count != 1
        || MessageSerializer.DeserializeEnvelope(replayEndpoint.SentMessages[0])?.type != "ReconnectState") return false;

    var snapshotEndpoint = new MahjongGame.Core.Network.Transport.GameEndpoint();
    var snapshotStream = Activator.CreateInstance(streamType, new object[] { snapshotEndpoint, 2 });
    send.Invoke(snapshotStream, new object[] { "One", new WallCountMessage { remainingCount = 30 } });
    send.Invoke(snapshotStream, new object[] { "Two", new WallCountMessage { remainingCount = 29 } });
    send.Invoke(snapshotStream, new object[] { "Three", new WallCountMessage { remainingCount = 28 } });

    var restoredEndpoint = new MahjongGame.Core.Network.Transport.GameEndpoint();
    var full = deliverRecovery.Invoke(snapshotStream, new object[]
    {
        restoredEndpoint,
        0,
        true,
        new Func<RoomGameSnapshot>(() =>
        {
            send.Invoke(snapshotStream, new object[] { "DuringRecovery", new WallCountMessage { remainingCount = 27 } });
            return new RoomGameSnapshot { roomId = "snapshot-room" };
        })
    });
    if (full == null
        || full.GetType().GetField("baselineSeq")?.GetValue(full) is not int fullBaseline || fullBaseline != 3
        || full.GetType().GetField("snapshot")?.GetValue(full) is not RoomGameSnapshot snapshot || snapshot.roomId != "snapshot-room"
        || full.GetType().GetField("missedMessages")?.GetValue(full) is not NetworkMessageEnvelope[] missed || missed.Length != 0
        || restoredEndpoint.SentMessages.Count != 2) return false;

    var recoveryEnvelope = MessageSerializer.DeserializeEnvelope(restoredEndpoint.SentMessages[0]);
    var flushedEnvelope = MessageSerializer.DeserializeEnvelope(restoredEndpoint.SentMessages[1]);
    return recoveryEnvelope?.type == "ReconnectState" && recoveryEnvelope.seq == 0
        && flushedEnvelope?.type == "DuringRecovery" && flushedEnvelope.seq == 4;
}

bool VerifyRoomLifecyclePolicy()
{
    var lifecycle = protocolAssembly.GetType("MahjongGame.Core.Network.RoomLifecyclePolicy");
    var decisionController = lifecycle?.GetMethod("SelectDecisionController");
    var disconnect = lifecycle?.GetMethod("GetDisconnectDisposition");
    var expiry = lifecycle?.GetMethod("GetExpiryDisposition");
    var closeForOfflineHumans = lifecycle?.GetMethod("ShouldCloseWhenNoHumanOnline");
    var autoReady = lifecycle?.GetMethod("ShouldAutoReadyOfflineSeat");
    if (lifecycle == null || decisionController == null || disconnect == null || expiry == null
        || closeForOfflineHumans == null || autoReady == null
        || !Enum.GetNames(typeof(RoomState)).Contains("SessionCompleted")) return false;

    return decisionController.Invoke(null, new object[] { false, true })?.ToString() == "Human"
        && decisionController.Invoke(null, new object[] { false, false })?.ToString() == "AI"
        && decisionController.Invoke(null, new object[] { true, false })?.ToString() == "Human"
        && disconnect.Invoke(null, new object[] { RoomState.WaitingForMatchReady, true })?.ToString() == "OfflineReserved"
        && disconnect.Invoke(null, new object[] { RoomState.InRound, true })?.ToString() == "OfflineReserved"
        && disconnect.Invoke(null, new object[] { RoomState.InRound, false })?.ToString() == "CloseRoom"
        && expiry.Invoke(null, new object[] { RoomState.WaitingForMatchReady })?.ToString() == "Vacant"
        && expiry.Invoke(null, new object[] { RoomState.InRound })?.ToString() == "PermanentAi"
        && expiry.Invoke(null, new object[] { RoomState.WaitingForNextRound })?.ToString() == "PermanentAi"
        && autoReady.Invoke(null, new object[] { RoomState.WaitingForNextRound }) is bool shouldAutoReady && shouldAutoReady
        && closeForOfflineHumans.Invoke(null, new object[] { 0 }) is bool shouldClose && shouldClose
        && closeForOfflineHumans.Invoke(null, new object[] { 1 }) is bool shouldStayOpen && !shouldStayOpen;
}

bool VerifySeatDecisionControlLatch()
{
    var latchType = protocolAssembly.GetType("MahjongGame.Core.Network.SeatDecisionControlLatch");
    var open = latchType?.GetMethod("OpenDecision");
    var markOffline = latchType?.GetMethod("MarkOffline");
    var markOnline = latchType?.GetMethod("MarkOnline");
    var close = latchType?.GetMethod("CloseDecision");
    var humanAllowed = latchType?.GetMethod("IsHumanSubmissionAllowed");
    if (latchType == null || open == null || markOffline == null || markOnline == null || close == null || humanAllowed == null) return false;

    object latch = Activator.CreateInstance(latchType);
    if (open.Invoke(latch, new object[] { 11L, true })?.ToString() != "Human") return false;
    markOffline.Invoke(latch, Array.Empty<object>());
    if (humanAllowed.Invoke(latch, new object[] { 11L }) is not bool humanKeepsOpenDecision || !humanKeepsOpenDecision) return false;
    close.Invoke(latch, new object[] { 11L });

    if (open.Invoke(latch, new object[] { 12L, false })?.ToString() != "AI") return false;
    markOnline.Invoke(latch, Array.Empty<object>());
    if (humanAllowed.Invoke(latch, new object[] { 12L }) is not bool humanCannotTakeOverAiDecision || humanCannotTakeOverAiDecision) return false;
    close.Invoke(latch, new object[] { 12L });

    return open.Invoke(latch, new object[] { 13L, true })?.ToString() == "Human"
        && humanAllowed.Invoke(latch, new object[] { 13L }) is bool humanOwnsNextBoundary && humanOwnsNextBoundary;
}

bool VerifyReconnectBaselineSequenceGate()
{
    var gateType = protocolAssembly.GetType("MahjongGame.Core.Network.ClientSequenceGate");
    var restore = gateType?.GetMethod("RestoreBaseline");
    var apply = gateType?.GetMethod("Apply");
    var lastSequence = gateType?.GetProperty("LastSequence");
    if (gateType == null || restore == null || apply == null || lastSequence == null) return false;

    object gate = Activator.CreateInstance(gateType);
    restore.Invoke(gate, new object[] { 41 });
    return lastSequence.GetValue(gate) is int baseline && baseline == 41
        && apply.Invoke(gate, new object[] { 41 })?.ToString() == "IgnoredDuplicate"
        && apply.Invoke(gate, new object[] { 42 })?.ToString() == "Accepted";
}

bool VerifyClientReconnectTicketPolicy()
{
    var ticketType = protocolAssembly.GetType("MahjongGame.Core.Network.ClientReconnectTicket");
    var storeType = protocolAssembly.GetType("MahjongGame.Core.Network.InMemoryClientReconnectTicketStore");
    var policyType = protocolAssembly.GetType("MahjongGame.Core.Network.ClientReconnectTicketPolicy");
    var shouldClearForError = policyType?.GetMethod("ShouldClearForRoomError");
    var shouldClearForFinalExit = policyType?.GetMethod("ShouldClearForFinalResultExit");
    var save = storeType?.GetMethod("Save");
    var tryLoad = storeType?.GetMethod("TryLoad");
    var clear = storeType?.GetMethod("Clear");
    if (ticketType == null || storeType == null || policyType == null || shouldClearForError == null
        || shouldClearForFinalExit == null || save == null || tryLoad == null || clear == null) return false;

    var ticketFields = ticketType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
        .Select(field => field.Name).OrderBy(name => name).ToArray();
    if (!ticketFields.SequenceEqual(new[] { "roomId", "serverAddress", "streamId", "username" })) return false;

    object ticket = Activator.CreateInstance(ticketType);
    ticketType.GetField("serverAddress")?.SetValue(ticket, "ws://127.0.0.1:9876/game");
    ticketType.GetField("username")?.SetValue(ticket, "Alice");
    ticketType.GetField("roomId")?.SetValue(ticket, "R0001");
    ticketType.GetField("streamId")?.SetValue(ticket, "stream-1");

    object store = Activator.CreateInstance(storeType);
    save.Invoke(store, new[] { ticket });
    var loadArgs = new object[] { null };
    if (tryLoad.Invoke(store, loadArgs) is not bool loaded || !loaded || loadArgs[0] == null
        || ticketType.GetField("roomId")?.GetValue(loadArgs[0]) as string != "R0001"
        || ticketType.GetField("streamId")?.GetValue(loadArgs[0]) as string != "stream-1") return false;

    clear.Invoke(store, Array.Empty<object>());
    if (tryLoad.Invoke(store, new object[] { null }) is not bool cleared || cleared) return false;

    return shouldClearForError.Invoke(null, new object[] { "RoomNotFound" }) is bool clearForMissing && clearForMissing
        && shouldClearForError.Invoke(null, new object[] { "SeatExpired" }) is bool clearForExpired && clearForExpired
        && shouldClearForError.Invoke(null, new object[] { "IdentityInUse" }) is bool retainForRetry && !retainForRetry
        && shouldClearForFinalExit.Invoke(null, Array.Empty<object>()) is bool clearForFinalExit && clearForFinalExit;
}

bool VerifyLoginReconnectTicketIdentityPolicy()
{
    var policyType = protocolAssembly.GetType("MahjongGame.Core.Network.ClientReconnectTicketPolicy");
    var matchesUsername = policyType?.GetMethod("MatchesUsername");
    if (matchesUsername == null) return false;

    var ticket = new ClientReconnectTicket
    {
        username = "Alice",
        serverAddress = "ws://127.0.0.1:9876/game",
        roomId = "R0001",
        streamId = "stream-a"
    };

    return matchesUsername.Invoke(null, new object[] { ticket, " alice " }) is bool sameIdentity && sameIdentity
        && matchesUsername.Invoke(null, new object[] { ticket, "Bob" }) is bool otherIdentity && !otherIdentity;
}

bool VerifyAutomaticLoginReconnectPolicy()
{
    var policyType = protocolAssembly.GetType("MahjongGame.Core.Network.ClientReconnectTicketPolicy");
    var shouldAutoReconnect = policyType?.GetMethod("ShouldAutoReconnectAfterLogin");
    if (shouldAutoReconnect == null) return false;

    var ticket = new ClientReconnectTicket { username = "Alice", roomId = "R0001", streamId = "stream-a" };
    return shouldAutoReconnect.Invoke(null, new object[] { ticket, "ALICE" }) is bool matchingLogin && matchingLogin
        && shouldAutoReconnect.Invoke(null, new object[] { ticket, "Bob" }) is bool differentLogin && !differentLogin;
}

bool VerifySnapshotFirstReconnectPolicy()
{
    var policyType = protocolAssembly.GetType("MahjongGame.Core.Network.ClientReconnectRecoveryPolicy");
    var shouldUseCachedProjection = policyType?.GetMethod("ShouldUseCachedProjection");
    return shouldUseCachedProjection?.Invoke(null, Array.Empty<object>()) is bool useCachedProjection
        && !useCachedProjection;
}

bool VerifyReservedSeatMembershipPolicy()
{
    var policyType = protocolAssembly.GetType("MahjongGame.Core.Network.RoomMembershipPolicy");
    var requiresReconnect = policyType?.GetMethod("RequiresReconnect");
    var requiresDisconnectedHumanReconnect = policyType?.GetMethod("RequiresReconnectForDisconnectedHumanSeat");
    if (requiresReconnect == null || requiresDisconnectedHumanReconnect == null) return false;

    return requiresReconnect.Invoke(null, new object[] { new[] { "Alice", "Bob" }, " alice " }) is bool duplicateReservation && duplicateReservation
        && requiresReconnect.Invoke(null, new object[] { new[] { "Alice", "Bob" }, "Carol" }) is bool unrelatedIdentity && !unrelatedIdentity
        && requiresReconnect.Invoke(null, new object[] { Array.Empty<string>(), "Alice" }) is bool noReservation && !noReservation
        && requiresDisconnectedHumanReconnect.Invoke(null, new object[] { false, false, "Alice", " alice " }) is bool offlineHumanReservation && offlineHumanReservation
        && requiresDisconnectedHumanReconnect.Invoke(null, new object[] { false, true, "Alice", "Alice" }) is bool onlineHumanIsNotReservation && !onlineHumanIsNotReservation
        && requiresDisconnectedHumanReconnect.Invoke(null, new object[] { true, false, "Alice", "Alice" }) is bool permanentAiIsNotReservation && !permanentAiIsNotReservation;
}

bool VerifyRecoveryPresentationContracts()
{
    string root = Directory.GetCurrentDirectory();
    string localPlayerPath = Path.Combine(root, "Assets", "Scripts", "Core", "Agents", "LocalPlayerClient.cs");
    string snapshotPath = Path.Combine(root, "Assets", "Scripts", "Core", "Network", "RoomGameSnapshot.cs");
    string roomPath = Path.Combine(root, "Assets", "Scripts", "Core", "Network", "Room.cs");
    string gameServerPath = Path.Combine(root, "Assets", "Scripts", "Core", "Network", "GameServer.cs");
    if (!new[] { localPlayerPath, snapshotPath, roomPath, gameServerPath }.All(File.Exists)) return false;

    string localPlayer = File.ReadAllText(localPlayerPath);
    string snapshot = File.ReadAllText(snapshotPath);
    string room = File.ReadAllText(roomPath);
    string gameServer = File.ReadAllText(gameServerPath);
    return snapshot.Contains("public SimpleTileData mainTurnDrawnTile;", StringComparison.Ordinal)
        && room.Contains("MainTurnDrawnTile = GameServer?.LastDrawnTile", StringComparison.Ordinal)
        && gameServer.Contains("public TileData LastDrawnTile", StringComparison.Ordinal)
        && localPlayer.Contains("ResumeMainTurnDecision(snapshot.mainTurnDrawnTile?.ToTileData(), remainingSeconds);", StringComparison.Ordinal)
        && localPlayer.Contains("private async Task BeginMainTurnDecision", StringComparison.Ordinal)
        && localPlayer.Contains("ActionValidator.CheckSelfActions", StringComparison.Ordinal)
        && localPlayer.Contains("_lastDiscarderId = activeDecision.discardingSeatIndex;", StringComparison.Ordinal)
        && localPlayer.Contains("_lastDiscarderId = discarderId;", StringComparison.Ordinal);
}

bool VerifyRecoverySnapshotWithoutDecisionContract()
{
    string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts", "Core", "Agents", "LocalPlayerClient.cs");
    if (!File.Exists(path)) return false;

    string source = File.ReadAllText(path);
    return source.Contains("var activeDecision = snapshot.activeDecision;", StringComparison.Ordinal)
        && source.Contains("if (activeDecision == null || !ClientRecoveryInputPolicy.CanRestoreInput(activeDecision, localSeat, PlayerId, now)) return;", StringComparison.Ordinal)
        && !source.Contains("(NetworkDecisionPhase)snapshot.activeDecision?.phase", StringComparison.Ordinal);
}

bool VerifyWebSocketGenerationContract()
{
    string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts", "Core", "Network", "Transport", "WebSocketClient.cs");
    if (!File.Exists(path)) return false;

    string socket = File.ReadAllText(path);
    return socket.Contains("_connectionGeneration", StringComparison.Ordinal)
        && socket.Contains("IsCurrentSocket", StringComparison.Ordinal)
        && socket.Contains("if (!IsCurrentSocket(socket, generation)) return;", StringComparison.Ordinal)
        && socket.Contains("_connectionGeneration++;", StringComparison.Ordinal);
}

bool VerifySupersededConnectionDetection()
{
    var registry = new ConnectionRegistry();
    var firstEndpoint = new MahjongGame.Core.Network.Transport.GameEndpoint();
    var register = typeof(ConnectionRegistry).GetMethod("Register", new[] { typeof(string), typeof(MahjongGame.Core.Network.Transport.GameEndpoint), typeof(long) });
    var findSuperseded = typeof(ConnectionRegistry).GetMethod("TryGetSupersededRecord");
    if (register == null || findSuperseded == null || register.Invoke(registry, new object[] { "C1", firstEndpoint, 4L }) is not bool registered || !registered) return false;

    var replacementArgs = new object[] { "C1", 5L, null };
    if (findSuperseded.Invoke(registry, replacementArgs) is not bool superseded || !superseded
        || replacementArgs[2] is not ConnectionRegistry.ConnectionRecord previous
        || !ReferenceEquals(previous.Endpoint, firstEndpoint) || previous.Generation != 4L) return false;

    var duplicateArgs = new object[] { "C1", 4L, null };
    return findSuperseded.Invoke(registry, duplicateArgs) is bool duplicateGeneration && !duplicateGeneration;
}

bool VerifyPhysicalSeatPresencePolicy()
{
    var lifecycle = protocolAssembly.GetType("MahjongGame.Core.Network.RoomLifecyclePolicy");
    var countOnlineHuman = lifecycle?.GetMethod("ShouldCountAsOnlineHuman");
    var shouldExpire = lifecycle?.GetMethod("ShouldExpireOfflineSeat");
    if (countOnlineHuman == null || shouldExpire == null) return false;

    var expiration = DateTime.UtcNow.AddSeconds(-1);
    return countOnlineHuman.Invoke(null, new object[] { false, true }) is bool onlineHuman && onlineHuman
        && countOnlineHuman.Invoke(null, new object[] { false, false }) is bool offlineHuman && !offlineHuman
        && countOnlineHuman.Invoke(null, new object[] { true, true }) is bool aiSeat && !aiSeat
        && shouldExpire.Invoke(null, new object[] { false, expiration, DateTime.UtcNow }) is bool offlineExpired && offlineExpired
        && shouldExpire.Invoke(null, new object[] { true, expiration, DateTime.UtcNow }) is bool onlineNeverExpires && !onlineNeverExpires;
}

bool VerifyClientProjectionLifecycle()
{
    var lineageType = protocolAssembly.GetType("MahjongGame.Core.Network.ClientProjectionLineage");
    var bind = lineageType?.GetMethod("Bind");
    var matches = lineageType?.GetMethod("Matches");
    var clear = lineageType?.GetMethod("Clear");
    if (lineageType == null || bind == null || matches == null || clear == null) return false;

    var gameState = new ClientGameState();
    if (!gameState.ApplySnapshot(new RoomGameSnapshot { roomId = "old-room" }, 7)) return false;
    gameState.Reset();
    if (gameState.Snapshot != null || gameState.LastSequence != 0 || gameState.IsResyncRequired) return false;

    object lineage = Activator.CreateInstance(lineageType);
    bind.Invoke(lineage, new object[] { "old-room", "old-stream" });
    if (matches.Invoke(lineage, new object[] { "old-room", "old-stream" }) is not bool oldMatches || !oldMatches
        || matches.Invoke(lineage, new object[] { "new-room", "new-stream" }) is not bool newDoesNotMatch || newDoesNotMatch) return false;
    clear.Invoke(lineage, Array.Empty<object>());
    return matches.Invoke(lineage, new object[] { "old-room", "old-stream" }) is bool cleared && !cleared;
}

bool VerifyOfflineNextRoundLifecyclePolicy()
{
    var lifecycle = protocolAssembly.GetType("MahjongGame.Core.Network.RoomLifecyclePolicy");
    var canAdvance = lifecycle?.GetMethod("ShouldAdvanceAfterWaitingMemberChange");
    var autoReady = lifecycle?.GetMethod("ShouldAutoReadyNextRoundSeat");
    if (canAdvance == null || autoReady == null) return false;

    return canAdvance.Invoke(null, new object[] { false, true }) is bool noAiFillStillAdvances && noAiFillStillAdvances
        && canAdvance.Invoke(null, new object[] { true, true }) is bool aiFillAdvances && aiFillAdvances
        && canAdvance.Invoke(null, new object[] { false, false }) is bool emptyRoomStops && !emptyRoomStops
        && autoReady.Invoke(null, new object[] { false }) is bool offlineAutoReady && offlineAutoReady
        && autoReady.Invoke(null, new object[] { true }) is bool onlineWaitsForReady && !onlineWaitsForReady;
}

bool VerifyNetworkActionAdmissionPolicy()
{
    var policy = protocolAssembly.GetType("MahjongGame.Core.Network.NetworkActionSubmissionPolicy");
    var canProceed = policy?.GetMethod("CanProceedToActionHandling");
    if (canProceed == null) return false;

    return canProceed.Invoke(null, new object[] { true, true, false }) is bool validatedNetworkAction && validatedNetworkAction
        && canProceed.Invoke(null, new object[] { false, true, false }) is bool unlatchedDirectAi && !unlatchedDirectAi
        && canProceed.Invoke(null, new object[] { false, true, true }) is bool latchedDirectAi && latchedDirectAi
        && canProceed.Invoke(null, new object[] { false, false, false }) is bool ordinaryDirectAction && ordinaryDirectAction;
}

bool VerifyDirectActionDecisionAdmissionPolicy()
{
    var policyType = protocolAssembly.GetType("MahjongGame.Core.Network.NetworkActionSubmissionPolicy");
    var canProcessDirectAction = policyType?.GetMethod("CanProcessDirectAction");
    if (canProcessDirectAction == null) return false;

    return canProcessDirectAction.Invoke(null, new object[] { true, true }) is bool accepted && accepted
        && canProcessDirectAction.Invoke(null, new object[] { true, false }) is bool expiredDecision && !expiredDecision
        && canProcessDirectAction.Invoke(null, new object[] { false, true }) is bool unauthorizedController && !unauthorizedController;
}

T ReadOption<T>(object options, string propertyName)
{
    return options?.GetType().GetProperty(propertyName)?.GetValue(options) is T value ? value : default;
}

bool IsClientMessageWithinLimit(string message)
{
    var type = protocolAssembly.GetType("MahjongGame.Core.Network.NetworkMessageLimits");
    var method = type?.GetMethod("IsWithinClientTextLimit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    return method?.Invoke(null, new object[] { message }) is bool result && result;
}

bool VerifyUiToolkitTextCoreFontConfiguration()
{
    string uiRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "UI");
    string textCoreFontAsset = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Font", "MSYH_UITK.asset");
    string textSettings = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "UI Toolkit", "SuperMajiangTextSettings.asset");
    string panelSettings = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "UI Toolkit", "PanelSettings.asset");
    if (!Directory.Exists(uiRoot) || !File.Exists(textCoreFontAsset) || !File.Exists(textSettings) || !File.Exists(panelSettings)) return false;

    string fontAsset = File.ReadAllText(textCoreFontAsset);
    string panelTextSettings = File.ReadAllText(textSettings);
    string panel = File.ReadAllText(panelSettings);
    if (!fontAsset.Contains("m_Script: {fileID: 19001, guid: 0000000000000000e000000000000000, type: 0}", StringComparison.Ordinal)
        || !fontAsset.Contains("m_SourceFontFileGUID:", StringComparison.Ordinal)
        || !panelTextSettings.Contains("m_DefaultFontAsset: {fileID: 11400000, guid:", StringComparison.Ordinal)
        || panelTextSettings.Contains("m_DefaultFontAsset: {fileID: 0}", StringComparison.Ordinal)
        || !panel.Contains("textSettings: {fileID: 11400000, guid:", StringComparison.Ordinal)
        || panel.Contains("textSettings: {fileID: 0}", StringComparison.Ordinal)) return false;

    var styleSheets = Directory.GetFiles(uiRoot, "*.uss", SearchOption.AllDirectories);
    return styleSheets.Length > 0 && styleSheets.All(path =>
    {
        string style = File.ReadAllText(path);
        return !style.Contains("Assets/Font/MSYH.TTC", StringComparison.Ordinal)
            && !style.Contains("Assets/Font/MSYH_SDF.asset", StringComparison.Ordinal)
            && (!style.Contains("-unity-font-definition", StringComparison.Ordinal)
                || style.Contains("Assets/Font/MSYH_UITK.asset", StringComparison.Ordinal));
    });
}

bool VerifyFloatingTilePanelPickingModeConfiguration()
{
    string panelUxmlPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "UI", "FloatingTilePanel.uxml");
    string panelStylePath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "UI", "FloatingTilePanelStyles.uss");
    if (!File.Exists(panelUxmlPath) || !File.Exists(panelStylePath)) return false;

    string panelUxml = File.ReadAllText(panelUxmlPath);
    string panelStyle = File.ReadAllText(panelStylePath);
    return panelUxml.Contains("name=\"FloatingTilePanelRoot\" class=\"floating-panel-root\" picking-mode=\"Ignore\"", StringComparison.Ordinal)
        && !panelUxml.Contains("picking-mode:", StringComparison.Ordinal)
        && !panelStyle.Contains("picking-mode:", StringComparison.Ordinal);
}

bool VerifyE4RecoveryPresentationWiring()
{
    string root = Directory.GetCurrentDirectory();
    string roomServicePath = Path.Combine(root, "Assets", "Scripts", "Core", "Network", "ClientRoomService.cs");
    string proxyPath = Path.Combine(root, "Assets", "Scripts", "Core", "Network", "RemoteServerProxy.cs");
    string managerPath = Path.Combine(root, "Assets", "Scripts", "Systems", "NetworkManager.cs");
    string gameManagerPath = Path.Combine(root, "Assets", "Scripts", "Core", "GameManager.cs");
    string lobbyPath = Path.Combine(root, "Assets", "UI", "LobbyController.cs");
    string overlayPath = Path.Combine(root, "Assets", "UI", "LoadingScreen.uxml");
    if (!new[] { roomServicePath, proxyPath, managerPath, gameManagerPath, lobbyPath, overlayPath }.All(File.Exists)) return false;

    string roomService = File.ReadAllText(roomServicePath);
    string proxy = File.ReadAllText(proxyPath);
    string manager = File.ReadAllText(managerPath);
    string gameManager = File.ReadAllText(gameManagerPath);
    string lobby = File.ReadAllText(lobbyPath);
    string overlay = File.ReadAllText(overlayPath);
    return roomService.Contains("RecoveryProgressChanged", StringComparison.Ordinal)
        && roomService.Contains("ClientReconnectRetryPolicy.GetDelaySeconds", StringComparison.Ordinal)
        && roomService.Contains("SeatSnapshotChanged?.Invoke(Seats)", StringComparison.Ordinal)
        && roomService.Contains("LeaveRoomOrAbandonRecovery", StringComparison.Ordinal)
        && roomService.Contains("CanStartNewRoomCommand", StringComparison.Ordinal)
        && roomService.Contains("RecoveryPresentationVersion", StringComparison.Ordinal)
        && roomService.Contains("!_isComposingRecovery", StringComparison.Ordinal)
        && proxy.Contains("AcceptedSequenceEnvelope +=", StringComparison.Ordinal)
        && !proxy.Contains("OnMessageReceived +=", StringComparison.Ordinal)
        && !proxy.Contains("GameManager.Instance?.ApplyNetworkRecoverySnapshot", StringComparison.Ordinal)
        && manager.Contains("ClientRecoverySceneRoutingPolicy.GetTarget", StringComparison.Ordinal)
        && manager.Contains("RecoveryPresentationVersion", StringComparison.Ordinal)
        && manager.Contains("ReconnectLeaveRequested", StringComparison.Ordinal)
        && gameManager.Contains("ApplyNetworkRecoverySnapshot", StringComparison.Ordinal)
        && gameManager.Contains("_lastRecoveryPresentationVersion", StringComparison.Ordinal)
        && gameManager.Contains("RestoreFromSnapshot", StringComparison.Ordinal)
        && lobby.Contains("ReconnectSnapshotApplied", StringComparison.Ordinal)
        && lobby.Contains("ShowRoom();", StringComparison.Ordinal)
        && overlay.Contains("ReconnectContainer", StringComparison.Ordinal)
        && overlay.Contains("ReconnectLeaveButton", StringComparison.Ordinal);
}

bool VerifyMcrConcealedKanPresentation()
{
    string handViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts", "Core", "MahjongHandViewBase.cs");
    if (!File.Exists(handViewPath)) return false;
    string handView = File.ReadAllText(handViewPath);
    return handView.Contains("bool isConcealed = false;", StringComparison.Ordinal)
        && handView.Contains("MCR declares concealed-kong tile faces", StringComparison.Ordinal);
}

bool VerifyLiveNetworkSessionProjection()
{
    string proxyPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts", "Core", "Network", "RemoteServerProxy.cs");
    if (!File.Exists(proxyPath)) return false;

    string proxy = File.ReadAllText(proxyPath);
    return proxy.Contains("SyncSessionAtRoundStart(roundMsg)", StringComparison.Ordinal)
        && proxy.Contains("private void SyncSessionAtRoundStart(RoundStartMessage roundStart)", StringComparison.Ordinal)
        && proxy.Contains("session.Mode = _roomService.GameMode", StringComparison.Ordinal)
        && proxy.Contains("session.TotalRoundsPlayed = Mathf.Clamp(roundStart.roundNumber - 1, 0, session.GetTotalRounds())", StringComparison.Ordinal)
        && proxy.Contains("GameHUDController.Instance?.UpdateRoundInfo(session)", StringComparison.Ordinal);
}

bool VerifyRecoveryHandSorting()
{
    string handControllerPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts", "Core", "HandController.cs");
    if (!File.Exists(handControllerPath)) return false;

    string handController = File.ReadAllText(handControllerPath);
    int rebuildStart = handController.IndexOf("public void RebuildFromSnapshot", StringComparison.Ordinal);
    int rebuildEnd = handController.IndexOf("private void UpdateHandPositionsImmediately", StringComparison.Ordinal);
    if (rebuildStart < 0 || rebuildEnd <= rebuildStart) return false;

    string rebuild = handController.Substring(rebuildStart, rebuildEnd - rebuildStart);
    return rebuild.Contains("SortHandImmediately();", StringComparison.Ordinal)
        && handController.Contains("private void SortHandImmediately()", StringComparison.Ordinal)
        && handController.Contains("_handTiles.Sort((a, b) =>", StringComparison.Ordinal)
        && handController.Contains("transform.SetSiblingIndex(i)", StringComparison.Ordinal);
}

bool VerifyConcealedKanEligibilityConsistency()
{
    string root = Directory.GetCurrentDirectory();
    string validatorPath = Path.Combine(root, "Assets", "Scripts", "Core", "ActionValidator.cs");
    string handControllerPath = Path.Combine(root, "Assets", "Scripts", "Core", "HandController.cs");
    if (!new[] { validatorPath, handControllerPath }.All(File.Exists)) return false;

    string validator = File.ReadAllText(validatorPath);
    string handController = File.ReadAllText(handControllerPath);
    return validator.Contains("public static List<TileData> GetConcealedKanOptions", StringComparison.Ordinal)
        && validator.Contains("g.Count() >= 4", StringComparison.Ordinal)
        && validator.Contains("GetConcealedKanOptions(myHand).Any()", StringComparison.Ordinal)
        && handController.Contains("ActionValidator.GetConcealedKanOptions(_handTiles.Select(tile => tile.Data))", StringComparison.Ordinal);
}

[TalentRule("network_test_small", "Network Test Small", "Regression-only small talent.", TalentTier.Small, 1)]
sealed class NetworkTestSmallTalent : TalentRule { }

[TalentRule("network_test_medium", "Network Test Medium", "Regression-only medium talent.", TalentTier.Medium, 1)]
sealed class NetworkTestMediumTalent : TalentRule { }
