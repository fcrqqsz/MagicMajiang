using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Core.Services;
using MahjongGame.Systems;
using MahjongGame.Talents;

internal static class RoomSessionTests
{
    public static void Run(RegressionRunner runner)
    {
        TestLoadoutValidation(runner);
        TestRoomAlienationAdmission(runner);
        TestRoomRevalidatesClonedLoadout(runner);
        TestWallAndSessionTalent(runner);
        TestRoomOwnsOneTalentRuntimeAcrossRounds(runner);
        TestAbnormalRoundCompletionUnwindsRoomOnce(runner);
        TestRoundFinalizationFailureTerminatesEveryHumanSeat(runner);
        TestRoomReadyAndDeparture(runner);
        TestResponseAndTurnPolicies(runner);
        TestClientRoomAndScoreProjection(runner);
    }

    private static void TestRoomRevalidatesClonedLoadout(RegressionRunner runner)
    {
        var room = new Room("stale-loadout", GameMode.Single, AlienationPreset.Low, "host", true, 8);
        PlayerLoadoutCodec.TryDecode(
            PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), new TalentSlotConfig()),
            AlienationPreset.Low,
            out var hostLoadout,
            out _);
        runner.Check(room.TryAddHuman(
                "host", new GameEndpoint(), "dev:host", "Host", hostLoadout, out int hostSeat)
            && hostSeat == 0,
            "Stale-loadout regression setup must establish one valid room seat.");
        room.TrySendToHumanSeat(0, "RoomSeatUpdated", new RoomSeatUpdatedMessage
        {
            roomId = room.RoomId,
            seat = room.GetSeatMessage(0)
        });

        PlayerLoadoutCodec.TryDecode(
            PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), new TalentSlotConfig()),
            AlienationPreset.Low,
            out var staleLoadout,
            out _);
        ConfigureThirtyAlienationDeck(staleLoadout.DeckConfig);
        staleLoadout.TalentConfig.SlotTalentIds[0] = "midas_touch";

        RoomState stateBefore = room.State;
        int onlineBefore = room.OnlineHumanCount;
        RoomSeat[] seatsBefore = room.Seats.ToArray();
        int sequenceBefore = room.Seats[0].MessageStream.LatestSequence;
        int messagesBefore = room.Seats[0].Endpoint.SentMessages.Count;

        bool added = room.TryAddHuman(
            "stale", new GameEndpoint(), "dev:stale", "Stale", staleLoadout, out int staleSeat);

        runner.Check(staleLoadout.TotalAlienation == 0
            && !added
            && staleSeat == -1
            && room.State == stateBefore
            && room.OnlineHumanCount == onlineBefore
            && room.Seats.SequenceEqual(seatsBefore)
            && room.Seats[0].MessageStream.LatestSequence == sequenceBefore
            && room.Seats[0].Endpoint.SentMessages.Count == messagesBefore,
            "Room admission must revalidate a cloned mutable loadout before changing seats, state, or streams.");
        room.Dispose();
    }

    private static void TestRoomAlienationAdmission(RegressionRunner runner)
    {
        var constructor = typeof(Room).GetConstructor(new[]
        {
            typeof(string), typeof(GameMode), typeof(AlienationPreset), typeof(string), typeof(bool), typeof(int)
        });
        var presetProperty = typeof(Room).GetProperty("AlienationPreset");
        Room directRoom = constructor?.Invoke(new object[]
        {
            "preset-room", GameMode.HalfGame, AlienationPreset.Standard, "host", true, 8
        }) as Room;
        runner.Check(directRoom != null
            && presetProperty?.GetValue(directRoom) is AlienationPreset preset
            && preset == AlienationPreset.Standard,
            "A room must lock the alienation preset selected at creation.");
        directRoom?.Dispose();

        Room lowRoom = constructor?.Invoke(new object[]
        {
            "low-room", GameMode.Single, AlienationPreset.Low, "host", true, 8
        }) as Room;
        PlayerLoadoutCodec.TryDecode(BuildOverLowPresetLoadout(), out var overLowTrusted, out _);
        bool addedOverBudget = lowRoom?.TryAddHuman(
            "bypass", new GameEndpoint(), "dev:bypass", "Bypass", overLowTrusted, out _) ?? false;
        runner.Check(!addedOverBudget && lowRoom?.Seats.All(seat => seat == null) == true,
            "Room admission must reject an over-budget trusted loadout before assigning any seat.");
        lowRoom?.Dispose();

        var connections = new ConnectionRegistry();
        using var manager = new RoomManager(2, true, connections, messageCacheSize: 8);
        var host = new GameEndpoint();
        var guest = new GameEndpoint();
        host.Connect("host-connection", 1);
        guest.Connect("guest-connection", 2);
        host.Receive("host-connection", 1, MessageSerializer.Serialize("Hello", 0,
            new HelloMessage { protocolVersion = NetworkProtocol.Version, username = "Host" }));
        guest.Receive("guest-connection", 2, MessageSerializer.Serialize("Hello", 0,
            new HelloMessage { protocolVersion = NetworkProtocol.Version, username = "Guest" }));

        var createRequest = new CreateRoomMessage
        {
            gameMode = (int)GameMode.HalfGame,
            loadout = PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), new TalentSlotConfig())
        };
        typeof(CreateRoomMessage).GetField("alienationPreset")?.SetValue(createRequest, (int)AlienationPreset.Low);
        host.Receive("host-connection", 1, MessageSerializer.Serialize("CreateRoom", 0, createRequest));
        var hostJoinedEnvelope = host.SentMessages.Select(MessageSerializer.DeserializeEnvelope)
            .Single(envelope => envelope.type == "RoomJoined");
        string hostJoinedJson = hostJoinedEnvelope.data;
        var hostJoined = MessageSerializer.DeserializePayload<RoomJoinedMessage>(hostJoinedJson);
        runner.Check(hostJoinedJson.Contains("\"alienationPreset\":40", StringComparison.Ordinal)
            && hostJoinedJson.Contains("\"ownTotalAlienation\":0", StringComparison.Ordinal)
            && hostJoined.seats.All(seat => !UnityEngine.JsonUtility.ToJson(seat)
                .Contains("totalAlienation", StringComparison.OrdinalIgnoreCase)),
            "RoomJoined must expose the public preset and only the owner's exact alienation total.");

        int hostMessageCountBeforeRejectedJoin = host.SentMessages.Count;
        guest.Receive("guest-connection", 2, MessageSerializer.Serialize("JoinRoom", 0, new JoinRoomMessage
        {
            roomId = hostJoined.roomId,
            loadout = BuildOverLowPresetLoadout()
        }));
        var rejectedEnvelope = MessageSerializer.DeserializeEnvelope(guest.SentMessages.Last());
        var rejected = rejectedEnvelope.type == "RoomError"
            ? MessageSerializer.DeserializePayload<RoomErrorMessage>(rejectedEnvelope.data)
            : null;
        connections.TryGet("guest-connection", out var guestAfterRejection);
        runner.Check(rejected?.code == PlayerLoadoutErrorCodes.AlienationLimitExceeded
            && rejectedEnvelope.data.Contains("\"actual\":45", StringComparison.Ordinal)
            && rejectedEnvelope.data.Contains("\"limit\":40", StringComparison.Ordinal)
            && string.IsNullOrEmpty(guestAfterRejection?.RoomId)
            && guestAfterRejection?.SeatIndex == -1
            && host.SentMessages.Count == hostMessageCountBeforeRejectedJoin,
            "An over-budget join must not allocate a seat, bind the connection, or advance another seat's stream.");

        guest.Receive("guest-connection", 2, MessageSerializer.Serialize("JoinRoom", 0, new JoinRoomMessage
        {
            roomId = hostJoined.roomId,
            loadout = PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), new TalentSlotConfig())
        }));
        var guestJoined = guest.SentMessages.Select(MessageSerializer.DeserializeEnvelope)
            .Where(envelope => envelope.type == "RoomJoined")
            .Select(envelope => MessageSerializer.DeserializePayload<RoomJoinedMessage>(envelope.data))
            .Single();
        runner.Check(guestJoined.seatIndex == 1,
            "A valid retry after an over-budget join must receive the first still-vacant seat.");
    }

    private static PlayerLoadoutMessage BuildOverLowPresetLoadout()
    {
        var deck = DeckConfig.CreateStandard();
        ConfigureThirtyAlienationDeck(deck);
        var talents = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "midas_touch", null, null, null, null, null },
            ReserveTalentIds = new string[TalentSlotConfig.ReserveSlotCount]
        };
        return PlayerLoadoutCodec.CreateMessage(deck, talents);
    }

    private static void ConfigureThirtyAlienationDeck(DeckConfig deck)
    {
        foreach (Suit suit in new[] { Suit.Man, Suit.Pin, Suit.Sou })
        {
            deck.SetCardCount(suit, 1, 6);
            for (int value = 2; value <= 6; value++) deck.SetCardCount(suit, value, 0);
        }
    }

    private static void TestLoadoutValidation(RegressionRunner runner)
    {
        var standardDeck = DeckConfig.CreateStandard();
        var emptyTalents = new TalentSlotConfig();
        runner.Check(PlayerLoadoutCodec.TryCreateMessage(standardDeck, emptyTalents, out var message, out _)
            && message.deckEntries.Length == 34
            && message.mainTalentSlotIds.Length == 6
            && message.reserveTalentSlotIds.Length == 3,
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
        duplicateTalent.mainTalentSlotIds[3] = "network_test_small";
        duplicateTalent.mainTalentSlotIds[4] = "network_test_small";
        runner.Check(!PlayerLoadoutCodec.TryDecode(duplicateTalent, out _, out var talentError)
            && talentError == "InvalidTalent",
            "Duplicate equipped talents must be rejected.");

        var tierMismatch = PlayerLoadoutCodec.CreateMessage(standardDeck, emptyTalents);
        tierMismatch.mainTalentSlotIds[3] = "network_test_medium";
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
        var talentRuntime = new TalentMatchRuntime(talentConfigs, TalentRegistry.Instance);
        talentRuntime.BeginMatch(session);
        runner.Check(session.Scores.SequenceEqual(new[] { 30, 0, 30, 0 }),
            "Session-start talents must apply to every equipped seat.");
    }

    private static void TestRoomOwnsOneTalentRuntimeAcrossRounds(RegressionRunner runner)
    {
        GameServer.ResetObservations();
        TalentSlotConfig sixTalents = new TalentSlotConfig
        {
            SlotTalentIds = new[]
            {
                "midas_touch", "dragon_ascent", "head_start",
                "draw_reward", "peek", "starting_capital"
            }
        };
        PlayerLoadoutCodec.TryDecode(
            PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), sixTalents),
            AlienationPreset.Standard,
            out TrustedPlayerLoadout hostLoadout,
            out _);
        PlayerLoadoutCodec.TryDecode(
            PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), new TalentSlotConfig()),
            AlienationPreset.Standard,
            out TrustedPlayerLoadout guestLoadout,
            out _);

        var hostEndpoint = new GameEndpoint();
        var guestEndpoint = new GameEndpoint();
        using var room = new Room(
            "runtime-room", GameMode.EastOnly, AlienationPreset.Standard, "host", true, 16);
        bool hostAdded = room.TryAddHuman(
            "host", hostEndpoint, "dev:host", "Host", hostLoadout, out int hostSeat);
        bool guestAdded = room.TryAddHuman(
            "guest", guestEndpoint, "dev:guest", "Guest", guestLoadout, out int guestSeat);
        bool matchReady = room.SetReady("host", ReadyPhase.MatchStart, out _)
                          && room.SetReady("guest", ReadyPhase.MatchStart, out _);

        runner.Check(hostAdded && guestAdded && hostSeat == 0 && guestSeat == 1 && matchReady,
            "two locked human loadouts establish the Room-owned runtime test match");
        runner.Check(room.Session.Scores.SequenceEqual(new[] { 30, 0, 0, 0 }),
            "Room begins the talent match once after all four loadouts are locked");
        runner.Check(CountRuntimeEvents(hostEndpoint) == 1
                     && CountRuntimeEvents(guestEndpoint) == 1,
            "public match-start talent events enter every human seat stream independently");

        bool sceneReady = room.SetReady("host", ReadyPhase.GameSceneLoaded, out _)
                          && room.SetReady("guest", ReadyPhase.GameSceneLoaded, out _);
        TalentMatchRuntime firstRuntime = room.GameServer?.TalentRuntime;
        RoomGameSnapshot hostSnapshot = room.BuildSnapshot(hostSeat);
        RoomGameSnapshot guestSnapshot = room.BuildSnapshot(guestSeat);
        runner.Check(sceneReady
                     && firstRuntime != null
                     && hostSnapshot.privateSeat.peekWallTiles.Length == 4
                     && guestSnapshot.privateSeat.peekWallTiles.Length == 0,
            "each GameServer receives the Room runtime while peek stays in its owner's snapshot");

        room.GameServer?.CompleteDrawRound();
        runner.Check(room.Session.TotalRoundsPlayed == 1
                     && room.Session.Scores.SequenceEqual(new[] { 35, 0, 0, 0 })
                     && CountRuntimeEvents(hostEndpoint) == 2
                     && CountRuntimeEvents(guestEndpoint) == 2,
            "Room ends a drawn round through the runtime before advancing the session");

        bool nextReady = room.SetReady("host", ReadyPhase.NextRound, out _)
                         && room.SetReady("guest", ReadyPhase.NextRound, out _);
        TalentMatchRuntime secondRuntime = room.GameServer?.TalentRuntime;
        runner.Check(nextReady
                     && ReferenceEquals(firstRuntime, secondRuntime)
                     && GameServer.ReceivedTalentRuntimes.Count == 2
                     && GameServer.ReceivedTalentRuntimes.All(runtime => ReferenceEquals(runtime, firstRuntime))
                     && room.Session.Scores.SequenceEqual(new[] { 35, 0, 0, 0 }),
            "the second GameServer reuses the match runtime without repeating match-start effects");

        room.GameServer?.CompleteDrawRound();
        runner.Check(room.Session.TotalRoundsPlayed == 2
                     && room.Session.Scores.SequenceEqual(new[] { 40, 0, 0, 0 })
                     && CountRuntimeEvents(hostEndpoint) == 3
                     && CountRuntimeEvents(guestEndpoint) == 3,
            "draw rewards execute exactly once in each of two Room rounds and remain seat-stream ordered");
    }

    private static int CountRuntimeEvents(GameEndpoint endpoint)
    {
        return endpoint.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope)
            .Count(envelope => envelope?.type == "TalentRuntimeEvent");
    }

    private static void TestAbnormalRoundCompletionUnwindsRoomOnce(RegressionRunner runner)
    {
        var latch = new GameRoundCompletionLatch();
        bool firstCompletion = latch.TryComplete(
            GameRoundCompletionKind.Draw,
            error: null,
            out GameRoundCompletion drawCompletion);
        bool duplicateCompletion = latch.TryComplete(
            GameRoundCompletionKind.Aborted,
            new InvalidOperationException("late failure"),
            out GameRoundCompletion duplicate);
        runner.Check(firstCompletion
                     && drawCompletion.Kind == GameRoundCompletionKind.Draw
                     && !duplicateCompletion
                     && duplicate == null,
            "a round completion latch admits only the first terminal result");

        VerifyAbnormalRoomUnwind(runner, StubGameStartFailure.Startup);
        VerifyAbnormalRoomUnwind(runner, StubGameStartFailure.Loop);

        GameServer.ResetObservations();
        using Room normalRoom = CreateRuntimeRoomWithDrawReward("normal-completion", out _);
        GameServer.NextStartFailure = StubGameStartFailure.None;
        normalRoom.SetReady("host", ReadyPhase.GameSceneLoaded, out _);
        normalRoom.GameServer?.CompleteDrawRound();
        normalRoom.GameServer?.CompleteDrawRound();
        runner.Check(normalRoom.State == RoomState.WaitingForNextRound
                     && normalRoom.Session.TotalRoundsPlayed == 1
                     && normalRoom.Session.Scores[0] == 5
                     && normalRoom.GameServer?.CompletionNotifications == 1,
            "a normal round completion advances and applies round-end effects exactly once");
    }

    private static void VerifyAbnormalRoomUnwind(
        RegressionRunner runner,
        StubGameStartFailure failure)
    {
        GameServer.ResetObservations();
        using Room room = CreateRuntimeRoomWithDrawReward($"abnormal-{failure}", out GameEndpoint endpoint);
        GameServer.NextStartFailure = failure;
        room.SetReady("host", ReadyPhase.GameSceneLoaded, out _);
        room.GameServer?.CompleteDrawRound();

        int terminalErrors = endpoint.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope)
            .Count(envelope => envelope?.type == "RoomError"
                               && MessageSerializer.DeserializePayload<RoomErrorMessage>(envelope.data)?.code
                               == NetworkErrorCodes.RoundAborted);
        int sessionEnds = endpoint.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope)
            .Count(envelope => envelope?.type == "SessionEnd");
        runner.Check(room.State == RoomState.SessionCompleted
                     && room.Session.TotalRoundsPlayed == 0
                     && room.Session.Scores[0] == 0
                     && room.GameServer?.CompletionNotifications == 1
                     && terminalErrors == 1
                     && sessionEnds == 1,
            $"a {failure.ToString().ToLowerInvariant()} exception ends the runtime and room exactly once without draw rewards");
    }

    private static Room CreateRuntimeRoomWithDrawReward(string roomId, out GameEndpoint endpoint)
    {
        var talents = new TalentSlotConfig();
        talents.SlotTalentIds[3] = "draw_reward";
        PlayerLoadoutCodec.TryDecode(
            PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), talents),
            AlienationPreset.Standard,
            out TrustedPlayerLoadout loadout,
            out _);

        endpoint = new GameEndpoint();
        var room = new Room(roomId, GameMode.EastOnly, AlienationPreset.Standard, "host", true, 16);
        room.TryAddHuman("host", endpoint, $"dev:{roomId}", "Host", loadout, out _);
        room.SetReady("host", ReadyPhase.MatchStart, out _);
        return room;
    }

    private static void TestRoundFinalizationFailureTerminatesEveryHumanSeat(RegressionRunner runner)
    {
        var hostTalents = new TalentSlotConfig();
        hostTalents.SlotTalentIds[3] = "draw_reward";
        hostTalents.SlotTalentIds[4] = "network_test_throw_round_end";
        PlayerLoadoutCodec.TryDecode(
            PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), hostTalents),
            AlienationPreset.Standard,
            out TrustedPlayerLoadout hostLoadout,
            out _);
        PlayerLoadoutCodec.TryDecode(
            PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), new TalentSlotConfig()),
            AlienationPreset.Standard,
            out TrustedPlayerLoadout guestLoadout,
            out _);

        var hostEndpoint = new GameEndpoint();
        var guestEndpoint = new GameEndpoint();
        using var room = new Room(
            "round-finalization-failure",
            GameMode.EastOnly,
            AlienationPreset.Standard,
            "host",
            true,
            16);
        room.TryAddHuman("host", hostEndpoint, "dev:failure-host", "Host", hostLoadout, out _);
        room.TryAddHuman("guest", guestEndpoint, "dev:failure-guest", "Guest", guestLoadout, out _);
        room.SetReady("host", ReadyPhase.MatchStart, out _);
        room.SetReady("guest", ReadyPhase.MatchStart, out _);
        room.SetReady("host", ReadyPhase.GameSceneLoaded, out _);
        room.SetReady("guest", ReadyPhase.GameSceneLoaded, out _);
        hostEndpoint.SendFailure = message =>
            MessageSerializer.DeserializeEnvelope(message)?.type == "RoomError";

        bool exceptionLeaked = false;
        try
        {
            room.GameServer?.CompleteDrawRound();
        }
        catch (InvalidOperationException)
        {
            exceptionLeaked = true;
        }

        int hostSessionEnds = CountMessages(hostEndpoint, "SessionEnd");
        int guestSessionEnds = CountMessages(guestEndpoint, "SessionEnd");
        int guestAborts = CountMessages(guestEndpoint, "RoomError", NetworkErrorCodes.RoundAborted);
        int scoreAfterFailure = room.Session.Scores[0];
        room.GameServer?.CompleteDrawRound();

        runner.Check(!exceptionLeaked
                     && room.State == RoomState.SessionCompleted
                     && room.Session.TotalRoundsPlayed == 0
                     && scoreAfterFailure == 5
                     && room.Session.Scores[0] == 5
                     && room.GameServer?.CompletionNotifications == 1
                     && hostSessionEnds == 1
                     && guestSessionEnds == 1
                     && guestAborts == 1
                     && CountMessages(guestEndpoint, "SessionEnd") == 1
                     && CountMessages(guestEndpoint, "RoomError", NetworkErrorCodes.RoundAborted) == 1,
            "throwing round-end talent aborts Room once, does not duplicate rewards, and isolates terminal seat failures");
    }

    private static int CountMessages(GameEndpoint endpoint, string type, string errorCode = null)
    {
        return endpoint.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope)
            .Count(envelope => envelope?.type == type
                               && (errorCode == null
                                   || MessageSerializer.DeserializePayload<RoomErrorMessage>(envelope.data)?.code
                                   == errorCode));
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
        var joined = new RoomJoinedMessage
        {
            roomId = "R0001",
            seatIndex = 1,
            gameMode = (int)GameMode.EastOnly,
            alienationPreset = (int)AlienationPreset.Standard,
            roomState = (int)RoomState.WaitingForMatchReady,
            aiFillEnabled = true,
            acceptedSchemaVersion = 2,
            ownTotalAlienation = 17,
            seats = new[]
            {
                new RoomSeatMessage { seatIndex = 0, isOccupied = true, displayName = "Host" },
                new RoomSeatMessage { seatIndex = 1, isOccupied = true, displayName = "Guest" }
            }
        };
        var game = new ClientGameState();
        var applyJoined = typeof(ClientGameState).GetMethod("ApplyRoomJoined");
        bool gameJoinedApplied = applyJoined?.Invoke(game, new object[] { joined }) is true;
        runner.Check(gameJoinedApplied
            && game.Snapshot.roomId == "R0001"
            && game.Snapshot.alienationPreset == (int)AlienationPreset.Standard
            && game.Snapshot.privateSeat.ownTotalAlienation == 17,
            "ClientGameState must atomically apply the public preset and private owner total from RoomJoined.");

        var room = new ClientRoomState();
        room.ApplyJoined(joined);
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

[TalentRule("network_test_small", "Network Test Small", "Regression-only small talent.", TalentTier.Small, 8)]
internal sealed class NetworkTestSmallTalent : TalentRule { }

[TalentRule("network_test_medium", "Network Test Medium", "Regression-only medium talent.", TalentTier.Medium, 1)]
internal sealed class NetworkTestMediumTalent : TalentRule { }

[TalentRule("network_test_throw_round_end", "Throw Round End", "Regression-only failure talent.", TalentTier.Small, 0)]
internal sealed class NetworkTestThrowRoundEndTalent : TalentRule
{
    public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome)
    {
        throw new InvalidOperationException("injected round-end talent failure");
    }
}
