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
Assert(!completedRoom.HasRoom, "A SessionEnd must clear the active room binding immediately.");
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
Assert(!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.LoadingGameScene, true, false), "A loading room without AI fill must close after a departure.");
Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForNextRound, true, true), "AI fill must preserve a between-round room after a departure.");
Assert(!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForNextRound, true, false), "A between-round room without AI fill must close after a departure.");
Assert(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForMatchReady, true, false), "A pre-match room without AI fill must retain an empty seat for a replacement human.");
Assert(!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.InRound, true, true), "An in-round departure must always close the room.");
Assert(!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForMatchReady, false, true), "A room with no remaining humans must be closed.");
Assert(!RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.WaitingForPlayers, true),
    "An AI-fill room must leave an empty seat when a player leaves before match-ready.");
Assert(!RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.WaitingForMatchReady, true),
    "An AI-fill room must leave an empty seat when a player leaves after match-ready but before loading.");
Assert(RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.LoadingGameScene, true),
    "AI fill must still replace a departure while the game scene is loading.");
Assert(RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.WaitingForNextRound, true),
    "AI fill must still replace a departure between rounds.");

Assert(LoginUsernamePolicy.Normalize("  Test_Player_02  ") == "Test_Player_02",
    "The lobby nickname must be derived from the username entered on the login screen.");
Assert(LoginUsernamePolicy.Normalize("   ") == "Player",
    "An empty login username must resolve to a safe fallback nickname.");

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

[TalentRule("network_test_small", "Network Test Small", "Regression-only small talent.", TalentTier.Small, 1)]
sealed class NetworkTestSmallTalent : TalentRule { }

[TalentRule("network_test_medium", "Network Test Medium", "Regression-only medium talent.", TalentTier.Medium, 1)]
sealed class NetworkTestMediumTalent : TalentRule { }
