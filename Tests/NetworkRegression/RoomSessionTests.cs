using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Services;
using MahjongGame.Systems;
using MahjongGame.Talents;

internal static class RoomSessionTests
{
    public static void Run(RegressionRunner runner)
    {
        TestLoadoutValidation(runner);
        TestWallAndSessionTalent(runner);
        TestRoomReadyAndDeparture(runner);
        TestResponseAndTurnPolicies(runner);
        TestClientRoomAndScoreProjection(runner);
    }

    private static void TestLoadoutValidation(RegressionRunner runner)
    {
        var standardDeck = DeckConfig.CreateStandard();
        var emptyTalents = new TalentSlotConfig();
        runner.Check(PlayerLoadoutCodec.TryCreateMessage(standardDeck, emptyTalents, out var message, out _)
            && message.deckEntries.Length == 34
            && message.talentSlotIds.Length == 6,
            "A valid loadout must contain 34 tile entries and six talent slots.");
        runner.Check(PlayerLoadoutCodec.TryDecode(message, out var decoded, out _)
            && decoded.DeckConfig.GetCardCount(Suit.Man, 1) == 1
            && decoded.TotalAlienation == 0,
            "A standard loadout must round-trip through server validation.");

        var custom = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
        foreach (var entry in custom.deckEntries) entry.count = 0;
        custom.deckEntries[0].count = 34;
        runner.Check(PlayerLoadoutCodec.TryDecode(custom, out var decodedCustom, out _)
            && decodedCustom.DeckConfig.GetCardCount(Suit.Man, 1) == 34,
            "Custom 34-tile compositions must preserve per-tile counts.");

        runner.Check(!PlayerLoadoutCodec.TryDecode(null, out _, out var missingError)
            && missingError == "MissingLoadout",
            "A missing loadout must use the stable error code.");

        var wrongVersion = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
        wrongVersion.schemaVersion++;
        runner.Check(!PlayerLoadoutCodec.TryDecode(wrongVersion, out _, out var versionError)
            && versionError == "UnsupportedLoadoutVersion",
            "Unsupported loadout schema versions must be rejected.");

        var invalidDeck = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
        invalidDeck.deckEntries[0].count = -1;
        runner.Check(!PlayerLoadoutCodec.TryDecode(invalidDeck, out _, out var deckError)
            && deckError == "InvalidDeck",
            "Negative tile counts must be rejected.");

        var duplicateTile = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
        duplicateTile.deckEntries[1].suit = duplicateTile.deckEntries[0].suit;
        duplicateTile.deckEntries[1].value = duplicateTile.deckEntries[0].value;
        runner.Check(!PlayerLoadoutCodec.TryDecode(duplicateTile, out _, out var duplicateError)
            && duplicateError == "InvalidDeck",
            "Duplicate tile entries must be rejected.");

        var duplicateTalent = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
        duplicateTalent.talentSlotIds[3] = "network_test_small";
        duplicateTalent.talentSlotIds[4] = "network_test_small";
        runner.Check(!PlayerLoadoutCodec.TryDecode(duplicateTalent, out _, out var talentError)
            && talentError == "InvalidTalent",
            "Duplicate equipped talents must be rejected.");

        var tierMismatch = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
        tierMismatch.talentSlotIds[3] = "network_test_medium";
        runner.Check(!PlayerLoadoutCodec.TryDecode(tierMismatch, out _, out var tierError)
            && tierError == "InvalidTalent",
            "Talent slot tier limits must be enforced.");
    }

    private static void TestWallAndSessionTalent(RegressionRunner runner)
    {
        var standardDeck = DeckConfig.CreateStandard();
        var customMessage = PlayerLoadoutCodec.CreateMessage(standardDeck, new TalentSlotConfig());
        foreach (var entry in customMessage.deckEntries) entry.count = 0;
        customMessage.deckEntries[0].count = 34;
        PlayerLoadoutCodec.TryDecode(customMessage, out var custom, out _);

        var wallService = new WallService(seed: 1234);
        wallService.BuildWall(new List<DeckConfig>
        {
            standardDeck,
            custom.DeckConfig,
            DeckConfig.CreateStandard(),
            DeckConfig.CreateStandard()
        });
        var wall = wallService.GetWallTiles();
        runner.Check(wall.Count == 136
            && Enumerable.Range(0, 4).All(owner => wall.Count(tile => tile.OriginalOwnerID == owner) == 34)
            && wall.Where(tile => tile.OriginalOwnerID == 1)
                .All(tile => tile.TileSuit == Suit.Man && tile.Value == 1),
            "The authoritative wall must contain each seat's locked 34-tile composition.");

        var session = new GameSession(GameMode.EastOnly);
        var talentConfigs = new Dictionary<int, TalentSlotConfig>
        {
            [0] = new() { SlotTalentIds = new[] { null, null, null, "starting_capital", null, null } },
            [1] = new() { SlotTalentIds = new string[6] },
            [2] = new() { SlotTalentIds = new[] { null, null, null, null, null, "starting_capital" } },
            [3] = new() { SlotTalentIds = new string[6] }
        };
        var applied = false;
        runner.Check(SessionTalentPolicy.ApplyStartingCapitalOnce(session, talentConfigs, ref applied)
            && session.Scores.SequenceEqual(new[] { 30, 0, 30, 0 }),
            "Session-start talents must apply to every equipped seat.");
        runner.Check(!SessionTalentPolicy.ApplyStartingCapitalOnce(session, talentConfigs, ref applied)
            && session.Scores.SequenceEqual(new[] { 30, 0, 30, 0 }),
            "Session-start talents must not repeat in later rounds.");
    }

    private static void TestRoomReadyAndDeparture(RegressionRunner runner)
    {
        runner.Check(!RoomReadyPolicy.CanMarkMatchReady(false, 3)
            && RoomReadyPolicy.CanMarkMatchReady(false, 4)
            && RoomReadyPolicy.CanMarkMatchReady(true, 1),
            "Ready admission must require four humans only when AI fill is disabled.");

        runner.Check(RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.LoadingGameScene, true, false)
            && RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.InRound, true, false)
            && !RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(RoomState.WaitingForMatchReady, false, true),
            "A room must keep reserved seats while another human remains and close with no humans.");
        runner.Check(!RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.WaitingForMatchReady, true)
            && RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.LoadingGameScene, false)
            && RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.InRound, false)
            && RoomDeparturePolicy.ShouldReplaceWithAi(RoomState.WaitingForNextRound, false),
            "Temporary AI takeover must begin only after pre-match waiting.");

        runner.Check(RoomLifecyclePolicy.ShouldAdvanceAfterWaitingMemberChange(false, true)
            && RoomLifecyclePolicy.ShouldAutoReadyNextRoundSeat(false)
            && !RoomLifecyclePolicy.ShouldAutoReadyNextRoundSeat(true),
            "Locked rooms must advance independently of aiFill and auto-ready only offline humans.");
    }

    private static void TestResponseAndTurnPolicies(RegressionRunner runner)
    {
        runner.Check(TurnActionPolicy.IsMainTurnAction(ClientActionType.Discard)
            && TurnActionPolicy.IsMainTurnAction(ClientActionType.Hu)
            && !TurnActionPolicy.IsMainTurnAction(ClientActionType.Skip)
            && TurnActionPolicy.IsResponseAction(ClientActionType.Skip)
            && TurnActionPolicy.IsResponseAction(ClientActionType.Chi)
            && !TurnActionPolicy.IsResponseAction(ClientActionType.Discard),
            "Main-turn and response actions must remain phase-specific.");

        runner.Check(!ResponseActionPolicy.CanRespondToDiscard(2, 2)
            && ResponseActionPolicy.CanRespondToDiscard(2, 1),
            "A seat must not respond to its own discard.");
        var selected = ResponseActionPolicy.SelectHighestPriorityResponse(new[]
        {
            new ClientAction(2, ClientActionType.Hu),
            new ClientAction(3, ClientActionType.Pon),
            new ClientAction(1, ClientActionType.Hu)
        }, discarderId: 0, playerCount: 4);
        runner.Check(selected?.ActionType == ClientActionType.Hu && selected.PlayerId == 1,
            "Hu must outrank Pon and use nearest-seat interception order.");
        runner.Check(!ResponseActionPolicy.AllPotentialHuRespondersAnswered(new[] { 1, 2 }, new[] { 1 })
            && ResponseActionPolicy.AllPotentialHuRespondersAnswered(new[] { 1, 2 }, new[] { 1, 2 }),
            "Resolution must wait for every Hu-eligible responder.");

        runner.Check(NetworkActionSubmissionPolicy.CanProceedToActionHandling(true, true, false)
            && !NetworkActionSubmissionPolicy.CanProceedToActionHandling(false, true, false)
            && NetworkActionSubmissionPolicy.CanProceedToActionHandling(false, true, true)
            && NetworkActionSubmissionPolicy.CanProcessDirectAction(true, true)
            && !NetworkActionSubmissionPolicy.CanProcessDirectAction(true, false),
            "Network and direct AI actions must pass their matching admission guards.");
    }

    private static void TestClientRoomAndScoreProjection(RegressionRunner runner)
    {
        var room = new ClientRoomState();
        room.ApplyJoined(new RoomJoinedMessage
        {
            roomId = "R0001",
            seatIndex = 1,
            gameMode = (int)GameMode.EastOnly,
            roomState = (int)RoomState.WaitingForMatchReady,
            aiFillEnabled = true,
            acceptedSchemaVersion = 1,
            acceptedTotalAlienation = 17,
            seats = new[]
            {
                new RoomSeatMessage { seatIndex = 0, isOccupied = true, displayName = "Host" },
                new RoomSeatMessage { seatIndex = 1, isOccupied = true, displayName = "Guest" }
            }
        });
        runner.Check(room.ApplySeatUpdate(new RoomSeatMessage
            {
                seatIndex = 1,
                isOccupied = true,
                displayName = "Guest",
                isReady = true
            })
            && room.Seats[1].isReady,
            "Room seat updates must project Ready state.");
        room.CompleteSession();
        runner.Check(room.HasRoom
            && room.IsSessionCompleted
            && room.ResultSeatIndex == 1
            && room.RoomStateValue == (int)RoomState.SessionCompleted,
            "Session completion must retain the room binding for recoverable final results.");
        room.Reset();
        runner.Check(!room.HasRoom && !room.IsSessionCompleted,
            "Leaving the result must clear active and completed room state.");

        var scoreSession = new GameSession(GameMode.EastOnly);
        runner.Check(SessionScorePolicy.ApplyAuthoritativeScores(scoreSession, new[] { 30, 0, 30, 0 })
            && scoreSession.Scores.SequenceEqual(new[] { 30, 0, 30, 0 }),
            "Clients must apply the server's authoritative score snapshot.");
        runner.Check(SceneTransitionPolicy.ShouldUnloadGameScene(true)
            && !SceneTransitionPolicy.ShouldUnloadGameScene(false),
            "Room closure must unload the Game scene whenever it is loaded.");
        runner.Check(LoginUsernamePolicy.Normalize("  Test_Player_02  ") == "Test_Player_02"
            && LoginUsernamePolicy.Normalize("   ") == "Player",
            "Lobby display names must derive from the login username.");
    }
}

[TalentRule("network_test_small", "Network Test Small", "Regression-only small talent.", TalentTier.Small, 1)]
internal sealed class NetworkTestSmallTalent : TalentRule { }

[TalentRule("network_test_medium", "Network Test Medium", "Regression-only medium talent.", TalentTier.Medium, 1)]
internal sealed class NetworkTestMediumTalent : TalentRule { }
